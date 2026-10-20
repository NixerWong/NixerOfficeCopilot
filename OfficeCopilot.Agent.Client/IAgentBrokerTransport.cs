using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Client
{
    public interface IAgentBrokerTransport
    {
        Task<AgentBrokerResponseEnvelope> SendAsync(
            AgentBrokerRequestEnvelope request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken);
    }
}
