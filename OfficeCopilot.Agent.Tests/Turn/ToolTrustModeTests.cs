using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.Agent.Tests.Harness;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class ToolTrustModeTests
    {
        [TestMethod]
        public async Task NeedConfirm_PromptsBeforeConfirmableTool_AndForwardsNeedConfirm()
        {
            var host = new FakeHostToolInvoker();
            var interaction = new RecordingInteractionHandler();
            var dispatcher = new DefaultToolDispatcher(host, null, null, null, null, interaction);
            var tool = CreateTool("host.write_cell", riskLevel: "medium", requiresConfirmation: true);

            var outcome = await DispatchAsync(dispatcher, tool);

            Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
            Assert.AreEqual(1, interaction.Requests.Count);
            Assert.AreEqual(1, host.CountOf(tool.Name));
            Assert.AreEqual(ToolMetadataKeys.ToolTrustModeNeedConfirm, TrustModeOf(host.Invocations[0]));
        }

        [TestMethod]
        public async Task AlwaysAllow_SkipsPromptForConfirmableTool_AndForwardsAlwaysAllow()
        {
            var host = new FakeHostToolInvoker();
            var interaction = new RecordingInteractionHandler();
            var dispatcher = new DefaultToolDispatcher(host, null, null, null, null, interaction)
            {
                CurrentAlwaysAllowTools = true
            };
            var tool = CreateTool("host.write_cell", riskLevel: "medium", requiresConfirmation: true);

            var outcome = await DispatchAsync(dispatcher, tool);

            Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
            Assert.AreEqual(0, interaction.Requests.Count);
            Assert.AreEqual(1, host.CountOf(tool.Name));
            Assert.AreEqual(ToolMetadataKeys.ToolTrustModeAlwaysAllow, TrustModeOf(host.Invocations[0]));
        }

        [TestMethod]
        public async Task AlwaysAllow_SkipsPromptForCriticalTool()
        {
            var host = new FakeHostToolInvoker();
            var interaction = new RecordingInteractionHandler();
            var dispatcher = new DefaultToolDispatcher(host, null, null, null, null, interaction)
            {
                CurrentAlwaysAllowTools = true
            };
            var tool = CreateTool("host.execute_code", riskLevel: "critical", requiresConfirmation: true);

            var outcome = await DispatchAsync(dispatcher, tool);

            Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
            Assert.AreEqual(0, interaction.Requests.Count);
            Assert.AreEqual(1, host.CountOf(tool.Name));
        }

        [TestMethod]
        public async Task AlwaysAllow_PlanModeStillBlocksWriteWithoutPrompt()
        {
            var host = new FakeHostToolInvoker();
            var interaction = new RecordingInteractionHandler();
            var dispatcher = new DefaultToolDispatcher(host, null, null, null, null, interaction)
            {
                CurrentAlwaysAllowTools = true
            };
            dispatcher.SetPermissionMode(PermissionMode.Plan);
            var tool = CreateTool("host.write_cell", riskLevel: "medium", requiresConfirmation: true);

            var outcome = await DispatchAsync(dispatcher, tool);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("permission.blocked_by_plan_mode", outcome.ErrorCode);
            Assert.AreEqual(0, interaction.Requests.Count);
            Assert.AreEqual(0, host.CountOf(tool.Name));
        }

        [TestMethod]
        public async Task AlwaysAllowDisabled_PlanModeBlocksWrite()
        {
            var host = new FakeHostToolInvoker();
            var dispatcher = new DefaultToolDispatcher(host, null, null, null, null, new RecordingInteractionHandler());
            dispatcher.SetPermissionMode(PermissionMode.Plan);
            var tool = CreateTool("host.write_cell", riskLevel: "medium", requiresConfirmation: true);

            var outcome = await DispatchAsync(dispatcher, tool);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("permission.blocked_by_plan_mode", outcome.ErrorCode);
            Assert.AreEqual(0, host.CountOf(tool.Name));
        }

        private static Task<ToolExecutionOutcome> DispatchAsync(DefaultToolDispatcher dispatcher, HostBridgeToolDescriptor tool)
        {
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };
            return dispatcher.DispatchToolCallAsync(
                "call-trust",
                tool.Name,
                "{}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);
        }

        private static string TrustModeOf(HostToolInvocationRequest request)
        {
            string value;
            return request.Metadata != null && request.Metadata.TryGetValue(ToolMetadataKeys.ToolTrustMode, out value)
                ? value
                : null;
        }

        private static HostBridgeToolDescriptor CreateTool(string name, string riskLevel, bool requiresConfirmation)
        {
            return new HostBridgeToolDescriptor
            {
                Name = name,
                DisplayName = name,
                Description = "Trust mode test tool.",
                InputSchemaJson = "{\"type\":\"object\"}",
                IsReadOnly = false,
                RequiresConfirmation = requiresConfirmation,
                RiskLevel = riskLevel
            };
        }

        private sealed class RecordingInteractionHandler : IUserInteractionHandler
        {
            public List<AgentBrokerToolUserInteractionRequest> Requests { get; } =
                new List<AgentBrokerToolUserInteractionRequest>();

            public Task<AgentBrokerToolUserInteractionResponse> RequestUserInteractionAsync(
                AgentBrokerToolUserInteractionRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = request.ToolCallId
                });
            }
        }
    }
}
