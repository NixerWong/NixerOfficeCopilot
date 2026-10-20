using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerCompletionRequest
    {
        public AgentBrokerCompletionRequest()
        {
            ModelOptions = new AgentModelOptions();
            Messages = new List<AgentBrokerCompletionMessage>();
        }

        public AgentModelOptions ModelOptions { get; set; }

        public IList<AgentBrokerCompletionMessage> Messages { get; set; }
    }

    public sealed class AgentBrokerCompletionMessage
    {
        public AgentBrokerCompletionMessage()
        {
            Role = string.Empty;
            Content = string.Empty;
        }

        public string Role { get; set; }

        public string Content { get; set; }
    }
}