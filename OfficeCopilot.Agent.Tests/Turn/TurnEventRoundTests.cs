using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.Agent.Runtime.Turn;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class TurnEventRoundTests
    {
        [TestMethod]
        public async Task EventEmitterIncludesCurrentModelRound()
        {
            var sink = new RecordingEventSink();
            var emitter = new DefaultTurnEventEmitter(
                new BrokerEventDispatcher(),
                sink,
                "conversation",
                "message");
            emitter.SetRound(3);

            await emitter.EmitStatusAsync("status", CancellationToken.None);
            await emitter.EmitToolCallStartedAsync(
                "tool-call",
                "excel_profile_range",
                "{}",
                false,
                string.Empty,
                CancellationToken.None);
            var quality = new AgentBrokerTurnQualitySummary
            {
                HasExcelDataToolCalls = true,
                OutputItemCount = 2,
                CoveredOutputItemCount = 1,
                UncoveredOutputItemCount = 1,
                HasNumberMatchReport = true,
                MatchedAnswerNumberCount = 3,
                UnmatchedAnswerNumberCount = 1,
                SelectedSkillName = "read-analysis",
                SkillSelectionReason = "Structured Excel analysis intent.",
                SkillSelectionConfidence = 0.8d
            };
            await emitter.EmitTurnCompletedAsync(
                "final answer",
                true,
                quality,
                CancellationToken.None);

            Assert.AreEqual(3, sink.Events.Count);
            Assert.AreEqual(3, sink.Events[0].Round);
            Assert.AreEqual(3, sink.Events[1].Round);
            Assert.AreEqual(AgentBrokerEventTypes.TurnCompleted, sink.Events[2].EventType);
            Assert.AreSame(quality, sink.Events[2].QualitySummary);

            var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(sink.Events[2]);
            var roundTripped = Newtonsoft.Json.JsonConvert.DeserializeObject<AgentBrokerEvent>(serialized);
            Assert.IsNotNull(roundTripped.QualitySummary);
            Assert.AreEqual(2, roundTripped.QualitySummary.OutputItemCount);
            Assert.AreEqual(1, roundTripped.QualitySummary.CoveredOutputItemCount);
            Assert.AreEqual(3, roundTripped.QualitySummary.MatchedAnswerNumberCount);
            Assert.AreEqual("read-analysis", roundTripped.QualitySummary.SelectedSkillName);
            Assert.AreEqual("Structured Excel analysis intent.", roundTripped.QualitySummary.SkillSelectionReason);
            Assert.AreEqual(0.8d, roundTripped.QualitySummary.SkillSelectionConfidence, 0.0001d);
        }

        [TestMethod]
        public void WriteEffectTelemetryEventRoundTripsStructuredLevels()
        {
            var brokerEvent = new AgentBrokerEvent
            {
                EventType = AgentBrokerEventTypes.WriteEffectEvaluated,
                ToolCallId = "tool-call-1",
                ToolName = "excel_range_write",
                ExpectedWriteEffect = "append",
                ActualWriteEffect = "modify",
                ExecutionModeDeclared = true,
                EffectExceedsDeclaredMaximum = true,
                ExceededExecutionModePreview = true
            };

            var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(brokerEvent);
            var roundTripped = Newtonsoft.Json.JsonConvert.DeserializeObject<AgentBrokerEvent>(serialized);

            Assert.AreEqual(AgentBrokerEventTypes.WriteEffectEvaluated, roundTripped.EventType);
            Assert.AreEqual("append", roundTripped.ExpectedWriteEffect);
            Assert.AreEqual("modify", roundTripped.ActualWriteEffect);
            Assert.IsTrue(roundTripped.ExecutionModeDeclared);
            Assert.IsTrue(roundTripped.EffectExceedsDeclaredMaximum);
            Assert.IsTrue(roundTripped.ExceededExecutionModePreview);
        }

        private sealed class RecordingEventSink : IAgentBrokerEventSink
        {
            public RecordingEventSink()
            {
                Events = new List<AgentBrokerEvent>();
            }

            public List<AgentBrokerEvent> Events { get; private set; }

            public Task OnEventAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken)
            {
                Events.Add(brokerEvent);
                return Task.CompletedTask;
            }
        }
    }
}
