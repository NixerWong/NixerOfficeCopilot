namespace OfficeCopilot.HostBridge.Contracts
{
    public sealed class HostCodeExecutionRequest
    {
        public HostCodeExecutionRequest()
        {
            RequestId = string.Empty;
            Language = string.Empty;
            Code = string.Empty;
        }

        public string RequestId { get; set; }

        public string Language { get; set; }

        public string Code { get; set; }
    }
}
