using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Client
{
    public interface IAgentBrokerClient
    {
        AgentBrokerConnectionOptions Options { get; }

        Task<AgentBrokerTurnResult> SendTurnAsync(
            AgentBrokerTurnRequest request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken);

        Task<AgentBrokerCancelTurnResult> CancelTurnAsync(
            AgentBrokerCancelTurnRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// Phase 1.3b: 把 panel 端已知的会话历史一次性同步给 broker 的 <c>IConversationStore</c>。
        /// 用于桥接"panel 主导 → broker 主导"过渡期：panel 在切到旧会话的首次 turn 之前先调一次本接口，
        /// broker 仅在自己 JSONL 该 conversationId 还为空时接受种子，避免覆盖。
        /// </summary>
        Task<AgentBrokerSeedConversationResult> SeedConversationAsync(
            AgentBrokerSeedConversationRequest request,
            CancellationToken cancellationToken);

        Task<AgentBrokerClearExecutionModePreferenceResult> ClearExecutionModePreferenceAsync(
            AgentBrokerClearExecutionModePreferenceRequest request,
            CancellationToken cancellationToken);

        /// <summary>Phase 1.4：列出 broker 已持久化的会话摘要。供面板"历史会话"列表使用。</summary>
        Task<AgentBrokerListConversationsResult> ListConversationsAsync(
            AgentBrokerListConversationsRequest request,
            CancellationToken cancellationToken);

        /// <summary>Phase 1.4：加载某会话完整消息体（含 metadata）。</summary>
        Task<AgentBrokerLoadConversationResult> LoadConversationAsync(
            AgentBrokerLoadConversationRequest request,
            CancellationToken cancellationToken);

        /// <summary>Phase 1.4：删除某会话（broker JSONL 文件 + 索引）。</summary>
        Task<AgentBrokerDeleteConversationResult> DeleteConversationAsync(
            AgentBrokerDeleteConversationRequest request,
            CancellationToken cancellationToken);

        Task<AgentBrokerCompletionResult> CompleteAsync(
            AgentBrokerCompletionRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// Phase 1: Panel 向 Broker 发送用户交互响应（如 AskUserQuestion 的答案）。
        /// 对应 Broker 端 <see cref="AgentBrokerMessageTypes.ToolUserInteractionResponse"/>。
        /// </summary>
        Task<AgentBrokerToolUserInteractionResponse> SendUserInteractionResponseAsync(
            AgentBrokerToolUserInteractionResponse response,
            CancellationToken cancellationToken);
    }
}
