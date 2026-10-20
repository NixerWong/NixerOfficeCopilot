using System.Collections.Generic;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 流式模型调用中产出的单个事件，替代一次性返回 <see cref="ModelClientCompletion"/> 的模式。
    /// 阶段 7A：与 <see cref="IModelInvoker.StreamAsync"/> 配合，让执行器边收事件边派发工具+检查 token 预算。
    /// </summary>
    internal sealed class ModelStreamEvent
    {
        private ModelStreamEvent(
            ModelStreamEventType type,
            string text,
            string toolCallId,
            string toolName,
            string argumentsJson,
            string finishReason,
            string errorMessage)
        {
            Type = type;
            Text = text ?? string.Empty;
            ToolCallId = toolCallId ?? string.Empty;
            ToolName = toolName ?? string.Empty;
            ArgumentsJson = argumentsJson ?? string.Empty;
            FinishReason = finishReason ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public ModelStreamEventType Type { get; }

        /// <summary>文本增量（TextDelta / ReasoningDelta 时有效）。</summary>
        public string Text { get; }

        /// <summary>工具调用 ID（ToolUse* 时有效）。</summary>
        public string ToolCallId { get; }

        /// <summary>工具名（ToolUseCompleted 时有效）。</summary>
        public string ToolName { get; }

        /// <summary>工具参数 JSON（ToolUseCompleted 时有效）。</summary>
        public string ArgumentsJson { get; }

        /// <summary>完成原因（TurnCompleted 时有效）：stop / tool_calls / length 等。</summary>
        public string FinishReason { get; }

        /// <summary>错误消息（Error 时有效）。</summary>
        public string ErrorMessage { get; }

        // ---- 工厂方法 ----

        public static ModelStreamEvent TextDelta(string text) =>
            new(ModelStreamEventType.TextDelta, text, null, null, null, null, null);

        public static ModelStreamEvent ReasoningDelta(string text) =>
            new(ModelStreamEventType.ReasoningDelta, text, null, null, null, null, null);

        public static ModelStreamEvent ToolUseStarted(string toolCallId) =>
            new(ModelStreamEventType.ToolUseStarted, null, toolCallId, null, null, null, null);

        public static ModelStreamEvent ToolUseDelta(string toolCallId, string argumentsDelta) =>
            new(ModelStreamEventType.ToolUseDelta, argumentsDelta, toolCallId, null, null, null, null);

        public static ModelStreamEvent ToolUseCompleted(string toolCallId, string toolName, string argumentsJson) =>
            new(ModelStreamEventType.ToolUseCompleted, null, toolCallId, toolName, argumentsJson, null, null);

        public static ModelStreamEvent TurnCompleted(string finishReason) =>
            new(ModelStreamEventType.TurnCompleted, null, null, null, null, finishReason, null);

        public static ModelStreamEvent Error(string errorMessage) =>
            new(ModelStreamEventType.Error, null, null, null, null, null, errorMessage);

        /// <summary>
        /// 多工具时，用于最后兜底返回 <see cref="ModelClientCompletion"/> 的工具调用列表。
        /// </summary>
        public static ModelStreamEvent ToolCalls(IReadOnlyList<ModelClientToolCall> toolCalls)
        {
            var evt = new ModelStreamEvent(
                ModelStreamEventType.ToolCallsReady,
                null, null, null, null, null, null);
            evt._completedToolCalls = toolCalls;
            return evt;
        }

        internal IReadOnlyList<ModelClientToolCall> _completedToolCalls;
    }

    internal enum ModelStreamEventType
    {
        /// <summary>文本增量（逐 token）。</summary>
        TextDelta,

        /// <summary>推理/思维链增量。</summary>
        ReasoningDelta,

        /// <summary>检测到新工具调用开始（分配了 toolCallId）。</summary>
        ToolUseStarted,

        /// <summary>工具参数增量（流式 JSON 片段）。</summary>
        ToolUseDelta,

        /// <summary>单个工具调用参数接收完毕。</summary>
        ToolUseCompleted,

        /// <summary>非流式工具调用批量就绪（兼容旧路径）。</summary>
        ToolCallsReady,

        /// <summary>模型本轮输出完毕。</summary>
        TurnCompleted,

        /// <summary>模型调用出错。</summary>
        Error,
    }
}
