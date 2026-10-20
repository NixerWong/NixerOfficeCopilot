using System;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerInputContentPart
    {
        public AgentBrokerInputContentPart()
        {
            Type = string.Empty;
            Text = string.Empty;
            MediaType = string.Empty;
            Url = string.Empty;
            FileName = string.Empty;
            FileContentBase64 = string.Empty;
        }

        public string Type { get; set; }

        public string Text { get; set; }

        public string MediaType { get; set; }

        public string Url { get; set; }

        public string FileName { get; set; }

        public string FileContentBase64 { get; set; }
    }
}
