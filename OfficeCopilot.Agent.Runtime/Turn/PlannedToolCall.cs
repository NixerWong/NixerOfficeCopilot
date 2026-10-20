using System;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 计划中的工具调用描述。
    /// 从 <see cref="BrokerTurnExecutor"/> 内部类提升（阶段 7B）。
    /// </summary>
    internal sealed class PlannedToolCall
    {
        public PlannedToolCall(string toolName, string argumentsJson)
            : this(toolName, argumentsJson, null)
        {
        }

        public PlannedToolCall(string toolName, string argumentsJson, string toolCallId)
        {
            ToolCallId = string.IsNullOrWhiteSpace(toolCallId) ? Guid.NewGuid().ToString("N") : toolCallId.Trim();
            ToolName = toolName ?? string.Empty;
            ArgumentsJson = NormalizeArgumentsJson(argumentsJson);
        }

        public string ToolCallId { get; }

        public string ToolName { get; }

        public string ArgumentsJson { get; }

        /// <summary>规范化参数 JSON：去除空白 + 兜底为 {}。</summary>
        internal static string NormalizeArgumentsJson(string argumentsJson)
        {
            return string.IsNullOrWhiteSpace(argumentsJson)
                ? "{}"
                : argumentsJson.Trim();
        }
    }
}
