namespace OfficeCopilot.Agent.Client
{
    /// <summary>
    /// Phase 1.4：删除某个会话（broker JSONL 文件 + 索引项）。
    /// </summary>
    public sealed class AgentBrokerDeleteConversationRequest
    {
        public AgentBrokerDeleteConversationRequest()
        {
            ConversationId = string.Empty;
        }

        public string ConversationId { get; set; }
    }
}
