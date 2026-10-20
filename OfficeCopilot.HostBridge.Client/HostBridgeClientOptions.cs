namespace OfficeCopilot.HostBridge.Client
{
    public sealed class HostBridgeClientOptions
    {
        public HostBridgeClientOptions()
        {
            Endpoint = string.Empty;
            SessionId = string.Empty;
            HandshakeTimeoutMilliseconds = 5000;
            RequestTimeoutMilliseconds = 30000;
        }

        public string Endpoint { get; set; }

        public string SessionId { get; set; }

        public int HandshakeTimeoutMilliseconds { get; set; }

        public int RequestTimeoutMilliseconds { get; set; }
    }
}
