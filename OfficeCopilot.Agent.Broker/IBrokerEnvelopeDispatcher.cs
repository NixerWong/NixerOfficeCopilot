using OfficeCopilot.Agent.Client;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal interface IBrokerEnvelopeDispatcher
    {
        Task<AgentBrokerResponseEnvelope> DispatchAsync(
            AgentBrokerRequestEnvelope request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken);
    }
}
