namespace OfficeCopilot.Agent.Client
{
    /// <summary>
    /// Phase 1.4：加载某个会话的完整消息体。
    /// </summary>
    public sealed class AgentBrokerLoadConversationRequest
    {
        public AgentBrokerLoadConversationRequest()
        {
            ConversationId = string.Empty;
        }

        public string ConversationId { get; set; }
    }
}
