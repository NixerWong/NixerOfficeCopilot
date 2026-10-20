namespace OfficeCopilot.Agent.Broker
{
    internal sealed class NamedPipeBrokerHostOptions
    {
        public NamedPipeBrokerHostOptions()
        {
            PipeName = "officecopilot-agent-broker";
            MaxServerInstances = 4;
        }

        public string PipeName { get; set; }

        public int MaxServerInstances { get; set; }
    }
}
