using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Client
{
    public interface IAgentBrokerEventSink
    {
        Task OnEventAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken);
    }
}
