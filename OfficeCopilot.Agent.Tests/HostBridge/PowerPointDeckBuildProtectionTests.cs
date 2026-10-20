using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.HostBridge.Contracts;
using OfficeCopilot.HostBridge.Server;

namespace OfficeCopilot.Agent.Tests.HostBridge
{
    [TestClass]
    public sealed class PowerPointDeckBuildProtectionTests
    {
        private const string ConversationId = "deck-build-conversation";
        private const string DeckId = "20261018-211613-pq9ehy";

        [TestMethod]
        public async Task BuildWithDifferentDeckId_IsRejectedBeforeBackup()
        {
            var host = new FakeHostAdapter();
            var tools = CreateTools();
            var handler = CreateHandler(host, tools);
            await EstablishReadyWorkspaceAsync(handler);

            var result = await InvokeAsync(handler, "ppt_deck_build", BuildArguments("deck-3f8a2c"));

            Assert.IsFalse(result.Success);
            Assert.AreEqual("ppt.deck_build_target_mismatch", result.ErrorCode);
            StringAssert.Contains(result.ErrorMessage, DeckId);
            StringAssert.Contains(result.ErrorMessage, "演示标题");
            StringAssert.Contains(result.ErrorMessage, "10 页");
            Assert.AreEqual(0, host.BackupCount);
            Assert.AreEqual(0, tools["ppt_deck_build"].ExecuteCount);
        }

        [TestMethod]
        public async Task BuildWithBoundUnchangedWorkspace_CreatesBackupAndExecutes()
        {
            var host = new FakeHostAdapter();
            var tools = CreateTools();
            var handler = CreateHandler(host, tools);
            await EstablishReadyWorkspaceAsync(handler);

            var result = await InvokeAsync(handler, "ppt_deck_build", BuildArguments(DeckId));

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(1, host.BackupCount);
            Assert.AreEqual(1, tools["ppt_deck_build"].ExecuteCount);
            Assert.AreEqual(0, tools["ppt_deck_validate"].ExecuteCount);
        }

        [TestMethod]
        public async Task BuildWithCatalogWrappedTool_KeepsWorkspacePreflight()
        {
            var host = new FakeHostAdapter();
            var tools = CreateTools();
            var wrapped = ToolCatalogBuilder.CreateGroupTools(
                "powerpoint",
                "ppt.deck",
                null,
                tools["ppt_deck_create"],
                tools["ppt_deck_validate"],
                tools["ppt_deck_preview"],
                tools["ppt_deck_build"]);
            Assert.IsInstanceOfType(wrapped[3], typeof(IToolExecutionPreflight));
            Assert.IsNotInstanceOfType(
                ToolCatalogBuilder.CreateGroupTools("powerpoint", "ppt.deck", null, new PlainTool())[0],
                typeof(IToolExecutionPreflight));

            var handler = new DefaultHostBridgeRequestHandler(host, new InMemoryToolRegistry(wrapped));
            await EstablishReadyWorkspaceAsync(handler);
            var backupsBeforeBuild = host.BackupCount;

            var result = await InvokeAsync(handler, "ppt_deck_build", BuildArguments(DeckId));

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(backupsBeforeBuild + 1, host.BackupCount);
            Assert.AreEqual(1, tools["ppt_deck_build"].ExecuteCount);
        }

        [TestMethod]
        public async Task BuildAfterWorkspaceRevisionChanges_IsRejectedBeforeBackup()
        {
            var host = new FakeHostAdapter();
            var tools = CreateTools();
            var handler = CreateHandler(host, tools);
            await EstablishReadyWorkspaceAsync(handler);
            tools["ppt_deck_build"].WorkspaceRevision = "revision-2";

            var result = await InvokeAsync(handler, "ppt_deck_build", BuildArguments(DeckId));

            Assert.IsFalse(result.Success);
            Assert.AreEqual("ppt.deck_build_target_changed", result.ErrorCode);
            Assert.AreEqual(0, host.BackupCount);
            Assert.AreEqual(0, tools["ppt_deck_build"].ExecuteCount);
        }

        [TestMethod]
        public async Task PreflightBuildWithoutDeckId_ResolvesBoundWorkspace_ThenBuilds()
        {
            var host = new FakeHostAdapter();
            var tools = CreateTools();
            var handler = CreateHandler(host, tools);
            await EstablishReadyWorkspaceAsync(handler);

            var resolved = await PreflightAsync(handler, ConversationId, "{\"mode\":\"fidelity\"}");

            Assert.IsTrue(resolved.Success, resolved.ErrorMessage);
            StringAssert.Contains(resolved.OperationSummary, DeckId);
            StringAssert.Contains(resolved.OperationSummary, "演示标题");
            StringAssert.Contains(resolved.OperationSummary, "10 页");
            var args = Newtonsoft.Json.Linq.JObject.Parse(resolved.ResolvedArgumentsJson);
            Assert.AreEqual(DeckId, (string)args["deckId"]);
            Assert.AreEqual("演示标题", (string)args["workspaceTitle"]);
            Assert.AreEqual(10, (int)args["slideCount"]);
            Assert.AreEqual("fidelity", (string)args["mode"]);
            Assert.AreEqual(0, host.BackupCount);

            var result = await InvokeAsync(handler, "ppt_deck_build", resolved.ResolvedArgumentsJson);
            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(1, tools["ppt_deck_build"].ExecuteCount);
        }

        [TestMethod]
        public async Task PreflightBuildWithoutDeckId_NoBoundWorkspace_Fails()
        {
            var handler = CreateHandler(new FakeHostAdapter(), CreateTools());

            var resolved = await PreflightAsync(handler, ConversationId, "{}");

            Assert.IsFalse(resolved.Success);
            Assert.AreEqual("ppt.deck_build_target_unbound", resolved.ErrorCode);
            Assert.IsNull(resolved.ResolvedArgumentsJson);
        }

        [TestMethod]
        public async Task PreflightBuildWithMismatchedDeckId_IsNotSubstituted()
        {
            var handler = CreateHandler(new FakeHostAdapter(), CreateTools());
            await EstablishReadyWorkspaceAsync(handler);

            var resolved = await PreflightAsync(handler, ConversationId, "{\"deckId\":\"20261018-220500-a1b2c3\"}");

            Assert.IsFalse(resolved.Success);
            Assert.AreEqual("ppt.deck_build_target_mismatch", resolved.ErrorCode);
            Assert.IsNull(resolved.ResolvedArgumentsJson);
        }

        [TestMethod]
        public async Task PreflightBuildWithoutDeckId_AfterWorkspaceChanged_Fails()
        {
            var tools = CreateTools();
            var handler = CreateHandler(new FakeHostAdapter(), tools);
            await EstablishReadyWorkspaceAsync(handler);
            tools["ppt_deck_build"].WorkspaceRevision = "revision-2";

            var resolved = await PreflightAsync(handler, ConversationId, "{}");

            Assert.IsFalse(resolved.Success);
            Assert.AreEqual("ppt.deck_build_target_changed", resolved.ErrorCode);
        }

        [TestMethod]
        public async Task PreflightBuildWithoutDeckId_OtherConversation_Fails()
        {
            var handler = CreateHandler(new FakeHostAdapter(), CreateTools());
            await EstablishReadyWorkspaceAsync(handler);

            var resolved = await PreflightAsync(handler, "another-conversation", "{}");

            Assert.IsFalse(resolved.Success);
            Assert.AreEqual("ppt.deck_build_target_unbound", resolved.ErrorCode);
        }

        private static async Task<PreflightToolCallResult> PreflightAsync(
            DefaultHostBridgeRequestHandler handler,
            string conversationId,
            string argumentsJson)
        {
            var response = await handler.HandleAsync(new HostBridgeRequest
            {
                SessionId = ConversationId,
                Operation = HostBridgeOperationNames.PreflightToolCall,
                PayloadJson = JsonConvert.SerializeObject(new PreflightToolCallRequest
                {
                    ConversationId = conversationId,
                    ToolCallId = System.Guid.NewGuid().ToString("N"),
                    ToolName = "ppt_deck_build",
                    ArgumentsJson = argumentsJson
                })
            }, CancellationToken.None);
            Assert.IsTrue(response.Success, response.ErrorMessage);
            return JsonConvert.DeserializeObject<PreflightToolCallResult>(response.PayloadJson);
        }

        private static Dictionary<string, FakeDeckTool> CreateTools()
        {
            var tools = new Dictionary<string, FakeDeckTool>();
            foreach (var name in new[] { "ppt_deck_create", "ppt_deck_validate", "ppt_deck_preview", "ppt_deck_build" })
            {
                tools[name] = new FakeDeckTool(name);
            }

            return tools;
        }

        private static string BuildArguments(string deckId)
        {
            return JsonConvert.SerializeObject(new
            {
                deckId,
                workspaceTitle = "演示标题",
                slideCount = 10
            });
        }

        private static DefaultHostBridgeRequestHandler CreateHandler(
            FakeHostAdapter host,
            IDictionary<string, FakeDeckTool> tools)
        {
            return new DefaultHostBridgeRequestHandler(
                host,
                new InMemoryToolRegistry(new IAgentTool[]
                {
                    tools["ppt_deck_create"],
                    tools["ppt_deck_validate"],
                    tools["ppt_deck_preview"],
                    tools["ppt_deck_build"]
                }));
        }

        private static async Task EstablishReadyWorkspaceAsync(DefaultHostBridgeRequestHandler handler)
        {
            var created = await InvokeAsync(handler, "ppt_deck_create", "{}");
            Assert.IsTrue(created.Success, created.ErrorMessage);

            var previewed = await InvokeAsync(handler, "ppt_deck_preview", "{\"deckId\":\"" + DeckId + "\"}");
            Assert.IsTrue(previewed.Success, previewed.ErrorMessage);
        }

        private static async Task<HostToolInvocationResult> InvokeAsync(
            DefaultHostBridgeRequestHandler handler,
            string toolName,
            string argumentsJson)
        {
            var invocation = new HostToolInvocationRequest
            {
                ToolCallId = System.Guid.NewGuid().ToString("N"),
                ToolName = toolName,
                ArgumentsJson = argumentsJson
            };
            invocation.Metadata[HostBridgeMetadataKeys.ConfirmationGranted] = HostBridgeMetadataKeys.TrueValue;

            var response = await handler.HandleAsync(new HostBridgeRequest
            {
                SessionId = ConversationId,
                Operation = HostBridgeOperationNames.InvokeTool,
                PayloadJson = JsonConvert.SerializeObject(invocation)
            }, CancellationToken.None);
            return JsonConvert.DeserializeObject<HostToolInvocationResult>(response.PayloadJson);
        }

        private sealed class PlainTool : IAgentTool
        {
            public ToolDefinition Definition
            {
                get { return new ToolDefinition { Name = "ppt_plain", DisplayName = "ppt_plain", IsReadOnly = true }; }
            }

            public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
            {
                return Task.FromResult(ToolExecutionResult.CreateSuccess(request.ToolName, request.ToolCallId, "ok", "{}"));
            }
        }

        private sealed class FakeDeckTool : IAgentTool, IToolExecutionPreflight
        {
            public FakeDeckTool(string name)
            {
                Definition = new ToolDefinition
                {
                    Name = name,
                    DisplayName = name,
                    IsReadOnly = name == "ppt_deck_preview" || name == "ppt_deck_validate",
                    RequiresBackup = name == "ppt_deck_build",
                    RequiresConfirmation = name == "ppt_deck_build",
                    RiskLevel = name == "ppt_deck_build" ? "high" : "low"
                };
                WorkspaceRevision = "revision-1";
            }

            public ToolDefinition Definition { get; private set; }

            public int ExecuteCount { get; private set; }

            public string WorkspaceRevision { get; set; }

            public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
            {
                ExecuteCount++;
                var result = ToolExecutionResult.CreateSuccess(
                    request.ToolName,
                    request.ToolCallId,
                    "completed",
                    JsonConvert.SerializeObject(new
                    {
                        DeckId,
                        Title = "演示标题",
                        SlideCount = 10,
                        WorkspaceRevision
                    }));
                return Task.FromResult(result);
            }

            public ToolExecutionPreflightResult Preflight(ToolExecutionContext context, ToolExecutionRequest request)
            {
                return ToolExecutionPreflightResult.CreateSuccess(JsonConvert.SerializeObject(new
                {
                    deckId = DeckId,
                    title = "演示标题",
                    slideCount = 10,
                    workspaceRevision = WorkspaceRevision
                }));
            }
        }

        private sealed class FakeHostAdapter : ICopilotHostAdapter, IDocumentBackupHostAdapter
        {
            public int BackupCount { get; private set; }

            public string HostApplicationName { get { return "PowerPoint"; } }

            public HostContextSnapshot GetContextSnapshot()
            {
                return new HostContextSnapshot { DocumentName = "Presentation.pptx" };
            }

            public void InsertText(string text)
            {
            }

            public DocumentBackupResult CreateBackup(DocumentBackupRequest request)
            {
                BackupCount++;
                return new DocumentBackupResult { Success = true };
            }
        }
    }
}
