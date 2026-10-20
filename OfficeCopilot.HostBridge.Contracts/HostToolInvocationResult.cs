using System;
using System.Collections.Generic;

namespace OfficeCopilot.HostBridge.Contracts
{
    public sealed class HostToolInvocationResult
    {
        public HostToolInvocationResult()
        {
            ToolCallId = string.Empty;
            ToolName = string.Empty;
            Content = string.Empty;
            StructuredContentJson = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ContentParts = new List<HostToolContentPart>();
        }

        public string ToolCallId { get; set; }

        public string ToolName { get; set; }

        public bool Success { get; set; }

        public string Content { get; set; }

        public string StructuredContentJson { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public IDictionary<string, string> Metadata { get; set; }

        public List<HostToolContentPart> ContentParts { get; set; }
    }

    public sealed class HostToolContentPart
    {
        public string Type { get; set; }

        public string MediaType { get; set; }

        public string Base64 { get; set; }

        public string Description { get; set; }
    }
}
