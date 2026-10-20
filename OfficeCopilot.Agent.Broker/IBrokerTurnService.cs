using OfficeCopilot.Agent.Client;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal interface IBrokerTurnService
    {
        Task<AgentBrokerTurnResult> ExecuteAsync(
            AgentBrokerTurnRequest request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken);

        Task<AgentBrokerCompletionResult> CompleteAsync(
            AgentBrokerCompletionRequest request,
            CancellationToken cancellationToken);

        Task<AgentBrokerSeedConversationResult> SeedConversationAsync(
            AgentBrokerSeedConversationRequest request,
            CancellationToken cancellationToken);

        Task<AgentBrokerClearExecutionModePreferenceResult> ClearExecutionModePreferenceAsync(
            AgentBrokerClearExecutionModePreferenceRequest request,
            CancellationToken cancellationToken);

        Task<AgentBrokerListConversationsResult> ListConversationsAsync(
            AgentBrokerListConversationsRequest request,
            CancellationToken cancellationToken);

        Task<AgentBrokerLoadConversationResult> LoadConversationAsync(
            AgentBrokerLoadConversationRequest request,
            CancellationToken cancellationToken);

        Task<AgentBrokerDeleteConversationResult> DeleteConversationAsync(
            AgentBrokerDeleteConversationRequest request,
            CancellationToken cancellationToken);
    }
}
