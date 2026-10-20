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
    public sealed class ExcelWriteEffectPreflightTests
    {
        [TestMethod]
        public async Task PreflightThenTargetEscalation_BlocksExecution()
        {
            var adapter = new FakeWriteEffectHostAdapter();
            var tool = new FakeWriteTool();
            var registry = new InMemoryToolRegistry(new IAgentTool[] { tool });
            var services = new AgentServiceProvider();
            services.AddService<IHostCallPolicy>(new InlineHostCallPolicy());
            var handler = new DefaultHostBridgeRequestHandler(adapter, registry, services);
            const string arguments = "{\"worksheetName\":\"Summary\",\"address\":\"A1\",\"value\":\"new\"}";

            var beginResponse = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.BeginWriteEffectTurn,
                PayloadJson = JsonConvert.SerializeObject(new WriteEffectTurnRequest
                {
                    ConversationId = "conversation-1",
                    MessageId = "message-1"
                })
            }, CancellationToken.None);
            var beginResult = JsonConvert.DeserializeObject<WriteEffectTurnResult>(beginResponse.PayloadJson);
            Assert.IsTrue(beginResult.Success, beginResult.ErrorMessage);

            var preflightResponse = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.PreflightToolCall,
                PayloadJson = JsonConvert.SerializeObject(new PreflightToolCallRequest
                {
                    ConversationId = "conversation-1",
                    MessageId = "message-1",
                    ToolCallId = "call-1",
                    ToolName = tool.Definition.Name,
                    ArgumentsJson = arguments
                })
            }, CancellationToken.None);
            var preflight = JsonConvert.DeserializeObject<PreflightToolCallResult>(preflightResponse.PayloadJson);
            Assert.IsTrue(preflight.Success, preflight.ErrorMessage);
            Assert.AreEqual(ToolWriteEffectLevels.Append, preflight.Effect);
            Assert.IsFalse(string.IsNullOrWhiteSpace(preflight.PreflightToken));

            var invocation = new HostToolInvocationRequest
            {
                ToolCallId = "call-1",
                ToolName = tool.Definition.Name,
                ArgumentsJson = arguments
            };
            invocation.Metadata[HostBridgeMetadataKeys.WriteEffectPreflightToken] = preflight.PreflightToken;
            invocation.Metadata[HostBridgeMetadataKeys.ConfirmationGranted] = HostBridgeMetadataKeys.TrueValue;
            var invokeResponse = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.InvokeTool,
                PayloadJson = JsonConvert.SerializeObject(invocation)
            }, CancellationToken.None);
            var result = JsonConvert.DeserializeObject<HostToolInvocationResult>(invokeResponse.PayloadJson);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("WRITE_EFFECT_ESCALATED", result.ErrorCode);
            Assert.AreEqual(0, tool.ExecuteCount);
            Assert.AreEqual(2, adapter.AssessmentCount);
            Assert.AreEqual(0, adapter.BackupCount, "An append assessment must not create a backup.");
        }

        [TestMethod]
        public async Task ModifyExecution_CreatesBackupBeforeToolExecution()
        {
            var adapter = new FakeWriteEffectHostAdapter(
                ToolWriteEffectLevels.Modify,
                ToolWriteEffectLevels.Modify);
            var tool = new FakeWriteTool();
            var registry = new InMemoryToolRegistry(new IAgentTool[] { tool });
            var services = new AgentServiceProvider();
            services.AddService<IHostCallPolicy>(new InlineHostCallPolicy());
            var handler = new DefaultHostBridgeRequestHandler(adapter, registry, services);
            const string arguments = "{\"worksheetName\":\"Summary\",\"address\":\"A1\",\"value\":\"updated\"}";

            await BeginTurnAsync(handler);
            var preflight = await PreflightAsync(handler, tool, "call-modify", arguments);
            var invokeResponse = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.InvokeTool,
                PayloadJson = JsonConvert.SerializeObject(CreateInvocation(
                    tool.Definition.Name,
                    "call-modify",
                    arguments,
                    preflight.PreflightToken))
            }, CancellationToken.None);
            var result = JsonConvert.DeserializeObject<HostToolInvocationResult>(invokeResponse.PayloadJson);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(1, adapter.BackupCount);
            Assert.AreEqual(1, tool.ExecuteCount);
        }

        [TestMethod]
        public async Task AppendExecution_DoesNotCreateBackup()
        {
            var adapter = new FakeWriteEffectHostAdapter(
                ToolWriteEffectLevels.Append,
                ToolWriteEffectLevels.Append);
            var tool = new FakeWriteTool();
            var registry = new InMemoryToolRegistry(new IAgentTool[] { tool });
            var services = new AgentServiceProvider();
            services.AddService<IHostCallPolicy>(new InlineHostCallPolicy());
            var handler = new DefaultHostBridgeRequestHandler(adapter, registry, services);
            const string arguments = "{\"worksheetName\":\"Summary\",\"address\":\"A1\",\"value\":\"new\"}";

            await BeginTurnAsync(handler);
            var preflight = await PreflightAsync(handler, tool, "call-append", arguments);
            var invokeResponse = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.InvokeTool,
                PayloadJson = JsonConvert.SerializeObject(CreateInvocation(
                    tool.Definition.Name,
                    "call-append",
                    arguments,
                    preflight.PreflightToken))
            }, CancellationToken.None);
            var result = JsonConvert.DeserializeObject<HostToolInvocationResult>(invokeResponse.PayloadJson);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(0, adapter.BackupCount);
            Assert.AreEqual(1, tool.ExecuteCount);
        }

        [TestMethod]
        public async Task BrokerGrantedConfirmation_DoesNotTriggerSecondHostConfirmation()
        {
            var adapter = new FakeWriteEffectHostAdapter(
                ToolWriteEffectLevels.Modify,
                ToolWriteEffectLevels.Modify);
            var tool = new FakeWriteTool(requiresConfirmation: true);
            var confirmation = new RecordingConfirmationHandler { ConfirmResult = true };
            var registry = new InMemoryToolRegistry(new IAgentTool[] { tool });
            var services = new AgentServiceProvider();
            services.AddService<IHostCallPolicy>(new InlineHostCallPolicy());
            services.AddService<IToolConfirmationHandler>(confirmation);
            var handler = new DefaultHostBridgeRequestHandler(adapter, registry, services);
            const string arguments = "{\"worksheetName\":\"Summary\",\"address\":\"A1\",\"value\":\"updated\"}";

            await BeginTurnAsync(handler);
            var preflight = await PreflightAsync(handler, tool, "call-confirmed", arguments);
            var invokeResponse = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.InvokeTool,
                PayloadJson = JsonConvert.SerializeObject(CreateInvocation(
                    tool.Definition.Name,
                    "call-confirmed",
                    arguments,
                    preflight.PreflightToken))
            }, CancellationToken.None);
            var result = JsonConvert.DeserializeObject<HostToolInvocationResult>(invokeResponse.PayloadJson);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(0, confirmation.CallCount, "A Broker-approved call must not prompt again in HostBridge.");
            Assert.AreEqual(1, tool.ExecuteCount);
        }

        [TestMethod]
        public async Task UnconfirmedPreflightedWrite_UsesOperationSummaryForHostConfirmation()
        {
            var adapter = new FakeWriteEffectHostAdapter(
                ToolWriteEffectLevels.Modify,
                ToolWriteEffectLevels.Modify);
            var tool = new FakeWriteTool(requiresConfirmation: true);
            var confirmation = new RecordingConfirmationHandler { ConfirmResult = true };
            var registry = new InMemoryToolRegistry(new IAgentTool[] { tool });
            var services = new AgentServiceProvider();
            services.AddService<IHostCallPolicy>(new InlineHostCallPolicy());
            services.AddService<IToolConfirmationHandler>(confirmation);
            var handler = new DefaultHostBridgeRequestHandler(adapter, registry, services);
            const string arguments = "{\"worksheetName\":\"Summary\",\"address\":\"A1\",\"value\":\"updated\"}";

            await BeginTurnAsync(handler);
            var preflight = await PreflightAsync(handler, tool, "call-host-confirmation", arguments);
            var invokeResponse = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.InvokeTool,
                PayloadJson = JsonConvert.SerializeObject(CreateInvocation(
                    tool.Definition.Name,
                    "call-host-confirmation",
                    arguments,
                    preflight.PreflightToken,
                    confirmationGranted: false))
            }, CancellationToken.None);
            var result = JsonConvert.DeserializeObject<HostToolInvocationResult>(invokeResponse.PayloadJson);

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(1, confirmation.CallCount);
            Assert.AreEqual("target write", confirmation.LastRequest.OperationSummary);
            Assert.AreEqual(1, tool.ExecuteCount);
        }

        [TestMethod]
        public async Task WriteInvocationWithoutPreflightTicket_IsRejected()
        {
            var adapter = new FakeWriteEffectHostAdapter();
            var tool = new FakeWriteTool();
            var registry = new InMemoryToolRegistry(new IAgentTool[] { tool });
            var services = new AgentServiceProvider();
            services.AddService<IHostCallPolicy>(new InlineHostCallPolicy());
            var handler = new DefaultHostBridgeRequestHandler(adapter, registry, services);

            var invokeResponse = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.InvokeTool,
                PayloadJson = JsonConvert.SerializeObject(new HostToolInvocationRequest
                {
                    ToolCallId = "call-without-ticket",
                    ToolName = tool.Definition.Name,
                    ArgumentsJson = "{}"
                })
            }, CancellationToken.None);
            var result = JsonConvert.DeserializeObject<HostToolInvocationResult>(invokeResponse.PayloadJson);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("excel.write_effect_preflight_required", result.ErrorCode);
            Assert.AreEqual(0, tool.ExecuteCount);
        }

        private sealed class FakeWriteTool : IAgentTool
        {
            public FakeWriteTool(bool requiresConfirmation = false)
            {
                Definition = new ToolDefinition
                {
                    Name = "excel_write_range",
                    IsReadOnly = false,
                    MaxWriteEffect = ToolWriteEffectLevels.Modify,
                    RequiresBackup = true,
                    RequiresConfirmation = requiresConfirmation
                };
            }

            public ToolDefinition Definition { get; private set; }

            public int ExecuteCount { get; private set; }

            public Task<ToolExecutionResult> ExecuteAsync(
                ToolExecutionContext context,
                ToolExecutionRequest request)
            {
                ExecuteCount++;
                return Task.FromResult(ToolExecutionResult.CreateSuccess(
                    request.ToolName,
                    request.ToolCallId,
                    "written"));
            }
        }

        private sealed class FakeWriteEffectHostAdapter : ICopilotHostAdapter,
            IExcelWriteEffectHostAdapter,
            IDocumentBackupHostAdapter
        {
            private readonly string _preflightEffect;
            private readonly string _executionEffect;

            public FakeWriteEffectHostAdapter()
                : this(ToolWriteEffectLevels.Append, ToolWriteEffectLevels.Modify)
            {
            }

            public FakeWriteEffectHostAdapter(string preflightEffect, string executionEffect)
            {
                _preflightEffect = preflightEffect;
                _executionEffect = executionEffect;
            }

            public int AssessmentCount { get; private set; }

            public int BackupCount { get; private set; }

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

            public ExcelWriteEffectTurnResult BeginWriteEffectTurn(string conversationId, string messageId)
            {
                return new ExcelWriteEffectTurnResult
                {
                    Success = true,
                    WorkbookFullName = "C:\\workbook.xlsx"
                };
            }

            public ExcelWriteEffectAssessment AssessWriteEffect(
                string conversationId,
                string messageId,
                string toolName,
                string argumentsJson)
            {
                AssessmentCount++;
                return new ExcelWriteEffectAssessment
                {
                    Success = true,
                    Effect = AssessmentCount == 1 ? _preflightEffect : _executionEffect,
                    OperationSummary = "target write",
                    TargetWorkbookFullName = "C:\\workbook.xlsx",
                    TargetWorksheetName = "Summary",
                    TargetAddress = "A1"
                };
            }

            public DocumentBackupResult CreateBackup(DocumentBackupRequest request)
            {
                BackupCount++;
                return new DocumentBackupResult
                {
                    Success = true,
                    BackupPath = "C:\\backups\\test.xlsx.bak"
                };
            }
        }

        private static async Task BeginTurnAsync(DefaultHostBridgeRequestHandler handler)
        {
            var beginResponse = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.BeginWriteEffectTurn,
                PayloadJson = JsonConvert.SerializeObject(new WriteEffectTurnRequest
                {
                    ConversationId = "conversation-1",
                    MessageId = "message-1"
                })
            }, CancellationToken.None);
            var beginResult = JsonConvert.DeserializeObject<WriteEffectTurnResult>(beginResponse.PayloadJson);
            Assert.IsTrue(beginResult.Success, beginResult.ErrorMessage);
        }

        private static async Task<PreflightToolCallResult> PreflightAsync(
            DefaultHostBridgeRequestHandler handler,
            FakeWriteTool tool,
            string toolCallId,
            string arguments)
        {
            var response = await handler.HandleAsync(new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.PreflightToolCall,
                PayloadJson = JsonConvert.SerializeObject(new PreflightToolCallRequest
                {
                    ConversationId = "conversation-1",
                    MessageId = "message-1",
                    ToolCallId = toolCallId,
                    ToolName = tool.Definition.Name,
                    ArgumentsJson = arguments
                })
            }, CancellationToken.None);
            var result = JsonConvert.DeserializeObject<PreflightToolCallResult>(response.PayloadJson);
            Assert.IsTrue(result.Success, result.ErrorMessage);
            return result;
        }

        private static HostToolInvocationRequest CreateInvocation(
            string toolName,
            string toolCallId,
            string arguments,
            string preflightToken,
            bool confirmationGranted = true)
        {
            var invocation = new HostToolInvocationRequest
            {
                ToolCallId = toolCallId,
                ToolName = toolName,
                ArgumentsJson = arguments
            };
            invocation.Metadata[HostBridgeMetadataKeys.WriteEffectPreflightToken] = preflightToken;
            if (confirmationGranted)
            {
                invocation.Metadata[HostBridgeMetadataKeys.ConfirmationGranted] = HostBridgeMetadataKeys.TrueValue;
            }

            return invocation;
        }

        private sealed class InlineHostCallPolicy : IHostCallPolicy
        {
            public Task<ToolExecutionResult> ExecuteAsync(
                ToolDefinition definition,
                ToolExecutionContext context,
                ToolExecutionRequest request,
                System.Func<Task<ToolExecutionResult>> execute)
            {
                return execute();
            }
        }

        private sealed class RecordingConfirmationHandler : IToolConfirmationHandler
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
