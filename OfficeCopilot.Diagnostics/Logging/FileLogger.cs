using Microsoft.Extensions.Logging;
using System;
using System.Text;

namespace OfficeCopilot.Diagnostics.Logging
{
    internal sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLoggerProvider _provider;

        public FileLogger(string categoryName, FileLoggerProvider provider)
        {
            _categoryName = categoryName ?? "Default";
            _provider = provider;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _provider.ScopeProvider.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _provider.IsEnabled(logLevel);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter == null)
            {
                return;
            }

            var message = formatter(state, exception);
            if (_provider.ScopeProvider != null)
            {
                var builder = new StringBuilder();
                _provider.ScopeProvider.ForEachScope((scope, text) =>
                {
                    if (text.Length > 0)
                    {
                        text.Append(" => ");
                    }

                    text.Append(scope == null ? string.Empty : scope.ToString());
                }, builder);

                if (builder.Length > 0)
                {
                    message = "[Scope] " + builder + " | " + message;
                }
            }

            _provider.WriteLine(_categoryName, logLevel, eventId, message, exception);
        }
    }
}