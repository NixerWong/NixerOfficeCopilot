using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Eval.Excel;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelEvalEventSinkTests
    {
        [TestMethod]
        public async Task ExpectedClarificationCancellationIsAcknowledged()
        {
            var tracePath = Path.Combine(
                Path.GetTempPath(),
                "excel-eval-clarification-" + System.Guid.NewGuid().ToString("N") + ".jsonl");
            var transport = new SuccessfulInteractionTransport();
            var client = new TransportAgentBrokerClient(
                new AgentBrokerConnectionOptions(),
                transport);
            var task = new ExcelEvalTaskDefinition
            {
                Expect = new Dictionary<string, object>
                {
                    ["clarificationOnly"] = true
                }
            };

            try
            {
                using (var eventSink = new ExcelEvalEventSink(
                    tracePath,
                    client,
                    task,
                    "clarification-task",
                    1,
                    false,
                    null))
                {
                    var request = new AgentBrokerToolUserInteractionRequest
                    {
                        ToolCallId = "interaction-1",
                        ToolName = "AskUserQuestion"
                    };
                    await eventSink.OnEventAsync(
                        new AgentBrokerEvent
                        {
                            EventType = AgentBrokerEventTypes.Status,
                            Text = "{\"toolCallId\":\""
                                + request.ToolCallId
                                + "\",\"toolName\":\""
                                + request.ToolName
                                + "\"}"
                        },
                        CancellationToken.None);

                    Assert.AreEqual(1, eventSink.ClarificationRequestCount);
                    Assert.IsFalse(eventSink.UnexpectedInteraction);
                    Assert.IsNotNull(transport.LastRequest);
                }
            }
            finally
            {
                if (File.Exists(tracePath))
                {
                    File.Delete(tracePath);
                }
            }
        }

        private sealed class SuccessfulInteractionTransport : IAgentBrokerTransport
        {
            public AgentBrokerRequestEnvelope LastRequest { get; private set; }

            public Task<AgentBrokerResponseEnvelope> SendAsync(
                AgentBrokerRequestEnvelope request,
                IAgentBrokerEventSink eventSink,
                CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new AgentBrokerResponseEnvelope
                {
                    Success = true
                });
            }
        }
    }
}
