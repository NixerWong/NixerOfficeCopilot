using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Planning
{
    public sealed class SkillPlannerRequest
    {
        public SkillPlannerRequest()
        {
            UserInput = string.Empty;
            HostApplicationName = string.Empty;
            HostKind = string.Empty;
            ConversationId = string.Empty;
            RequestedSkillName = string.Empty;
            DefaultSkillName = string.Empty;
            AvailableToolNames = new List<string>();
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string UserInput { get; set; }

        public string HostApplicationName { get; set; }

        public string HostKind { get; set; }

        public string ConversationId { get; set; }

        public string RequestedSkillName { get; set; }

        public string DefaultSkillName { get; set; }

        public IList<string> AvailableToolNames { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
