namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerCompletionResult
    {
        public AgentBrokerCompletionResult()
        {
            Content = string.Empty;
        }

        public bool Success { get; set; }

        public string Content { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}