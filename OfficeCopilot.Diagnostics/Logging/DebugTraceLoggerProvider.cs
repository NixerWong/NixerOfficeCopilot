using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace OfficeCopilot.Diagnostics.Logging
{
    public sealed class DebugTraceLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly OfficeCopilotLogOptions _options;
        private readonly ConcurrentDictionary<string, DebugTraceLogger> _loggers;
        private IExternalScopeProvider _scopeProvider;
        private bool _disposed;

        public DebugTraceLoggerProvider(OfficeCopilotLogOptions options)
        {
            _options = options ?? new OfficeCopilotLogOptions();
            _loggers = new ConcurrentDictionary<string, DebugTraceLogger>(StringComparer.OrdinalIgnoreCase);
            _scopeProvider = new LoggerExternalScopeProvider();
        }

        public ILogger CreateLogger(string categoryName)
        {
            var name = string.IsNullOrWhiteSpace(categoryName) ? "Default" : categoryName;
            return _loggers.GetOrAdd(name, n => new DebugTraceLogger(n, this));
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

        internal IExternalScopeProvider ScopeProvider
        {
            get { return _scopeProvider; }
        }

        internal bool IsEnabled(LogLevel logLevel)
        {
            if (_disposed || !_options.IsEnabled || !_options.EnableDebugTrace)
            {
                return false;
            }

            if (logLevel == LogLevel.None)
            {
                return false;
            }

            return logLevel >= _options.MinimumLevel;
        }
    }
}