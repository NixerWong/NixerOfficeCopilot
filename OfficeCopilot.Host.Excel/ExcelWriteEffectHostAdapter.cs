using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Tools;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Excel
{
    public sealed partial class ExcelHostAdapter
    {
        private static readonly ExcelWipSheetRegistryStore _wipSheetRegistryStore =
            new ExcelWipSheetRegistryStore();
        private readonly object _writeEffectBaselineSync = new object();
        private readonly Dictionary<string, WorkbookWriteEffectBaseline> _writeEffectBaselines =
            new Dictionary<string, WorkbookWriteEffectBaseline>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WorkbookWipSheetRegistry> _writeEffectWipRegistries =
            new Dictionary<string, WorkbookWipSheetRegistry>(StringComparer.OrdinalIgnoreCase);

        public ExcelWriteEffectTurnResult BeginWriteEffectTurn(string conversationId, string messageId)
        {
            ExcelInterop.Workbook workbook = null;
            try
            {
                workbook = GetRequiredWorkbook();
                var baseline = CaptureWriteEffectBaseline(workbook);
                var key = GetWriteEffectTurnKey(conversationId, messageId);
                lock (_writeEffectBaselineSync)
                {
                    _writeEffectBaselines.Clear();
                    _writeEffectBaselines[key] = baseline;
                    ReconcileWipRegistry(conversationId, baseline);
                }

                return new ExcelWriteEffectTurnResult
                {
                    Success = true,
                    WorkbookFullName = baseline.WorkbookFullName
                };
            }
            catch (Exception ex)
            {
                return new ExcelWriteEffectTurnResult
                {
                    Success = false,
                    ErrorCode = "excel.write_effect_baseline_failed",
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                ReleaseComObject(workbook);
            }
        }

        public ExcelWriteEffectAssessment AssessWriteEffect(
            string conversationId,
            string messageId,
            string toolName,
            string argumentsJson)
        {
            var key = GetWriteEffectTurnKey(conversationId, messageId);
            WorkbookWriteEffectBaseline baseline;
            lock (_writeEffectBaselineSync)
            {
                if (!_writeEffectBaselines.TryGetValue(key, out baseline))
                {
                    return FailedAssessment(
                        "excel.write_effect_baseline_unavailable",
                        "本轮工作簿基线不可用；为避免误写，已阻止本次写入。");
                }
            }

            ExcelInterop.Worksheet worksheet = null;
            ExcelInterop.Range target = null;
            ExcelInterop.Workbook workbook = null;
            try
            {
                workbook = GetRequiredWorkbook();
                var currentWorkbookFullName = workbook.FullName ?? string.Empty;
                var input = new ExcelWriteEffectInput
                {
                    TargetWorkbook = workbook.Name ?? string.Empty,
                    IsOtherWorkbook = !string.Equals(
                        currentWorkbookFullName,
                        baseline.WorkbookFullName,
                        StringComparison.OrdinalIgnoreCase)
                };

                if (string.Equals(toolName, "excel_create_worksheet", StringComparison.OrdinalIgnoreCase))
                {
                    var createRequest = ExcelToolJsonSerializer.Deserialize<ExcelWorksheetCreationRequest>(argumentsJson)
                        ?? new ExcelWorksheetCreationRequest();
                    var worksheetName = (createRequest.WorksheetName ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(worksheetName))
                    {
                        return FailedAssessment("excel.worksheet_name_required", "新工作表必须提供名称。");
                    }

                    var isWipWorksheet = ExcelStagingConventions.IsWipSheet(worksheetName);
                    var nameViolation = ExcelStagingConventions.GetStagedNameViolation(worksheetName);
                    if (isWipWorksheet && nameViolation != null)
                    {
                        return FailedAssessment("excel.wip_sheet_name_invalid", nameViolation);
                    }

                    if (CaptureWorksheetCodeNames(workbook).ContainsKey(worksheetName))
                    {
                        return FailedAssessment(
                            "excel.worksheet_already_exists",
                            "工作表已存在，未执行任何更改：" + worksheetName);
                    }

                    input.TargetWorksheet = worksheetName;
                    input.IsNewRegisteredWipWorksheetCreation = isWipWorksheet;
                    input.IsNewFormalSheetThisTurn = !isWipWorksheet;
                    var createEvaluation = ExcelWriteEffectEvaluator.Evaluate(input);
                    createEvaluation.OperationSummary = (isWipWorksheet
                            ? "创建并登记本会话中间表："
                            : "创建正式工作表：")
                        + worksheetName + "；级别：" + createEvaluation.Effect;
                    return CreateAssessment(
                        createEvaluation,
                        currentWorkbookFullName,
                        worksheetName,
                        string.Empty);
                }

                if (string.Equals(toolName, "excel_cleanup_workspace", StringComparison.OrdinalIgnoreCase))
                {
                    var cleanupRequest = ExcelToolJsonSerializer.Deserialize<ExcelCleanupWorkspaceRequest>(argumentsJson)
                        ?? new ExcelCleanupWorkspaceRequest();
                    var registryResult = GetRegisteredWipWorksheets(conversationId);
                    if (registryResult == null || !registryResult.Success)
                    {
                        return FailedAssessment(
                            registryResult == null ? "excel.wip_registry_read_failed" : registryResult.ErrorCode,
                            registryResult == null
                                ? "读取本会话中间表登记失败；为避免删除用户工作表，已阻止清理。"
                                : registryResult.ErrorMessage);
                    }

                    var prefix = string.IsNullOrWhiteSpace(cleanupRequest.Prefix)
                        ? ExcelStagingConventions.WipPrefix
                        : cleanupRequest.Prefix.Trim();
                    var registeredWorksheetNames = new HashSet<string>(
                        registryResult.RegisteredWorksheetNames,
                        StringComparer.OrdinalIgnoreCase);
                    var targetNames = registryResult.RegisteredWorksheetNames
                        .Where(name => ExcelStagingConventions.IsRegisteredWipWorksheetForCleanup(
                            name,
                            prefix,
                            registeredWorksheetNames))
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var cleanupEffect = cleanupRequest.DryRun || targetNames.Count == 0
                        ? ToolWriteEffectLevels.None
                        : ToolWriteEffectLevels.Scratch;
                    var cleanupSummary = cleanupRequest.DryRun
                        ? "清理预演；仅会匹配本会话登记的中间表：" + string.Join("、", targetNames)
                        : targetNames.Count == 0
                            ? "没有符合前缀且已登记的本会话中间表；不会删除工作表。"
                            : "清理本会话登记的中间表：" + string.Join("、", targetNames);
                    cleanupSummary += "；级别：" + cleanupEffect;

                    return CreateAssessment(
                        new ExcelWriteEffectEvaluation
                        {
                            Effect = cleanupEffect,
                            OperationSummary = cleanupSummary
                        },
                        currentWorkbookFullName,
                        string.Join(", ", targetNames),
                        cleanupRequest.DryRun ? "dry-run" : prefix);
                }

                if (string.Equals(toolName, "excel_execute_vba", StringComparison.OrdinalIgnoreCase))
                {
                    var vbaRequest = ExcelToolJsonSerializer.Deserialize<ExcelVbaStageRequest>(argumentsJson)
                        ?? new ExcelVbaStageRequest();
                    return AssessVbaWriteTargets(
                        conversationId,
                        baseline,
                        workbook,
                        currentWorkbookFullName,
                        vbaRequest);
                }

                var isFormatOperation = false;
                if (!TryResolveEffectTarget(workbook, toolName, argumentsJson, out worksheet, out target, out isFormatOperation))
                {
                    input.IsStructuralOperation = true;
                    var unknown = ExcelWriteEffectEvaluator.Evaluate(input);
                    unknown.OperationSummary = "目标或写入工具无法解析；按 modify 级别保守处理。工具：" + (toolName ?? string.Empty);
                    return CreateAssessment(unknown, currentWorkbookFullName, string.Empty, string.Empty);
                }

                input.TargetWorksheet = GetWorksheetName(worksheet);
                input.TargetAddress = GetAddress(target);
                input.IsFormatOperation = isFormatOperation;
                var worksheetIdentity = GetWipWorksheetIdentity(worksheet);
                if (ExcelStagingConventions.IsWipSheet(input.TargetWorksheet))
                {
                    var currentWorksheetIdentities = CaptureWorksheetIdentities(workbook);
                    currentWorksheetIdentities.TryGetValue(
                        input.TargetWorksheet,
                        out worksheetIdentity);
                }

                input.IsRegisteredWipSheet = IsRegisteredWipWorksheet(
                    conversationId,
                    currentWorkbookFullName,
                    input.TargetWorksheet,
                    worksheetIdentity);

                WorksheetWriteEffectBaseline sheetBaseline;
                var hasBaselineSheet = baseline.Worksheets.TryGetValue(input.TargetWorksheet, out sheetBaseline);
                if (!input.IsOtherWorkbook && !hasBaselineSheet)
                {
                    input.IsNewFormalSheetThisTurn = true;
                }
                else if (!input.IsOtherWorkbook)
                {
                    var currentStructures = CaptureWorksheetStructures(worksheet);
                    if (!string.Equals(
                            sheetBaseline.CodeName,
                            worksheet.CodeName ?? string.Empty,
                            StringComparison.OrdinalIgnoreCase)
                        || !sheetBaseline.StructuralMetadataComplete
                        || !currentStructures.StructuralMetadataComplete
                        || !sheetBaseline.Structures.SequenceEqual(currentStructures.Structures))
                    {
                        input.IsStructuralOperation = true;
                    }

                    var overlapWithTable = FindRelatedTables(target, worksheet, sheetBaseline.Tables);
                    var overlapWithPivot = FindRelatedPivots(target, worksheet, sheetBaseline.Pivots);
                    input.IsStructuralOperation |= overlapWithTable.Count > 0 || overlapWithPivot.Count > 0;
                    input.ListObjectNames = overlapWithTable;
                    input.PivotTableNames = overlapWithPivot;
                    if (Convert.ToInt32(target.Areas.Count) != 1)
                    {
                        input.IsStructuralOperation = true;
                    }

                    if (!string.IsNullOrWhiteSpace(sheetBaseline.UsedRangeAddress))
                    {
                        ExcelInterop.Range baselineRange = null;
                        ExcelInterop.Range intersection = null;
                        try
                        {
                            baselineRange = worksheet.Range[sheetBaseline.UsedRangeAddress] as ExcelInterop.Range;
                            intersection = baselineRange == null
                                ? null
                                : _application.Intersect(target, baselineRange) as ExcelInterop.Range;
                            input.IntersectsBaselineUsedRange = intersection != null;
                            if (intersection != null)
                            {
                                input.NonEmptyCellCountInIntersection = Convert.ToInt32(
                                    _application.WorksheetFunction.CountA(intersection));
                                input.FormulaCountInIntersection = CountFormulas(intersection);
                            }
                        }
                        finally
                        {
                            ReleaseComObject(intersection);
                            ReleaseComObject(baselineRange);
                        }
                    }
                }

                input.TargetWorkbook = currentWorkbookFullName;
                var evaluation = ExcelWriteEffectEvaluator.Evaluate(input);
                WorksheetWriteEffectBaseline targetSheetBaseline;
                if (baseline.Worksheets.TryGetValue(input.TargetWorksheet, out targetSheetBaseline)
                    && !targetSheetBaseline.StructuralMetadataComplete)
                {
                    evaluation.OperationSummary += "；表格/透视表结构读取不完整，已按 modify 处理";
                }
                if (baseline.WorkbookFullName != currentWorkbookFullName)
                {
                    evaluation.OperationSummary = "活动工作簿已不同于本轮基线；按 modify 级别保守处理。"
                        + " 当前目标：" + currentWorkbookFullName + "；" + evaluation.OperationSummary;
                }

                return CreateAssessment(
                    evaluation,
                    currentWorkbookFullName,
                    input.TargetWorksheet,
                    input.TargetAddress);
            }
            catch (Exception ex)
            {
                return FailedAssessment(
                    "excel.write_effect_assessment_failed",
                    "写入影响预检失败；为避免误写，已阻止本次写入：" + ex.Message);
            }
            finally
            {
                ReleaseComObject(target);
                ReleaseComObject(worksheet);
                ReleaseComObject(workbook);
            }
        }

        public ExcelWipSheetRegistryResult GetRegisteredWipWorksheets(string conversationId)
        {
            ExcelInterop.Workbook workbook = null;
            try
            {
                workbook = GetRequiredWorkbook();
                var workbookFullName = workbook.FullName ?? string.Empty;
                var currentWorksheets = CaptureWorksheetIdentities(workbook);
                lock (_writeEffectBaselineSync)
                {
                    var registry = GetOrCreateWipRegistry(conversationId, workbookFullName);
                    if (ReconcileWipRegistry(registry, currentWorksheets))
                    {
                        PersistWipRegistry(conversationId, registry);
                    }

                    return CreateWipRegistryResult(registry, currentWorksheets);
                }
            }
            catch (Exception ex)
            {
                return new ExcelWipSheetRegistryResult
                {
                    Success = false,
                    ErrorCode = "excel.wip_registry_read_failed",
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                ReleaseComObject(workbook);
            }
        }

        public ExcelWipSheetRegistryResult RefreshWipSheetRegistry(
            string conversationId,
            bool registerNewWorksheets,
            string expectedWorksheetName,
            IList<string> worksheetNamesBeforeOperation)
        {
            ExcelInterop.Workbook workbook = null;
            try
            {
                workbook = GetRequiredWorkbook();
                var workbookFullName = workbook.FullName ?? string.Empty;
                var currentWorksheets = CaptureWorksheetIdentities(workbook);
                lock (_writeEffectBaselineSync)
                {
                    var registry = GetOrCreateWipRegistry(conversationId, workbookFullName);
                    if (ReconcileWipRegistry(registry, currentWorksheets))
                    {
                        PersistWipRegistry(conversationId, registry);
                    }

                    if (registerNewWorksheets)
                    {
                        if (worksheetNamesBeforeOperation == null)
                        {
                            return new ExcelWipSheetRegistryResult
                            {
                                Success = false,
                                ErrorCode = "excel.wip_registry_snapshot_unavailable",
                                ErrorMessage = "未提供本次操作的执行前工作表快照，无法登记新建中间表。"
                            };
                        }

                        var conversationPrefix = (conversationId ?? string.Empty) + "\n";
                        var baselineEntry = _writeEffectBaselines.FirstOrDefault(
                            item => item.Key.StartsWith(conversationPrefix, StringComparison.OrdinalIgnoreCase));
                        var baseline = baselineEntry.Value;
                        if (baseline == null)
                        {
                            return new ExcelWipSheetRegistryResult
                            {
                                Success = false,
                                ErrorCode = "excel.wip_registry_baseline_unavailable",
                                ErrorMessage = "未找到当前会话的工作簿写入基线，无法验证新建的中间表"
                                    + "；请检查宿主是否传递了当前会话标识。"
                            };
                        }

                        if (!string.Equals(
                            baseline.WorkbookFullName,
                            workbookFullName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return new ExcelWipSheetRegistryResult
                            {
                                Success = false,
                                ErrorCode = "excel.wip_registry_workbook_mismatch",
                                ErrorMessage = "当前活动工作簿与本轮写入基线不一致，无法验证新建的中间表。"
                            };
                        }

                        var worksheetsBeforeOperation = new HashSet<string>(
                            worksheetNamesBeforeOperation,
                            StringComparer.OrdinalIgnoreCase);
                        var worksheetNamesAtTurnStart = new HashSet<string>(
                            baseline.Worksheets.Keys,
                            StringComparer.OrdinalIgnoreCase);
                        var worksheetName = (expectedWorksheetName ?? string.Empty).Trim();
                        if (!ExcelStagingConventions.TryGetCreatedWipWorksheet(
                            worksheetName,
                            currentWorksheets,
                            worksheetsBeforeOperation,
                            worksheetNamesAtTurnStart,
                            out var registrationFailureReason))
                        {
                            return new ExcelWipSheetRegistryResult
                            {
                                Success = false,
                                ErrorCode = "excel.wip_registry_registration_unverified",
                                ErrorMessage = "无法验证指定中间表由本次创建操作新增；为避免登记其他工作表，未登记该中间表。"
                                    + " " + registrationFailureReason
                            };
                        }

                        var worksheetIdentity = SetWipWorksheetIdentity(workbook, worksheetName);
                        currentWorksheets[worksheetName] = worksheetIdentity;
                        registry.WorksheetIdentities[worksheetName] = worksheetIdentity;
                        PersistWipRegistry(conversationId, registry);
                        return CreateWipRegistryResult(registry, currentWorksheets);
                    }

                    PersistWipRegistry(conversationId, registry);
                    return CreateWipRegistryResult(registry, currentWorksheets);
                }
            }
            catch (Exception ex)
            {
                return new ExcelWipSheetRegistryResult
                {
                    Success = false,
                    ErrorCode = "excel.wip_registry_refresh_failed",
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                ReleaseComObject(workbook);
            }
        }

        private WorkbookWriteEffectBaseline CaptureWriteEffectBaseline(ExcelInterop.Workbook workbook)
        {
            var baseline = new WorkbookWriteEffectBaseline
            {
                WorkbookFullName = workbook.FullName ?? string.Empty
            };
            ExcelInterop.Sheets worksheets = null;
            try
            {
                worksheets = workbook.Worksheets;
                var count = worksheets.Count;
                for (var index = 1; index <= count; index++)
                {
                    ExcelInterop.Worksheet worksheet = null;
                    ExcelInterop.Range usedRange = null;
                    try
                    {
                        worksheet = worksheets.Item[index] as ExcelInterop.Worksheet;
                        if (worksheet == null)
                        {
                            throw new InvalidOperationException("无法读取工作簿中的工作表 #" + index + "。");
                        }

                        usedRange = worksheet.UsedRange as ExcelInterop.Range;
                        var structures = CaptureWorksheetStructures(worksheet);
                        baseline.Worksheets[GetWorksheetName(worksheet)] = new WorksheetWriteEffectBaseline
                        {
                            CodeName = worksheet.CodeName ?? string.Empty,
                            WipIdentity = GetWipWorksheetIdentity(worksheet),
                            UsedRangeAddress = GetAddress(usedRange),
                            UsedRangeRow = usedRange == null ? 1 : Convert.ToInt32(usedRange.Row),
                            UsedRangeColumn = usedRange == null ? 1 : Convert.ToInt32(usedRange.Column),
                            UsedRangeRowCount = usedRange == null ? 1 : Convert.ToInt32(usedRange.Rows.Count),
                            UsedRangeColumnCount = usedRange == null ? 1 : Convert.ToInt32(usedRange.Columns.Count),
                            IsVisible = worksheet.Visible == ExcelInterop.XlSheetVisibility.xlSheetVisible,
                            IsProtected = worksheet.ProtectContents,
                            StructuralMetadataComplete = structures.StructuralMetadataComplete,
                            Tables = structures.Tables,
                            Pivots = structures.Pivots,
                            Structures = structures.Structures
                        };
                    }
                    finally
                    {
                        ReleaseComObject(usedRange);
                        ReleaseComObject(worksheet);
                    }
                }
            }
            finally
            {
                ReleaseComObject(worksheets);
            }

            if (baseline.Worksheets.Count == 0)
            {
                throw new InvalidOperationException("工作簿基线未能记录任何工作表。");
            }

            return baseline;
        }

        private WorksheetStructureSnapshot CaptureWorksheetStructures(ExcelInterop.Worksheet worksheet)
        {
            var result = new WorksheetStructureSnapshot();
            object listObjectsObject = null;
            object listObjectObject = null;
            object listObjectRangeObject = null;
            object pivotTablesObject = null;
            object pivotTableObject = null;
            object pivotRangeObject = null;
            try
            {
                listObjectsObject = worksheet.ListObjects;
                dynamic listObjects = listObjectsObject;
                var tableCount = Convert.ToInt32(listObjects.Count);
                for (var index = 1; index <= tableCount; index++)
                {
                    listObjectObject = listObjects.Item(index);
                    dynamic listObject = listObjectObject;
                    listObjectRangeObject = listObject.Range;
                    var range = listObjectRangeObject as ExcelInterop.Range;
                    if (range == null)
                    {
                        throw new InvalidOperationException("无法读取 Excel 表格的区域。");
                    }

                    var bounds = CreateBounds(range);
                    var name = Convert.ToString(listObject.Name) ?? string.Empty;
                    result.Tables.Add(new NamedRangeBounds(name, bounds));
                    result.Structures.Add("table|" + name + "|" + bounds.ToString());
                    ReleaseComObject(listObjectRangeObject);
                    listObjectRangeObject = null;
                    ReleaseComObject(listObjectObject);
                    listObjectObject = null;
                }

                pivotTablesObject = worksheet.PivotTables(Type.Missing);
                dynamic pivotTables = pivotTablesObject;
                var pivotCount = Convert.ToInt32(pivotTables.Count);
                for (var index = 1; index <= pivotCount; index++)
                {
                    pivotTableObject = pivotTables.Item(index);
                    dynamic pivotTable = pivotTableObject;
                    pivotRangeObject = pivotTable.TableRange2;
                    var range = pivotRangeObject as ExcelInterop.Range;
                    if (range == null)
                    {
                        throw new InvalidOperationException("无法读取数据透视表的输出区域。");
                    }

                    var bounds = CreateBounds(range);
                    var name = Convert.ToString(pivotTable.Name) ?? string.Empty;
                    result.Pivots.Add(new NamedRangeBounds(name, bounds));
                    result.Structures.Add("pivot|" + name + "|" + bounds.ToString());
                    ReleaseComObject(pivotRangeObject);
                    pivotRangeObject = null;
                    ReleaseComObject(pivotTableObject);
                    pivotTableObject = null;
                }

                var sortedStructures = result.Structures.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
                result.Structures.Clear();
                foreach (var structure in sortedStructures)
                {
                    result.Structures.Add(structure);
                }
            }
            catch (Exception ex)
            {
                result.StructuralMetadataComplete = false;
                result.StructuralMetadataError = ex.Message;
                result.Tables.Clear();
                result.Pivots.Clear();
                result.Structures.Clear();
            }
            finally
            {
                ReleaseComObject(pivotRangeObject);
                ReleaseComObject(pivotTableObject);
                ReleaseComObject(pivotTablesObject);
                ReleaseComObject(listObjectRangeObject);
                ReleaseComObject(listObjectObject);
                ReleaseComObject(listObjectsObject);
            }

            return result;
        }

        private ExcelWriteEffectAssessment AssessVbaWriteTargets(
            string conversationId,
            WorkbookWriteEffectBaseline baseline,
            ExcelInterop.Workbook workbook,
            string currentWorkbookFullName,
            ExcelVbaStageRequest request)
        {
            var targets = request == null || request.DeclaredWriteTargets == null
                ? new List<ExcelVbaDeclaredWriteTarget>()
                : request.DeclaredWriteTargets;
            if (targets.Count == 0)
            {
                var undeclared = ExcelWriteEffectEvaluator.Evaluate(new ExcelWriteEffectInput
                {
                    IsStructuralOperation = true,
                    IsOtherWorkbook = !string.Equals(
                        currentWorkbookFullName,
                        baseline.WorkbookFullName,
                        StringComparison.OrdinalIgnoreCase)
                });
                undeclared.OperationSummary = "VBA 未声明写入目标；按 modify 级别保守处理。";
                return CreateAssessment(undeclared, currentWorkbookFullName, string.Empty, string.Empty);
            }

            var currentWorksheets = CaptureWorksheetIdentities(workbook);
            var effects = new List<string>();
            var summaries = new List<string>();
            foreach (var target in targets)
            {
                if (target == null || string.IsNullOrWhiteSpace(target.WorksheetName))
                {
                    return FailedAssessment(
                        "excel.vba_declared_target_invalid",
                        "VBA 声明目标缺少有效的 sheet；已阻止执行。");
                }

                var worksheetName = target.WorksheetName.Trim();
                WorksheetWriteEffectBaseline sheetBaseline;
                var existedAtTurnStart = baseline.Worksheets.TryGetValue(worksheetName, out sheetBaseline);
                string worksheetIdentity;
                var existsNow = currentWorksheets.TryGetValue(worksheetName, out worksheetIdentity);
                var isWip = ExcelStagingConventions.IsWipSheet(worksheetName);
                var nameViolation = isWip
                    ? ExcelStagingConventions.GetStagedNameViolation(worksheetName)
                    : null;
                if (nameViolation != null)
                {
                    return FailedAssessment("excel.wip_sheet_name_invalid", nameViolation);
                }

                if (target.IsNewSheet && existedAtTurnStart)
                {
                    return FailedAssessment(
                        "excel.vba_declared_target_conflict",
                        "VBA 声明为新建的工作表在本轮开始前已存在：" + worksheetName);
                }

                if (!target.IsNewSheet && !existsNow)
                {
                    return FailedAssessment(
                        "excel.vba_declared_target_missing",
                        "VBA 声明的现有目标工作表不存在：" + worksheetName);
                }

                var isRegisteredWip = false;
                if (isWip && existsNow)
                {
                    isRegisteredWip = IsRegisteredWipWorksheet(
                        conversationId,
                        currentWorkbookFullName,
                        worksheetName,
                        worksheetIdentity);
                }

                var evaluation = ExcelWriteEffectEvaluator.Evaluate(new ExcelWriteEffectInput
                {
                    TargetWorkbook = workbook.Name ?? string.Empty,
                    TargetWorksheet = worksheetName,
                    TargetAddress = target.Range ?? string.Empty,
                    IsOtherWorkbook = !string.Equals(
                        currentWorkbookFullName,
                        baseline.WorkbookFullName,
                        StringComparison.OrdinalIgnoreCase),
                    IsRegisteredWipSheet = isRegisteredWip,
                    IsNewRegisteredWipWorksheetCreation = isWip && target.IsNewSheet && !existsNow,
                    IsNewFormalSheetThisTurn = target.IsNewSheet && !isWip
                        || (!existedAtTurnStart && existsNow && !isWip),
                    IsStructuralOperation = !target.IsNewSheet && !isRegisteredWip
                });
                effects.Add(evaluation.Effect);
                summaries.Add(worksheetName
                    + (string.IsNullOrWhiteSpace(target.Range) ? string.Empty : "!" + target.Range.Trim())
                    + "（" + evaluation.Effect + "）");
            }

            var declaredTargetNames = targets
                .Where(target => target != null)
                .Select(target => target.WorksheetName);
            var declaredNewTargetNames = targets
                .Where(target => target != null && target.IsNewSheet)
                .Select(target => target.WorksheetName);
            var writeScopeProven = StagedWriteConfirmationPolicy.IsDeclaredVbaWriteScopeProvable(
                request.Code,
                declaredTargetNames,
                declaredNewTargetNames);
            if (!writeScopeProven)
            {
                effects.Add(ToolWriteEffectLevels.Modify);
                summaries.Add("静态检查无法证明所有 VBA 写入都限定于声明目标（modify）");
            }

            var highestEffect = effects.OrderByDescending(GetWriteEffectOrder).First();
            var summary = "VBA 声明写入目标：" + string.Join("、", summaries)
                + "；整体级别：" + highestEffect;
            if (targets.Any(target => target != null && !string.IsNullOrWhiteSpace(target.Range)))
            {
                summary += "；VBA 声明范围尚不能作为范围级隔离保证，现有工作表目标按 modify 保守处理";
            }

            return CreateAssessment(
                new ExcelWriteEffectEvaluation
                {
                    Effect = highestEffect,
                    OperationSummary = summary
                },
                currentWorkbookFullName,
                string.Join(", ", targets.Where(target => target != null).Select(target => target.WorksheetName)),
                string.Join(", ", targets.Where(target => target != null).Select(target => target.Range ?? string.Empty)));
        }

        private static int GetWriteEffectOrder(string effect)
        {
            if (string.Equals(effect, ToolWriteEffectLevels.Modify, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(effect, ToolWriteEffectLevels.Append, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(effect, ToolWriteEffectLevels.Scratch, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 0;
        }

        private bool TryResolveEffectTarget(
            ExcelInterop.Workbook workbook,
            string toolName,
            string argumentsJson,
            out ExcelInterop.Worksheet worksheet,
            out ExcelInterop.Range target,
            out bool isFormatOperation)
        {
            worksheet = null;
            target = null;
            isFormatOperation = false;
            string worksheetName;
            string address;
            bool useSelection;
            int matrixRows = 0;
            int matrixColumns = 0;

            if (string.Equals(toolName, "excel_write_range", StringComparison.OrdinalIgnoreCase))
            {
                var request = ExcelToolJsonSerializer.Deserialize<ExcelRangeWriteRequest>(argumentsJson);
                request = ExcelToolRequestNormalizer.NormalizeRangeWriteRequest(argumentsJson, request);
                worksheetName = request.WorksheetName;
                address = request.Address;
                useSelection = request.UseSelectionWhenAddressEmpty;
                GetMatrixSize(request.Values, out matrixRows, out matrixColumns);
            }
            else if (string.Equals(toolName, "excel_set_range_formulas", StringComparison.OrdinalIgnoreCase))
            {
                var request = ExcelToolJsonSerializer.Deserialize<ExcelRangeFormulaWriteRequest>(argumentsJson);
                worksheetName = request.WorksheetName ?? string.Empty;
                address = request.Address ?? string.Empty;
                useSelection = request.UseSelectionWhenAddressEmpty;
                GetMatrixSize(request.Formulas, out matrixRows, out matrixColumns);
            }
            else if (string.Equals(toolName, "excel_set_range_format", StringComparison.OrdinalIgnoreCase))
            {
                var request = ExcelToolJsonSerializer.Deserialize<ExcelRangeFormatWriteRequest>(argumentsJson);
                worksheetName = request.WorksheetName ?? string.Empty;
                address = request.Address ?? string.Empty;
                useSelection = request.UseSelectionWhenAddressEmpty;
                isFormatOperation = true;
            }
            else if (string.Equals(toolName, "excel_clear_range", StringComparison.OrdinalIgnoreCase))
            {
                var request = ExcelToolRequestNormalizer.NormalizeRangeRequest(
                    argumentsJson,
                    ExcelToolJsonSerializer.Deserialize<ExcelRangeRequest>(argumentsJson));
                worksheetName = request.WorksheetName;
                address = request.Address;
                useSelection = request.UseSelectionWhenAddressEmpty;
            }
            else
            {
                return false;
            }

            worksheet = ResolveWorksheet(workbook, worksheetName);
            target = ResolveWriteRange(worksheet, address, useSelection);
            if (target == null)
            {
                throw new InvalidOperationException("无法解析本次写入的目标区域。");
            }

            if (matrixRows > 0 && matrixColumns > 0)
            {
                var initialTarget = target;
                var resizedTarget = ResizeRangeForWrite(initialTarget, matrixRows, matrixColumns);
                if (resizedTarget == null)
                {
                    throw new InvalidOperationException("无法确定矩阵写入后的目标区域。");
                }

                if (!ReferenceEquals(initialTarget, resizedTarget))
                {
                    ReleaseComObject(initialTarget);
                }

                target = resizedTarget;
            }

            return true;
        }

        private static void GetMatrixSize(IList<string[]> rows, out int rowCount, out int columnCount)
        {
            rowCount = rows == null ? 0 : rows.Count;
            columnCount = 0;
            if (rows == null)
            {
                return;
            }

            for (var index = 0; index < rows.Count; index++)
            {
                var columns = rows[index] == null ? 0 : rows[index].Length;
                if (index == 0)
                {
                    columnCount = columns;
                }
                else if (columns != columnCount)
                {
                    columnCount = 0;
                    return;
                }
            }

            if (columnCount <= 0)
            {
                rowCount = 0;
            }
        }

        private static int CountFormulas(ExcelInterop.Range range)
        {
            ExcelInterop.Range formulas = null;
            try
            {
                formulas = range.SpecialCells(ExcelInterop.XlCellType.xlCellTypeFormulas, Type.Missing)
                    as ExcelInterop.Range;
                return formulas == null ? 0 : Convert.ToInt32(formulas.CountLarge);
            }
            catch (COMException ex)
            {
                if (ex.ErrorCode == unchecked((int)0x800A03EC))
                {
                    return 0;
                }

                throw;
            }
            finally
            {
                ReleaseComObject(formulas);
            }
        }

        private static List<string> FindRelatedTables(
            ExcelInterop.Range target,
            ExcelInterop.Worksheet worksheet,
            IList<NamedRangeBounds> tables)
        {
            return FindRelatedNamedRanges(target, worksheet, tables, true);
        }

        private static List<string> FindRelatedPivots(
            ExcelInterop.Range target,
            ExcelInterop.Worksheet worksheet,
            IList<NamedRangeBounds> pivots)
        {
            return FindRelatedNamedRanges(target, worksheet, pivots, false);
        }

        private static List<string> FindRelatedNamedRanges(
            ExcelInterop.Range target,
            ExcelInterop.Worksheet worksheet,
            IList<NamedRangeBounds> items,
            bool includeExpansionEdges)
        {
            var names = new List<string>();
            var targetBounds = CreateBounds(target);
            foreach (var item in items ?? new List<NamedRangeBounds>())
            {
                if (targetBounds.Intersects(item.Bounds)
                    || (includeExpansionEdges && targetBounds.IsAdjacentToTable(item.Bounds)))
                {
                    names.Add(item.Name);
                }
            }

            return names;
        }

        private static RangeBounds CreateBounds(ExcelInterop.Range range)
        {
            var firstRow = Convert.ToInt32(range.Row);
            var firstColumn = Convert.ToInt32(range.Column);
            return new RangeBounds(
                firstRow,
                firstColumn,
                Convert.ToInt32(range.Rows.Count),
                Convert.ToInt32(range.Columns.Count));
        }

        private static ExcelWriteEffectAssessment CreateAssessment(
            ExcelWriteEffectEvaluation evaluation,
            string workbookFullName,
            string worksheetName,
            string address)
        {
            return new ExcelWriteEffectAssessment
            {
                Success = true,
                Effect = evaluation.Effect,
                OperationSummary = evaluation.OperationSummary,
                TargetWorkbookFullName = workbookFullName ?? string.Empty,
                TargetWorksheetName = worksheetName ?? string.Empty,
                TargetAddress = address ?? string.Empty
            };
        }

        private static ExcelWriteEffectAssessment FailedAssessment(string errorCode, string errorMessage)
        {
            return new ExcelWriteEffectAssessment
            {
                Success = false,
                ErrorCode = errorCode ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }

        private static string GetWriteEffectTurnKey(string conversationId, string messageId)
        {
            return (conversationId ?? string.Empty) + "\n" + (messageId ?? string.Empty);
        }

        private bool IsRegisteredWipWorksheet(
            string conversationId,
            string workbookFullName,
            string worksheetName,
            string worksheetIdentity)
        {
            if (string.IsNullOrWhiteSpace(worksheetIdentity))
            {
                return false;
            }

            lock (_writeEffectBaselineSync)
            {
                WorkbookWipSheetRegistry registry;
                string registeredIdentity;
                return _writeEffectWipRegistries.TryGetValue(conversationId ?? string.Empty, out registry)
                    && string.Equals(
                        registry.WorkbookFullName,
                        workbookFullName ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)
                    && registry.WorksheetIdentities.TryGetValue(worksheetName ?? string.Empty, out registeredIdentity)
                    && string.Equals(
                        registeredIdentity,
                        worksheetIdentity,
                        StringComparison.OrdinalIgnoreCase);
            }
        }

        private void ReconcileWipRegistry(
            string conversationId,
            WorkbookWriteEffectBaseline baseline)
        {
            var registry = GetOrCreateWipRegistry(conversationId, baseline.WorkbookFullName);
            var currentWorksheets = baseline.Worksheets.ToDictionary(
                worksheet => worksheet.Key,
                worksheet => worksheet.Value.WipIdentity,
                StringComparer.OrdinalIgnoreCase);
            InvalidateDuplicateWipWorksheetIdentities(currentWorksheets);
            if (ReconcileWipRegistry(registry, currentWorksheets))
            {
                PersistWipRegistry(conversationId, registry);
            }
        }

        private WorkbookWipSheetRegistry GetOrCreateWipRegistry(
            string conversationId,
            string workbookFullName)
        {
            var key = conversationId ?? string.Empty;
            WorkbookWipSheetRegistry registry;
            if (!_writeEffectWipRegistries.TryGetValue(key, out registry)
                || !string.Equals(
                    registry.WorkbookFullName,
                    workbookFullName ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
            {
                registry = new WorkbookWipSheetRegistry(
                    workbookFullName,
                    _wipSheetRegistryStore.Load(key, workbookFullName));
                _writeEffectWipRegistries[key] = registry;
            }

            return registry;
        }

        private static bool ReconcileWipRegistry(
            WorkbookWipSheetRegistry registry,
            IDictionary<string, string> currentWorksheets)
        {
            var changed = false;
            foreach (var name in registry.WorksheetIdentities.Keys.ToList())
            {
                string currentIdentity;
                if (!currentWorksheets.TryGetValue(name, out currentIdentity)
                    || !string.Equals(
                        registry.WorksheetIdentities[name],
                        currentIdentity ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase))
                {
                    registry.WorksheetIdentities.Remove(name);
                    changed = true;
                }
            }

            return changed;
        }

        private static void PersistWipRegistry(
            string conversationId,
            WorkbookWipSheetRegistry registry)
        {
            if (string.IsNullOrWhiteSpace(conversationId)
                && registry.WorksheetIdentities.Count == 0)
            {
                return;
            }

            _wipSheetRegistryStore.Save(
                conversationId,
                registry.WorkbookFullName,
                registry.WorksheetIdentities);
        }

        private static ExcelWipSheetRegistryResult CreateWipRegistryResult(
            WorkbookWipSheetRegistry registry,
            IDictionary<string, string> currentWorksheets)
        {
            return new ExcelWipSheetRegistryResult
            {
                Success = true,
                RegisteredWorksheetNames = registry.WorksheetIdentities.Keys
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                CurrentWorksheetNames = currentWorksheets.Keys
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        private static IDictionary<string, string> CaptureWorksheetCodeNames(ExcelInterop.Workbook workbook)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ExcelInterop.Sheets worksheets = null;
            try
            {
                worksheets = workbook.Worksheets;
                for (var index = 1; index <= worksheets.Count; index++)
                {
                    ExcelInterop.Worksheet worksheet = null;
                    try
                    {
                        worksheet = worksheets.Item[index] as ExcelInterop.Worksheet;
                        if (worksheet == null)
                        {
                            throw new InvalidOperationException("无法读取工作簿中的工作表 #" + index + "。");
                        }

                        result[GetWorksheetName(worksheet)] = worksheet.CodeName ?? string.Empty;
                    }
                    finally
                    {
                        ReleaseComObject(worksheet);
                    }
                }
            }
            finally
            {
                ReleaseComObject(worksheets);
            }

            return result;
        }

        private static IDictionary<string, string> CaptureWorksheetIdentities(ExcelInterop.Workbook workbook)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ExcelInterop.Sheets worksheets = null;
            try
            {
                worksheets = workbook.Worksheets;
                for (var index = 1; index <= worksheets.Count; index++)
                {
                    ExcelInterop.Worksheet worksheet = null;
                    try
                    {
                        worksheet = worksheets.Item[index] as ExcelInterop.Worksheet;
                        if (worksheet == null)
                        {
                            throw new InvalidOperationException("无法读取工作簿中的工作表 #" + index + "。");
                        }

                        result[GetWorksheetName(worksheet)] = GetWipWorksheetIdentity(worksheet);
                    }
                    finally
                    {
                        ReleaseComObject(worksheet);
                    }
                }
            }
            finally
            {
                ReleaseComObject(worksheets);
            }

            InvalidateDuplicateWipWorksheetIdentities(result);

            return result;
        }

        private static void InvalidateDuplicateWipWorksheetIdentities(
            IDictionary<string, string> worksheetIdentities)
        {
            var duplicatedIdentities = worksheetIdentities
                .Where(pair => ExcelStagingConventions.IsWipWorksheetRegistryIdentity(pair.Value))
                .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group.Select(pair => pair.Key))
                .ToList();
            foreach (var worksheetName in duplicatedIdentities)
            {
                worksheetIdentities[worksheetName] = string.Empty;
            }
        }

        private static string GetWipWorksheetIdentity(ExcelInterop.Worksheet worksheet)
        {
            object customPropertiesObject = null;
            object customPropertyObject = null;
            try
            {
                customPropertiesObject = worksheet.CustomProperties;
                dynamic customProperties = customPropertiesObject;
                var count = Convert.ToInt32(customProperties.Count);
                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        customPropertyObject = customProperties.Item(index);
                        dynamic customProperty = customPropertyObject;
                        var propertyName = Convert.ToString(customProperty.Name) ?? string.Empty;
                        if (string.Equals(
                            propertyName,
                            ExcelStagingConventions.WipIdentityCustomPropertyName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            var marker = Convert.ToString(customProperty.Value) ?? string.Empty;
                            string registryIdentity;
                            if (!ExcelStagingConventions.TryFormatWipWorksheetIdentity(
                                marker,
                                out registryIdentity))
                            {
                                throw new InvalidOperationException(
                                    "工作表 " + GetWorksheetName(worksheet)
                                    + " 的 WIP 身份标记无效。");
                            }

                            return registryIdentity;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(customPropertyObject);
                        customPropertyObject = null;
                    }
                }

                return worksheet.CodeName ?? string.Empty;
            }
            finally
            {
                ReleaseComObject(customPropertyObject);
                ReleaseComObject(customPropertiesObject);
            }
        }

        private static string SetWipWorksheetIdentity(
            ExcelInterop.Workbook workbook,
            string worksheetName)
        {
            ExcelInterop.Sheets worksheets = null;
            ExcelInterop.Worksheet worksheet = null;
            try
            {
                worksheets = workbook.Worksheets;
                for (var index = 1; index <= worksheets.Count; index++)
                {
                    worksheet = worksheets.Item[index] as ExcelInterop.Worksheet;
                    if (worksheet == null)
                    {
                        continue;
                    }

                    if (string.Equals(
                        GetWorksheetName(worksheet),
                        worksheetName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return SetWipWorksheetIdentity(worksheet);
                    }

                    ReleaseComObject(worksheet);
                    worksheet = null;
                }

                throw new InvalidOperationException(
                    "执行后未找到待登记的中间表：" + (worksheetName ?? string.Empty));
            }
            finally
            {
                ReleaseComObject(worksheet);
                ReleaseComObject(worksheets);
            }
        }

        private static string SetWipWorksheetIdentity(ExcelInterop.Worksheet worksheet)
        {
            var marker = ExcelStagingConventions.CreateWipWorksheetIdentityMarker();
            object customPropertiesObject = null;
            object customPropertyObject = null;
            try
            {
                customPropertiesObject = worksheet.CustomProperties;
                dynamic customProperties = customPropertiesObject;
                var count = Convert.ToInt32(customProperties.Count);
                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        customPropertyObject = customProperties.Item(index);
                        dynamic customProperty = customPropertyObject;
                        var propertyName = Convert.ToString(customProperty.Name) ?? string.Empty;
                        if (string.Equals(
                            propertyName,
                            ExcelStagingConventions.WipIdentityCustomPropertyName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            customProperty.Value = marker;
                            return GetWipWorksheetIdentity(worksheet);
                        }
                    }
                    finally
                    {
                        ReleaseComObject(customPropertyObject);
                        customPropertyObject = null;
                    }
                }

                customPropertyObject = customProperties.Add(
                    ExcelStagingConventions.WipIdentityCustomPropertyName,
                    marker);
                return GetWipWorksheetIdentity(worksheet);
            }
            finally
            {
                ReleaseComObject(customPropertyObject);
                ReleaseComObject(customPropertiesObject);
            }
        }

        private sealed class WorkbookWriteEffectBaseline
        {
            public WorkbookWriteEffectBaseline()
            {
                WorkbookFullName = string.Empty;
                Worksheets = new Dictionary<string, WorksheetWriteEffectBaseline>(StringComparer.OrdinalIgnoreCase);
            }

            public string WorkbookFullName { get; set; }

            public IDictionary<string, WorksheetWriteEffectBaseline> Worksheets { get; private set; }
        }

        private sealed class WorkbookWipSheetRegistry
        {
            public WorkbookWipSheetRegistry(
                string workbookFullName,
                IDictionary<string, string> worksheetIdentities)
            {
                WorkbookFullName = workbookFullName ?? string.Empty;
                WorksheetIdentities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (worksheetIdentities == null)
                {
                    return;
                }

                foreach (var worksheet in worksheetIdentities)
                {
                    WorksheetIdentities.Add(worksheet.Key, worksheet.Value);
                }
            }

            public string WorkbookFullName { get; private set; }

            public IDictionary<string, string> WorksheetIdentities { get; private set; }
        }

        private sealed class WorksheetWriteEffectBaseline
        {
            public WorksheetWriteEffectBaseline()
            {
                CodeName = string.Empty;
                WipIdentity = string.Empty;
                UsedRangeAddress = string.Empty;
                Tables = new List<NamedRangeBounds>();
                Pivots = new List<NamedRangeBounds>();
                Structures = new List<string>();
            }

            public string CodeName { get; set; }

            public string WipIdentity { get; set; }

            public string UsedRangeAddress { get; set; }

            public int UsedRangeRow { get; set; }

            public int UsedRangeColumn { get; set; }

            public int UsedRangeRowCount { get; set; }

            public int UsedRangeColumnCount { get; set; }

            public bool IsVisible { get; set; }

            public bool IsProtected { get; set; }

            public bool StructuralMetadataComplete { get; set; }

            public IList<NamedRangeBounds> Tables { get; set; }

            public IList<NamedRangeBounds> Pivots { get; set; }

            public IList<string> Structures { get; set; }
        }

        private sealed class WorksheetStructureSnapshot
        {
            public WorksheetStructureSnapshot()
            {
                StructuralMetadataComplete = true;
                Tables = new List<NamedRangeBounds>();
                Pivots = new List<NamedRangeBounds>();
                Structures = new List<string>();
            }

            public bool StructuralMetadataComplete { get; set; }

            public string StructuralMetadataError { get; set; }

            public IList<NamedRangeBounds> Tables { get; private set; }

            public IList<NamedRangeBounds> Pivots { get; private set; }

            public IList<string> Structures { get; private set; }
        }

        private sealed class NamedRangeBounds
        {
            public NamedRangeBounds(string name, RangeBounds bounds)
            {
                Name = name ?? string.Empty;
                Bounds = bounds;
            }

            public string Name { get; private set; }

            public RangeBounds Bounds { get; private set; }
        }

        private struct RangeBounds : IEquatable<RangeBounds>
        {
            public RangeBounds(int firstRow, int firstColumn, int rowCount, int columnCount)
            {
                FirstRow = firstRow;
                FirstColumn = firstColumn;
                RowCount = rowCount;
                ColumnCount = columnCount;
            }

            public int FirstRow { get; private set; }

            public int FirstColumn { get; private set; }

            public int RowCount { get; private set; }

            public int ColumnCount { get; private set; }

            private int LastRow { get { return FirstRow + RowCount - 1; } }

            private int LastColumn { get { return FirstColumn + ColumnCount - 1; } }

            public bool Intersects(RangeBounds other)
            {
                return FirstRow <= other.LastRow
                    && LastRow >= other.FirstRow
                    && FirstColumn <= other.LastColumn
                    && LastColumn >= other.FirstColumn;
            }

            public bool IsAdjacentToTable(RangeBounds table)
            {
                var sharesColumns = FirstColumn <= table.LastColumn && LastColumn >= table.FirstColumn;
                var sharesRows = FirstRow <= table.LastRow && LastRow >= table.FirstRow;
                return (sharesColumns && FirstRow == table.LastRow + 1)
                    || (sharesRows && FirstColumn == table.LastColumn + 1);
            }

            public bool Equals(RangeBounds other)
            {
                return FirstRow == other.FirstRow
                    && FirstColumn == other.FirstColumn
                    && RowCount == other.RowCount
                    && ColumnCount == other.ColumnCount;
            }

            public override bool Equals(object obj)
            {
                return obj is RangeBounds && Equals((RangeBounds)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (((FirstRow * 397) ^ FirstColumn) * 397 ^ RowCount) * 397 ^ ColumnCount;
                }
            }

            public override string ToString()
            {
                return FirstRow + "," + FirstColumn + "," + RowCount + "," + ColumnCount;
            }
        }
    }
}
