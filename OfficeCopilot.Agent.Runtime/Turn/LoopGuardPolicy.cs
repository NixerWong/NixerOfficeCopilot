using System;
using System.Collections.Generic;
using System.Text;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 循环守卫策略：检测重复/无限工具调用并触发停止。
    /// 从 <see cref="BrokerTurnExecutor"/> 迁出（阶段 7B），原 7 个 private static 方法 + 2 个内部类。
    /// 全部为纯静态计算，无外部依赖。
    /// </summary>
    internal static class LoopGuardPolicy
    {
        private const int RepeatedProbeToolThreshold = 3;
        private const int RepeatedSameCallThreshold = 3;
        private const string ExcelAnalyzeRangeToolName = "excel_analyze_range";
        private const string ExcelFindInRangeToolName = "excel_find_in_range";

        /// <summary>评估工具调用轨迹，判断是否应触发循环守卫停止。</summary>
        public static LoopGuardDecision Evaluate(IList<ToolCallTrace> toolCallTraces)
        {
            if (toolCallTraces == null || toolCallTraces.Count == 0)
            {
                return LoopGuardDecision.None;
            }

            var lastTrace = toolCallTraces[toolCallTraces.Count - 1];
            if (lastTrace == null || !lastTrace.Success)
            {
                return LoopGuardDecision.None;
            }

            var repeatedSameCallCount = CountTrailingSameCall(toolCallTraces);
            if (repeatedSameCallCount >= RepeatedSameCallThreshold)
            {
                return new LoopGuardDecision(
                    true,
                    "检测到相同工具调用重复出现，停止工具调用并要求模型直接总结。",
                    "Repeated identical tool calls were detected.");
            }

            var repeatedProbeCount = CountTrailingToolName(toolCallTraces, lastTrace.ToolName);
            if (repeatedProbeCount >= RepeatedProbeToolThreshold
                && IsProbeStyleTool(lastTrace.ToolName))
            {
                return new LoopGuardDecision(
                    true,
                    "检测到重复探测式工具调用，停止工具调用并要求模型基于已有证据回答。",
                    "Repeated probe-style search calls were detected.");
            }

            if (HasSuccessfulToolCall(toolCallTraces, ExcelAnalyzeRangeToolName)
                && IsProbeStyleTool(lastTrace.ToolName)
                && repeatedProbeCount >= 2)
            {
                return new LoopGuardDecision(
                    true,
                    "已存在分析结果但仍继续探测式调用，停止工具调用并要求模型直接给出结论。",
                    "An analysis result already exists, so further probe-style search calls should stop.");
            }

            // RepeatedAnyToolThreshold check removed: same tool with different args
            // is legitimate (e.g., reading different cell ranges, searching different files).
            // RepeatedSameCallThreshold (same tool + same args) and RepeatedProbeToolThreshold
            // still protect against true infinite loops.

            return LoopGuardDecision.None;
        }

        public static bool HasSuccessfulToolCall(IList<ToolCallTrace> toolCallTraces, string toolName)
        {
            if (toolCallTraces == null || string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            for (var i = 0; i < toolCallTraces.Count; i++)
            {
                var trace = toolCallTraces[i];
                if (trace != null
                    && trace.Success
                    && string.Equals(trace.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static int CountTrailingToolName(IList<ToolCallTrace> toolCallTraces, string toolName)
        {
            if (toolCallTraces == null || string.IsNullOrWhiteSpace(toolName))
            {
                return 0;
            }

            var count = 0;
            for (var i = toolCallTraces.Count - 1; i >= 0; i--)
            {
                var trace = toolCallTraces[i];
                if (trace == null
                    || !trace.Success
                    || !string.Equals(trace.ToolName, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                count++;
            }

            return count;
        }

        public static int CountTrailingSameCall(IList<ToolCallTrace> toolCallTraces)
        {
            if (toolCallTraces == null || toolCallTraces.Count == 0)
            {
                return 0;
            }

            var lastTrace = toolCallTraces[toolCallTraces.Count - 1];
            if (lastTrace == null || !lastTrace.Success)
            {
                return 0;
            }

            var count = 0;
            for (var i = toolCallTraces.Count - 1; i >= 0; i--)
            {
                var trace = toolCallTraces[i];
                if (trace == null
                    || !trace.Success
                    || !string.Equals(trace.ToolName, lastTrace.ToolName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(trace.NormalizedArguments, lastTrace.NormalizedArguments, StringComparison.Ordinal))
                {
                    break;
                }

                count++;
            }

            return count;
        }

        public static bool IsProbeStyleTool(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            return string.Equals(toolName, ExcelFindInRangeToolName, StringComparison.OrdinalIgnoreCase)
                || toolName.IndexOf("find", StringComparison.OrdinalIgnoreCase) >= 0
                || toolName.IndexOf("search", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string BuildSystemPrompt(LoopGuardDecision loopGuardDecision)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Stop calling tools for this turn.");
            builder.AppendLine("Use only the existing tool results and conversation context to produce the final answer in Chinese.");
            builder.AppendLine("If the request is about max/min/average/sum/count/rank/top or matching a row by an extreme value, do not continue probing for a larger or smaller value.");
            builder.AppendLine("If the available evidence is sufficient, answer directly. If any uncertainty remains, state it briefly and stop.");

            if (loopGuardDecision != null && !string.IsNullOrWhiteSpace(loopGuardDecision.ModelInstruction))
            {
                builder.Append("Reason: ")
                    .Append(loopGuardDecision.ModelInstruction.Trim());
            }

            return builder.ToString().Trim();
        }

        /// <summary>规范化工具参数 JSON：去除所有空白字符（用于比较两个调用是否相同）。</summary>
        public static string NormalizeArguments(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return "{}";
            }

            var builder = new StringBuilder(argumentsJson.Length);
            for (var i = 0; i < argumentsJson.Length; i++)
            {
                var current = argumentsJson[i];
                if (!char.IsWhiteSpace(current))
                {
                    builder.Append(current);
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>循环守卫决策结果。</summary>
    internal sealed class LoopGuardDecision
    {
        public static readonly LoopGuardDecision None = new LoopGuardDecision(false, string.Empty, string.Empty);

        public LoopGuardDecision(bool shouldStop, string statusMessage, string modelInstruction)
        {
            ShouldStop = shouldStop;
            StatusMessage = statusMessage ?? string.Empty;
            ModelInstruction = modelInstruction ?? string.Empty;
        }

        public bool ShouldStop { get; }

        public string StatusMessage { get; }

        public string ModelInstruction { get; }
    }

    /// <summary>单次工具调用轨迹记录，用于循环守卫分析。</summary>
    internal sealed class ToolCallTrace
    {
        private const int MaxResultPreviewLength = 300;

        public ToolCallTrace(string toolName, string argumentsJson, bool success, string errorCode, string errorMessage, string resultContent = null)
        {
            ToolName = toolName ?? string.Empty;
            NormalizedArguments = LoopGuardPolicy.NormalizeArguments(argumentsJson);
            Success = success;
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            ResultPreview = resultContent == null
                ? string.Empty
                : (resultContent.Length > MaxResultPreviewLength ? resultContent.Substring(0, MaxResultPreviewLength) : resultContent);
        }

        /// <summary>结果正文前若干字符，供轮次上限收尾时生成进度摘要（如 ask_user 的回答）。</summary>
        public string ResultPreview { get; }

        public string ToolName { get; }

        public string NormalizedArguments { get; }

        public bool Success { get; }

        public string ErrorCode { get; }

        public string ErrorMessage { get; }
    }
}
