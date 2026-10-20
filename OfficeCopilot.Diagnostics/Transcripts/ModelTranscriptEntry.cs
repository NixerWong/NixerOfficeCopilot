using System;
using System.Collections.Generic;

namespace OfficeCopilot.Diagnostics.Transcripts
{
    public sealed class ModelTranscriptEntry
    {
        public ModelTranscriptEntry()
        {
            CreatedAt = DateTime.Now;
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public DateTime CreatedAt { get; set; }

        public string Direction { get; set; }

        public string Category { get; set; }

        public string ProviderKind { get; set; }

        public string ModelId { get; set; }

        public string Endpoint { get; set; }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public string ContentType { get; set; }

        public string Content { get; set; }

        public IDictionary<string, string> Properties { get; set; }
    }
}