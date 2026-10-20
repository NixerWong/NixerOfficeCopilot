namespace OfficeCopilot.HostBridge.Contracts
{
    public sealed class HostCodeExecutionResult
    {
        public HostCodeExecutionResult()
        {
            RequestId = string.Empty;
            Message = string.Empty;
            ErrorCode = string.Empty;
        }

        public string RequestId { get; set; }

        public bool Success { get; set; }

        public string Message { get; set; }

        public string ErrorCode { get; set; }
    }
}
