using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Planning;
using OfficeCopilot.Agent.Runtime.Verification;
using OfficeCopilot.HostBridge.Contracts;
using System.Threading;

namespace OfficeCopilot.Agent.Runtime
{
    public sealed class BrokerTurnContext
    {
        public BrokerTurnContext(
            AgentBrokerTurnRequest request,
            BrokerConversationState conversation,
            IAgentBrokerEventSink eventSink,
            HostContextSnapshotDto hostContextSnapshot,
            CancellationToken cancellationToken)
        {
            Request = request;
            Conversation = conversation;
            EventSink = eventSink;
            HostContextSnapshot = hostContextSnapshot;
            CancellationToken = cancellationToken;
        }

        public AgentBrokerTurnRequest Request { get; private set; }

        public BrokerConversationState Conversation { get; private set; }

        public IAgentBrokerEventSink EventSink { get; private set; }

        public HostContextSnapshotDto HostContextSnapshot { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        internal int CurrentRound { get; set; }

        internal EvidenceLedger EvidenceLedger { get; set; }

        internal RuntimeSkillPlan SkillPlan { get; set; }
    }
}
