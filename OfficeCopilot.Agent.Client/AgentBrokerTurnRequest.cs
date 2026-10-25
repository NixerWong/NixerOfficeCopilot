using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerTurnRequest
    {
        public AgentBrokerTurnRequest()
        {
            ConversationId = string.Empty;
            MessageId = Guid.NewGuid().ToString("N");
            UserInput = string.Empty;
            UserInputContentParts = new List<AgentBrokerInputContentPart>();
            TransientSystemMessages = new List<string>();
            InteractiveMode = "agent";
            SkillId = string.Empty;
            SessionId = string.Empty;
            UiCulture = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public string UserInput { get; set; }

        public IList<AgentBrokerInputContentPart> UserInputContentParts { get; set; }

        public IList<string> TransientSystemMessages { get; set; }

        public string InteractiveMode { get; set; }

        public string SkillId { get; set; }

        public int? ModelProviderId { get; set; }

        public AgentModelOptions ModelOptions { get; set; }

        public string SessionId { get; set; }

        public string UiCulture { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
