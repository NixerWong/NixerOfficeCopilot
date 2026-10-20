using OfficeCopilot.Agent.Client;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime
{
    public sealed class BrokerEventDispatcher
    {
        public Task DispatchAsync(
            IAgentBrokerEventSink eventSink,
            AgentBrokerEvent brokerEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (eventSink == null || brokerEvent == null)
            {
                return Task.CompletedTask;
            }

            return eventSink.OnEventAsync(brokerEvent, cancellationToken);
        }

        public Task DispatchStatusAsync(
            IAgentBrokerEventSink eventSink,
            string conversationId,
            string messageId,
            string text,
            CancellationToken cancellationToken)
        {
            return DispatchAsync(
                eventSink,
                new AgentBrokerEvent
                {
                    EventType = AgentBrokerEventTypes.Status,
                    ConversationId = conversationId ?? string.Empty,
                    MessageId = messageId ?? string.Empty,
                    Text = text ?? string.Empty
                },
                cancellationToken);
        }
    }
}
