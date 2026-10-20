using System;
using System.Globalization;

namespace OfficeCopilot.Agent.Abstractions
{
    public static class ToolCallDiagnosticsLogger
    {
        private static readonly object SyncRoot = new object();
        private static bool _isEnabled = true;
        private static IToolCallLogSink _sink;

        public static void Configure(bool isEnabled)
        {
            lock (SyncRoot)
            {
                _isEnabled = isEnabled;
            }
        }

        /// <summary>
        /// Set the log sink that receives formatted tool call entries.
        /// Call once during application startup. Pass null to disable writing.
        /// </summary>
        public static void SetSink(IToolCallLogSink sink)
        {
            lock (SyncRoot)
            {
                _sink = sink;
            }
        }

        public static bool IsEnabled
        {
            get
            {
                lock (SyncRoot)
                {
                    return _isEnabled;
                }
            }
        }

        public static string GetCurrentLogFilePath(string hostApplicationName)
        {
            if (!IsEnabled)
            {
                return string.Empty;
            }

            var sink = _sink;
            return sink != null ? sink.GetCurrentLogFilePath(hostApplicationName) : string.Empty;
        }

        public static void Log(
            string hostApplicationName,
            string toolName,
            ToolExecutionContext context,
            ToolExecutionRequest request,
            string stage,
            string message)
        {
            if (!IsEnabled)
            {
                return;
            }

            var sink = _sink;
            if (sink == null || !sink.IsEnabled)
            {
                return;
            }

            try
            {
                sink.Write(hostApplicationName, toolName, context, request, stage, message);
            }
            catch
            {
            }
        }

        public static string BuildResultDetails(ToolExecutionResult result, long durationMilliseconds)
        {
            if (result == null)
            {
                return "durationMs=" + durationMilliseconds.ToString(CultureInfo.InvariantCulture) + "; result=<null>";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "success={0}; durationMs={1}; errorCode={2}; errorMessage={3}; content={4}; structuredContentLength={5}",
                result.Success ? "true" : "false",
                durationMilliseconds,
                Sanitize(result.ErrorCode),
                Sanitize(result.ErrorMessage),
                Sanitize(TruncateForLog(result.Content, 4000)),
                string.IsNullOrEmpty(result.StructuredContentJson)
                    ? 0
                    : result.StructuredContentJson.Length);
        }

        public static string BuildFailureDetails(Exception exception, long durationMilliseconds)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "durationMs={0}; exception={1}",
                durationMilliseconds,
                Sanitize(TruncateForLog(exception == null ? string.Empty : exception.ToString(), 8000)));
        }

        public static string TruncateForLog(string value, int maxLength)
        {
            var normalized = value ?? string.Empty;
            if (maxLength <= 0 || normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized.Substring(0, maxLength) + "...(truncated)";
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "-";
            }

            return value
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
