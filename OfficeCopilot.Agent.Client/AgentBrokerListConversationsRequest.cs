namespace OfficeCopilot.Agent.Client
{
    /// <summary>
    /// Phase 1.4：列出 broker 端持久化的所有会话。
    /// </summary>
    public sealed class AgentBrokerListConversationsRequest
    {
        public AgentBrokerListConversationsRequest()
        {
            HostApplicationName = string.Empty;
            Take = 0;
        }

        /// <summary>可选，按宿主应用过滤（"Excel" / "Word" / "PowerPoint"）；空字符串 = 不过滤。</summary>
        public string HostApplicationName { get; set; }

        /// <summary>最多返回多少条；0 或负数表示不限。</summary>
        public int Take { get; set; }
    }
}
