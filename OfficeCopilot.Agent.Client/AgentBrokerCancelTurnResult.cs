namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerCancelTurnResult
    {
        public AgentBrokerCancelTurnResult()
        {
            ConversationId = string.Empty;
            MessageId = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
        }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public bool Success { get; set; }

        public bool WasCancelled { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}
