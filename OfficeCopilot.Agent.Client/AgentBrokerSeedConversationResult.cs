namespace OfficeCopilot.Agent.Client
{
    /// <summary>
    /// 种子注入结果。
    /// </summary>
    public sealed class AgentBrokerSeedConversationResult
    {
        public AgentBrokerSeedConversationResult()
        {
            ConversationId = string.Empty;
            SkippedReason = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
        }

        public string ConversationId { get; set; }

        /// <summary>整体调用是否成功（包括"被合理跳过"情形）。</summary>
        public bool Success { get; set; }

        /// <summary>true = 实际写入了消息；false = 因为已存在上下文等原因跳过。</summary>
        public bool WasSeeded { get; set; }

        /// <summary>
        /// 当 <see cref="WasSeeded"/> = false 时的原因。
        /// 常见值：<c>"conversation_not_empty"</c>（broker 那边已有内容，未覆盖）/ <c>"no_messages"</c>（请求里没有消息）。
        /// </summary>
        public string SkippedReason { get; set; }

        public int WrittenMessageCount { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}
