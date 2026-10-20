using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class ExcelWriteEffectConfirmationTests
    {
        [TestMethod]
        public void WriteGateRecoveryToolsExistInRuntimeCatalog()
        {
            var catalog = DefaultToolDispatcher.BuildToolCatalog(new HostBridgeCapabilities
            {
                SupportsWriteEffectPreflight = true
            });

            Assert.IsTrue(catalog.ContainsKey(DefaultToolDispatcher.RuntimeAskUserQuestionName));
            string previewImageIdPattern;
            using (var askUserSchema = JsonDocument.Parse(
                catalog[DefaultToolDispatcher.RuntimeAskUserQuestionName].InputSchemaJson))
            {
                previewImageIdPattern = askUserSchema.RootElement
                    .GetProperty("properties").GetProperty("questions")
                    .GetProperty("items").GetProperty("properties")
                    .GetProperty("options").GetProperty("items").GetProperty("properties")
                    .GetProperty("previewImageId").GetProperty("pattern").GetString();
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(
                    "20261019-223004-tfg4to/daisy-cover/ae6c436fabe3",
                    previewImageIdPattern));
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(
                    "20261019-223004-tfg4to/daisy-cover/ae6c436f",
                    previewImageIdPattern));
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(
                    "20261019-223004-tfg4to/daisy-cover/ae6c436fz",
                    previewImageIdPattern));
            }

            var settingsAskUserDefinition = RuntimeBuiltInTools.GetAllDefinitions()
                .FirstOrDefault(definition => definition.Name == DefaultToolDispatcher.RuntimeAskUserQuestionName);
            Assert.IsNotNull(settingsAskUserDefinition);
            using (var settingsAskUserSchema = JsonDocument.Parse(settingsAskUserDefinition.InputSchemaJson))
            {
                var settingsPreviewImageIdPattern = settingsAskUserSchema.RootElement
                    .GetProperty("properties").GetProperty("questions")
                    .GetProperty("items").GetProperty("properties")
                    .GetProperty("options").GetProperty("items").GetProperty("properties")
                    .GetProperty("previewImageId").GetProperty("pattern").GetString();
                Assert.AreEqual(previewImageIdPattern, settingsPreviewImageIdPattern);
            }
            Assert.IsTrue(catalog.ContainsKey(DefaultToolDispatcher.RuntimeDeclareExecutionModeName));
        }

        [TestMethod]
        public void ExecutionModeSchemaPlacesNestedConstraintsOnObjectSchemasAndRuntimeValidatesClarifications()
        {
            var catalog = DefaultToolDispatcher.BuildToolCatalog(new HostBridgeCapabilities
            {
                SupportsWriteEffectPreflight = true
            });
            var schema = JsonDocument.Parse(
                catalog[DefaultToolDispatcher.RuntimeDeclareExecutionModeName].InputSchemaJson);

            using (schema)
            {
                var root = schema.RootElement;
                Assert.IsFalse(root.GetProperty("additionalProperties").GetBoolean());
                Assert.AreEqual(
                    "candidates",
                    root.GetProperty("required")[0].GetString());

                var candidatesItem = root.GetProperty("properties")
                    .GetProperty("candidates")
                    .GetProperty("items");
                Assert.IsFalse(candidatesItem.GetProperty("additionalProperties").GetBoolean());
                Assert.AreEqual(2, candidatesItem.GetProperty("required").GetArrayLength());
                Assert.IsFalse(candidatesItem.GetProperty("properties").TryGetProperty("required", out _));

                var clarificationItem = root.GetProperty("properties")
                    .GetProperty("clarifications")
                    .GetProperty("items");
                Assert.IsFalse(clarificationItem.GetProperty("additionalProperties").GetBoolean());
                Assert.AreEqual(3, clarificationItem.GetProperty("required").GetArrayLength());
                Assert.IsFalse(clarificationItem.GetProperty("properties").TryGetProperty("required", out _));

                var optionItem = clarificationItem.GetProperty("properties")
                    .GetProperty("options")
                    .GetProperty("items");
                Assert.IsFalse(optionItem.GetProperty("additionalProperties").GetBoolean());
                Assert.AreEqual("label", optionItem.GetProperty("required")[0].GetString());
                Assert.IsFalse(optionItem.GetProperty("properties").TryGetProperty("required", out _));
            }

            Assert.IsFalse(TurnExecutionContract.TryParseClarifications(
                "{\"clarifications\":[{\"header\":\"统计口径\",\"options\":[]}]}",
                out _,
                out var parseError));
            StringAssert.Contains(parseError, "必须包含 question、header 和 options");
        }

        [TestMethod]
        public async Task SingleCandidateDeclarationStatesHostPreflightHasNotRun()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Modify);
            var dispatcher = CreateDispatcher(invoker, new RecordingInteractionHandler());
            dispatcher.CurrentExecutionContract = new TurnExecutionContract();
            var declaration = DefaultToolDispatcher.BuildToolCatalog(new HostBridgeCapabilities
            {
                SupportsWriteEffectPreflight = true
            })[DefaultToolDispatcher.RuntimeDeclareExecutionModeName];
            var catalog = new Dictionary<string, HostBridgeToolDescriptor>
            {
                [declaration.Name] = declaration
            };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "declare-preview",
                declaration.Name,
                "{\"candidates\":[{\"mode\":\"modify_in_place\",\"targets\":[\"Monthly!P1\"],"
                    + "\"summary\":\"在 P1 写入标记。\"}]}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
            StringAssert.Contains(outcome.Content, "尚未触发 HostBridge 写入预检");
            StringAssert.Contains(outcome.Content, "实际 write effect 只能由后续写入工具的宿主预检确定");
            Assert.AreEqual(0, invoker.Invocations.Count);
            Assert.IsFalse(dispatcher.CurrentExecutionContract.IsUserSelected);
        }

        [TestMethod]
        public async Task Append_ConfirmsOncePerTargetAndDisplaysOperationSummary()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append);
            invoker.InvocationResult = new HostToolInvocationResult
            {
                Success = true,
                Content = "written",
                StructuredContentJson = "{\"writtenCellCount\":1}"
            };
            var interaction = new RecordingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            var tool = CreateTool(requiresConfirmation: true);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };
            var longArgumentsJson = "{\"payload\":\"" + new string('x', 300) + "\"}";

            var first = await dispatcher.DispatchToolCallAsync(
                "call-1", tool.Name, longArgumentsJson, new HostContextSnapshotDto(), catalog, null, CancellationToken.None);
            var second = await dispatcher.DispatchToolCallAsync(
                "call-2", tool.Name, "{}", new HostContextSnapshotDto(), catalog, null, CancellationToken.None);

            Assert.IsTrue(first.Success);
            Assert.IsTrue(second.Success);
            Assert.AreEqual(1, interaction.Requests.Count);
            Assert.AreEqual(2, invoker.Invocations.Count);
            var interactionData = JsonDocument.Parse(interaction.Requests[0].InteractionDataJson).RootElement;
            var operationSummary = interactionData.GetProperty("operationSummary").GetString();
            StringAssert.Contains(operationSummary, "宿主实测 write effect：`append`");
            StringAssert.Contains(operationSummary, "Data!A2:B3");
            StringAssert.Contains(operationSummary, "Append rows to the target table.");
            Assert.AreEqual(longArgumentsJson, interactionData.GetProperty("argumentsJson").GetString());
            Assert.AreEqual(
                longArgumentsJson.Substring(0, 197) + "...",
                interactionData.GetProperty("argumentsSummary").GetString());
            Assert.AreEqual(
                invoker.PreflightResult.PreflightToken,
                invoker.Invocations[0].Metadata[HostBridgeMetadataKeys.WriteEffectPreflightToken]);
            Assert.AreEqual(
                "conversation-1",
                invoker.Invocations[0].Metadata[ToolMetadataKeys.ConversationId]);
            Assert.AreEqual(
                HostBridgeMetadataKeys.TrueValue,
                invoker.Invocations[0].Metadata[HostBridgeMetadataKeys.ConfirmationGranted]);
            using (var structuredResult = JsonDocument.Parse(first.StructuredContentJson))
            {
                Assert.AreEqual(
                    1,
                    structuredResult.RootElement.GetProperty("writtenCellCount").GetInt32());
                var writeEffect = structuredResult.RootElement.GetProperty("writeEffect");
                Assert.AreEqual("append", writeEffect.GetProperty("actualWriteEffect").GetString());
                Assert.IsTrue(writeEffect.GetProperty("writeSucceeded").GetBoolean());
                Assert.AreEqual("Data", writeEffect.GetProperty("targetWorksheetName").GetString());
                Assert.AreEqual("A2:B3", writeEffect.GetProperty("targetAddress").GetString());
                Assert.AreEqual("approved", writeEffect.GetProperty("confirmationStatus").GetString());
            }
            StringAssert.Contains(first.Content, "宿主实测 write effect：`append`");
        }

        [TestMethod]
        public async Task CancelledModifyConfirmationReturnsMeasuredEffectAndDoesNotInvokeWrite()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Modify);
            var interaction = new CancellingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            var tool = CreateTool(requiresConfirmation: false);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "cancel-modify",
                tool.Name,
                "{}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("permission.denied_by_user", outcome.ErrorCode);
            Assert.AreEqual(0, invoker.Invocations.Count);
            Assert.AreEqual(1, interaction.Requests.Count);
            using (var interactionData = JsonDocument.Parse(interaction.Requests[0].InteractionDataJson))
            {
                StringAssert.Contains(
                    interactionData.RootElement.GetProperty("operationSummary").GetString(),
                    "宿主实测 write effect：`modify`");
            }
            using (var structuredResult = JsonDocument.Parse(outcome.StructuredContentJson))
            {
                var writeEffect = structuredResult.RootElement.GetProperty("writeEffect");
                Assert.AreEqual("modify", writeEffect.GetProperty("actualWriteEffect").GetString());
                Assert.IsFalse(writeEffect.GetProperty("writeSucceeded").GetBoolean());
                Assert.AreEqual("denied", writeEffect.GetProperty("confirmationStatus").GetString());
            }
            StringAssert.Contains(outcome.Content, "确认状态：denied");
        }

        [TestMethod]
        public async Task Modify_RequiresConfirmationInAcceptEditsEvenWhenToolMetadataDoesNot()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Modify);
            var interaction = new RecordingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            dispatcher.SetPermissionMode(PermissionMode.AcceptEdits);
            var tool = CreateTool(requiresConfirmation: false);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1", tool.Name, "{}", new HostContextSnapshotDto(), catalog, null, CancellationToken.None);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(1, interaction.Requests.Count);
            Assert.AreEqual(1, invoker.Invocations.Count);
        }

        [TestMethod]
        public async Task Append_DoesNotConfirmWhenToolMetadataDisablesIt()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append);
            var interaction = new RecordingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            var tool = CreateTool(requiresConfirmation: false);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1", tool.Name, "{}", new HostContextSnapshotDto(), catalog, null, CancellationToken.None);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(0, interaction.Requests.Count);
            Assert.AreEqual(1, invoker.Invocations.Count);
        }

        [TestMethod]
        public async Task Append_AcceptEditsAndBypassModesDoNotConfirm()
        {
            foreach (var mode in new[] { PermissionMode.AcceptEdits, PermissionMode.BypassPermissions })
            {
                var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append);
                var interaction = new RecordingInteractionHandler();
                var dispatcher = CreateDispatcher(invoker, interaction);
                dispatcher.SetPermissionMode(mode);
                var tool = CreateTool(requiresConfirmation: true);
                var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

                var outcome = await dispatcher.DispatchToolCallAsync(
                    "append-" + mode,
                    tool.Name,
                    "{}",
                    new HostContextSnapshotDto(),
                    catalog,
                    null,
                    CancellationToken.None);

                Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
                Assert.AreEqual(0, interaction.Requests.Count, mode.ToString());
                Assert.AreEqual(1, invoker.Invocations.Count, mode.ToString());
                Assert.AreEqual(
                    HostBridgeMetadataKeys.TrueValue,
                    invoker.Invocations[0].Metadata[HostBridgeMetadataKeys.ConfirmationGranted],
                    mode.ToString());
            }
        }

        [TestMethod]
        public async Task NoneAndScratchEffects_DoNotConfirmInAnyPermissionMode()
        {
            foreach (var effect in new[] { ToolWriteEffectLevels.None, ToolWriteEffectLevels.Scratch })
            {
                foreach (var mode in new[]
                {
                    PermissionMode.Default,
                    PermissionMode.AcceptEdits,
                    PermissionMode.BypassPermissions
                })
                {
                    var invoker = new FakePreflightInvoker(effect);
                    var interaction = new RecordingInteractionHandler();
                    var dispatcher = CreateDispatcher(invoker, interaction);
                    dispatcher.SetPermissionMode(mode);
                    var tool = CreateTool(requiresConfirmation: true);
                    var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

                    var outcome = await dispatcher.DispatchToolCallAsync(
                        effect + "-" + mode,
                        tool.Name,
                        "{}",
                        new HostContextSnapshotDto(),
                        catalog,
                        null,
                        CancellationToken.None);

                    Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
                    Assert.AreEqual(0, interaction.Requests.Count, effect + " / " + mode);
                    Assert.AreEqual(1, invoker.Invocations.Count, effect + " / " + mode);
                }
            }
        }

        [TestMethod]
        public async Task Modify_BypassModeDoesNotConfirm()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Modify);
            var interaction = new RecordingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            dispatcher.SetPermissionMode(PermissionMode.BypassPermissions);
            dispatcher.CurrentExecutionContract = new ExecutionModeCandidate
            {
                Mode = "modify_in_place",
                MaxWriteEffect = ToolWriteEffectLevels.Modify,
                Targets = new List<string> { "Data" },
                Summary = "修改目标区域。"
            }.ToContract(userSelected: false, inherited: false);
            var tool = CreateTool(requiresConfirmation: false);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "modify-bypass",
                tool.Name,
                "{}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
            Assert.AreEqual(0, interaction.Requests.Count);
            Assert.AreEqual(1, invoker.Invocations.Count);
        }

        [TestMethod]
        public async Task Append_ExceedingResultOnlyPreviewForcesConfirmation()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append);
            var interaction = new RecordingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            var writeEffectEvents = new List<AgentBrokerEvent>();
            dispatcher.CurrentWriteEffectEventEmitter = (brokerEvent, cancellationToken) =>
            {
                writeEffectEvents.Add(brokerEvent);
                return Task.CompletedTask;
            };
            dispatcher.CurrentExecutionContract = new ExecutionModeCandidate
            {
                Mode = "result_only",
                MaxWriteEffect = ToolWriteEffectLevels.Scratch,
                Summary = "只在对话中返回结果。"
            }.ToContract(userSelected: false, inherited: false);
            var tool = CreateTool(requiresConfirmation: false);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1", tool.Name, "{}", new HostContextSnapshotDto(), catalog, null, CancellationToken.None);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(1, interaction.Requests.Count);
            Assert.AreEqual(1, invoker.Invocations.Count);
            Assert.AreEqual(1, writeEffectEvents.Count);
            Assert.AreEqual(ToolWriteEffectLevels.Scratch, writeEffectEvents[0].ExpectedWriteEffect);
            Assert.AreEqual(ToolWriteEffectLevels.Append, writeEffectEvents[0].ActualWriteEffect);
            Assert.IsTrue(writeEffectEvents[0].ExecutionModeDeclared);
            Assert.IsFalse(writeEffectEvents[0].ExecutionModeUserSelected);
            Assert.IsTrue(writeEffectEvents[0].ExceededExecutionModePreview);
            StringAssert.Contains(
                JsonDocument.Parse(interaction.Requests[0].InteractionDataJson)
                    .RootElement.GetProperty("operationSummary").GetString(),
                "超出预告");
        }

        [TestMethod]
        public async Task AcceptEditsAndBypass_ExceedingExecutionPreviewStillRequiresConfirmation()
        {
            foreach (var mode in new[] { PermissionMode.AcceptEdits, PermissionMode.BypassPermissions })
            {
                var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append);
                var interaction = new RecordingInteractionHandler();
                var dispatcher = CreateDispatcher(invoker, interaction);
                dispatcher.SetPermissionMode(mode);
                dispatcher.CurrentExecutionContract = new ExecutionModeCandidate
                {
                    Mode = "result_only",
                    MaxWriteEffect = ToolWriteEffectLevels.Scratch,
                    Summary = "只在对话中返回结果。"
                }.ToContract(userSelected: false, inherited: false);
                var tool = CreateTool(requiresConfirmation: false);
                var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

                var outcome = await dispatcher.DispatchToolCallAsync(
                    "append-over-preview-" + mode,
                    tool.Name,
                    "{}",
                    new HostContextSnapshotDto(),
                    catalog,
                    null,
                    CancellationToken.None);

                Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
                Assert.AreEqual(1, interaction.Requests.Count, mode.ToString());
                Assert.AreEqual(1, invoker.Invocations.Count, mode.ToString());
                using (var interactionData = JsonDocument.Parse(interaction.Requests[0].InteractionDataJson))
                {
                    StringAssert.Contains(
                        interactionData.RootElement.GetProperty("operationSummary").GetString(),
                        "超出预告");
                }
            }
        }

        [TestMethod]
        public async Task WriteWithoutExecutionModeDeclaration_IsBlockedBeforeConfirmation()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append);
            var interaction = new RecordingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            dispatcher.CurrentExecutionContract = new TurnExecutionContract();
            var tool = CreateTool(requiresConfirmation: true);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1", tool.Name, "{}", new HostContextSnapshotDto(), catalog, null, CancellationToken.None);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("EXECUTION_MODE_UNDECLARED", outcome.ErrorCode);
            AssertWriteGateErrorFormat(outcome.ErrorMessage, "EXECUTION_MODE_UNDECLARED");
            StringAssert.Contains(outcome.ErrorMessage, "runtime.declare_execution_mode");
            Assert.AreEqual(0, interaction.Requests.Count);
            Assert.AreEqual(0, invoker.Invocations.Count);
        }

        [TestMethod]
        public async Task FailedWritePreflightIncludesReadOnlyRecoveryPath()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append)
            {
                PreflightResult = new PreflightToolCallResult
                {
                    Success = false,
                    ErrorCode = "excel.write_effect_preflight_failed",
                    ErrorMessage = "宿主无法读取当前写入目标。"
                }
            };
            var interaction = new RecordingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            var tool = CreateTool(requiresConfirmation: true);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1", tool.Name, "{}", new HostContextSnapshotDto(), catalog, null, CancellationToken.None);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("excel.write_effect_preflight_failed", outcome.ErrorCode);
            AssertWriteGateErrorFormat(outcome.ErrorMessage, "excel.write_effect_preflight_failed");
            StringAssert.Contains(outcome.ErrorMessage, "runtime.ask_user_question");
            Assert.AreEqual(0, interaction.Requests.Count);
            Assert.AreEqual(0, invoker.Invocations.Count);
        }

        [TestMethod]
        public async Task RecheckEscalationIncludesUserConfirmationRecoveryPath()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append)
            {
                InvocationResult = new HostToolInvocationResult
                {
                    Success = false,
                    ErrorCode = "WRITE_EFFECT_ESCALATED",
                    ErrorMessage = "预检后目标发生变化；本次执行已阻止。"
                }
            };
            var interaction = new RecordingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            var tool = CreateTool(requiresConfirmation: false);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [tool.Name] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1", tool.Name, "{}", new HostContextSnapshotDto(), catalog, null, CancellationToken.None);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("WRITE_EFFECT_ESCALATED", outcome.ErrorCode);
            AssertWriteGateErrorFormat(outcome.ErrorMessage, "WRITE_EFFECT_ESCALATED");
            StringAssert.Contains(outcome.ErrorMessage, "runtime.ask_user_question");
            Assert.AreEqual(1, invoker.Invocations.Count);
        }

        [TestMethod]
        public async Task UserSelectedExecutionMode_BlocksDifferentTargetAndPersistsSessionPreference()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Modify);
            var interaction = new SelectingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            dispatcher.CurrentExecutionContract = new TurnExecutionContract();
            dispatcher.CurrentConversationMetadata = new Dictionary<string, string>();
            dispatcher.CurrentUserInteractionEmitter = (request, cancellationToken) => Task.CompletedTask;
            var declaration = new HostBridgeToolDescriptor
            {
                Name = DefaultToolDispatcher.RuntimeDeclareExecutionModeName,
                IsReadOnly = true
            };
            var writeTool = CreateTool(requiresConfirmation: true);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor>
            {
                [declaration.Name] = declaration,
                [writeTool.Name] = writeTool
            };
            const string declarationArguments = "{\"candidates\":["
                + "{\"mode\":\"new_sheet_static\",\"targets\":[\"Summary\"],\"sources\":[\"Data\"],\"summary\":\"生成汇总表。\"},"
                + "{\"mode\":\"modify_in_place\",\"targets\":[\"Data\"],\"sources\":[\"Data\"],\"summary\":\"直接修改明细。\"}]}";

            var selected = await dispatcher.DispatchToolCallAsync(
                "declare-1",
                declaration.Name,
                declarationArguments,
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);
            var write = await dispatcher.DispatchToolCallAsync(
                "write-1",
                writeTool.Name,
                "{}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsTrue(selected.Success, selected.ErrorMessage);
            Assert.IsTrue(dispatcher.CurrentExecutionContract.IsUserSelected);
            Assert.AreEqual("new_sheet_static", dispatcher.CurrentExecutionContract.Mode);
            Assert.IsTrue(dispatcher.CurrentConversationMetadata.ContainsKey(
                TurnExecutionContract.SessionPreferenceMetadataKey));
            Assert.IsFalse(write.Success);
            Assert.AreEqual("EXECUTION_MODE_EXCEEDED", write.ErrorCode);
            AssertWriteGateErrorFormat(write.ErrorMessage, "EXECUTION_MODE_EXCEEDED");
            StringAssert.Contains(write.ErrorMessage, "runtime.ask_user_question");
            Assert.AreEqual(0, invoker.Invocations.Count);
        }

        [TestMethod]
        public async Task ModelSingleCandidateCannotReplaceAUserSelectedHardLimit()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append);
            var interaction = new RecordingInteractionHandler();
            var dispatcher = CreateDispatcher(invoker, interaction);
            dispatcher.CurrentExecutionContract = new ExecutionModeCandidate
            {
                Mode = "new_sheet_static",
                MaxWriteEffect = ToolWriteEffectLevels.Append,
                Targets = new List<string> { "Summary!A1:F200" },
                Summary = "用户选择生成新表。"
            }.ToContract(userSelected: true, inherited: false);
            var declaration = DefaultToolDispatcher.BuildToolCatalog(new HostBridgeCapabilities
            {
                SupportsWriteEffectPreflight = true
            })[DefaultToolDispatcher.RuntimeDeclareExecutionModeName];
            var catalog = new Dictionary<string, HostBridgeToolDescriptor>
            {
                [declaration.Name] = declaration
            };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "declare-2",
                declaration.Name,
                "{\"candidates\":[{\"mode\":\"modify_in_place\",\"targets\":[\"Data\"],\"summary\":\"修改原表。\"}]}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("execution_mode.user_choice_locked", outcome.ErrorCode);
            Assert.AreEqual("new_sheet_static", dispatcher.CurrentExecutionContract.Mode);
            Assert.IsTrue(dispatcher.CurrentExecutionContract.AllowsTarget("Summary", "A2:B3"));
            Assert.IsFalse(dispatcher.CurrentExecutionContract.AllowsTarget("Data", "A2:B3"));
        }

        [TestMethod]
        public async Task SingleCandidateWithClarification_AsksOnceAndReturnsTheUserAnswer()
        {
            var interaction = new SelectingInteractionHandler();
            var dispatcher = CreateDispatcher(new FakePreflightInvoker(ToolWriteEffectLevels.Append), interaction);
            dispatcher.CurrentExecutionContract = new TurnExecutionContract();
            dispatcher.CurrentUserInteractionEmitter = (request, cancellationToken) => Task.CompletedTask;
            var declaration = DefaultToolDispatcher.BuildToolCatalog(new HostBridgeCapabilities
            {
                SupportsWriteEffectPreflight = true
            })[DefaultToolDispatcher.RuntimeDeclareExecutionModeName];
            var catalog = new Dictionary<string, HostBridgeToolDescriptor>
            {
                [declaration.Name] = declaration
            };
            const string arguments = "{\"candidates\":[{\"mode\":\"new_sheet_static\",\"targets\":[\"Summary\"],"
                + "\"sources\":[\"Orders\"],\"summary\":\"按订单生成汇总表。\"}],"
                + "\"clarifications\":[{\"question\":\"按哪个日期字段统计？\",\"header\":\"统计口径\","
                + "\"options\":[{\"label\":\"下单日期\"},{\"label\":\"付款日期\"}]}]}";

            var outcome = await dispatcher.DispatchToolCallAsync(
                "declare-clarification",
                declaration.Name,
                arguments,
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsTrue(outcome.Success, outcome.ErrorMessage);
            Assert.AreEqual(1, interaction.Requests.Count);
            using (var document = JsonDocument.Parse(interaction.Requests[0].InteractionDataJson))
            {
                var questions = document.RootElement.GetProperty("questions");
                Assert.AreEqual(3, questions.GetArrayLength());
                Assert.AreEqual("统计口径", questions[1].GetProperty("header").GetString());
            }
            StringAssert.Contains(outcome.Content, "用户口径澄清答复");
            StringAssert.Contains(outcome.Content, "按哪个日期字段统计？：下单日期");
            Assert.IsTrue(dispatcher.CurrentExecutionContract.IsUserSelected);
        }

        [TestMethod]
        public async Task MissingRequiredClarificationAnswer_BlocksSubsequentWorkbookWrites()
        {
            var invoker = new FakePreflightInvoker(ToolWriteEffectLevels.Append);
            var dispatcher = CreateDispatcher(invoker, new CancellingInteractionHandler());
            dispatcher.CurrentExecutionContract = new TurnExecutionContract();
            dispatcher.CurrentUserInteractionEmitter = (request, cancellationToken) => Task.CompletedTask;
            var declaration = DefaultToolDispatcher.BuildToolCatalog(new HostBridgeCapabilities
            {
                SupportsWriteEffectPreflight = true
            })[DefaultToolDispatcher.RuntimeDeclareExecutionModeName];
            var writeTool = CreateTool(requiresConfirmation: false);
            var catalog = new Dictionary<string, HostBridgeToolDescriptor>
            {
                [declaration.Name] = declaration,
                [writeTool.Name] = writeTool
            };
            const string arguments = "{\"candidates\":[{\"mode\":\"new_sheet_static\",\"targets\":[\"Summary\"],"
                + "\"summary\":\"按订单生成汇总表。\"}],"
                + "\"clarifications\":[{\"question\":\"按哪个日期字段统计？\",\"header\":\"统计口径\","
                + "\"options\":[{\"label\":\"下单日期\"},{\"label\":\"付款日期\"}]}]}";

            var declarationOutcome = await dispatcher.DispatchToolCallAsync(
                "declare-cancelled",
                declaration.Name,
                arguments,
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);
            var writeOutcome = await dispatcher.DispatchToolCallAsync(
                "write-after-cancel",
                writeTool.Name,
                "{}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsFalse(declarationOutcome.Success);
            Assert.AreEqual("execution_mode.clarification_required", declarationOutcome.ErrorCode);
            Assert.IsFalse(writeOutcome.Success);
            Assert.AreEqual("EXECUTION_MODE_UNDECLARED", writeOutcome.ErrorCode);
            Assert.AreEqual(0, invoker.Invocations.Count);
        }

        private static DefaultToolDispatcher CreateDispatcher(
            FakePreflightInvoker invoker,
            IUserInteractionHandler interaction)
        {
            return new DefaultToolDispatcher(invoker, null, null, null, null, interaction)
            {
                CurrentWriteEffectBaselineReady = true,
                CurrentConversationId = "conversation-1",
                CurrentTurnMessageId = "message-1",
                CurrentConfirmedAppendScopes = new System.Collections.Generic.HashSet<string>(),
                CurrentExecutionContract = new ExecutionModeCandidate
                {
                    Mode = "new_sheet_static",
                    MaxWriteEffect = ToolWriteEffectLevels.Append,
                    Targets = new List<string> { "Data" },
                    Summary = "追加到目标表。"
                }.ToContract(userSelected: false, inherited: false)
            };
        }

        private static void AssertWriteGateErrorFormat(string message, string code)
        {
            StringAssert.Contains(message, "[代码] " + code);
            StringAssert.Contains(message, "[原因]");
            StringAssert.Contains(message, "[约束]");
            StringAssert.Contains(message, "[出路]");
        }

        private static HostBridgeToolDescriptor CreateTool(bool requiresConfirmation)
        {
            return new HostBridgeToolDescriptor
            {
                Name = "excel_write_range",
                DisplayName = "Write range",
                Description = "Write cell values.",
                InputSchemaJson = "{\"type\":\"object\"}",
                IsReadOnly = false,
                RequiresConfirmation = requiresConfirmation,
                MaxWriteEffect = ToolWriteEffectLevels.Modify,
                RequiresWriteEffectPreflight = true
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

        private sealed class SelectingInteractionHandler : IUserInteractionHandler
        {
            public List<AgentBrokerToolUserInteractionRequest> Requests { get; } =
                new List<AgentBrokerToolUserInteractionRequest>();

            public Task<AgentBrokerToolUserInteractionResponse> RequestUserInteractionAsync(
                AgentBrokerToolUserInteractionRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                using (var document = JsonDocument.Parse(request.InteractionDataJson))
                {
                    var questions = document.RootElement.GetProperty("questions");
                    var answers = new Dictionary<string, string>();
                    foreach (var question in questions.EnumerateArray())
                    {
                        var questionText = question.GetProperty("question").GetString();
                        var header = question.GetProperty("header").GetString();
                        var options = question.GetProperty("options");
                        answers[questionText] = string.Equals(header, "应用范围", System.StringComparison.Ordinal)
                            ? "本会话都按这个方式"
                            : options[0].GetProperty("label").GetString();
                    }

                    return Task.FromResult(new AgentBrokerToolUserInteractionResponse
                    {
                        ToolCallId = request.ToolCallId,
                        AnswersJson = System.Text.Json.JsonSerializer.Serialize(answers)
                    });
                }
            }
        }

        private sealed class CancellingInteractionHandler : IUserInteractionHandler
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
                    ToolCallId = request.ToolCallId,
                    Cancelled = true
                });
            }
        }

        private sealed class FakePreflightInvoker : IHostToolInvoker, IHostToolPreflightInvoker
        {
            public FakePreflightInvoker(string effect)
            {
                PreflightResult = new PreflightToolCallResult
                {
                    Success = true,
                    Effect = effect,
                    OperationSummary = "Append rows to the target table.",
                    TargetWorkbookFullName = "C:\\workbooks\\book.xlsx",
                    TargetWorksheetName = "Data",
                    TargetAddress = "A2:B3",
                    PreflightToken = "preflight-token"
                };
            }

            public PreflightToolCallResult PreflightResult { get; set; }

            public HostToolInvocationResult InvocationResult { get; set; }

            public List<HostToolInvocationRequest> Invocations { get; } =
                new List<HostToolInvocationRequest>();

            public Task<PreflightToolCallResult> PreflightToolCallAsync(
                PreflightToolCallRequest request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(PreflightResult);
            }

            public Task<HostToolInvocationResult> InvokeAsync(
                HostToolInvocationRequest request,
                CancellationToken cancellationToken)
            {
                Invocations.Add(request);
                return Task.FromResult(InvocationResult ?? new HostToolInvocationResult
                {
                    ToolCallId = request.ToolCallId,
                    ToolName = request.ToolName,
                    Success = true,
                    Content = "written"
                });
            }
        }
    }
}
