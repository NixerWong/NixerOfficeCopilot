using System;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 内置只读工具 <c>runtime.get_current_time</c>：返回 Broker 所在机器的当前日期、时间、星期与时区。
    /// </summary>
    internal static class CurrentTimeTool
    {
        internal const string ToolName = "runtime.get_current_time";

        public static ToolExecutionOutcome Execute(string toolCallId, string toolName)
        {
            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? ToolName,
                Success = true,
                Content = CurrentTimeFormatter.FormatLine(DateTimeOffset.Now)
            };
        }
    }
}
