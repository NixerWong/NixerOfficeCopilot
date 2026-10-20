using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Phase 4: 向用户发送格式化消息（支持 Markdown + 可选附件）。
    /// 对应 cc-haha <c>BriefTool.ts</c>。单向通知，不需要用户交互。
    /// </summary>
    internal sealed class BriefTool
    {
        public const string ToolName = "runtime.brief";

        private readonly Action<string> _onBriefMessage;

        public BriefTool(Action<string> onBriefMessage)
        {
            _onBriefMessage = onBriefMessage ?? throw new ArgumentNullException(nameof(onBriefMessage));
        }

        public ToolExecutionOutcome Execute(string toolCallId, string toolName, string argumentsJson)
        {
            string message = string.Empty;
            string status = "normal";
            List<string> attachments = null;

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson ?? "{}");
                if (doc.RootElement.TryGetProperty("message", out var msgProp))
                    message = msgProp.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("status", out var stProp))
                    status = stProp.GetString() ?? "normal";
                if (doc.RootElement.TryGetProperty("attachments", out var attProp) && attProp.ValueKind == JsonValueKind.Array)
                {
                    attachments = new List<string>();
                    foreach (var item in attProp.EnumerateArray())
                    {
                        var path = item.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                            attachments.Add(path);
                    }
                }
            }
            catch
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "brief.parse_error",
                    ErrorMessage = "Failed to parse Brief tool arguments."
                };
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "brief.empty_message",
                    ErrorMessage = "Brief message cannot be empty."
                };
            }

            // Emit structured status event with type "brief.message"
            var briefJson = JsonSerializer.Serialize(new
            {
                type = "brief.message",
                message,
                status,
                attachments = attachments ?? new List<string>()
            });

            _onBriefMessage?.Invoke(briefJson);

            var statusLabel = status == "proactive" ? " [proactive notification]" : "";
            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = $"Message sent to user{statusLabel}: {Truncate(message, 200)}"
            };
        }

        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }
    }

    /// <summary>
    /// Phase 4: 向用户发送文件（建议保存路径，用户通过 SaveFileDialog 确认）。
    /// 对应 cc-haha <c>SendUserFileTool.ts</c>。需要用户交互。
    /// </summary>
    internal sealed class SendUserFileTool
    {
        public const string ToolName = "runtime.send_user_file";

        public ToolExecutionOutcome Execute(string toolCallId, string toolName, string argumentsJson)
        {
            string filePath = string.Empty;
            string suggestedName = string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson ?? "{}");
                if (doc.RootElement.TryGetProperty("file_path", out var fp))
                    filePath = fp.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("suggested_destination", out var sd))
                    suggestedName = sd.GetString() ?? string.Empty;
            }
            catch
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "sendfile.parse_error",
                    ErrorMessage = "Failed to parse SendUserFile arguments."
                };
            }

            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "sendfile.not_found",
                    ErrorMessage = $"File not found: {filePath}"
                };
            }

            // The actual file saving is handled by Panel via SaveFileDialog.
            // We return success and let the Panel show the dialog.
            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = filePath, // Panel uses this as source path
                StructuredContentJson = JsonSerializer.Serialize(new
                {
                    type = "sendfile.request",
                    filePath,
                    suggestedDestination = suggestedName
                })
            };
        }
    }
}
