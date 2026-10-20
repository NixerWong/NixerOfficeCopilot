using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class DeckBuildTargetResolutionTests
    {
        private const string ToolName = "ppt_deck_build";
        private const string ResolvedArgs =
            "{\"mode\":\"fidelity\",\"deckId\":\"20261018-220144-9uboh2\",\"workspaceTitle\":\"季度经营汇报\",\"slideCount\":8}";

        [TestMethod]
        public async Task OmittedDeckId_IsResolvedBeforeConfirmationAndInvocation()
        {
            var invoker = new FakeDeckInvoker
            {
                PreflightResult = new PreflightToolCallResult { Success = true, ResolvedArgumentsJson = ResolvedArgs }
            };
            var interaction = new ApprovingInteraction();
            var dispatcher = CreateDispatcher(invoker, interaction);

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1", ToolName, "{\"mode\":\"fidelity\"}", new HostContextSnapshotDto(), Catalog(), null, CancellationToken.None);

            Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
            Assert.AreEqual(1, invoker.Preflights.Count);
            Assert.AreEqual("conversation-1", invoker.Preflights[0].ConversationId);
            Assert.AreEqual("{\"mode\":\"fidelity\"}", invoker.Preflights[0].ArgumentsJson);
            Assert.AreEqual(1, interaction.Requests.Count);
            using (var data = JsonDocument.Parse(interaction.Requests[0].InteractionDataJson))
            {
                Assert.AreEqual(
                    "构建工作区「季度经营汇报」；ID 20261018-220144-9uboh2；共 8 页。",
                    data.RootElement.GetProperty("operationSummary").GetString());
            }

            Assert.AreEqual(1, invoker.Invocations.Count);
            Assert.AreEqual(ResolvedArgs, invoker.Invocations[0].ArgumentsJson);
        }

        [TestMethod]
        public async Task OmittedDeckId_UnresolvedTarget_FailsWithoutConfirmationOrInvocation()
        {
            var invoker = new FakeDeckInvoker
            {
                PreflightResult = new PreflightToolCallResult
                {
                    Success = false,
                    ErrorCode = "ppt.deck_build_target_unbound",
                    ErrorMessage = "当前会话没有已预览的工作区"
                }
            };
            var interaction = new ApprovingInteraction();
            var dispatcher = CreateDispatcher(invoker, interaction);

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1", ToolName, "{}", new HostContextSnapshotDto(), Catalog(), null, CancellationToken.None);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("ppt.deck_build_target_unbound", outcome.ErrorCode);
            StringAssert.Contains(outcome.Content, "没有已预览的工作区");
            Assert.AreEqual(0, interaction.Requests.Count);
            Assert.AreEqual(0, invoker.Invocations.Count);
        }

        [TestMethod]
        public async Task CompleteExplicitTarget_SkipsResolution()
        {
            var invoker = new FakeDeckInvoker();
            var interaction = new ApprovingInteraction();
            var dispatcher = CreateDispatcher(invoker, interaction);
            const string explicitArgs =
                "{\"deckId\":\"20261018-211613-pq9ehy\",\"workspaceTitle\":\"演示\",\"slideCount\":3}";

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1", ToolName, explicitArgs, new HostContextSnapshotDto(), Catalog(), null, CancellationToken.None);

            Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
            Assert.AreEqual(0, invoker.Preflights.Count);
            Assert.AreEqual(explicitArgs, invoker.Invocations[0].ArgumentsJson);
        }

        [TestMethod]
        public void IsDeckBuildTargetIncomplete_DetectsMissingFields()
        {
            Assert.IsTrue(DefaultToolDispatcher.IsDeckBuildTargetIncomplete(null));
            Assert.IsTrue(DefaultToolDispatcher.IsDeckBuildTargetIncomplete("{}"));
            Assert.IsTrue(DefaultToolDispatcher.IsDeckBuildTargetIncomplete("{\"deckId\":\"20261018-211613-pq9ehy\"}"));
            Assert.IsTrue(DefaultToolDispatcher.IsDeckBuildTargetIncomplete(
                "{\"deckId\":\"\",\"workspaceTitle\":\"a\",\"slideCount\":1}"));
            Assert.IsFalse(DefaultToolDispatcher.IsDeckBuildTargetIncomplete(
                "{\"deckId\":\"20261018-211613-pq9ehy\",\"workspaceTitle\":\"\",\"slideCount\":1}"));
        }

        private static DefaultToolDispatcher CreateDispatcher(FakeDeckInvoker invoker, ApprovingInteraction interaction)
        {
            var dispatcher = new DefaultToolDispatcher(invoker, null, null, null, null, interaction)
            {
                CurrentConversationId = "conversation-1",
                CurrentTurnMessageId = "message-1"
            };
            dispatcher.SetPermissionMode(PermissionMode.Default);
            return dispatcher;
        }

        private static Dictionary<string, HostBridgeToolDescriptor> Catalog()
        {
            return new Dictionary<string, HostBridgeToolDescriptor>
            {
                [ToolName] = new HostBridgeToolDescriptor
                {
                    Name = ToolName,
                    DisplayName = "Build deck",
                    Description = "Build deck",
                    InputSchemaJson = "{\"type\":\"object\"}",
                    IsReadOnly = false,
                    RequiresConfirmation = true,
                    RiskLevel = "high"
                }
            };
        }

        private sealed class ApprovingInteraction : IUserInteractionHandler
        {
            public List<AgentBrokerToolUserInteractionRequest> Requests { get; } =
                new List<AgentBrokerToolUserInteractionRequest>();

            public Task<AgentBrokerToolUserInteractionResponse> RequestUserInteractionAsync(
                AgentBrokerToolUserInteractionRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(new AgentBrokerToolUserInteractionResponse { ToolCallId = request.ToolCallId });
            }
        }

        private sealed class FakeDeckInvoker : IHostToolInvoker, IHostToolPreflightInvoker
        {
            public PreflightToolCallResult PreflightResult { get; set; }

            public List<PreflightToolCallRequest> Preflights { get; } = new List<PreflightToolCallRequest>();

            public List<HostToolInvocationRequest> Invocations { get; } = new List<HostToolInvocationRequest>();

            public Task<PreflightToolCallResult> PreflightToolCallAsync(
                PreflightToolCallRequest request,
                CancellationToken cancellationToken)
            {
                Preflights.Add(request);
                return Task.FromResult(PreflightResult);
            }

            public Task<HostToolInvocationResult> InvokeAsync(
                HostToolInvocationRequest request,
                CancellationToken cancellationToken)
            {
                Invocations.Add(request);
                return Task.FromResult(new HostToolInvocationResult
                {
                    ToolCallId = request.ToolCallId,
                    ToolName = request.ToolName,
                    Success = true,
                    Content = "built"
                });
            }
        }
    }
}
