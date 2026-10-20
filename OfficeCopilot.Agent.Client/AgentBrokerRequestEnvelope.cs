using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerRequestEnvelope
    {
        public AgentBrokerRequestEnvelope()
        {
            RequestId = Guid.NewGuid().ToString("N");
            MessageType = string.Empty;
            PayloadJson = "{}";
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string RequestId { get; set; }

        public string MessageType { get; set; }

        public string PayloadJson { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
