using System;
using System.Collections.Generic;

namespace OfficeCopilot.HostBridge.Contracts
{
    public sealed class HostBridgeRequest
    {
        public HostBridgeRequest()
        {
            RequestId = Guid.NewGuid().ToString("N");
            SessionId = string.Empty;
            Operation = string.Empty;
            PayloadJson = "{}";
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string RequestId { get; set; }

        public string SessionId { get; set; }

        public string Operation { get; set; }

        public string PayloadJson { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
