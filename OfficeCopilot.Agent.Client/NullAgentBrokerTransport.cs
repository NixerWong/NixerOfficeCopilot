using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Client
{
    public sealed class NullAgentBrokerTransport : IAgentBrokerTransport
    {
        public Task<AgentBrokerResponseEnvelope> SendAsync(
            AgentBrokerRequestEnvelope request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new AgentBrokerResponseEnvelope
            {
                RequestId = request == null ? string.Empty : request.RequestId ?? string.Empty,
                MessageType = AgentBrokerMessageTypes.Error,
                Success = false,
                ErrorCode = "broker.transport.not_connected",
                ErrorMessage = "No broker transport has been configured yet."
            });
        }
    }
}
