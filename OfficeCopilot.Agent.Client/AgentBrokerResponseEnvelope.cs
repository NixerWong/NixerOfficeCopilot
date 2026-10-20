using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerResponseEnvelope
    {
        public AgentBrokerResponseEnvelope()
        {
            RequestId = string.Empty;
            MessageType = string.Empty;
            PayloadJson = "{}";
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string RequestId { get; set; }

        public string MessageType { get; set; }

        public bool Success { get; set; }

        public string PayloadJson { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
