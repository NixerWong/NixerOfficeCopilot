using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Threading;

namespace OfficeCopilot.Diagnostics.Logging
{
    public sealed class FileLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly OfficeCopilotLogOptions _options;
        private readonly ConcurrentDictionary<string, FileLogger> _loggers;
        private readonly object _syncRoot;
        private IExternalScopeProvider _scopeProvider;
        private bool _disposed;

        public FileLoggerProvider(OfficeCopilotLogOptions options)
        {
            _options = options ?? new OfficeCopilotLogOptions();
            _loggers = new ConcurrentDictionary<string, FileLogger>(StringComparer.OrdinalIgnoreCase);
            _syncRoot = new object();
            _scopeProvider = new LoggerExternalScopeProvider();

            EnsureLogDirectory();
        }

        public ILogger CreateLogger(string categoryName)
        {
            var name = string.IsNullOrWhiteSpace(categoryName) ? "Default" : categoryName;
            return _loggers.GetOrAdd(name, n => new FileLogger(n, this));
        }

        public void Dispose()
        {
            _disposed = true;
            _loggers.Clear();
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
        }

        internal bool IsEnabled(LogLevel logLevel)
        {
            if (_disposed || !_options.IsEnabled || !_options.EnableFile)
            {
                return false;
            }

            if (logLevel == LogLevel.None)
            {
                return false;
            }

            return logLevel >= _options.MinimumLevel;
        }

        internal IExternalScopeProvider ScopeProvider
        {
            get { return _scopeProvider; }
        }

        internal void WriteLine(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception exception)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                lock (_syncRoot)
                {
                    EnsureLogDirectory();
                    RollIfNeeded();

                    var path = GetCurrentLogFilePath();
                    var line = BuildLine(categoryName, logLevel, eventId, message, exception);
                    WithCrossProcessWriteLock(path, delegate
                    {
                        File.AppendAllText(path, line + Environment.NewLine);
                    });
                }
            }
            catch
            {
            }
        }

        private string BuildLine(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception exception)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} [{1}] {2}",
                timestamp,
                logLevel,
                string.IsNullOrWhiteSpace(categoryName) ? "Default" : categoryName);

            if (eventId.Id != 0)
            {
                text += " (" + eventId.Id.ToString(CultureInfo.InvariantCulture) + ")";
            }

            text += " " + (message ?? string.Empty);

            if (exception != null)
            {
                text += Environment.NewLine + exception;
            }

            return text;
        }

        private string GetCurrentLogFilePath()
        {
            return Path.Combine(_options.RootFolder ?? string.Empty, _options.ApplicationLogFileName ?? "application.log");
        }

        private void EnsureLogDirectory()
        {
            var rootFolder = _options.RootFolder;
            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                return;
            }

            Directory.CreateDirectory(rootFolder);
        }

        private void RollIfNeeded()
        {
            var currentFilePath = GetCurrentLogFilePath();
            if (!File.Exists(currentFilePath))
            {
                return;
            }

            var info = new FileInfo(currentFilePath);
            if (info.Length < Math.Max(1024, _options.MaxFileSizeBytes))
            {
                return;
            }

            var archiveFileName = Path.GetFileNameWithoutExtension(currentFilePath)
                + "-"
                + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + Path.GetExtension(currentFilePath);

            var archiveFilePath = Path.Combine(_options.RootFolder, archiveFileName);
            File.Move(currentFilePath, archiveFilePath);

            CleanupArchives();
        }

        private void CleanupArchives()
        {
            var retained = Math.Max(1, _options.RetainedFileCountLimit);
            var currentFileName = Path.GetFileName(GetCurrentLogFilePath());
            var prefix = Path.GetFileNameWithoutExtension(currentFileName) + "-";
            var extension = Path.GetExtension(currentFileName);

            var files = Directory
                .GetFiles(_options.RootFolder, prefix + "*" + extension)
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();

            if (files.Count <= retained)
            {
                return;
            }

            foreach (var file in files.Skip(retained))
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                }
            }
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

                return @"Local\NixerOfficeCopilot-AppLog-" + builder.ToString();
            }
        }
    }
}