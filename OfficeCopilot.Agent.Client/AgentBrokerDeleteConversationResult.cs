namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerDeleteConversationResult
    {
        public AgentBrokerDeleteConversationResult()
        {
            ConversationId = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }

        /// <summary>true = 实际删除；false = 该会话不存在但调用本身视为成功（也置 Success=true）。</summary>
        public bool WasDeleted { get; set; }

        public string ConversationId { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}
