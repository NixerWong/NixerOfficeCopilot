using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Client
{
    public sealed class NullAgentBrokerClient : IAgentBrokerClient
    {
        public NullAgentBrokerClient()
            : this(new AgentBrokerConnectionOptions())
        {
        }

        public NullAgentBrokerClient(AgentBrokerConnectionOptions options)
        {
            Options = options ?? new AgentBrokerConnectionOptions();
        }

        public AgentBrokerConnectionOptions Options { get; private set; }

        public Task<AgentBrokerCompletionResult> CompleteAsync(
            AgentBrokerCompletionRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentBrokerCompletionResult
            {
                Success = false,
                ErrorCode = "broker.client.unavailable",
                ErrorMessage = "The broker client is unavailable."
            });
        }

        public async Task<AgentBrokerTurnResult> SendTurnAsync(
            AgentBrokerTurnRequest request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (eventSink != null)
            {
                await eventSink.OnEventAsync(
                    new AgentBrokerEvent
                    {
                        EventType = AgentBrokerEventTypes.Status,
                        ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                        MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                        Text = "Agent broker client skeleton is active, but no remote broker is connected."
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            return new AgentBrokerTurnResult
            {
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                Success = false,
                ErrorCode = "broker.not_connected",
                ErrorMessage = "No broker connection has been configured yet."
            };
        }

        public Task<AgentBrokerCancelTurnResult> CancelTurnAsync(
            AgentBrokerCancelTurnRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AgentBrokerCancelTurnResult
            {
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                Success = false,
                WasCancelled = false,
                ErrorCode = "broker.not_connected",
                ErrorMessage = "No broker connection has been configured yet."
            });
        }

        public Task<AgentBrokerSeedConversationResult> SeedConversationAsync(
            AgentBrokerSeedConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AgentBrokerSeedConversationResult
            {
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                Success = false,
                ErrorCode = "broker.not_connected",
                ErrorMessage = "No broker connection has been configured yet."
            });
        }

        public Task<AgentBrokerClearExecutionModePreferenceResult> ClearExecutionModePreferenceAsync(
            AgentBrokerClearExecutionModePreferenceRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AgentBrokerClearExecutionModePreferenceResult
            {
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                Success = false,
                ErrorCode = "broker.not_connected",
                ErrorMessage = "No broker connection has been configured yet."
            });
        }

        public Task<AgentBrokerListConversationsResult> ListConversationsAsync(
            AgentBrokerListConversationsRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AgentBrokerListConversationsResult
            {
                Success = false,
                ErrorCode = "broker.not_connected",
                ErrorMessage = "No broker connection has been configured yet."
            });
        }

        public Task<AgentBrokerLoadConversationResult> LoadConversationAsync(
            AgentBrokerLoadConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AgentBrokerLoadConversationResult
            {
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                Success = false,
                ErrorCode = "broker.not_connected",
                ErrorMessage = "No broker connection has been configured yet."
            });
        }

        public Task<AgentBrokerDeleteConversationResult> DeleteConversationAsync(
            AgentBrokerDeleteConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AgentBrokerDeleteConversationResult
            {
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                Success = false,
                ErrorCode = "broker.not_connected",
                ErrorMessage = "No broker connection has been configured yet."
            });
        }

        /// <summary>
        /// Phase 1: 无 broker 连接时，返回"不可用"。
        /// </summary>
        public Task<AgentBrokerToolUserInteractionResponse> SendUserInteractionResponseAsync(
            AgentBrokerToolUserInteractionResponse response,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AgentBrokerToolUserInteractionResponse
            {
                ToolCallId = response?.ToolCallId ?? string.Empty,
                Cancelled = true,
                ErrorCode = "broker.not_connected",
                ErrorMessage = "No broker connection has been configured yet."
            });
        }
    }
}
