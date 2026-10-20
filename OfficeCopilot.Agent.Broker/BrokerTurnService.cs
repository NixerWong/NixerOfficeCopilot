using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal sealed class BrokerTurnService : IBrokerTurnService
    {
        private static readonly TimeSpan McpReadyTimeout = TimeSpan.FromSeconds(30);

        private readonly IAgentRuntime _runtime;
        private readonly Task _mcpReady;

        public BrokerTurnService(IAgentRuntime runtime)
            : this(runtime, Task.CompletedTask)
        {
        }

        public BrokerTurnService(IAgentRuntime runtime, Task mcpReady)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _mcpReady = mcpReady ?? Task.CompletedTask;
        }

        public async Task<AgentBrokerTurnResult> ExecuteAsync(
            AgentBrokerTurnRequest request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            await WaitForMcpReadyAsync(cancellationToken).ConfigureAwait(false);
            return await _runtime.ExecuteTurnAsync(request, eventSink, cancellationToken).ConfigureAwait(false);
        }

        // 超时后不阻塞回合：本回合会缺少尚未连上的 MCP 工具，但不会卡住整个会话。
        private async Task WaitForMcpReadyAsync(CancellationToken cancellationToken)
        {
            if (_mcpReady.IsCompleted)
            {
                return;
            }

            var finished = await Task.WhenAny(_mcpReady, Task.Delay(McpReadyTimeout, cancellationToken)).ConfigureAwait(false);
            if (finished != _mcpReady)
            {
                BrokerRuntimeLogger.Warning("BrokerTurnService", "MCP servers are still loading after the timeout; running the turn without them.");
            }
        }

        public Task<AgentBrokerCompletionResult> CompleteAsync(
            AgentBrokerCompletionRequest request,
            CancellationToken cancellationToken)
        {
            return _runtime.CompleteAsync(request, cancellationToken);
        }

        public Task<AgentBrokerSeedConversationResult> SeedConversationAsync(
            AgentBrokerSeedConversationRequest request,
            CancellationToken cancellationToken)
        {
            return _runtime.SeedConversationAsync(request, cancellationToken);
        }

        public Task<AgentBrokerClearExecutionModePreferenceResult> ClearExecutionModePreferenceAsync(
            AgentBrokerClearExecutionModePreferenceRequest request,
            CancellationToken cancellationToken)
        {
            return _runtime.ClearExecutionModePreferenceAsync(request, cancellationToken);
        }

        public Task<AgentBrokerListConversationsResult> ListConversationsAsync(
            AgentBrokerListConversationsRequest request,
            CancellationToken cancellationToken)
        {
            return _runtime.ListConversationsAsync(request, cancellationToken);
        }

        public Task<AgentBrokerLoadConversationResult> LoadConversationAsync(
            AgentBrokerLoadConversationRequest request,
            CancellationToken cancellationToken)
        {
            return _runtime.LoadConversationAsync(request, cancellationToken);
        }

        public Task<AgentBrokerDeleteConversationResult> DeleteConversationAsync(
            AgentBrokerDeleteConversationRequest request,
            CancellationToken cancellationToken)
        {
            return _runtime.DeleteConversationAsync(request, cancellationToken);
        }
    }
}
