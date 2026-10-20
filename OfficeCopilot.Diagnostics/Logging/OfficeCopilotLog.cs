using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace OfficeCopilot.Diagnostics.Logging
{
    public static class OfficeCopilotLog
    {
        private static readonly object SyncRoot = new object();
        private static readonly ConcurrentDictionary<string, ILogger> Loggers =
            new ConcurrentDictionary<string, ILogger>(StringComparer.OrdinalIgnoreCase);
        private static ILoggerFactory _factory;

        public static void Configure(ILoggerFactory factory)
        {
            lock (SyncRoot)
            {
                if (ReferenceEquals(_factory, factory))
                {
                    return;
                }

                _factory = factory;
                Loggers.Clear();
            }
        }

        public static void InitializeDefault()
        {
            lock (SyncRoot)
            {
                if (_factory != null)
                {
                    return;
                }

                var options = new OfficeCopilotLogOptions
                {
                    MinimumLevel = LogLevel.Information
                };
                _factory = OfficeCopilotDiagnostics.CreateLoggerFactory(options);
            }
        }

        public static ILogger For(string category)
        {
            InitializeDefault();
            var name = string.IsNullOrWhiteSpace(category) ? "Application" : category.Trim();
            return Loggers.GetOrAdd(name, key => _factory.CreateLogger(key));
        }

        public static void Write(string category, string message)
        {
            For(category).LogInformation(message ?? string.Empty);
        }

        public static void Info(string category, string message)
        {
            For(category).LogInformation(message ?? string.Empty);
        }

        public static void Warning(string category, string message)
        {
            For(category).LogWarning(message ?? string.Empty);
        }

        public static void Error(string category, string message)
        {
            For(category).LogError(message ?? string.Empty);
        }

        public static void Error(string category, string message, Exception exception)
        {
            For(category).LogError(exception, message ?? string.Empty);
        }

        public static string GetCurrentLogFilePath()
        {
            InitializeDefault();
            var options = new OfficeCopilotLogOptions();
            return System.IO.Path.Combine(options.RootFolder, options.ApplicationLogFileName);
        }
    }
}