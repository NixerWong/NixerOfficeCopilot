namespace OfficeCopilot.Agent.Client
{
    public static class AgentBrokerMessageTypes
    {
        public const string ExecuteTurnRequest = "broker.turn.execute.request";
        public const string ExecuteTurnResponse = "broker.turn.execute.response";
        public const string TurnEvent = "broker.turn.event";
        public const string CancelTurnRequest = "broker.turn.cancel.request";
        public const string CancelTurnResponse = "broker.turn.cancel.response";

        /// <summary>
        /// Phase 1.3b — Step B: 把 panel 端 sessions.json 里已有的会话历史"种子注入"到 broker JSONL。
        /// 调用方式：panel 在切到某个旧会话的第一次 turn 之前调一次，参数携带该会话的 ChatMessageState 序列；
        /// broker 端只在自己 JSONL 该 conversationId 还为空时才接受种子，避免覆盖已有上下文。
        /// </summary>
        public const string SeedConversationRequest = "broker.conversation.seed.request";
        public const string SeedConversationResponse = "broker.conversation.seed.response";
        public const string ClearExecutionModePreferenceRequest = "broker.conversation.clear_execution_mode_preference.request";
        public const string ClearExecutionModePreferenceResponse = "broker.conversation.clear_execution_mode_preference.response";

        /// <summary>Phase 1.4：列出 broker 已持久化的会话摘要。</summary>
        public const string ListConversationsRequest = "broker.conversation.list.request";
        public const string ListConversationsResponse = "broker.conversation.list.response";

        /// <summary>Phase 1.4：加载某会话完整消息体。</summary>
        public const string LoadConversationRequest = "broker.conversation.load.request";
        public const string LoadConversationResponse = "broker.conversation.load.response";

        /// <summary>Phase 1.4：删除某会话。</summary>
        public const string DeleteConversationRequest = "broker.conversation.delete.request";
        public const string DeleteConversationResponse = "broker.conversation.delete.response";

        public const string CompleteRequest = "broker.completion.single.request";
        public const string CompleteResponse = "broker.completion.single.response";

        public const string PingRequest = "broker.ping.request";
        public const string PingResponse = "broker.ping.response";
        public const string ShutdownRequest = "broker.shutdown.request";
        public const string ShutdownResponse = "broker.shutdown.response";
        public const string Error = "broker.error";

        /// <summary>
        /// Phase 0 (UI Interaction): Broker → Panel, 通知面板需要用户交互（如 AskUserQuestion）。
        /// 通过 IAgentBrokerEventSink 发送，EventType = "tool.user_interaction.request"。
        /// </summary>
        public const string ToolUserInteractionRequest = "tool.user_interaction.request";

        /// <summary>
        /// Phase 0 (UI Interaction): Panel → Broker, 用户交互的响应。
        /// 面板通过 NamedPipe 发送新的 RequestEnvelope，MessageType 为此常量。
        /// </summary>
        public const string ToolUserInteractionResponse = "broker.tool.user_interaction.response";
    }
}
