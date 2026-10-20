using OfficeCopilot.Agent.Client;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime
{
    public interface IAgentRuntime
    {
        AgentRuntimeOptions Options { get; }

        Task<AgentBrokerTurnResult> ExecuteTurnAsync(
            AgentBrokerTurnRequest request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken);

        Task<AgentBrokerCompletionResult> CompleteAsync(
            AgentBrokerCompletionRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// Phase 1.3b: 接收来自 panel 的会话历史种子注入请求，把消息写到底层 <c>IConversationStore</c>。
        /// 默认仅在 store 中该 conversationId 没有任何消息时才写入；通过 <see cref="AgentBrokerSeedConversationRequest.OverwriteExisting"/> 可强制覆盖。
        /// </summary>
        Task<AgentBrokerSeedConversationResult> SeedConversationAsync(
            AgentBrokerSeedConversationRequest request,
            CancellationToken cancellationToken);

        Task<AgentBrokerClearExecutionModePreferenceResult> ClearExecutionModePreferenceAsync(
            AgentBrokerClearExecutionModePreferenceRequest request,
            CancellationToken cancellationToken);

        /// <summary>Phase 1.4：列出 broker 已持久化的会话摘要。</summary>
        Task<AgentBrokerListConversationsResult> ListConversationsAsync(
            AgentBrokerListConversationsRequest request,
            CancellationToken cancellationToken);

        /// <summary>Phase 1.4：加载某会话完整消息体。</summary>
        Task<AgentBrokerLoadConversationResult> LoadConversationAsync(
            AgentBrokerLoadConversationRequest request,
            CancellationToken cancellationToken);

        /// <summary>Phase 1.4：删除某会话。</summary>
        Task<AgentBrokerDeleteConversationResult> DeleteConversationAsync(
            AgentBrokerDeleteConversationRequest request,
            CancellationToken cancellationToken);
    }
}
