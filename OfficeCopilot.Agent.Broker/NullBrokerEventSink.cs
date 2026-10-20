using OfficeCopilot.Agent.Client;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal sealed class NullBrokerEventSink : IAgentBrokerEventSink
    {
        public Task OnEventAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
