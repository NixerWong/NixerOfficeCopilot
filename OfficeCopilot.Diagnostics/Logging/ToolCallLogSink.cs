using Microsoft.Extensions.Logging;
using OfficeCopilot.Agent.Abstractions;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace OfficeCopilot.Diagnostics.Logging
{
    /// <summary>
    /// ILogger-backed implementation of <see cref="IToolCallLogSink"/>.
    /// Routes tool call diagnostics through the standard ILogger pipeline,
    /// which handles file output, Debug/Trace output, and log level filtering.
    /// </summary>
    public sealed class ToolCallLogSink : IToolCallLogSink
    {
        private readonly ILogger _logger;
        private readonly OfficeCopilotLogOptions _options;

        public ToolCallLogSink(ILogger logger, OfficeCopilotLogOptions options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new OfficeCopilotLogOptions();
        }

        public bool IsEnabled => _options.IsEnabled && _options.EnableFile;

        public string GetCurrentLogFilePath(string hostApplicationName)
        {
            if (!IsEnabled)
            {
                return string.Empty;
            }

            var folder = string.IsNullOrWhiteSpace(_options.RootFolder)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NixerOfficeCopilot",
                    "logs")
                : _options.RootFolder;

            return Path.Combine(folder, _options.ApplicationLogFileName ?? "application.log");
        }

        public void Write(
            string hostApplicationName,
            string toolName,
            ToolExecutionContext context,
            ToolExecutionRequest request,
            string stage,
            string message)
        {
            _logger.LogInformation(
                "ToolCall host={HostApp} tool={ToolName} toolCallId={ToolCallId} conversationId={ConversationId} messageId={MessageId} stage={Stage} details={Details}",
                hostApplicationName ?? "-",
                toolName ?? "-",
                request?.ToolCallId ?? "-",
                context?.ConversationId ?? "-",
                context?.MessageId ?? "-",
                stage ?? "info",
                message ?? "-");
        }
    }
}
