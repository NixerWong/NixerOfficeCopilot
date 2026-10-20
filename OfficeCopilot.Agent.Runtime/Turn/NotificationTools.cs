using System;
using System.Text;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Phase 5: 后台通知工具集合。
    /// - PushNotification: 发送 OS 级通知（通过 Panel 中转）。
    /// - MonitorTool: 后台任务进度监控。
    /// </summary>

    /// <summary>
    /// 发送操作系统级通知，通过 Panel 中转（Windows Toast / WebView 通知）。
    /// 对应 cc-haha <c>PushNotificationTool.ts</c>。
    /// </summary>
    internal sealed class PushNotificationTool
    {
        public const string ToolName = "runtime.push_notification";

        private readonly Action<string> _onNotify;

        public PushNotificationTool(Action<string> onNotify)
        {
            _onNotify = onNotify ?? throw new ArgumentNullException(nameof(onNotify));
        }

        public ToolExecutionOutcome Execute(string toolCallId, string toolName, string argumentsJson)
        {
            string message = string.Empty;
            string notificationType = "info";

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson ?? "{}");
                if (doc.RootElement.TryGetProperty("message", out var msg))
                    message = msg.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("notificationType", out var nt))
                    notificationType = nt.GetString() ?? "info";
            }
            catch
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "notification.parse_error",
                    ErrorMessage = "Failed to parse notification arguments."
                };
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "notification.empty",
                    ErrorMessage = "Notification message cannot be empty."
                };
            }

            var notifyJson = JsonSerializer.Serialize(new
            {
                type = "notification.push",
                message,
                notificationType
            });

            _onNotify(notifyJson);

            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = $"Notification sent: {message}"
            };
        }
    }

    /// <summary>
    /// 后台任务进度监控工具。发送进度更新到 Panel。
    /// 对应 cc-haha <c>MonitorTool.ts</c>。
    /// </summary>
    internal sealed class MonitorTool
    {
        public const string ToolName = "runtime.monitor";

        private readonly Action<string> _onProgress;

        public MonitorTool(Action<string> onProgress)
        {
            _onProgress = onProgress ?? throw new ArgumentNullException(nameof(onProgress));
        }

        public ToolExecutionOutcome Execute(string toolCallId, string toolName, string argumentsJson)
        {
            string taskDescription = string.Empty;
            int progressPercent = 0;
            string status = "running";

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson ?? "{}");
                if (doc.RootElement.TryGetProperty("task", out var t))
                    taskDescription = t.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("progress", out var p) && p.TryGetInt32(out var pct))
                    progressPercent = pct < 0 ? 0 : (pct > 100 ? 100 : pct);
                if (doc.RootElement.TryGetProperty("status", out var s))
                    status = s.GetString() ?? "running";
            }
            catch
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "monitor.parse_error",
                    ErrorMessage = "Failed to parse monitor arguments."
                };
            }

            var progressJson = JsonSerializer.Serialize(new
            {
                type = "monitor.progress",
                task = taskDescription,
                progress = progressPercent,
                status
            });

            _onProgress(progressJson);

            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = $"Progress: {progressPercent}% - {taskDescription}"
            };
        }
    }
}
