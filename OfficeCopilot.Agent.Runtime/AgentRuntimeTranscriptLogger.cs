using OfficeCopilot.Agent.Client;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OfficeCopilot.Agent.Runtime
{
    internal sealed class AgentRuntimeTranscriptLogger
    {
        private const string DefaultTranscriptFileName = "model-transcript.log";
        private readonly object _syncRoot;

        public AgentRuntimeTranscriptLogger()
        {
            _syncRoot = new object();
        }

        public void WriteRequest(
            AgentModelOptions modelOptions,
            string category,
            string endpoint,
            string contentType,
            string content)
        {
            Write(modelOptions, "request", category, endpoint, contentType, content);
        }

        public void WriteResponse(
            AgentModelOptions modelOptions,
            string category,
            string endpoint,
            string contentType,
            string content)
        {
            Write(modelOptions, "response", category, endpoint, contentType, content);
        }

        public void WriteException(
            AgentModelOptions modelOptions,
            string category,
            string endpoint,
            Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            Write(modelOptions, "response", category, endpoint, "text/plain", exception.ToString());
        }

        private void Write(
            AgentModelOptions modelOptions,
            string direction,
            string category,
            string endpoint,
            string contentType,
            string content)
        {
            TranscriptSettings settings;
            if (!TryGetSettings(modelOptions, out settings))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(settings.FilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var builder = new StringBuilder();
                builder.AppendLine("==================================================");
                builder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                builder.AppendLine("Direction: " + Safe(direction));
                builder.AppendLine("Category: " + Safe(category));
                builder.AppendLine("ProviderKind: " + Safe(modelOptions == null ? null : modelOptions.ProviderKind));
                builder.AppendLine("ModelId: " + Safe(modelOptions == null ? null : modelOptions.ModelId));
                builder.AppendLine("Endpoint: " + Safe(endpoint));
                builder.AppendLine("ConversationId: -");
                builder.AppendLine("MessageId: -");
                builder.AppendLine("ContentType: " + Safe(contentType));
                builder.AppendLine("Properties: {\"Source\":\"BrokerRuntime\"}");

                if (settings.Mode == TranscriptLogMode.MetadataOnly)
                {
                    builder.AppendLine("ContentLength: " + (content ?? string.Empty).Length.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.AppendLine("Content:");
                    builder.AppendLine(PrepareContent(settings.Mode, content));
                }

                builder.AppendLine();

                lock (_syncRoot)
                {
                    WithCrossProcessWriteLock(settings.FilePath, delegate
                    {
                        File.AppendAllText(settings.FilePath, builder.ToString(), Encoding.UTF8);
                    });
                }
            }
            catch
            {
            }
        }

        private static bool TryGetSettings(AgentModelOptions modelOptions, out TranscriptSettings settings)
        {
            settings = null;

            var additionalParameters = modelOptions == null ? null : modelOptions.AdditionalParameters;
            if (!ReadBoolean(additionalParameters, AgentModelAdditionalParameterNames.ModelTranscriptEnabled))
            {
                return false;
            }

            var filePath = ReadString(additionalParameters, AgentModelAdditionalParameterNames.ModelTranscriptFilePath);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                filePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NixerOfficeCopilot",
                    "logs",
                    DefaultTranscriptFileName);
            }

            settings = new TranscriptSettings(
                filePath,
                ParseMode(ReadString(additionalParameters, AgentModelAdditionalParameterNames.ModelTranscriptMode)));

            return settings.Mode != TranscriptLogMode.Disabled;
        }

        private static TranscriptLogMode ParseMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TranscriptLogMode.RawContent;
            }

            TranscriptLogMode mode;
            if (Enum.TryParse(value.Trim(), true, out mode))
            {
                return mode;
            }

            int numericMode;
            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numericMode)
                && Enum.IsDefined(typeof(TranscriptLogMode), numericMode))
            {
                return (TranscriptLogMode)numericMode;
            }

            return TranscriptLogMode.RawContent;
        }

        private static string PrepareContent(TranscriptLogMode mode, string content)
        {
            var value = content ?? string.Empty;
            return mode == TranscriptLogMode.RawContent
                ? value
                : value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static bool ReadBoolean(IDictionary<string, object> values, string key)
        {
            if (values == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            object rawValue;
            if (!values.TryGetValue(key, out rawValue) || rawValue == null)
            {
                return false;
            }

            if (rawValue is bool)
            {
                return (bool)rawValue;
            }

            bool result;
            return bool.TryParse(Convert.ToString(rawValue, CultureInfo.InvariantCulture), out result) && result;
        }

        private static string ReadString(IDictionary<string, object> values, string key)
        {
            if (values == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            object rawValue;
            return values.TryGetValue(key, out rawValue) && rawValue != null
                ? Convert.ToString(rawValue, CultureInfo.InvariantCulture) ?? string.Empty
                : string.Empty;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static void WithCrossProcessWriteLock(string filePath, Action writeAction)
        {
            if (writeAction == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                writeAction();
                return;
            }

            Mutex mutex = null;
            var lockTaken = false;

            try
            {
                mutex = new Mutex(false, BuildMutexName(filePath));
                lockTaken = mutex.WaitOne(TimeSpan.FromSeconds(5));
                writeAction();
            }
            finally
            {
                if (mutex != null)
                {
                    try
                    {
                        if (lockTaken)
                        {
                            mutex.ReleaseMutex();
                        }
                    }
                    catch
                    {
                    }

                    mutex.Dispose();
                }
            }
        }

        private static string BuildMutexName(string filePath)
        {
            var normalizedPath = Path.GetFullPath(filePath).Trim().ToLowerInvariant();
            var bytes = Encoding.UTF8.GetBytes(normalizedPath);

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);

                foreach (var value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return @"Local\NixerOfficeCopilot-Transcript-" + builder.ToString();
            }
        }

        private sealed class TranscriptSettings
        {
            public TranscriptSettings(string filePath, TranscriptLogMode mode)
            {
                FilePath = filePath ?? string.Empty;
                Mode = mode;
            }

            public string FilePath { get; private set; }

            public TranscriptLogMode Mode { get; private set; }
        }

        private enum TranscriptLogMode
        {
            Disabled = 0,
            MetadataOnly = 1,
            SanitizedContent = 2,
            RawContent = 3
        }
    }
}
