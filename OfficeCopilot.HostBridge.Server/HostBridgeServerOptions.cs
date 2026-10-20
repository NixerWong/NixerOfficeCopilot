namespace OfficeCopilot.HostBridge.Server
{
    public sealed class HostBridgeServerOptions
    {
        public HostBridgeServerOptions()
        {
            Endpoint = string.Empty;
            SessionId = string.Empty;
        }

        public string Endpoint { get; set; }

        public string SessionId { get; set; }
    }
}
