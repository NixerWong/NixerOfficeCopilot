using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerListConversationsResult
    {
        public AgentBrokerListConversationsResult()
        {
            Summaries = new List<AgentBrokerConversationSummary>();
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }

        public IList<AgentBrokerConversationSummary> Summaries { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}
