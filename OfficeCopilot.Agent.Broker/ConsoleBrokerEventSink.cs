using OfficeCopilot.Agent.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal sealed class ConsoleBrokerEventSink : IAgentBrokerEventSink
    {
        public Task OnEventAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (brokerEvent == null)
            {
                return Task.CompletedTask;
            }

            Console.WriteLine("[{0}] {1}", brokerEvent.EventType ?? string.Empty, brokerEvent.Text ?? string.Empty);
            return Task.CompletedTask;
        }
    }
}
