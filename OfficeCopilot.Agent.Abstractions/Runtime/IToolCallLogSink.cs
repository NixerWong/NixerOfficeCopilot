namespace OfficeCopilot.Agent.Abstractions
{
    /// <summary>
    /// Sink for tool call diagnostic log entries.
    /// Set via <see cref="ToolCallDiagnosticsLogger.SetSink"/> to route tool call logs
    /// through the application's logging infrastructure (e.g. ILogger).
    /// </summary>
    public interface IToolCallLogSink
    {
        /// <summary>Whether the sink is currently accepting log entries.</summary>
        bool IsEnabled { get; }

        /// <summary>Write a formatted tool call log entry.</summary>
        void Write(
            string hostApplicationName,
            string toolName,
            ToolExecutionContext context,
            ToolExecutionRequest request,
            string stage,
            string message);

        /// <summary>Get the file path where tool call logs are written, or empty if file logging is disabled.</summary>
        string GetCurrentLogFilePath(string hostApplicationName);
    }
}
