namespace OfficeCopilot.Agent.Client
{
    public sealed class NamedPipeAgentBrokerTransportOptions
    {
        public NamedPipeAgentBrokerTransportOptions()
        {
            ServerName = ".";
            PipeName = "officecopilot-agent-broker";
            ConnectTimeoutMilliseconds = 5000;
            ReadTimeoutMilliseconds = 30000;
            WriteTimeoutMilliseconds = 30000;
        }

        public string ServerName { get; set; }

        public string PipeName { get; set; }

        public int ConnectTimeoutMilliseconds { get; set; }

        public int ReadTimeoutMilliseconds { get; set; }

        public int WriteTimeoutMilliseconds { get; set; }
    }
}
