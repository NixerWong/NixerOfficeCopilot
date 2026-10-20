using Newtonsoft.Json;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OfficeCopilot.Diagnostics.Transcripts
{
    public sealed class FileModelTranscriptLogger : IModelTranscriptLogger
    {
        private readonly string _filePath;
        private readonly ModelTranscriptLogMode _mode;
        private readonly object _syncRoot;

        public FileModelTranscriptLogger(string filePath, ModelTranscriptLogMode mode)
        {
            _filePath = filePath;
            _mode = mode;
            _syncRoot = new object();
        }

        public void Write(ModelTranscriptEntry entry)
        {
            if (_mode == ModelTranscriptLogMode.Disabled || entry == null || string.IsNullOrWhiteSpace(_filePath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var builder = new StringBuilder();
                builder.AppendLine("==================================================");
                builder.AppendLine(entry.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                builder.AppendLine("Direction: " + Safe(entry.Direction));
                builder.AppendLine("Category: " + Safe(entry.Category));
                builder.AppendLine("ProviderKind: " + Safe(entry.ProviderKind));
                builder.AppendLine("ModelId: " + Safe(entry.ModelId));
                builder.AppendLine("Endpoint: " + Safe(entry.Endpoint));
                builder.AppendLine("ConversationId: " + Safe(entry.ConversationId));
                builder.AppendLine("MessageId: " + Safe(entry.MessageId));
                builder.AppendLine("ContentType: " + Safe(entry.ContentType));

                if (entry.Properties != null && entry.Properties.Count > 0)
                {
                    builder.AppendLine("Properties: " + JsonConvert.SerializeObject(entry.Properties, Formatting.None));
                }

                if (_mode == ModelTranscriptLogMode.MetadataOnly)
                {
                    builder.AppendLine("ContentLength: " + (entry.Content ?? string.Empty).Length);
                }
                else
                {
                    builder.AppendLine("Content:");
                    builder.AppendLine(PrepareContent(entry.Content));
                }

                builder.AppendLine();

                lock (_syncRoot)
                {
                    WithCrossProcessWriteLock(_filePath, delegate
                    {
                        File.AppendAllText(_filePath, builder.ToString(), Encoding.UTF8);
                    });
                }
            }
            catch
            {
            }
        }

        private string PrepareContent(string content)
        {
            var value = content ?? string.Empty;
            if (_mode == ModelTranscriptLogMode.RawContent)
            {
                return value;
            }

            return Sanitize(value);
        }

        private static string Sanitize(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            var text = content;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return text;
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
                    builder.Append(value.ToString("x2"));
                }

                return @"Local\NixerOfficeCopilot-Transcript-" + builder.ToString();
            }
        }
    }
}