namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerConnectionOptions
    {
        public AgentBrokerConnectionOptions()
        {
            Endpoint = string.Empty;
            ClientId = string.Empty;
            SessionId = string.Empty;
            RequestTimeoutMilliseconds = 30000;
        }

        public string Endpoint { get; set; }

        public string ClientId { get; set; }

        public string SessionId { get; set; }

        public int RequestTimeoutMilliseconds { get; set; }
    }
}
