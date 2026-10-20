using System;
using System.Collections.Generic;

namespace OfficeCopilot.HostBridge.Contracts
{
    public sealed class HostToolInvocationRequest
    {
        public HostToolInvocationRequest()
        {
            ToolCallId = Guid.NewGuid().ToString("N");
            ToolName = string.Empty;
            ArgumentsJson = "{}";
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ToolCallId { get; set; }

        public string ToolName { get; set; }

        public string ArgumentsJson { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
