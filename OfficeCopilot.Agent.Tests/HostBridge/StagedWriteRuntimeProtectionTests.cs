using System;
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
    public sealed class StagedWriteRuntimeProtectionTests
    {
        [TestMethod]
        public async Task ProtectedAndLockedWorkbook_SetsInternalStagedOnlyFlagWithoutConfirmation()
        {
            var tool = new CapturingTool();
            var confirmation = new CapturingConfirmationHandler();
            var callPolicy = new InlineHostCallPolicy();
            var result = await InvokeAsync(
                true,
                string.Empty,
                tool,
                confirmation,
                callPolicy);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsTrue(tool.StagedOnly);
            Assert.AreEqual(2, callPolicy.CallCount);
            Assert.AreEqual(0, confirmation.CallCount);
        }

        [TestMethod]
        public async Task UnlockedFormalSheet_RequiresConfirmationAndDoesNotSetStagedOnlyFlag()
        {
            const string reason = "正式工作表含未锁定单元格。";
            var tool = new CapturingTool();
            var confirmation = new CapturingConfirmationHandler { ConfirmResult = true };
            var callPolicy = new InlineHostCallPolicy();
            var result = await InvokeAsync(
                false,
                reason,
                tool,
                confirmation,
                callPolicy);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsFalse(tool.StagedOnly);
            Assert.AreEqual(2, callPolicy.CallCount);
            Assert.AreEqual(1, confirmation.CallCount);
            StringAssert.Contains(confirmation.LastRequest.OperationSummary, reason);
        }

        [TestMethod]
        public async Task StaticallyProvenDeclaredTarget_ReceivesTrustedProtectionScopeAndStillRequiresConfirmation()
        {
            var tool = new CapturingTool();
            var confirmation = new CapturingConfirmationHandler { ConfirmResult = true };
            var callPolicy = new InlineHostCallPolicy();
            var arguments = JsonConvert.SerializeObject(new
            {
                outputSheet = "Summary",
                declaredWriteTargets = new[]
                {
                    new { sheet = "Summary", isNewSheet = true }
                },
                code = "Sub Build()\n"
                    + "Dim output As Worksheet\n"
                    + "Set output = ThisWorkbook.Worksheets.Add\n"
                    + "output.Name = \"Summary\"\n"
                    + "output.Cells(1, 1).Value = 5\n"
                    + "End Sub"
            });

            var result = await InvokeAsync(
                true,
                string.Empty,
                tool,
                confirmation,
                callPolicy,
                arguments);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.IsFalse(tool.StagedOnly);
            Assert.AreEqual(1, confirmation.CallCount);
            CollectionAssert.AreEqual(
                new List<string> { "Summary" },
                JsonConvert.DeserializeObject<List<string>>(tool.ProtectionAllowedWorksheetNamesJson));
        }

        [TestMethod]
        public async Task ScopeViolation_ForcesConfirmationForSubsequentWipWritesInTheSameConversation()
        {
            var tool = new CapturingTool { ReturnScopeViolation = true };
            var confirmation = new CapturingConfirmationHandler { ConfirmResult = true };
            var callPolicy = new InlineHostCallPolicy();
            var services = new AgentServiceProvider();
            services.AddService<IHostCallPolicy>(callPolicy);
            services.AddService<IToolConfirmationHandler>(confirmation);
            var handler = new DefaultHostBridgeRequestHandler(
                new StagedHostAdapter(true, string.Empty),
                new InMemoryToolRegistry(new IAgentTool[] { tool }),
                services);
            const string conversationId = "scope-violation-conversation";
            var arguments = JsonConvert.SerializeObject(new
            {
                outputSheet = "_wip_01_汇总",
                declaredWriteTargets = new[]
                {
                    new { sheet = "_wip_01_汇总", isNewSheet = true }
                },
                code = "Sub Build()\nEnd Sub"
            });

            var firstResult = await InvokeHandlerAsync(handler, tool, arguments, conversationId, false);
            Assert.IsFalse(firstResult.Success);
            Assert.AreEqual("VBA_SCOPE_VIOLATION", firstResult.ErrorCode);
            Assert.AreEqual(0, confirmation.CallCount);

            tool.ReturnScopeViolation = false;
            var secondResult = await InvokeHandlerAsync(handler, tool, arguments, conversationId, true);

            Assert.IsTrue(secondResult.Success, secondResult.ErrorMessage);
            Assert.IsFalse(tool.StagedOnly);
            Assert.AreEqual(1, confirmation.CallCount);
            StringAssert.Contains(confirmation.LastRequest.OperationSummary, "范围越界");
        }

        private static async Task<HostToolInvocationResult> InvokeAsync(
            bool canProtect,
            string reason,
            CapturingTool tool,
            CapturingConfirmationHandler confirmation,
            InlineHostCallPolicy callPolicy,
            string argumentsJson = null)
        {
            var services = new AgentServiceProvider();
            services.AddService<IHostCallPolicy>(callPolicy);
            services.AddService<IToolConfirmationHandler>(confirmation);

            var registry = new InMemoryToolRegistry(new IAgentTool[] { tool });
            var handler = new DefaultHostBridgeRequestHandler(
                new StagedHostAdapter(canProtect, reason),
                registry,
                services);
            return await InvokeHandlerAsync(
                handler,
                tool,
                argumentsJson ?? JsonConvert.SerializeObject(new
                {
                    outputSheet = "_wip_01_汇总",
                    declaredWriteTargets = new[]
                    {
                        new { sheet = "_wip_01_汇总", isNewSheet = true }
                    },
                    code = "Sub Build()\nEnd Sub"
                }),
                "staged-protection-conversation",
                false);
        }

        private static async Task<HostToolInvocationResult> InvokeHandlerAsync(
            DefaultHostBridgeRequestHandler handler,
            CapturingTool tool,
            string argumentsJson,
            string conversationId,
            bool confirmationAlreadyGranted)
        {
            var payload = new HostToolInvocationRequest
            {
                ToolCallId = "staged-protection-test",
                ToolName = tool.Definition.Name,
                ArgumentsJson = argumentsJson
            };
            payload.Metadata[HostBridgeMetadataKeys.StagedOnly] = HostBridgeMetadataKeys.TrueValue;
            payload.Metadata[ToolMetadataKeys.ConversationId] = conversationId;
            if (confirmationAlreadyGranted)
            {
                payload.Metadata[HostBridgeMetadataKeys.ConfirmationGranted] =
                    HostBridgeMetadataKeys.TrueValue;
            }

            var response = await handler.HandleAsync(
                new HostBridgeRequest
                {
                    Operation = HostBridgeOperationNames.InvokeTool,
                    PayloadJson = JsonConvert.SerializeObject(payload)
                },
                CancellationToken.None);

            Assert.IsTrue(response.Success, response.ErrorMessage);
            return JsonConvert.DeserializeObject<HostToolInvocationResult>(response.PayloadJson);
        }

        private sealed class CapturingTool : IAgentTool
        {
            public CapturingTool()
            {
                Definition = new ToolDefinition
                {
                    Name = "excel_execute_vba",
                    DisplayName = "执行 VBA",
                    Description = "运行测试 VBA。",
                    RequiresConfirmation = true,
                    IsReadOnly = false
                };
            }

            public ToolDefinition Definition { get; private set; }

            public bool StagedOnly { get; private set; }

            public string ProtectionAllowedWorksheetNamesJson { get; private set; }

            public bool ReturnScopeViolation { get; set; }

            public Task<ToolExecutionResult> ExecuteAsync(
                ToolExecutionContext context,
                ToolExecutionRequest request)
            {
                string stagedOnlyValue;
                StagedOnly = request.Metadata != null
                    && request.Metadata.TryGetValue(HostBridgeMetadataKeys.StagedOnly, out stagedOnlyValue)
                    && string.Equals(
                        stagedOnlyValue,
                        HostBridgeMetadataKeys.TrueValue,
                        StringComparison.OrdinalIgnoreCase);
                string protectionAllowedWorksheetNames = null;
                if (request.Metadata != null)
                {
                    request.Metadata.TryGetValue(
                        HostBridgeMetadataKeys.VbaProtectionAllowedWorksheets,
                        out protectionAllowedWorksheetNames);
                }

                ProtectionAllowedWorksheetNamesJson = protectionAllowedWorksheetNames;
                if (ReturnScopeViolation)
                {
                    return Task.FromResult(ToolExecutionResult.CreateFailure(
                        request.ToolName,
                        request.ToolCallId,
                        "VBA_SCOPE_VIOLATION",
                        "VBA_SCOPE_VIOLATION"));
                }

                return Task.FromResult(ToolExecutionResult.CreateSuccess(
                    request.ToolName,
                    request.ToolCallId,
                    "completed"));
            }
        }

        private sealed class StagedHostAdapter : ICopilotHostAdapter,
            IStagedWriteProtectionHostAdapter,
            IVbaDeclaredWriteProtectionHostAdapter
        {
            private readonly bool _canProtect;
            private readonly string _reason;

            public StagedHostAdapter(bool canProtect, string reason)
            {
                _canProtect = canProtect;
                _reason = reason ?? string.Empty;
            }

            public string HostApplicationName
            {
                get { return "Excel"; }
            }

            public HostContextSnapshot GetContextSnapshot()
            {
                return new HostContextSnapshot { DocumentName = "Test.xlsx" };
            }

            public void InsertText(string text)
            {
            }

            public VbaExecutionAvailability GetStagedWriteProtectionAvailability()
            {
                return new VbaExecutionAvailability
                {
                    CanExecute = _canProtect,
                    Reason = _reason
                };
            }

            public VbaExecutionAvailability GetDeclaredWriteProtectionAvailability(
                IList<string> allowedWorksheetNames)
            {
                return new VbaExecutionAvailability
                {
                    CanExecute = _canProtect,
                    Reason = _reason
                };
            }
        }

        private sealed class InlineHostCallPolicy : IHostCallPolicy
        {
            public int CallCount { get; private set; }

            public Task<ToolExecutionResult> ExecuteAsync(
                ToolDefinition definition,
                ToolExecutionContext context,
                ToolExecutionRequest request,
                Func<Task<ToolExecutionResult>> execute)
            {
                CallCount++;
                return execute();
            }
        }

        private sealed class CapturingConfirmationHandler : IToolConfirmationHandler
        {
            public int CallCount { get; private set; }

            public bool ConfirmResult { get; set; }

            public ToolConfirmationRequest LastRequest { get; private set; }

            public bool Confirm(ToolConfirmationRequest request)
            {
                CallCount++;
                LastRequest = request;
                return ConfirmResult;
            }
        }
    }
}
