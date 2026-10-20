using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerEventEnvelope
    {
        public AgentBrokerEventEnvelope()
        {
            RequestId = string.Empty;
            MessageType = AgentBrokerMessageTypes.TurnEvent;
            PayloadJson = "{}";
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string RequestId { get; set; }

        public string MessageType { get; set; }

        public string PayloadJson { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
