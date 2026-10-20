namespace OfficeCopilot.Agent.Client
{
    public static class AgentBrokerEventTypes
    {
        public const string TurnStarted = "turn.started";
        public const string ThinkingDelta = "thinking.delta";
        public const string ContentDelta = "content.delta";
        public const string ToolCallStarted = "tool_call.started";
        public const string ToolCallCompleted = "tool_call.completed";
        public const string Status = "status";
        public const string ExecutionMode = "execution.mode";
        public const string WriteEffectEvaluated = "write_effect.evaluated";
        public const string TurnCompleted = "turn.completed";
        public const string Error = "error";

        /// <summary>
        /// Phase 1: Broker → Panel, 工具执行中需要用户交互（如 AskUserQuestion）。
        /// Panel 收到此事件后应展示问答 UI，用户回答后通过
        /// <see cref="IAgentBrokerClient.SendUserInteractionResponseAsync"/> 返回答案。
        /// </summary>
        public const string ToolUserInteractionRequest = "tool.user_interaction.request";
    }
}
