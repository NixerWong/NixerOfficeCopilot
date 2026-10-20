using System;

namespace OfficeCopilot.Agent.Client
{
    /// <summary>
    /// Broker → Panel: 工具执行中需要用户交互（如 AskUserQuestion）。
    /// 通过 IAgentBrokerEventSink 以 EventType="tool.user_interaction.request" 发送。
    /// </summary>
    public sealed class AgentBrokerToolUserInteractionRequest
    {
        public AgentBrokerToolUserInteractionRequest()
        {
            ToolCallId = string.Empty;
            ToolName = string.Empty;
            InteractionDataJson = "{}";
            TimeoutSeconds = 300;
        }

        /// <summary>触发交互的工具调用 ID。</summary>
        public string ToolCallId { get; set; }

        /// <summary>触发交互的工具名称。</summary>
        public string ToolName { get; set; }

        /// <summary>
        /// 交互数据的 JSON 字符串。
        /// 对于 AskUserQuestion: {"questions":[...]}
        /// </summary>
        public string InteractionDataJson { get; set; }

        /// <summary>交互等待超时秒数，默认 300。</summary>
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>
    /// Panel → Broker: 用户对交互请求的响应。
    /// Panel 通过 NamedPipe 发送 RequestEnvelope(MessageType="broker.tool.user_interaction.response")。
    /// </summary>
    public sealed class AgentBrokerToolUserInteractionResponse
    {
        public AgentBrokerToolUserInteractionResponse()
        {
            ToolCallId = string.Empty;
            AnswersJson = "{}";
            AnnotationsJson = "{}";
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
        }

        /// <summary>对应的工具调用 ID。</summary>
        public string ToolCallId { get; set; }

        /// <summary>
        /// 用户答案 JSON。
        /// 对于 AskUserQuestion: {"question text 1": "selected label", ...}
        /// </summary>
        public string AnswersJson { get; set; }

        /// <summary>
        /// 附加注释 JSON（可选）。
        /// 对于 AskUserQuestion: {"question text 1": {"preview": "...", "notes": "..."}, ...}
        /// </summary>
        public string AnnotationsJson { get; set; }

        /// <summary>用户是否取消了交互。</summary>
        public bool Cancelled { get; set; }

        /// <summary>取消或错误时的错误码。</summary>
        public string ErrorCode { get; set; }

        /// <summary>取消或错误时的错误信息。</summary>
        public string ErrorMessage { get; set; }
    }
}
