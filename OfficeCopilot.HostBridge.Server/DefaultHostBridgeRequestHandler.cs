using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.HostBridge.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OfficeCopilot.HostBridge.Server
{
    public sealed class DefaultHostBridgeRequestHandler : IHostBridgeRequestHandler
    {
        private readonly ICopilotHostAdapter _hostAdapter;
        private readonly IToolRegistry _toolRegistry;
        private readonly IServiceProvider _services;
        private readonly IVbaExecutionHostAdapter _vbaExecutionHostAdapter;
        private readonly IDocumentBackupHostAdapter _documentBackupHostAdapter;
        private readonly object _writeEffectPreflightSync = new object();
        private readonly Dictionary<string, WriteEffectPreflightTicket> _writeEffectPreflightTickets =
            new Dictionary<string, WriteEffectPreflightTicket>(StringComparer.OrdinalIgnoreCase);
        private readonly object _scopeViolationConfirmationSync = new object();
        private readonly HashSet<string> _scopeViolationConfirmationConversationIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _deckBuildWorkspaceSync = new object();
        private readonly Dictionary<string, DeckBuildWorkspaceState> _deckBuildWorkspaceByConversation =
            new Dictionary<string, DeckBuildWorkspaceState>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _sharedBackupSyncRoot = new object();
        private static readonly Dictionary<string, string> _sharedBackupPathByMessage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public DefaultHostBridgeRequestHandler(
            ICopilotHostAdapter hostAdapter,
            IToolRegistry toolRegistry)
            : this(hostAdapter, toolRegistry, null)
        {
        }

        public DefaultHostBridgeRequestHandler(
            ICopilotHostAdapter hostAdapter,
            IToolRegistry toolRegistry,
            IServiceProvider services)
        {
            _hostAdapter = hostAdapter;
            _toolRegistry = toolRegistry;
            _services = services;
            _vbaExecutionHostAdapter = hostAdapter as IVbaExecutionHostAdapter;
            _documentBackupHostAdapter = hostAdapter as IDocumentBackupHostAdapter;
        }

        public async Task<HostBridgeResponse> HandleAsync(HostBridgeRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new HostBridgeRequest();
            var operation = request.Operation ?? string.Empty;

            var response = await RouteAsync(request, operation, cancellationToken).ConfigureAwait(false);
            if (response != null && string.IsNullOrEmpty(response.Operation))
            {
                response.Operation = operation;
            }

            return response;
        }

        private async Task<HostBridgeResponse> RouteAsync(HostBridgeRequest request, string operation, CancellationToken cancellationToken)
        {
            if (string.Equals(operation, HostBridgeOperationNames.GetCapabilities, StringComparison.OrdinalIgnoreCase))
            {
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(BuildCapabilities()));
            }

            if (string.Equals(operation, HostBridgeOperationNames.GetContextSnapshot, StringComparison.OrdinalIgnoreCase))
            {
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(BuildContextSnapshotDto()));
            }

            if (string.Equals(operation, HostBridgeOperationNames.BeginWriteEffectTurn, StringComparison.OrdinalIgnoreCase))
            {
                return await HandleBeginWriteEffectTurnAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(operation, HostBridgeOperationNames.PreflightToolCall, StringComparison.OrdinalIgnoreCase))
            {
                return await HandlePreflightToolCallAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(operation, HostBridgeOperationNames.InsertText, StringComparison.OrdinalIgnoreCase))
            {
                return HandleInsertText(request);
            }

            if (string.Equals(operation, HostBridgeOperationNames.ExecuteCode, StringComparison.OrdinalIgnoreCase))
            {
                return HandleExecuteCode(request);
            }

            if (string.Equals(operation, HostBridgeOperationNames.InvokeTool, StringComparison.OrdinalIgnoreCase))
            {
                return await HandleInvokeToolAsync(request, cancellationToken).ConfigureAwait(false);
            }

            return HostBridgeResponse.CreateFailure(
                request.RequestId,
                HostBridgeErrorCodes.OperationUnsupported,
                "Unsupported host bridge operation.");
        }

        private HostBridgeCapabilities BuildCapabilities()
        {
            var supportsWriteEffectPreflight = _hostAdapter is IExcelWriteEffectHostAdapter;
            var capabilities = new HostBridgeCapabilities
            {
                HostApplicationName = _hostAdapter == null ? string.Empty : _hostAdapter.HostApplicationName ?? string.Empty,
                HostVersion = string.Empty,
                CanInsertText = _hostAdapter != null,
                CanExecuteCode = _vbaExecutionHostAdapter != null && _vbaExecutionHostAdapter.CanExecuteVba,
                CanCreateDocumentBackup = _documentBackupHostAdapter != null,
                SupportsWriteEffectPreflight = supportsWriteEffectPreflight,
                ToolNames = _toolRegistry == null
                    ? new List<string>()
                    : _toolRegistry.GetDefinitions().Select(d => d.Name ?? string.Empty).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
                Tools = _toolRegistry == null
                    ? new List<HostBridgeToolDescriptor>()
                    : _toolRegistry.GetDefinitions()
                        .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Name))
                        .Select(d => new HostBridgeToolDescriptor
                        {
                            Name = d.Name ?? string.Empty,
                            DisplayName = d.DisplayName ?? string.Empty,
                            Description = d.Description ?? string.Empty,
                            InputSchemaJson = string.IsNullOrWhiteSpace(d.InputSchemaJson) ? "{}" : d.InputSchemaJson,
                            IsReadOnly = d.IsReadOnly,
                            RequiresConfirmation = d.RequiresConfirmation,
                            RequiresBackup = d.RequiresBackup,
                            RiskLevel = string.IsNullOrWhiteSpace(d.RiskLevel) ? "low" : d.RiskLevel,
                            MaxWriteEffect = d.MaxWriteEffect ?? string.Empty,
                            RequiresWriteEffectPreflight = supportsWriteEffectPreflight && !d.IsReadOnly,
                            TimeoutSeconds = d.TimeoutSeconds
                        })
                        .ToList()
            };

            if (capabilities.CanExecuteCode && _vbaExecutionHostAdapter != null)
            {
                var availability = _vbaExecutionHostAdapter.GetVbaExecutionAvailability(false);
                capabilities.Metadata["vba.reason"] = availability == null ? string.Empty : availability.Reason ?? string.Empty;
            }

            return capabilities;
        }

        private async Task<HostBridgeResponse> HandleBeginWriteEffectTurnAsync(
            HostBridgeRequest request,
            CancellationToken cancellationToken)
        {
            var adapter = _hostAdapter as IExcelWriteEffectHostAdapter;
            WriteEffectTurnRequest payload;
            var result = new WriteEffectTurnResult();
            try
            {
                payload = JsonConvert.DeserializeObject<WriteEffectTurnRequest>(request.PayloadJson ?? "{}")
                    ?? new WriteEffectTurnRequest();
            }
            catch (JsonException ex)
            {
                result.ErrorCode = HostBridgeErrorCodes.InvalidPayload;
                result.ErrorMessage = ex.Message;
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
            }

            ClearScopeViolationConfirmation(payload.ConversationId);

            if (adapter == null)
            {
                result.ErrorCode = "excel.write_effect_preflight_unavailable";
                result.ErrorMessage = "当前宿主不支持 Excel 写入影响基线。";
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
            }

            var callPolicy = _services == null
                ? null
                : _services.GetService(typeof(IHostCallPolicy)) as IHostCallPolicy;
            if (callPolicy == null)
            {
                result.ErrorCode = "excel.write_effect_dispatcher_unavailable";
                result.ErrorMessage = "无法在 Excel 所属线程建立本轮写入影响基线。";
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
            }

            var context = new ToolExecutionContext
            {
                ConversationId = payload.ConversationId ?? string.Empty,
                MessageId = payload.MessageId ?? string.Empty,
                HostApplicationName = _hostAdapter.HostApplicationName ?? string.Empty,
                Services = _services,
                CancellationToken = cancellationToken
            };
            var executionRequest = new ToolExecutionRequest
            {
                ToolCallId = payload.MessageId ?? string.Empty,
                ToolName = "runtime.begin_write_effect_turn",
                ArgumentsJson = request.PayloadJson ?? "{}"
            };
            var definition = new ToolDefinition
            {
                Name = executionRequest.ToolName,
                IsReadOnly = true,
                MaxWriteEffect = ToolWriteEffectLevels.None
            };

            try
            {
                ExcelWriteEffectTurnResult hostResult = null;
                var dispatchResult = await callPolicy.ExecuteAsync(
                    definition,
                    context,
                    executionRequest,
                    () =>
                    {
                        hostResult = adapter.BeginWriteEffectTurn(
                            payload.ConversationId ?? string.Empty,
                            payload.MessageId ?? string.Empty);
                        return Task.FromResult(ToolExecutionResult.CreateSuccess(
                            executionRequest.ToolName,
                            executionRequest.ToolCallId,
                            "已建立本轮工作簿写入影响基线。"));
                    }).ConfigureAwait(false);

                if (dispatchResult == null || !dispatchResult.Success)
                {
                    result.ErrorCode = dispatchResult == null
                        ? "excel.write_effect_baseline_dispatch_failed"
                        : dispatchResult.ErrorCode ?? "excel.write_effect_baseline_dispatch_failed";
                    result.ErrorMessage = dispatchResult == null
                        ? "基线调度没有返回结果。"
                        : dispatchResult.ErrorMessage ?? dispatchResult.Content ?? "基线调度失败。";
                }
                else if (hostResult == null)
                {
                    result.ErrorCode = "excel.write_effect_baseline_failed";
                    result.ErrorMessage = "Excel 宿主没有返回基线状态。";
                }
                else
                {
                    result.Success = hostResult.Success;
                    result.WorkbookFullName = hostResult.WorkbookFullName ?? string.Empty;
                    result.ErrorCode = hostResult.ErrorCode ?? string.Empty;
                    result.ErrorMessage = hostResult.ErrorMessage ?? string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.ErrorCode = "excel.write_effect_baseline_failed";
                result.ErrorMessage = ex.Message;
            }

            return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
        }

        private async Task<HostBridgeResponse> HandlePreflightToolCallAsync(
            HostBridgeRequest request,
            CancellationToken cancellationToken)
        {
            PreflightToolCallRequest payload;
            var result = new PreflightToolCallResult();
            try
            {
                payload = JsonConvert.DeserializeObject<PreflightToolCallRequest>(request.PayloadJson ?? "{}")
                    ?? new PreflightToolCallRequest();
            }
            catch (JsonException ex)
            {
                result.ErrorCode = HostBridgeErrorCodes.InvalidPayload;
                result.ErrorMessage = ex.Message;
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
            }

            if (string.Equals(payload.ToolName, "ppt_deck_build", StringComparison.OrdinalIgnoreCase))
            {
                return HostBridgeResponse.CreateSuccess(
                    request.RequestId,
                    JsonConvert.SerializeObject(ResolveDeckBuildTarget(payload, cancellationToken)));
            }

            var adapter = _hostAdapter as IExcelWriteEffectHostAdapter;
            IAgentTool tool;
            if (adapter == null)
            {
                result.ErrorCode = "excel.write_effect_preflight_unavailable";
                result.ErrorMessage = "当前宿主不支持 Excel 写入影响预检。";
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
            }

            if (_toolRegistry == null
                || string.IsNullOrWhiteSpace(payload.ToolCallId)
                || string.IsNullOrWhiteSpace(payload.ToolName)
                || !_toolRegistry.TryGetTool(payload.ToolName, out tool)
                || tool == null
                || tool.Definition == null)
            {
                result.ErrorCode = "excel.write_effect_preflight_invalid";
                result.ErrorMessage = "预检请求缺少有效的工具名称或调用 ID。";
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
            }

            var definition = tool.Definition;
            if (definition.IsReadOnly
                || string.Equals(definition.MaxWriteEffect, ToolWriteEffectLevels.None, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.Effect = ToolWriteEffectLevels.None;
                result.OperationSummary = "只读调用，不改变工作簿。";
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
            }

            var callPolicy = _services == null
                ? null
                : _services.GetService(typeof(IHostCallPolicy)) as IHostCallPolicy;
            if (callPolicy == null)
            {
                result.ErrorCode = "excel.write_effect_dispatcher_unavailable";
                result.ErrorMessage = "无法在 Excel 所属线程完成写入影响预检。";
                return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
            }

            var argumentsJson = string.IsNullOrWhiteSpace(payload.ArgumentsJson) ? "{}" : payload.ArgumentsJson;
            var context = new ToolExecutionContext
            {
                ConversationId = payload.ConversationId ?? string.Empty,
                MessageId = payload.MessageId ?? string.Empty,
                HostApplicationName = _hostAdapter.HostApplicationName ?? string.Empty,
                Services = _services,
                CancellationToken = cancellationToken
            };
            var executionRequest = new ToolExecutionRequest
            {
                ToolCallId = payload.ToolCallId ?? string.Empty,
                ToolName = definition.Name ?? payload.ToolName ?? string.Empty,
                ArgumentsJson = argumentsJson
            };

            try
            {
                ExcelWriteEffectAssessment assessment = null;
                var dispatchResult = await callPolicy.ExecuteAsync(
                    definition,
                    context,
                    executionRequest,
                    () =>
                    {
                        assessment = adapter.AssessWriteEffect(
                            payload.ConversationId ?? string.Empty,
                            payload.MessageId ?? string.Empty,
                            payload.ToolName ?? string.Empty,
                            argumentsJson);
                        return Task.FromResult(ToolExecutionResult.CreateSuccess(
                            executionRequest.ToolName,
                            executionRequest.ToolCallId,
                            "已完成写入影响预检。"));
                    }).ConfigureAwait(false);

                if (dispatchResult == null || !dispatchResult.Success)
                {
                    result.ErrorCode = dispatchResult == null
                        ? "excel.write_effect_preflight_failed"
                        : dispatchResult.ErrorCode ?? "excel.write_effect_preflight_failed";
                    result.ErrorMessage = dispatchResult == null
                        ? "预检调度没有返回结果。"
                        : dispatchResult.ErrorMessage ?? dispatchResult.Content ?? "预检调度失败。";
                }
                else if (assessment == null || !assessment.Success)
                {
                    result.ErrorCode = assessment == null
                        ? "excel.write_effect_preflight_failed"
                        : assessment.ErrorCode ?? "excel.write_effect_preflight_failed";
                    result.ErrorMessage = assessment == null
                        ? "Excel 宿主没有返回预检结果。"
                        : assessment.ErrorMessage ?? "Excel 宿主无法确定本次写入影响。";
                }
                else if (!ToolWriteEffectLevels.IsValid(assessment.Effect))
                {
                    result.ErrorCode = "excel.write_effect_invalid";
                    result.ErrorMessage = "宿主返回了无效的写入影响级别；本次写入已阻止。";
                }
                else if (!ToolWriteEffectLevels.IsWithinLimit(assessment.Effect, definition.MaxWriteEffect))
                {
                    result.ErrorCode = "excel.write_effect_limit_exceeded";
                    result.ErrorMessage = "本次实际写入级别 " + assessment.Effect
                        + " 超出工具上限 " + definition.MaxWriteEffect + "；本次写入已阻止。";
                }
                else
                {
                    result.Success = true;
                    result.Effect = assessment.Effect;
                    result.OperationSummary = assessment.OperationSummary ?? string.Empty;
                    result.TargetWorkbookFullName = assessment.TargetWorkbookFullName ?? string.Empty;
                    result.TargetWorksheetName = assessment.TargetWorksheetName ?? string.Empty;
                    result.TargetAddress = assessment.TargetAddress ?? string.Empty;
                    result.PreflightToken = Guid.NewGuid().ToString("N");
                    RememberWriteEffectPreflightTicket(payload, argumentsJson, assessment, result);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.ErrorCode = "excel.write_effect_preflight_failed";
                result.ErrorMessage = ex.Message;
            }

            return HostBridgeResponse.CreateSuccess(request.RequestId, JsonConvert.SerializeObject(result));
        }

        private HostContextSnapshotDto BuildContextSnapshotDto()
        {
            var snapshot = _hostAdapter == null ? null : _hostAdapter.GetContextSnapshot();
            return new HostContextSnapshotDto
            {
                HostApplicationName = snapshot == null ? string.Empty : snapshot.HostApplicationName ?? string.Empty,
                DocumentName = snapshot == null ? string.Empty : snapshot.DocumentName ?? string.Empty,
                WorksheetName = snapshot == null ? string.Empty : snapshot.WorksheetName ?? string.Empty,
                SelectionAddress = snapshot == null ? string.Empty : snapshot.SelectionAddress ?? string.Empty,
                SelectionPreview = snapshot == null ? string.Empty : snapshot.SelectionPreview ?? string.Empty,
                Summary = snapshot == null ? string.Empty : snapshot.Summary ?? string.Empty,
                WorkbookMapSummary = snapshot == null ? string.Empty : snapshot.WorkbookMapSummary ?? string.Empty,
                WorkbookMap = snapshot == null ? null : ToWorkbookMapSnapshotDto(snapshot.WorkbookMap)
            };
        }

        private static WorkbookMapSnapshotDto ToWorkbookMapSnapshotDto(ExcelWorksheetListResult workbookMap)
        {
            if (workbookMap == null)
            {
                return null;
            }

            var result = new WorkbookMapSnapshotDto
            {
                WorkbookName = workbookMap.WorkbookName ?? string.Empty,
                TotalWorksheetCount = workbookMap.TotalWorksheetCount,
                IsTruncated = workbookMap.IsTruncated
            };

            if (workbookMap.Worksheets != null)
            {
                foreach (var worksheet in workbookMap.Worksheets)
                {
                    if (worksheet == null)
                    {
                        continue;
                    }

                    result.Worksheets.Add(new WorkbookWorksheetSnapshotDto
                    {
                        Name = worksheet.Name ?? string.Empty,
                        CodeName = worksheet.CodeName ?? string.Empty,
                        Index = worksheet.Index,
                        IsVisible = worksheet.IsVisible,
                        UsedRangeAddress = worksheet.UsedRangeAddress ?? string.Empty,
                        UsedRangeRows = worksheet.UsedRangeRows,
                        UsedRangeColumns = worksheet.UsedRangeColumns,
                        HeaderSignature = worksheet.HeaderSignature ?? string.Empty,
                        HeaderColumns = worksheet.HeaderColumns == null
                            ? new List<string>()
                            : new List<string>(worksheet.HeaderColumns),
                        HasFormulas = worksheet.HasFormulas,
                        IsProtected = worksheet.IsProtected,
                        RoleGuess = worksheet.RoleGuess ?? "other",
                        InspectionError = worksheet.InspectionError ?? string.Empty
                    });
                }
            }

            if (workbookMap.WorksheetGroups != null)
            {
                foreach (var group in workbookMap.WorksheetGroups)
                {
                    if (group == null)
                    {
                        continue;
                    }

                    result.WorksheetGroups.Add(new WorkbookWorksheetHeaderGroupSnapshotDto
                    {
                        GroupId = group.GroupId ?? string.Empty,
                        MinimumHeaderSimilarity = group.MinimumHeaderSimilarity,
                        WorksheetNames = group.WorksheetNames == null
                            ? new List<string>()
                            : new List<string>(group.WorksheetNames)
                    });
                }
            }

            if (workbookMap.SharedKeyCandidates != null)
            {
                foreach (var candidate in workbookMap.SharedKeyCandidates)
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    result.SharedColumnCandidates.Add(new WorkbookSharedColumnCandidateSnapshotDto
                    {
                        ColumnName = candidate.ColumnName ?? string.Empty,
                        WorksheetNames = candidate.WorksheetNames == null
                            ? new List<string>()
                            : new List<string>(candidate.WorksheetNames),
                        UniquenessVerified = candidate.UniquenessVerified
                    });
                }
            }

            return result;
        }

        private HostBridgeResponse HandleInsertText(HostBridgeRequest request)
        {
            if (_hostAdapter == null)
            {
                return HostBridgeResponse.CreateFailure(
                    request.RequestId,
                    HostBridgeErrorCodes.HostUnavailable,
                    "No host adapter has been configured.");
            }

            try
            {
                var payload = JsonConvert.DeserializeObject<InsertTextPayload>(request.PayloadJson ?? "{}")
                    ?? new InsertTextPayload();
                _hostAdapter.InsertText(payload.Text ?? string.Empty);
                return HostBridgeResponse.CreateSuccess(request.RequestId, "{}");
            }
            catch (Exception ex)
            {
                return HostBridgeResponse.CreateFailure(request.RequestId, HostBridgeErrorCodes.InsertTextFailed, ex.Message);
            }
        }

        private HostBridgeResponse HandleExecuteCode(HostBridgeRequest request)
        {
            if (_vbaExecutionHostAdapter == null)
            {
                return HostBridgeResponse.CreateFailure(
                    request.RequestId,
                    HostBridgeErrorCodes.CodeExecutionUnavailable,
                    "The current host does not support code execution.");
            }

            try
            {
                var payload = JsonConvert.DeserializeObject<HostCodeExecutionRequest>(request.PayloadJson ?? "{}")
                    ?? new HostCodeExecutionRequest();

                if (!IsRunnableVbaLanguage(payload.Language))
                {
                    return HostBridgeResponse.CreateFailure(
                        request.RequestId,
                        HostBridgeErrorCodes.CodeExecutionUnsupportedLanguage,
                        "Only VBA execution is currently supported.");
                }

                var result = _vbaExecutionHostAdapter.ExecuteVba(new VbaExecutionRequest
                {
                    Code = payload.Code ?? string.Empty,
                    ModuleName = string.Empty,
                    RemoveModuleAfterRun = true
                }) ?? new VbaExecutionResult();

                return HostBridgeResponse.CreateSuccess(
                    request.RequestId,
                    JsonConvert.SerializeObject(new HostCodeExecutionResult
                    {
                        RequestId = request.RequestId ?? string.Empty,
                        Success = result.Success,
                        Message = result.Message ?? string.Empty,
                        ErrorCode = result.Success ? string.Empty : HostBridgeErrorCodes.CodeExecutionFailed
                    }));
            }
            catch (Exception ex)
            {
                return HostBridgeResponse.CreateFailure(request.RequestId, HostBridgeErrorCodes.CodeExecutionFailed, ex.Message);
            }
        }

        private async Task<HostBridgeResponse> HandleInvokeToolAsync(HostBridgeRequest request, CancellationToken cancellationToken)
        {
            if (_toolRegistry == null)
            {
                return HostBridgeResponse.CreateFailure(
                    request.RequestId,
                    HostBridgeErrorCodes.ToolRegistryUnavailable,
                    "No tool registry has been configured.");
            }

            HostToolInvocationRequest payload;
            try
            {
                payload = JsonConvert.DeserializeObject<HostToolInvocationRequest>(request.PayloadJson ?? "{}")
                    ?? new HostToolInvocationRequest();
                payload.Metadata = payload.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                payload.Metadata.Remove(HostBridgeMetadataKeys.StagedOnly);
                payload.Metadata.Remove(HostBridgeMetadataKeys.VbaProtectionAllowedWorksheets);
            }
            catch (JsonException ex)
            {
                return HostBridgeResponse.CreateFailure(request.RequestId, HostBridgeErrorCodes.InvalidToolPayload, ex.Message);
            }

            var hostApplicationName = _hostAdapter == null ? string.Empty : _hostAdapter.HostApplicationName ?? string.Empty;
            var conversationId = GetToolConversationId(request, payload);
            var forceWriteConfirmation = RequiresScopeViolationConfirmation(conversationId);
            var argumentsJson = string.IsNullOrWhiteSpace(payload.ArgumentsJson) ? "{}" : payload.ArgumentsJson;
            var logContext = new ToolExecutionContext
            {
                ConversationId = conversationId,
                MessageId = payload.ToolCallId ?? string.Empty,
                HostApplicationName = hostApplicationName,
                Services = _services,
                CancellationToken = cancellationToken
            };
            var logRequest = new ToolExecutionRequest
            {
                ToolCallId = payload.ToolCallId ?? string.Empty,
                ToolName = payload.ToolName ?? string.Empty,
                ArgumentsJson = argumentsJson,
                Metadata = payload.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            ToolCallDiagnosticsLogger.Log(
                hostApplicationName,
                logRequest.ToolName,
                logContext,
                logRequest,
                "received",
                "argumentsJson=" + ToolCallDiagnosticsLogger.TruncateForLog(argumentsJson, 8000));

            var watch = Stopwatch.StartNew();

            IAgentTool tool;
            if (!_toolRegistry.TryGetTool(payload.ToolName, out tool))
            {
                var notFound = new HostToolInvocationResult
                {
                    ToolCallId = payload.ToolCallId ?? string.Empty,
                    ToolName = payload.ToolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "tool.not_found",
                    ErrorMessage = "Tool not found: " + (payload.ToolName ?? string.Empty)
                };

                watch.Stop();
                ToolCallDiagnosticsLogger.Log(
                    hostApplicationName,
                    logRequest.ToolName,
                    logContext,
                    logRequest,
                    "failed",
                    ToolCallDiagnosticsLogger.BuildResultDetails(
                        new ToolExecutionResult
                        {
                            ToolCallId = notFound.ToolCallId,
                            ToolName = notFound.ToolName,
                            Success = false,
                            ErrorCode = notFound.ErrorCode,
                            ErrorMessage = notFound.ErrorMessage
                        },
                        watch.ElapsedMilliseconds));

                return HostBridgeResponse.CreateFailure(request.RequestId, notFound.ErrorCode, notFound.ErrorMessage);
            }

            var definition = tool == null ? null : tool.Definition;
            if (IsDeckBuildTool(definition))
            {
                var deckBuildPreflight = PreflightDeckBuild(
                    conversationId,
                    argumentsJson,
                    tool,
                    logContext,
                    logRequest);
                if (!deckBuildPreflight.Success)
                {
                    return CreateToolInvocationFailureResponse(
                        request,
                        payload,
                        deckBuildPreflight.ErrorCode,
                        deckBuildPreflight.ErrorMessage);
                }
            }

            WriteEffectPreflightTicket preflightTicket = null;
            if (_hostAdapter is IExcelWriteEffectHostAdapter && RequiresWriteEffectPreflight(definition))
            {
                string preflightError;
                if (!TryConsumeWriteEffectPreflightTicket(payload, argumentsJson, out preflightTicket, out preflightError))
                {
                    return CreateToolInvocationFailureResponse(
                        request,
                        payload,
                        "excel.write_effect_preflight_required",
                        preflightError);
                }
            }

            var stagedWriteCandidate = StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite(
                definition == null ? string.Empty : definition.Name,
                payload.ArgumentsJson);
            IList<string> declaredVbaTargets = new List<string>();
            IList<string> declaredNewVbaTargets = new List<string>();
            var hasDeclaredVbaTargets = string.Equals(
                    definition == null ? string.Empty : definition.Name,
                    "excel_execute_vba",
                    StringComparison.OrdinalIgnoreCase)
                && StagedWriteConfirmationPolicy.TryGetDeclaredVbaWriteTargetSheetNames(
                    payload.ArgumentsJson,
                    out declaredVbaTargets,
                    out declaredNewVbaTargets);
            if (!hasDeclaredVbaTargets)
            {
                declaredVbaTargets = new List<string>();
                declaredNewVbaTargets = new List<string>();
            }

            var declaredVbaScopeProven = hasDeclaredVbaTargets
                && StagedWriteConfirmationPolicy.IsDeclaredVbaWriteScopeProvable(
                    StagedWriteConfirmationPolicy.ExtractVbaCode(payload.ArgumentsJson),
                    declaredVbaTargets,
                    declaredNewVbaTargets);
            var scopedProtectionTargets = declaredVbaScopeProven && !stagedWriteCandidate
                ? declaredVbaTargets
                : new List<string>();
            var stagedProtectionAvailability = stagedWriteCandidate || scopedProtectionTargets.Count > 0
                ? await GetStagedProtectionAvailabilityAsync(
                    definition,
                    logContext,
                    logRequest,
                    cancellationToken,
                    scopedProtectionTargets).ConfigureAwait(false)
                : new VbaExecutionAvailability
                {
                    CanExecute = false,
                    Reason = string.Empty
                };
            var shouldCreateBackup = preflightTicket == null
                || string.Equals(
                    preflightTicket.ApprovedEffect,
                    ToolWriteEffectLevels.Modify,
                    StringComparison.OrdinalIgnoreCase);
            var safetyPreparation = PrepareToolExecution(
                request,
                payload,
                definition,
                shouldCreateBackup,
                stagedWriteCandidate,
                stagedProtectionAvailability.CanExecute,
                stagedProtectionAvailability.Reason,
                scopedProtectionTargets,
                forceWriteConfirmation,
                preflightTicket == null ? string.Empty : preflightTicket.OperationSummary);
            if (!safetyPreparation.Success)
            {
                watch.Stop();
                ToolCallDiagnosticsLogger.Log(
                    hostApplicationName,
                    logRequest.ToolName,
                    logContext,
                    logRequest,
                    "failed",
                    ToolCallDiagnosticsLogger.BuildResultDetails(
                        new ToolExecutionResult
                        {
                            ToolCallId = payload.ToolCallId ?? string.Empty,
                            ToolName = payload.ToolName ?? string.Empty,
                            Success = false,
                            Content = safetyPreparation.ErrorMessage ?? string.Empty,
                            ErrorCode = safetyPreparation.ErrorCode ?? string.Empty,
                            ErrorMessage = safetyPreparation.ErrorMessage ?? string.Empty,
                            Metadata = safetyPreparation.Metadata
                        },
                        watch.ElapsedMilliseconds));

                return HostBridgeResponse.CreateSuccess(
                    request.RequestId,
                    JsonConvert.SerializeObject(new HostToolInvocationResult
                    {
                        ToolCallId = payload.ToolCallId ?? string.Empty,
                        ToolName = payload.ToolName ?? string.Empty,
                        Success = false,
                        Content = safetyPreparation.ErrorMessage ?? string.Empty,
                        ErrorCode = safetyPreparation.ErrorCode ?? string.Empty,
                        ErrorMessage = safetyPreparation.ErrorMessage ?? string.Empty,
                        Metadata = safetyPreparation.Metadata
                    }));
            }

            try
            {
                var executionContext = new ToolExecutionContext
                {
                    ConversationId = conversationId,
                    MessageId = payload.ToolCallId ?? string.Empty,
                    HostApplicationName = hostApplicationName,
                    Services = _services,
                    CancellationToken = cancellationToken
                };

                var executionRequest = new ToolExecutionRequest
                {
                    ToolCallId = payload.ToolCallId ?? string.Empty,
                    ToolName = payload.ToolName ?? string.Empty,
                    ArgumentsJson = argumentsJson,
                    Metadata = payload.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };
                var callPolicy = executionContext.GetService<IHostCallPolicy>();
                Func<Task<ToolExecutionResult>> executeTool = async () =>
                {
                    if (preflightTicket != null)
                    {
                        var writeEffectHost = _hostAdapter as IExcelWriteEffectHostAdapter;
                        if (writeEffectHost == null)
                        {
                            return ToolExecutionResult.CreateFailure(
                                executionRequest.ToolName,
                                executionRequest.ToolCallId,
                                "Excel 写入影响复核服务不可用；本次写入已阻止。",
                                "excel.write_effect_recheck_unavailable");
                        }

                        var currentAssessment = writeEffectHost.AssessWriteEffect(
                            preflightTicket.ConversationId,
                            preflightTicket.MessageId,
                            executionRequest.ToolName,
                            executionRequest.ArgumentsJson);
                        if (currentAssessment == null || !currentAssessment.Success)
                        {
                            return ToolExecutionResult.CreateFailure(
                                executionRequest.ToolName,
                                executionRequest.ToolCallId,
                                currentAssessment == null
                                    ? "执行前写入影响复核未返回结果；本次写入已阻止。"
                                    : currentAssessment.ErrorMessage ?? "执行前写入影响复核失败；本次写入已阻止。",
                                "excel.write_effect_recheck_failed");
                        }

                        var targetChanged = !TargetsMatch(preflightTicket, currentAssessment);
                        if (targetChanged
                            || !ToolWriteEffectLevels.IsWithinLimit(
                                currentAssessment.Effect,
                                preflightTicket.ApprovedEffect))
                        {
                            return ToolExecutionResult.CreateFailure(
                                executionRequest.ToolName,
                                executionRequest.ToolCallId,
                                "预检后目标或写入影响发生变化；本次执行已阻止。重新预检结果：" +
                                (currentAssessment.OperationSummary ?? string.Empty),
                                "WRITE_EFFECT_ESCALATED");
                        }
                    }

                    return await tool.ExecuteAsync(executionContext, executionRequest).ConfigureAwait(false);
                };
                if (preflightTicket != null && callPolicy == null)
                {
                    return CreateToolInvocationFailureResponse(
                        request,
                        payload,
                        "excel.write_effect_dispatcher_unavailable",
                        "无法在 Excel 所属线程完成执行前影响复核；本次写入已阻止。");
                }

                var result = (callPolicy == null
                    ? await executeTool().ConfigureAwait(false)
                    : await callPolicy.ExecuteAsync(
                        definition,
                        executionContext,
                        executionRequest,
                        executeTool).ConfigureAwait(false))
                    ?? ToolExecutionResult.CreateFailure(payload.ToolName, payload.ToolCallId, "Tool returned no result.", "tool.no_result");

                if (!result.Success
                    && string.Equals(result.ErrorCode, "VBA_SCOPE_VIOLATION", StringComparison.Ordinal))
                {
                    RequireScopeViolationConfirmation(conversationId);
                }

                RememberDeckWorkspaceState(conversationId, definition, argumentsJson, result);
                AppendSafetyMetadata(result, safetyPreparation);

                watch.Stop();
                ToolCallDiagnosticsLogger.Log(
                    hostApplicationName,
                    logRequest.ToolName,
                    executionContext,
                    logRequest,
                    result.Success ? "completed" : "failed",
                    ToolCallDiagnosticsLogger.BuildResultDetails(result, watch.ElapsedMilliseconds));

                return HostBridgeResponse.CreateSuccess(
                    request.RequestId,
                    JsonConvert.SerializeObject(new HostToolInvocationResult
                    {
                        ToolCallId = result.ToolCallId ?? string.Empty,
                        ToolName = result.ToolName ?? string.Empty,
                        Success = result.Success,
                        Content = result.Content ?? string.Empty,
                        StructuredContentJson = result.StructuredContentJson ?? string.Empty,
                        ErrorCode = result.ErrorCode ?? string.Empty,
                        ErrorMessage = result.ErrorMessage ?? string.Empty,
                        Metadata = result.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        ContentParts = MapContentParts(result.ContentParts)
                    }));
            }
            catch (Exception ex)
            {
                watch.Stop();
                ToolCallDiagnosticsLogger.Log(
                    hostApplicationName,
                    logRequest.ToolName,
                    logContext,
                    logRequest,
                    "failed",
                    ToolCallDiagnosticsLogger.BuildFailureDetails(ex, watch.ElapsedMilliseconds));

                return HostBridgeResponse.CreateFailure(request.RequestId, HostBridgeErrorCodes.ToolExecutionFailed, ex.Message);
            }
        }

        private void RememberWriteEffectPreflightTicket(
            PreflightToolCallRequest request,
            string argumentsJson,
            ExcelWriteEffectAssessment assessment,
            PreflightToolCallResult result)
        {
            var ticket = new WriteEffectPreflightTicket
            {
                Token = result.PreflightToken ?? string.Empty,
                ToolCallId = request.ToolCallId ?? string.Empty,
                ConversationId = request.ConversationId ?? string.Empty,
                MessageId = request.MessageId ?? string.Empty,
                ToolName = request.ToolName ?? string.Empty,
                ArgumentsJson = argumentsJson ?? "{}",
                ApprovedEffect = result.Effect ?? string.Empty,
                OperationSummary = assessment.OperationSummary ?? string.Empty,
                TargetWorkbookFullName = assessment.TargetWorkbookFullName ?? string.Empty,
                TargetWorksheetName = assessment.TargetWorksheetName ?? string.Empty,
                TargetAddress = assessment.TargetAddress ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };

            lock (_writeEffectPreflightSync)
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-10);
                foreach (var expiredKey in _writeEffectPreflightTickets
                    .Where(pair => pair.Value.CreatedAtUtc < cutoff)
                    .Select(pair => pair.Key)
                    .ToList())
                {
                    _writeEffectPreflightTickets.Remove(expiredKey);
                }

                if (_writeEffectPreflightTickets.Count >= 256)
                {
                    var oldest = _writeEffectPreflightTickets
                        .OrderBy(pair => pair.Value.CreatedAtUtc)
                        .FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(oldest.Key))
                    {
                        _writeEffectPreflightTickets.Remove(oldest.Key);
                    }
                }

                _writeEffectPreflightTickets[ticket.ToolCallId] = ticket;
            }
        }

        private bool TryConsumeWriteEffectPreflightTicket(
            HostToolInvocationRequest payload,
            string argumentsJson,
            out WriteEffectPreflightTicket ticket,
            out string errorMessage)
        {
            ticket = null;
            errorMessage = string.Empty;
            if (payload == null || string.IsNullOrWhiteSpace(payload.ToolCallId))
            {
                errorMessage = "工具调用缺少预检关联 ID。";
                return false;
            }

            var token = GetMetadataValue(payload, HostBridgeMetadataKeys.WriteEffectPreflightToken);
            lock (_writeEffectPreflightSync)
            {
                if (!_writeEffectPreflightTickets.TryGetValue(payload.ToolCallId, out ticket))
                {
                    errorMessage = "本次写入没有有效的宿主预检记录，请重新发起工具调用。";
                    return false;
                }

                _writeEffectPreflightTickets.Remove(payload.ToolCallId);
            }

            if (!string.Equals(ticket.Token, token, StringComparison.Ordinal)
                || !string.Equals(ticket.ToolName, payload.ToolName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(ticket.ArgumentsJson, argumentsJson ?? "{}", StringComparison.Ordinal))
            {
                ticket = null;
                errorMessage = "工具参数与已预检调用不一致；本次写入已阻止。";
                return false;
            }

            return true;
        }

        private static bool RequiresWriteEffectPreflight(ToolDefinition definition)
        {
            return definition != null
                && !definition.IsReadOnly
                && !string.Equals(
                    definition.MaxWriteEffect,
                    ToolWriteEffectLevels.None,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool TargetsMatch(
            WriteEffectPreflightTicket ticket,
            ExcelWriteEffectAssessment assessment)
        {
            return string.Equals(
                    ticket.TargetWorkbookFullName,
                    assessment.TargetWorkbookFullName ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    ticket.TargetWorksheetName,
                    assessment.TargetWorksheetName ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    ticket.TargetAddress,
                    assessment.TargetAddress ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static HostBridgeResponse CreateToolInvocationFailureResponse(
            HostBridgeRequest request,
            HostToolInvocationRequest payload,
            string errorCode,
            string errorMessage)
        {
            return HostBridgeResponse.CreateSuccess(
                request.RequestId,
                JsonConvert.SerializeObject(new HostToolInvocationResult
                {
                    ToolCallId = payload == null ? string.Empty : payload.ToolCallId ?? string.Empty,
                    ToolName = payload == null ? string.Empty : payload.ToolName ?? string.Empty,
                    Success = false,
                    ErrorCode = errorCode ?? string.Empty,
                    ErrorMessage = errorMessage ?? string.Empty,
                    Content = errorMessage ?? string.Empty
                }));
        }

        private static bool IsDeckBuildTool(ToolDefinition definition)
        {
            return definition != null
                && string.Equals(definition.Name, "ppt_deck_build", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 为 ppt_deck_build 补全省略的 deckId / workspaceTitle / slideCount：只取本会话绑定的
        /// 已验证且已预览的工作区；显式提供的值保持原样，由随后的严格检查决定是否一致。
        /// </summary>
        private PreflightToolCallResult ResolveDeckBuildTarget(
            PreflightToolCallRequest payload,
            CancellationToken cancellationToken)
        {
            var result = new PreflightToolCallResult();
            JObject arguments;
            try
            {
                arguments = JObject.Parse(string.IsNullOrWhiteSpace(payload.ArgumentsJson) ? "{}" : payload.ArgumentsJson);
            }
            catch (JsonException ex)
            {
                result.ErrorCode = "ppt.deck_invalid_argument";
                result.ErrorMessage = ex.Message;
                return result;
            }

            var conversationId = (payload.ConversationId ?? string.Empty).Trim();
            DeckBuildWorkspaceState bound = null;
            if (conversationId.Length > 0)
            {
                lock (_deckBuildWorkspaceSync)
                {
                    _deckBuildWorkspaceByConversation.TryGetValue(conversationId, out bound);
                }
            }

            if (bound == null)
            {
                result.ErrorCode = "ppt.deck_build_target_unbound";
                result.ErrorMessage = "当前会话没有已预览的工作区，无法确定构建目标；请先对目标工作区运行 ppt_deck_preview，或显式提供 deckId。";
                return result;
            }

            if (string.IsNullOrWhiteSpace((string)arguments["deckId"]))
            {
                arguments["deckId"] = bound.DeckId;
            }

            if (arguments["workspaceTitle"] == null || arguments["workspaceTitle"].Type == JTokenType.Null)
            {
                arguments["workspaceTitle"] = bound.Title ?? string.Empty;
            }

            if (arguments["slideCount"] == null || arguments["slideCount"].Type == JTokenType.Null)
            {
                arguments["slideCount"] = bound.SlideCount;
            }

            var resolvedArgumentsJson = arguments.ToString(Formatting.None);
            IAgentTool tool = null;
            if (_toolRegistry != null && !string.IsNullOrWhiteSpace(payload.ToolName))
            {
                _toolRegistry.TryGetTool(payload.ToolName, out tool);
            }

            var check = PreflightDeckBuild(
                conversationId,
                resolvedArgumentsJson,
                tool,
                new ToolExecutionContext
                {
                    ConversationId = conversationId,
                    MessageId = payload.ToolCallId ?? string.Empty,
                    HostApplicationName = _hostAdapter == null ? string.Empty : _hostAdapter.HostApplicationName ?? string.Empty,
                    Services = _services,
                    CancellationToken = cancellationToken
                },
                new ToolExecutionRequest
                {
                    ToolCallId = payload.ToolCallId ?? string.Empty,
                    ToolName = payload.ToolName ?? string.Empty,
                    ArgumentsJson = resolvedArgumentsJson,
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                });
            if (check == null || !check.Success)
            {
                result.ErrorCode = check == null ? "ppt.deck_build_preflight_failed" : check.ErrorCode;
                result.ErrorMessage = check == null ? "构建前工作区检查未返回结果。" : check.ErrorMessage;
                return result;
            }

            result.Success = true;
            result.Effect = ToolWriteEffectLevels.Modify;
            result.ResolvedArgumentsJson = resolvedArgumentsJson;
            result.OperationSummary = "构建目标：「" + (string.IsNullOrWhiteSpace(bound.Title) ? "(无标题)" : bound.Title)
                + "」，ID " + (string)arguments["deckId"] + "，共 " + (int?)arguments["slideCount"] + " 页（本会话最近预览的工作区）。";
            return result;
        }

        private ToolExecutionPreflightResult PreflightDeckBuild(
            string conversationId,
            string argumentsJson,
            IAgentTool tool,
            ToolExecutionContext context,
            ToolExecutionRequest request)
        {
            JObject arguments;
            try
            {
                arguments = JObject.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            }
            catch (JsonException ex)
            {
                return ToolExecutionPreflightResult.CreateFailure("ppt.deck_invalid_argument", ex.Message);
            }

            var requestedDeckId = (string)arguments["deckId"];
            var requestedTitle = (string)arguments["workspaceTitle"];
            var requestedSlideCount = (int?)arguments["slideCount"];
            if (string.IsNullOrWhiteSpace(requestedDeckId))
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_target_required",
                    "构建请求缺少 deckId；请先预览并验证目标工作区。");
            }

            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_target_unbound",
                    "当前调用没有会话标识，无法确认构建目标；请重新预览并验证工作区后再构建。");
            }

            DeckBuildWorkspaceState selectedWorkspace;
            lock (_deckBuildWorkspaceSync)
            {
                _deckBuildWorkspaceByConversation.TryGetValue(conversationId.Trim(), out selectedWorkspace);
            }

            if (selectedWorkspace == null)
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_target_unbound",
                    "当前会话没有已绑定的工作区；请对目标工作区运行 ppt_deck_preview，再使用其原始 deckId 构建。");
            }

            if (!string.Equals(selectedWorkspace.DeckId, requestedDeckId.Trim(), StringComparison.Ordinal))
            {
                var targetSummary = string.IsNullOrWhiteSpace(selectedWorkspace.Title)
                    ? selectedWorkspace.DeckId
                    : "「" + selectedWorkspace.Title + "」(" + selectedWorkspace.DeckId + ")";
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_target_mismatch",
                    "构建目标与本会话最后预览的工作区不一致。当前目标为 " + targetSummary
                        + "，共 " + selectedWorkspace.SlideCount + " 页；本次请求为 " + requestedDeckId.Trim()
                        + "。请使用当前目标的完整 deckId，不会自动替换构建目标。");
            }

            if (requestedTitle == null || !requestedSlideCount.HasValue)
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_confirmation_target_required",
                    "构建确认必须包含工作区标题、完整 deckId 和页数；当前目标为 "
                        + (string.IsNullOrWhiteSpace(selectedWorkspace.Title) ? "(无标题)" : "「" + selectedWorkspace.Title + "」")
                        + "，ID " + selectedWorkspace.DeckId + "，共 " + selectedWorkspace.SlideCount
                        + " 页。请从最近一次预览结果原样填写。");
            }

            if (!string.Equals(selectedWorkspace.Title ?? string.Empty, requestedTitle, StringComparison.Ordinal)
                || selectedWorkspace.SlideCount != requestedSlideCount.Value)
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_confirmation_target_mismatch",
                    "构建确认中的工作区标题或页数与最近一次预览结果不一致。请确认目标 "
                        + (string.IsNullOrWhiteSpace(selectedWorkspace.Title) ? "(无标题)" : "「" + selectedWorkspace.Title + "」")
                        + "，ID " + selectedWorkspace.DeckId + "，共 " + selectedWorkspace.SlideCount + " 页。");
            }

            if (string.IsNullOrWhiteSpace(selectedWorkspace.PreviewedRevision))
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_target_not_ready",
                    "工作区尚未预览；请在最后一次修改后重新运行 ppt_deck_preview，再构建。");
            }

            var workspacePreflight = tool as IToolExecutionPreflight;
            if (workspacePreflight == null)
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_preflight_unavailable",
                    "构建工具未提供工作区执行前检查；为避免错误目标触发备份或写入，本次构建已阻止。");
            }

            ToolExecutionPreflightResult currentWorkspace;
            try
            {
                currentWorkspace = workspacePreflight.Preflight(context, request);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_preflight_failed",
                    "构建前工作区检查失败，尚未创建备份或修改演示文稿：" + ex.Message);
            }

            if (currentWorkspace == null || !currentWorkspace.Success)
            {
                return currentWorkspace ?? ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_preflight_failed",
                    "构建前工作区检查未返回结果；尚未创建备份或修改演示文稿。");
            }

            JObject workspaceDetails;
            try
            {
                workspaceDetails = JObject.Parse(currentWorkspace.DetailsJson ?? "{}");
            }
            catch (JsonException ex)
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_preflight_failed",
                    "构建前工作区检查返回了无效结果；尚未创建备份或修改演示文稿。" + ex.Message);
            }

            var actualDeckId = (string)workspaceDetails.GetValue("DeckId", StringComparison.OrdinalIgnoreCase);
            var actualTitle = (string)workspaceDetails.GetValue("Title", StringComparison.OrdinalIgnoreCase) ?? string.Empty;
            var actualSlideCount = (int?)workspaceDetails.GetValue("SlideCount", StringComparison.OrdinalIgnoreCase);
            var currentRevision = (string)workspaceDetails.GetValue("WorkspaceRevision", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(actualDeckId, selectedWorkspace.DeckId, StringComparison.Ordinal)
                || !string.Equals(actualDeckId, requestedDeckId.Trim(), StringComparison.Ordinal)
                || !string.Equals(actualTitle, requestedTitle, StringComparison.Ordinal)
                || actualSlideCount != requestedSlideCount)
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_target_mismatch",
                    "构建前工作区检查发现目标 ID 不一致；尚未创建备份或修改演示文稿。");
            }

            if (string.IsNullOrWhiteSpace(currentRevision)
                || !string.Equals(currentRevision, selectedWorkspace.PreviewedRevision, StringComparison.Ordinal))
            {
                return ToolExecutionPreflightResult.CreateFailure(
                    "ppt.deck_build_target_changed",
                    "工作区在预览后发生变化；请重新预览当前版本后再构建。尚未创建备份或修改演示文稿。");
            }

            return ToolExecutionPreflightResult.CreateSuccess(currentWorkspace.DetailsJson);
        }

        private void RememberDeckWorkspaceState(
            string conversationId,
            ToolDefinition definition,
            string argumentsJson,
            ToolExecutionResult result)
        {
            if (definition == null
                || result == null
                || !result.Success
                || string.IsNullOrWhiteSpace(conversationId)
                || string.IsNullOrWhiteSpace(definition.Name)
                || !definition.Name.StartsWith("ppt_deck_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(definition.Name, "ppt_deck_build", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            JObject output;
            JObject arguments;
            try
            {
                output = JObject.Parse(result.StructuredContentJson ?? "{}");
                arguments = JObject.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            }
            catch (JsonException)
            {
                ForgetDeckWorkspaceState(conversationId);
                return;
            }

            var deckId = (string)output.GetValue("DeckId", StringComparison.OrdinalIgnoreCase)
                ?? (string)arguments.GetValue("deckId", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(deckId))
            {
                ForgetDeckWorkspaceState(conversationId);
                return;
            }

            var toolName = definition.Name;
            var isCreate = string.Equals(toolName, "ppt_deck_create", StringComparison.OrdinalIgnoreCase);
            var isPreview = string.Equals(toolName, "ppt_deck_preview", StringComparison.OrdinalIgnoreCase);
            var revision = (string)output.GetValue("WorkspaceRevision", StringComparison.OrdinalIgnoreCase);
            var title = (string)output.GetValue("Title", StringComparison.OrdinalIgnoreCase);
            var slideCount = (int?)output.GetValue("SlideCount", StringComparison.OrdinalIgnoreCase);
            var key = conversationId.Trim();

            lock (_deckBuildWorkspaceSync)
            {
                DeckBuildWorkspaceState state = null;
                var hasState = !isCreate
                    && _deckBuildWorkspaceByConversation.TryGetValue(key, out state)
                    && string.Equals(state.DeckId, deckId, StringComparison.Ordinal);
                if (!hasState)
                {
                    if (_deckBuildWorkspaceByConversation.Count >= 256)
                    {
                        var oldest = _deckBuildWorkspaceByConversation
                            .OrderBy(pair => pair.Value.LastUpdatedUtc)
                            .FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(oldest.Key))
                        {
                            _deckBuildWorkspaceByConversation.Remove(oldest.Key);
                        }
                    }

                    state = new DeckBuildWorkspaceState { DeckId = deckId };
                    _deckBuildWorkspaceByConversation[key] = state;
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    state.Title = title;
                }

                if (slideCount.HasValue)
                {
                    state.SlideCount = slideCount.Value;
                }

                if (isPreview)
                {
                    state.PreviewedRevision = revision ?? string.Empty;
                }

                state.LastUpdatedUtc = DateTime.UtcNow;
            }
        }

        private void ForgetDeckWorkspaceState(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return;
            }

            lock (_deckBuildWorkspaceSync)
            {
                _deckBuildWorkspaceByConversation.Remove(conversationId.Trim());
            }
        }

        // 单张图像经 base64 后的上限（约 1.5 MB 原始字节）；超限的图像只保留说明文字，避免管道单行过大。
        private const int MaxContentPartBase64Length = 2 * 1024 * 1024;
        private const int MaxContentPartCount = 6;

        private static List<HostToolContentPart> MapContentParts(IList<ToolResultContentPart> parts)
        {
            var result = new List<HostToolContentPart>();
            if (parts == null)
            {
                return result;
            }

            foreach (var part in parts)
            {
                if (part == null || string.IsNullOrWhiteSpace(part.Base64) || result.Count >= MaxContentPartCount)
                {
                    continue;
                }

                if (part.Base64.Length > MaxContentPartBase64Length)
                {
                    continue;
                }

                result.Add(new HostToolContentPart
                {
                    Type = part.Type ?? ToolResultContentPart.ImageType,
                    MediaType = part.MediaType ?? "image/png",
                    Base64 = part.Base64,
                    Description = part.Description ?? string.Empty
                });
            }

            return result;
        }

        private static bool IsRunnableVbaLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return true;
            }

            var normalized = language.Trim();
            return string.Equals(normalized, "vba", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "vb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "visual basic for applications", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<VbaExecutionAvailability> GetStagedProtectionAvailabilityAsync(
            ToolDefinition definition,
            ToolExecutionContext context,
            ToolExecutionRequest executionRequest,
            CancellationToken cancellationToken,
            IList<string> allowedWorksheetNames = null)
        {
            var useDeclaredTargets = allowedWorksheetNames != null && allowedWorksheetNames.Count > 0;
            var stagedProtectionHost = _hostAdapter as IStagedWriteProtectionHostAdapter;
            var declaredProtectionHost = _hostAdapter as IVbaDeclaredWriteProtectionHostAdapter;
            if ((useDeclaredTargets && declaredProtectionHost == null)
                || (!useDeclaredTargets && stagedProtectionHost == null))
            {
                return new VbaExecutionAvailability
                {
                    CanExecute = false,
                    Reason = useDeclaredTargets
                        ? "当前宿主不支持按声明目标保护工作表。"
                        : "当前宿主不支持中间表运行时保护。"
                };
            }

            var callPolicy = _services == null
                ? null
                : _services.GetService(typeof(IHostCallPolicy)) as IHostCallPolicy;
            if (callPolicy == null)
            {
                return new VbaExecutionAvailability
                {
                    CanExecute = false,
                    Reason = "无法在 Excel 所属线程检查工作表锁定状态。"
                };
            }

            VbaExecutionAvailability availability = null;
            ToolExecutionResult dispatchResult;
            try
            {
                dispatchResult = await callPolicy.ExecuteAsync(
                    definition,
                    context,
                    executionRequest,
                    () =>
                    {
                        availability = useDeclaredTargets
                            ? declaredProtectionHost.GetDeclaredWriteProtectionAvailability(allowedWorksheetNames)
                            : stagedProtectionHost.GetStagedWriteProtectionAvailability();
                        return Task.FromResult(ToolExecutionResult.CreateSuccess(
                            definition.Name,
                            executionRequest.ToolCallId,
                            useDeclaredTargets
                                ? "已检查声明目标外工作表的运行时保护条件。"
                                : "已检查中间表运行时保护条件。"));
                    }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new VbaExecutionAvailability
                {
                    CanExecute = false,
                    Reason = "工作表保护条件检查失败：" + ex.Message
                };
            }

            if (dispatchResult == null || !dispatchResult.Success)
            {
                return new VbaExecutionAvailability
                {
                    CanExecute = false,
                    Reason = dispatchResult == null
                        ? "工作表保护条件检查没有返回结果。"
                        : "工作表保护条件检查未能完成：" + (dispatchResult.ErrorMessage ?? dispatchResult.Content)
                };
            }

            return availability ?? new VbaExecutionAvailability
            {
                CanExecute = false,
                Reason = "工作表保护条件检查没有返回状态。"
            };
        }

        private ToolExecutionSafetyPreparation PrepareToolExecution(
    HostBridgeRequest request,
    HostToolInvocationRequest payload,
    ToolDefinition definition,
    bool shouldCreateBackup,
    bool stagedWriteCandidate,
    bool stagedProtectionEligible,
    string stagedProtectionFailureReason,
    IList<string> scopedProtectionTargets,
    bool forceWriteConfirmation,
    string writeEffectOperationSummary)
        {
            var preparation = new ToolExecutionSafetyPreparation();
            if (definition == null)
            {
                return preparation;
            }

            var riskLevel = string.IsNullOrWhiteSpace(definition.RiskLevel)
                ? "low"
                : definition.RiskLevel.Trim().ToLowerInvariant();

            if (definition.RequiresBackup && shouldCreateBackup)
            {
                var sharedBackupPath = ResolveSharedBackupPath(request, payload);

                if (CanReuseBackupPath(sharedBackupPath))
                {
                    preparation.Metadata[ToolMetadataKeys.BackupPath] = sharedBackupPath;
                    EnsurePayloadBackupPath(payload, sharedBackupPath);
                }
                else
                {
                    if (_documentBackupHostAdapter == null)
                    {
                        return ToolExecutionSafetyPreparation.CreateFailure(
                            "tool.backup_unavailable",
                            "Document backup is not configured for the current host.",
                            riskLevel);
                    }

                    try
                    {
                        var backupResult = _documentBackupHostAdapter.CreateBackup(new DocumentBackupRequest
                        {
                            SessionId = GetMetadataValue(payload, ToolMetadataKeys.SessionId),
                            ConversationId = request == null ? string.Empty : request.SessionId ?? string.Empty,
                            MessageId = GetMetadataValue(payload, ToolMetadataKeys.SourceMessageId),
                            ToolName = payload == null ? string.Empty : payload.ToolName ?? string.Empty,
                            OperationDescription = definition.DisplayName ?? definition.Description ?? definition.Name ?? string.Empty,
                            UserIntent = GetMetadataValue(payload, ToolMetadataKeys.UserIntent),
                            RiskLevel = riskLevel
                        }) ?? new DocumentBackupResult();

                        if (!backupResult.Success)
                        {
                            return ToolExecutionSafetyPreparation.CreateFailure(
                                "tool.backup_failed",
                                string.IsNullOrWhiteSpace(backupResult.ErrorMessage)
                                    ? "Failed to create the pre-execution backup."
                                    : backupResult.ErrorMessage,
                                riskLevel);
                        }

                        preparation.BackupResult = backupResult;
                        preparation.Metadata[ToolMetadataKeys.BackupPath] = backupResult.BackupPath ?? string.Empty;

                        EnsurePayloadBackupPath(payload, backupResult.BackupPath);
                        RememberSharedBackupPath(request, payload, backupResult.BackupPath);

                        var backupStore = _services == null
                            ? null
                            : _services.GetService(typeof(IBackupArtifactStore)) as IBackupArtifactStore;
                        if (backupStore != null)
                        {
                            backupStore.Record(backupResult);
                        }
                    }
                    catch (Exception ex)
                    {
                        return ToolExecutionSafetyPreparation.CreateFailure(
                            "tool.backup_failed",
                            ex.Message,
                            riskLevel);
                    }
                }
            }

            // 参数级豁免：只写 _wip_ 中间表的调用不触碰既有数据，弹窗只会稀释真正需要确认的那一次。
            var stagedWriteExempt = stagedWriteCandidate
                && stagedProtectionEligible
                && !forceWriteConfirmation;
            var requiresConfirmation = definition.RequiresConfirmation
                || (forceWriteConfirmation && !definition.IsReadOnly);
            if (stagedWriteExempt && payload != null && payload.Metadata != null)
            {
                payload.Metadata[HostBridgeMetadataKeys.StagedOnly] = HostBridgeMetadataKeys.TrueValue;
            }
            else if (scopedProtectionTargets != null
                && scopedProtectionTargets.Count > 0
                && stagedProtectionEligible
                && payload != null
                && payload.Metadata != null)
            {
                payload.Metadata[HostBridgeMetadataKeys.VbaProtectionAllowedWorksheets] =
                    JsonConvert.SerializeObject(scopedProtectionTargets);
            }

            // 「始终放行」：宿主侧不再弹确认（包括范围越界强制确认），与调度层的放行判定保持一致。
            var trustAlwaysAllow = string.Equals(
                GetMetadataValue(payload, ToolMetadataKeys.ToolTrustMode),
                ToolMetadataKeys.ToolTrustModeAlwaysAllow,
                StringComparison.OrdinalIgnoreCase);
            if (requiresConfirmation
                && !stagedWriteExempt
                && !trustAlwaysAllow
                && (forceWriteConfirmation || !IsConfirmationAlreadyGranted(payload)))
            {
                var confirmationHandler = _services == null
                    ? null
                    : _services.GetService(typeof(IToolConfirmationHandler)) as IToolConfirmationHandler;
                if (confirmationHandler == null)
                {
                    return ToolExecutionSafetyPreparation.CreateFailure(
                        "tool.confirmation_handler_unavailable",
                        "No tool confirmation handler is configured for the current host."
                            + (stagedWriteCandidate && !stagedProtectionEligible
                                ? " 中间表免确认保护不可用：" + (stagedProtectionFailureReason ?? string.Empty)
                                : string.Empty),
                        riskLevel);
                }

                var confirmed = confirmationHandler.Confirm(new ToolConfirmationRequest
                {
                    ToolName = definition.Name ?? string.Empty,
                    ToolCallId = payload == null ? string.Empty : payload.ToolCallId ?? string.Empty,
                    ToolDisplayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                        ? definition.Name ?? string.Empty
                        : definition.DisplayName,
                    ToolDescription = definition.Description ?? string.Empty,
                    OperationSummary = (string.IsNullOrWhiteSpace(writeEffectOperationSummary)
                        ? (string.IsNullOrWhiteSpace(definition.Description)
                            ? definition.Name ?? string.Empty
                            : definition.Description)
                        : writeEffectOperationSummary)
                        + (forceWriteConfirmation
                            ? "\r\n\r\n上一次 VBA 执行发生声明范围越界；本轮后续写入必须逐次确认。"
                            : string.Empty)
                        + (stagedWriteCandidate && !stagedProtectionEligible
                            ? "\r\n\r\n免确认运行时保护不可用：" + (stagedProtectionFailureReason ?? "无法验证所有正式工作表均已锁定。")
                                + "因此本次操作需要确认。"
                            : string.Empty),
                    ArgumentsSummary = SummarizeArguments(payload == null ? string.Empty : payload.ArgumentsJson),
                    RiskLevel = riskLevel,
                    DocumentName = GetDocumentName(),
                    BackupPath = GetPreparationBackupPath(preparation),
                    SessionId = GetMetadataValue(payload, ToolMetadataKeys.SessionId),
                    ConversationId = request == null ? string.Empty : request.SessionId ?? string.Empty,
                    MessageId = GetMetadataValue(payload, ToolMetadataKeys.SourceMessageId)
                });

                if (!confirmed)
                {
                    return ToolExecutionSafetyPreparation.CreateFailure(
                        "tool.confirmation_declined",
                        "The user declined the high-risk tool call.",
                        riskLevel,
                        preparation.Metadata,
                        preparation.BackupResult);
                }
            }

            preparation.Metadata[ToolMetadataKeys.RiskLevel] = riskLevel;
            return preparation;
        }

        private bool RequiresScopeViolationConfirmation(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return false;
            }

            lock (_scopeViolationConfirmationSync)
            {
                return _scopeViolationConfirmationConversationIds.Contains(conversationId.Trim());
            }
        }

        private void RequireScopeViolationConfirmation(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return;
            }

            lock (_scopeViolationConfirmationSync)
            {
                _scopeViolationConfirmationConversationIds.Add(conversationId.Trim());
            }
        }

        private void ClearScopeViolationConfirmation(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return;
            }

            lock (_scopeViolationConfirmationSync)
            {
                _scopeViolationConfirmationConversationIds.Remove(conversationId.Trim());
            }
        }

        private string ResolveSharedBackupPath(
            HostBridgeRequest request,
            HostToolInvocationRequest payload)
        {
            var payloadBackupPath = GetMetadataValue(payload, ToolMetadataKeys.BackupPath);
            if (CanReuseBackupPath(payloadBackupPath))
            {
                var normalized = payloadBackupPath.Trim();
                RememberSharedBackupPath(request, payload, normalized);
                return normalized;
            }

            var cacheKey = GetSharedBackupCacheKey(request, payload);
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return string.Empty;
            }

            lock (_sharedBackupSyncRoot)
            {
                string cachedBackupPath;
                if (!_sharedBackupPathByMessage.TryGetValue(cacheKey, out cachedBackupPath))
                {
                    return string.Empty;
                }

                if (CanReuseBackupPath(cachedBackupPath))
                {
                    return cachedBackupPath.Trim();
                }

                _sharedBackupPathByMessage.Remove(cacheKey);
                return string.Empty;
            }
        }

        private void RememberSharedBackupPath(
            HostBridgeRequest request,
            HostToolInvocationRequest payload,
            string backupPath)
        {
            var normalizedBackupPath = string.IsNullOrWhiteSpace(backupPath)
                ? string.Empty
                : backupPath.Trim();

            if (!CanReuseBackupPath(normalizedBackupPath))
            {
                return;
            }

            var cacheKey = GetSharedBackupCacheKey(request, payload);
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return;
            }

            lock (_sharedBackupSyncRoot)
            {
                _sharedBackupPathByMessage[cacheKey] = normalizedBackupPath;
            }
        }

        private static string GetSharedBackupCacheKey(
            HostBridgeRequest request,
            HostToolInvocationRequest payload)
        {
            var sourceMessageId = GetMetadataValue(payload, ToolMetadataKeys.SourceMessageId);
            if (!string.IsNullOrWhiteSpace(sourceMessageId))
            {
                return "message:" + sourceMessageId.Trim();
            }

            var sessionId = GetMetadataValue(payload, ToolMetadataKeys.SessionId);
            var conversationId = request == null ? string.Empty : request.SessionId ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(sessionId)
                && !string.IsNullOrWhiteSpace(conversationId))
            {
                return "session:" + sessionId.Trim() + "|conversation:" + conversationId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(conversationId))
            {
                return "conversation:" + conversationId.Trim();
            }

            return string.Empty;
        }

        private static void EnsurePayloadBackupPath(HostToolInvocationRequest payload, string backupPath)
        {
            if (payload == null || string.IsNullOrWhiteSpace(backupPath))
            {
                return;
            }

            if (payload.Metadata == null)
            {
                payload.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            payload.Metadata[ToolMetadataKeys.BackupPath] = backupPath.Trim();
        }

        private static bool CanReuseBackupPath(string backupPath)
        {
            var value = string.IsNullOrWhiteSpace(backupPath)
                ? string.Empty
                : backupPath.Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return File.Exists(value) || Directory.Exists(value);
        }

        private static void AppendSafetyMetadata(ToolExecutionResult result, ToolExecutionSafetyPreparation preparation)
        {
            if (result == null || preparation == null)
            {
                return;
            }

            if (result.Metadata == null)
            {
                result.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var pair in preparation.Metadata)
            {
                result.Metadata[pair.Key] = pair.Value;
            }

            if (preparation.BackupResult != null && !string.IsNullOrWhiteSpace(preparation.BackupResult.BackupPath))
            {
                var backupNotice = "A pre-execution backup was created: " + preparation.BackupResult.BackupPath;
                result.Content = string.IsNullOrWhiteSpace(result.Content)
                    ? backupNotice
                    : backupNotice + Environment.NewLine + result.Content;
            }
        }

        private string GetDocumentName()
        {
            var snapshot = _hostAdapter == null ? null : _hostAdapter.GetContextSnapshot();
            return snapshot == null ? string.Empty : snapshot.DocumentName ?? string.Empty;
        }

        private static string GetPreparationBackupPath(ToolExecutionSafetyPreparation preparation)
        {
            if (preparation == null)
            {
                return string.Empty;
            }

            if (preparation.BackupResult != null && !string.IsNullOrWhiteSpace(preparation.BackupResult.BackupPath))
            {
                return preparation.BackupResult.BackupPath ?? string.Empty;
            }

            string backupPath;
            return preparation.Metadata != null
                && preparation.Metadata.TryGetValue(ToolMetadataKeys.BackupPath, out backupPath)
                ? backupPath ?? string.Empty
                : string.Empty;
        }

        private static string GetMetadataValue(HostToolInvocationRequest payload, string key)
        {
            if (payload == null || payload.Metadata == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string value;
            return payload.Metadata.TryGetValue(key, out value)
                ? (value ?? string.Empty)
                : string.Empty;
        }

        /// <summary>
        /// Broker 侧的权限门已就本次调用取得用户确认（或因权限模式而豁免）时返回 true。
        /// 此时宿主侧不再重复弹确认框，避免同一次工具调用被询问两次。
        /// </summary>
        private static bool IsConfirmationAlreadyGranted(HostToolInvocationRequest payload)
        {
            return string.Equals(
                GetMetadataValue(payload, HostBridgeMetadataKeys.ConfirmationGranted),
                HostBridgeMetadataKeys.TrueValue,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetToolConversationId(HostBridgeRequest request, HostToolInvocationRequest payload)        {
            var conversationId = GetMetadataValue(payload, ToolMetadataKeys.ConversationId);
            if (!string.IsNullOrWhiteSpace(conversationId))
            {
                return conversationId.Trim();
            }

            return request == null ? string.Empty : request.SessionId ?? string.Empty;
        }

        private static string SummarizeArguments(string argumentsJson)
        {
            var text = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson.Trim();
            return text.Length <= 240 ? text : text.Substring(0, 240) + "...";
        }

        private sealed class ToolExecutionSafetyPreparation
        {
            public ToolExecutionSafetyPreparation()
            {
                Success = true;
                ErrorCode = string.Empty;
                ErrorMessage = string.Empty;
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            public bool Success { get; set; }

            public string ErrorCode { get; set; }

            public string ErrorMessage { get; set; }

            public IDictionary<string, string> Metadata { get; set; }

            public DocumentBackupResult BackupResult { get; set; }

            public static ToolExecutionSafetyPreparation CreateFailure(
                string errorCode,
                string errorMessage,
                string riskLevel,
                IDictionary<string, string> metadata = null,
                DocumentBackupResult backupResult = null)
            {
                var preparation = new ToolExecutionSafetyPreparation
                {
                    Success = false,
                    ErrorCode = errorCode ?? string.Empty,
                    ErrorMessage = errorMessage ?? string.Empty,
                    BackupResult = backupResult
                };

                if (!string.IsNullOrWhiteSpace(riskLevel))
                {
                    preparation.Metadata[ToolMetadataKeys.RiskLevel] = riskLevel;
                }

                if (metadata != null)
                {
                    foreach (var pair in metadata)
                    {
                        preparation.Metadata[pair.Key] = pair.Value;
                    }
                }

                if (backupResult != null && !string.IsNullOrWhiteSpace(backupResult.BackupPath))
                {
                    preparation.Metadata[ToolMetadataKeys.BackupPath] = backupResult.BackupPath ?? string.Empty;
                }

                return preparation;
            }
        }

        private sealed class WriteEffectPreflightTicket
        {
            public string Token { get; set; }

            public string ToolCallId { get; set; }

            public string ConversationId { get; set; }

            public string MessageId { get; set; }

            public string ToolName { get; set; }

            public string ArgumentsJson { get; set; }

            public string ApprovedEffect { get; set; }

            public string OperationSummary { get; set; }

            public string TargetWorkbookFullName { get; set; }

            public string TargetWorksheetName { get; set; }

            public string TargetAddress { get; set; }

            public DateTime CreatedAtUtc { get; set; }
        }

        private sealed class DeckBuildWorkspaceState
        {
            public string DeckId { get; set; }

            public string Title { get; set; }

            public int SlideCount { get; set; }

            public string PreviewedRevision { get; set; }

            public DateTime LastUpdatedUtc { get; set; }
        }

        private sealed class InsertTextPayload
        {
            public InsertTextPayload()
            {
                Text = string.Empty;
            }

            public string Text { get; set; }
        }
    }
}
