using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerCancelTurnRequest
    {
        public AgentBrokerCancelTurnRequest()
        {
            TargetRequestId = string.Empty;
            ConversationId = string.Empty;
            MessageId = string.Empty;
            SessionId = string.Empty;
            Reason = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string TargetRequestId { get; set; }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public string SessionId { get; set; }

        public string Reason { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
