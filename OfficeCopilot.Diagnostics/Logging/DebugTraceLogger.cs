using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OfficeCopilot.Diagnostics.Logging
{
    internal sealed class DebugTraceLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly DebugTraceLoggerProvider _provider;

        public DebugTraceLogger(string categoryName, DebugTraceLoggerProvider provider)
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

            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            builder.Append(" [");
            builder.Append(logLevel);
            builder.Append("] ");
            builder.Append(_categoryName);

            if (eventId.Id != 0)
            {
                builder.Append(" (");
                builder.Append(eventId.Id.ToString(CultureInfo.InvariantCulture));
                builder.Append(")");
            }

            builder.Append(" ");

            _provider.ScopeProvider.ForEachScope((scope, text) =>
            {
                if (scope == null)
                {
                    return;
                }

                text.Append("[Scope:");
                text.Append(scope);
                text.Append("] ");
            }, builder);

            builder.Append(formatter(state, exception));

            if (exception != null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            var line = builder.ToString();
            Debug.WriteLine(line);
            Trace.WriteLine(line);
        }
    }
}