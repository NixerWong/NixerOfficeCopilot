using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Verification;
using OfficeCopilot.Host.Excel.Verification.Backends.Com;
using OfficeCopilot.Host.Excel.Verification.Backends.Managed;
using OfficeCopilot.HostBridge.Contracts;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel
{
    /// <summary>
    /// ExcelHostAdapter 的门面部分：把区域/表格/查询/数据模型/分析/备份等能力
    /// 委托给对应的 Excel 服务实现，适配器本身只负责组合与转发。
    /// </summary>
    public sealed partial class ExcelHostAdapter
    {
        private ExcelAnalysisService _analysisService;
        private ExcelAggregationService _aggregationService;
        private ExcelBackupService _backupService;
        private ExcelConnectionService _connectionService;
        private ExcelDataModelService _dataModelService;
        private ExcelRangeService _rangeService;
        private ExcelTableService _tableService;
        private ExcelVbaService _vbaService;
        private ComCalcBackend _comCalcBackend;
        private ExcelAggregateVerificationService _aggregateVerificationService;
        private VerificationEngine _verificationEngine;

        private void InitializeServices(ExcelInterop.Application application)
        {
            _analysisService = new ExcelAnalysisService(application);
            _aggregationService = new ExcelAggregationService(application);
            _backupService = new ExcelBackupService(application);
            _connectionService = new ExcelConnectionService(application);
            _dataModelService = new ExcelDataModelService(application);
            _rangeService = new ExcelRangeService(application, this);
            _tableService = new ExcelTableService(application);
            _vbaService = new ExcelVbaService(application);
            _comCalcBackend = new ComCalcBackend(application);
            _aggregateVerificationService = new ExcelAggregateVerificationService(
                application,
                _aggregationService,
                _comCalcBackend);
            _verificationEngine = new VerificationEngine(
                _aggregationService,
                _comCalcBackend,
                new ManagedCalcBackend(application));
        }

        public bool CanExecuteVba
        {
            get { return _vbaService.CanExecuteVba; }
        }

        public bool CanOpenInVbaEditor
        {
            get { return _vbaService.CanOpenInVbaEditor; }
        }

        public VbaExecutionAvailability GetVbaExecutionAvailability(bool verifyWriteAccess)
        {
            return _vbaService.GetVbaExecutionAvailability(verifyWriteAccess);
        }

        public VbaExecutionAvailability GetStagedWriteProtectionAvailability()
        {
            return _vbaService.GetStagedWriteProtectionAvailability();
        }

        public VbaExecutionAvailability GetDeclaredWriteProtectionAvailability(
            IList<string> allowedWorksheetNames)
        {
            return _vbaService.GetDeclaredWriteProtectionAvailability(allowedWorksheetNames);
        }

        public VbaExecutionAvailability GetVbaEditorAvailability(bool verifyWriteAccess)
        {
            return _vbaService.GetVbaEditorAvailability(verifyWriteAccess);
        }

        public VbaEditorOpenResult OpenInVbaEditor(VbaEditorOpenRequest request)
        {
            return _vbaService.OpenInVbaEditor(request);
        }

        public ExcelVbaModuleListResult ListVbaModules()
        {
            return _vbaService.ListVbaModules();
        }

        public ExcelVbaModuleViewResult ViewVbaModule(ExcelVbaModuleViewRequest request)
        {
            return _vbaService.ViewVbaModule(request);
        }

        public VbaSyntaxValidationResult ValidateVbaSyntax(string code)
        {
            return _vbaService.ValidateVbaSyntax(code);
        }

        public VbaExecutionResult ExecuteVba(VbaExecutionRequest request)
        {
            return _vbaService.ExecuteVba(request);
        }

        public ExcelAnalyzeRangeResult AnalyzeRange(ExcelAnalyzeRangeRequest request)
        {
            return _analysisService.AnalyzeRange(request);
        }

        public ExcelRangeFindResult FindInRange(ExcelRangeFindRequest request)
        {
            return _analysisService.FindInRange(request);
        }

        public ExcelRangeFormulaValidationResult ValidateRangeFormulas(ExcelRangeFormulaValidationRequest request)
        {
            return _analysisService.ValidateRangeFormulas(request);
        }

        public ExcelProfileRangeResult ProfileRange(ExcelProfileRangeRequest request)
        {
            return _aggregationService.ProfileRange(request);
        }

        public ExcelAggregateResult Aggregate(ExcelAggregateRequest request)
        {
            request = request ?? new ExcelAggregateRequest();
            var result = _aggregationService.Aggregate(request);
            var basis = BuildAggregateVerificationBasis(request, result);
            var report = VerifyAggregateResult(request, result, basis);
            result.Verification = ExcelVerificationResult.FromReport(
                report,
                ToolResultVerificationFields.ProducerManaged,
                false,
                basis);
            var hasMultipleSources = (request.Sources != null && request.Sources.Count > 0)
                || (request.Lookups != null && request.Lookups.Count > 0);
            var verificationWarning = hasMultipleSources
                ? "多源或 lookup 只读结果没有已写入的结果范围可供独立复核；请报告完整扫描结果并保留 C 档，不要调用 excel_assert 为对话结果补造验证。"
                : "自动交叉复核未完全通过；请查看 verification 报告中的差异与可信度。";
            if (!result.Verification.Passed
                && !result.Warnings.Contains(verificationWarning))
            {
                result.Warnings.Add(verificationWarning);
            }

            return result;
        }

        public ExcelReconcileResult Reconcile(ExcelReconcileRequest request)
        {
            return _aggregationService.Reconcile(request);
        }

        public ExcelVerificationReport VerifyAssertions(ExcelVerificationPlan request)
        {
            return _verificationEngine.Verify(request);
        }

        private ExcelVerificationReport VerifyAggregateResult(
            ExcelAggregateRequest request,
            ExcelAggregateResult result,
            ExcelVerificationBasis basis)
        {
            if (request.Lookups != null && request.Lookups.Count > 0)
            {
                return CreateUnverifiedReport(
                    "lookup 只读结果当前没有受支持的独立复核路径；应报告完整扫描结果并保留 C 档标记。");
            }

            try
            {
                var groupedReport = _aggregateVerificationService.TryVerify(
                    request,
                    result,
                    basis);
                if (groupedReport != null)
                {
                    return groupedReport;
                }
            }
            catch (Exception exception)
            {
                if (Com.ExcelComErrorClassifier.IsBusyException(exception))
                {
                    throw;
                }

                return CreateUnverifiedReport(
                    "自动独立聚合复核无法完成：" + exception.Message);
            }

            if (result.IsTruncated)
            {
                return CreateUnverifiedReport(
                    "聚合输出已截断，无法用已返回分组计算完整总计，因此未执行自动独立复核。");
            }

            var measures = (request.Measures ?? new List<ExcelAggregateMeasure>())
                .Where(measure => measure != null)
                .ToList();
            var groupColumnCount = result.Columns.Count - measures.Count;
            if (groupColumnCount < 0)
            {
                return CreateUnverifiedReport("聚合结果列结构与请求度量不一致，无法自动复核。");
            }

            var plan = new ExcelVerificationPlan
            {
                Basis = basis
            };
            var skippedFunctions = new List<string>();
            for (var index = 0; index < measures.Count; index++)
            {
                var measure = measures[index];
                var function = (measure.Function ?? string.Empty).Trim();
                if (!string.Equals(function, "sum", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(function, "count", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(function, "errorCount", StringComparison.OrdinalIgnoreCase))
                {
                    skippedFunctions.Add(function);
                    continue;
                }

                var isRowCount = string.Equals(function, "count", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(measure.Column);
                if (isRowCount)
                {
                    skippedFunctions.Add("count(*)（与空白行过滤后的聚合行计数语义不同）");
                    continue;
                }

                double expectedValue = 0d;
                foreach (var row in result.Rows)
                {
                    if (row == null || row.Length <= groupColumnCount + index)
                    {
                        return CreateUnverifiedReport(
                            "聚合输出行结构不完整，无法计算 " + function + " 的已输出合计。");
                    }

                    double measureValue;
                    if (!double.TryParse(
                        row[groupColumnCount + index],
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out measureValue)
                        || double.IsNaN(measureValue)
                        || double.IsInfinity(measureValue))
                    {
                        return CreateUnverifiedReport(
                            "聚合输出中的 " + function + " 度量不是有限数值，无法自动复核。");
                    }

                    expectedValue += measureValue;
                }

                if (double.IsNaN(expectedValue) || double.IsInfinity(expectedValue))
                {
                    return CreateUnverifiedReport(
                        "聚合输出 " + function + " 度量总计超出有限数值范围，无法自动复核。");
                }

                var verificationFunction = string.Equals(
                    function,
                    "count",
                    StringComparison.OrdinalIgnoreCase)
                    ? "counta"
                    : function;
                plan.Assertions.Add(new ExcelAssertion
                {
                    Id = string.IsNullOrWhiteSpace(measure.Alias)
                        ? (measure.Column ?? function) + "_" + function
                        : measure.Alias,
                    Kind = "scalar",
                    Target = new ExcelAssertionSide
                    {
                        WorksheetName = result.WorksheetName ?? string.Empty,
                        Address = string.IsNullOrWhiteSpace(result.TableName)
                            ? (result.Address ?? string.Empty)
                            : string.Empty,
                        TableName = result.TableName ?? string.Empty,
                        Column = measure.Column ?? string.Empty,
                        Function = verificationFunction,
                        Filters = plan.Basis.Filters.Select(filter => new CalcFilter
                        {
                            Column = filter.Column,
                            Operator = filter.Operator,
                            Value = filter.Value
                        }).ToList()
                    },
                    ExpectedValue = expectedValue,
                    Tolerance = Math.Max(1e-6d, result.Rows.Count * 1e-10d)
                });
            }

            if (plan.Assertions.Count == 0)
            {
                return CreateUnverifiedReport(
                    "当前聚合没有可自动交叉复核的 sum/count/errorCount 度量。");
            }

            ExcelVerificationReport report;
            try
            {
                report = _verificationEngine.Verify(plan, targetProducedByManaged: true);
            }
            catch (Exception exception)
            {
                if (Com.ExcelComErrorClassifier.IsBusyException(exception))
                {
                    throw;
                }

                return CreateUnverifiedReport(
                    "自动交叉复核无法完成：" + exception.Message);
            }

            foreach (var function in skippedFunctions)
            {
                report.Notes.Add(
                    "未自动复核聚合函数 " + function
                    + "；V3-5 当前仅自动复核 sum/count/errorCount。");
            }

            if (skippedFunctions.Count > 0)
            {
                report.Passed = false;
                report.Tier = "C";
            }

            if (!report.Passed)
            {
                report.Notes.Add(
                    "自动交叉复核未完全通过；请查看差异与后端可信度。");
            }

            return report;
        }

        internal static ExcelVerificationBasis BuildAggregateVerificationBasis(
            ExcelAggregateRequest request,
            ExcelAggregateResult result)
        {
            var isUnion = request.Sources != null && request.Sources.Count > 0;
            var hasLookups = request.Lookups != null && request.Lookups.Count > 0;
            var lookups = (request.Lookups ?? new List<ExcelAggregateLookup>())
                .Where(lookup => lookup != null)
                .ToList();
            var lookupJoins = lookups
                .Select(lookup => string.IsNullOrWhiteSpace(lookup.Join)
                    ? "left"
                    : lookup.Join.Trim().ToLowerInvariant())
                .ToList();
            var lookupSummaries = result.LookupSummaries ?? new List<ExcelAggregateLookupSummary>();
            return new ExcelVerificationBasis
            {
                HeaderMode = request.HeaderMode ?? "auto",
                Source = isUnion
                    ? null
                    : new ExcelSourceRef
                    {
                        WorksheetName = result.WorksheetName ?? string.Empty,
                        Address = string.IsNullOrWhiteSpace(result.TableName)
                            ? (result.Address ?? string.Empty)
                            : string.Empty,
                        TableName = result.TableName ?? string.Empty
                    },
                Sources = isUnion
                    ? (result.SourceSummaries ?? new List<ExcelAggregateSourceSummary>())
                        .Select(source => new ExcelVerificationSource
                        {
                            Sheet = source.WorksheetName,
                            Range = source.Address,
                            Alias = source.Alias
                        }).ToList()
                    : new List<ExcelVerificationSource>(),
                Topology = hasLookups ? "lookup" : isUnion ? "union" : "single",
                Keys = hasLookups
                    ? lookups
                        .Where(lookup => !string.IsNullOrWhiteSpace(lookup.ForeignKey))
                        .Select(lookup => lookup.ForeignKey)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : new List<string>(),
                Join = lookupJoins.Count > 0
                    && lookupJoins.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                    ? lookupJoins[0]
                    : null,
                Lookups = lookups.Select((lookup, index) =>
                {
                    var summary = index < lookupSummaries.Count
                        ? lookupSummaries[index]
                        : null;
                    return new ExcelVerificationLookup
                    {
                        Sheet = summary == null
                            ? (lookup.Source == null ? string.Empty : lookup.Source.WorksheetName)
                            : summary.WorksheetName,
                        Range = summary == null
                            ? (lookup.Source == null ? string.Empty : lookup.Source.Address)
                            : summary.Address,
                        TableName = summary == null
                            ? (lookup.Source == null ? string.Empty : lookup.Source.TableName)
                            : summary.TableName,
                        Key = lookup.Key ?? string.Empty,
                        ForeignKey = lookup.ForeignKey ?? string.Empty,
                        Columns = lookup.Columns == null
                            ? new List<string>()
                            : new List<string>(lookup.Columns),
                        Join = lookupJoins[index],
                        OnDuplicateKey = string.IsNullOrWhiteSpace(lookup.OnDuplicateKey)
                            ? "error"
                            : lookup.OnDuplicateKey.Trim().ToLowerInvariant()
                    };
                }).ToList(),
                Note = hasLookups
                    ? string.Join("; ", lookups.Select((lookup, index) =>
                        "lookup " + lookup.ForeignKey + " -> " + lookup.Key
                        + " (" + lookupJoins[index] + ")"))
                    : null,
                Filters = (request.Filters ?? new List<ExcelAggregateFilter>())
                    .Where(filter => filter != null)
                    .Select(filter => new CalcFilter
                    {
                        Column = filter.Column,
                        Operator = filter.Operator,
                        Value = filter.Value
                    }).ToList(),
                GroupBy = request.GroupBy == null
                    ? new List<string>()
                    : new List<string>(request.GroupBy),
                Measures = (request.Measures ?? new List<ExcelAggregateMeasure>())
                    .Where(measure => measure != null)
                    .Select(measure => new ExcelVerificationMeasure
                    {
                        Column = measure.Column,
                        Function = measure.Function
                    }).ToList(),
                DerivedGroupBy = (request.DerivedGroupBy ?? new List<ExcelAggregateDerivedGroup>())
                    .Where(group => group != null)
                    .Select(group => new ExcelVerificationDerivedGroup
                    {
                        Column = group.Column,
                        Transform = group.Transform,
                        Alias = group.Alias,
                        Size = group.Size
                    }).ToList()
            };
        }

        private static ExcelVerificationReport CreateUnverifiedReport(string reason)
        {
            var report = new ExcelVerificationReport
            {
                Passed = false,
                Tier = "C"
            };
            report.Notes.Add(reason);
            return report;
        }

        /// <summary>
        /// 分步执行 VBA：先校验中间表命名，可选地做金丝雀试跑，再执行全量，
        /// 最后回读输出表画像并校验期望，让模型在不逐行读取的前提下确认这一步到底做出了什么。
        /// </summary>
        public ExcelVbaStageResult ExecuteVbaStage(ExcelVbaStageRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            var namingViolation = ExcelStagingConventions.GetStagedNameViolation(request.OutputSheet);
            if (namingViolation != null)
            {
                // 在执行之前拦下，而不是执行完再抱怨命名——否则表已经写出去了，纠正命名要再改一次表。
                return new ExcelVbaStageResult
                {
                    Success = false,
                    Message = namingViolation,
                    StageName = request.StageName ?? string.Empty,
                    OutputSheet = request.OutputSheet ?? string.Empty,
                    ExpectationsPassed = false,
                    ExpectationFailures = { namingViolation }
                };
            }

            var result = new ExcelVbaStageResult
            {
                StageName = request.StageName ?? string.Empty,
                OutputSheet = request.OutputSheet ?? string.Empty
            };

            if (ShouldRunProbe(request) && !TryRunProbe(request, result))
            {
                return result;
            }

            var declaredTargetNames = request.DeclaredWriteTargets == null
                ? new List<string>()
                : request.DeclaredWriteTargets
                    .Where(target => target != null && !string.IsNullOrWhiteSpace(target.WorksheetName))
                    .Select(target => target.WorksheetName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            var declaredNewTargetNames = request.DeclaredWriteTargets == null
                ? new List<string>()
                : request.DeclaredWriteTargets
                    .Where(target => target != null
                        && target.IsNewSheet
                        && !string.IsNullOrWhiteSpace(target.WorksheetName))
                    .Select(target => target.WorksheetName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            ExcelVbaScopeSnapshot scopeBaseline = null;
            VbaExecutionResult execution = null;
            ExcelInterop.Workbook scopeWorkbook = null;
            try
            {
                if (declaredTargetNames.Count > 0)
                {
                    scopeWorkbook = GetRequiredWorkbook();
                    var hashUntargetedValues = !request.StagedOnly
                        && (request.ProtectionAllowedWorksheetNames == null
                            || request.ProtectionAllowedWorksheetNames.Count == 0);
                    try
                    {
                        scopeBaseline = ExcelVbaScopeSnapshot.Capture(
                            scopeWorkbook,
                            declaredTargetNames,
                            hashUntargetedValues);
                        foreach (var note in scopeBaseline.Notes)
                        {
                            result.ScopeChecks.Add(note);
                        }
                    }
                    catch (Exception ex)
                    {
                        return FailVbaScopeCheck(
                            result,
                            "VBA_SCOPE_BASELINE_FAILED",
                            "无法建立执行前工作簿范围基线，已阻止 VBA 执行。",
                            ex.Message);
                    }
                }

                Exception executionFailure = null;
                try
                {
                    execution = _vbaService.ExecuteVba(new VbaExecutionRequest
                    {
                        Code = request.Code,
                        ModuleName = request.ModuleName,
                        EntryPoint = request.EntryPoint,
                        RemoveModuleAfterRun = request.RemoveModuleAfterRun,
                        TimeoutSeconds = request.TimeoutSeconds,
                        StagedOnly = request.StagedOnly,
                        ProtectionAllowedWorksheetNames = request.ProtectionAllowedWorksheetNames
                    });
                }
                catch (Exception ex)
                {
                    executionFailure = ex;
                }

                if (execution != null)
                {
                    result.Success = execution.Success;
                    result.Message = execution.Message;
                    result.ErrorCode = execution.ErrorCode ?? string.Empty;
                    result.ModuleName = execution.ModuleName;
                    result.EntryPoint = execution.EntryPoint;
                    result.ElapsedMilliseconds = execution.ElapsedMilliseconds;
                    result.LogEntries = execution.LogEntries ?? new List<string>();
                    result.TimedOut = execution.TimedOut;
                }

                if (scopeBaseline != null)
                {
                    ExcelVbaScopeSnapshot scopeAfter = null;
                    try
                    {
                        scopeAfter = ExcelVbaScopeSnapshot.Capture(
                            scopeWorkbook,
                            declaredTargetNames,
                            !request.StagedOnly
                                && (request.ProtectionAllowedWorksheetNames == null
                                    || request.ProtectionAllowedWorksheetNames.Count == 0));
                        foreach (var note in scopeAfter.Notes)
                        {
                            if (!result.ScopeChecks.Contains(note))
                            {
                                result.ScopeChecks.Add(note);
                            }
                        }

                        var scopeViolations = scopeBaseline.FindOutOfScopeChanges(
                            scopeAfter,
                            declaredTargetNames,
                            declaredNewTargetNames);
                        if (scopeViolations.Count > 0)
                        {
                            var executionFailureDetails = executionFailure == null
                                ? string.Empty
                                : " VBA 同时报告错误：" + executionFailure.Message;
                            return FailVbaScopeCheck(
                                result,
                                "VBA_SCOPE_VIOLATION",
                                "VBA_SCOPE_VIOLATION：执行后发现超出 declaredWriteTargets 的工作簿变化。"
                                    + executionFailureDetails,
                                string.Join("；", scopeViolations));
                        }
                    }
                    catch (Exception ex)
                    {
                        var executionFailureDetails = executionFailure == null
                            ? string.Empty
                            : " VBA 同时报告错误：" + executionFailure.Message;
                        return FailVbaScopeCheck(
                            result,
                            "VBA_SCOPE_VERIFICATION_FAILED",
                            "VBA 已运行，但无法完成执行后工作簿范围核对；本次结果不能视为通过。"
                                + executionFailureDetails,
                            ex.Message);
                    }
                    finally
                    {
                        if (scopeAfter != null)
                        {
                            scopeAfter.Dispose();
                        }
                    }
                }

                if (executionFailure != null)
                {
                    ExceptionDispatchInfo.Capture(executionFailure).Throw();
                }

                if (execution == null)
                {
                    return FailVbaScopeCheck(
                        result,
                        "VBA_EXECUTION_RESULT_MISSING",
                        "VBA 执行没有返回结果。",
                        "宿主未返回 VbaExecutionResult。");
                }
            }
            finally
            {
                if (scopeBaseline != null)
                {
                    scopeBaseline.Dispose();
                }
                ReleaseComObject(scopeWorkbook);
            }

            if (!execution.Success)
            {
                result.ExpectationsPassed = false;
                return result;
            }

            if (string.IsNullOrWhiteSpace(request.OutputSheet))
            {
                return result;
            }

            try
            {
                result.OutputProfile = _aggregationService.ProfileRange(new ExcelProfileRangeRequest
                {
                    WorksheetName = request.OutputSheet
                });
            }
            catch (Exception ex)
            {
                result.ExpectationsPassed = false;
                result.ExpectationFailures.Add("无法读取输出表 \"" + request.OutputSheet + "\" 的画像：" + ex.Message);
                return result;
            }

            EvaluateStageExpectations(request.Expect, result);
            return result;
        }

        private static ExcelVbaStageResult FailVbaScopeCheck(
            ExcelVbaStageResult result,
            string errorCode,
            string message,
            string details)
        {
            result.Success = false;
            result.ErrorCode = errorCode ?? string.Empty;
            result.Message = string.IsNullOrWhiteSpace(details)
                ? message ?? string.Empty
                : (message ?? string.Empty) + " 具体差异：" + details;
            result.ExpectationsPassed = false;
            var failure = string.IsNullOrWhiteSpace(details)
                ? result.Message
                : result.Message + " " + details;
            result.ExpectationFailures.Add(failure);
            result.ScopeChecks.Add(failure);
            return result;
        }

        private static bool ShouldRunProbe(ExcelVbaStageRequest request)
            => request.Probe != null
               && request.Probe.Rows > 0
               && !string.IsNullOrWhiteSpace(request.Probe.SourceWorksheetName);

        /// <summary>
        /// 金丝雀：同一段代码先在源表前 N 行的副本上跑一遍，expect 通过了才对全量跑。
        /// 样本上不校验行数上下限（那是全量的量级），只校验列名与守恒关系——
        /// 列名写错、金额算漏这两类错误在样本上就会暴露，而代价只是一张可弃的样本表。
        /// </summary>
        private bool TryRunProbe(ExcelVbaStageRequest request, ExcelVbaStageResult result)
        {
            string probeCode;
            string redirectFailure;
            if (!ExcelStagingConventions.TryRedirectToProbe(
                    request.Code, request.Probe.SourceWorksheetName, request.OutputSheet, out probeCode, out redirectFailure))
            {
                result.Success = false;
                result.ExpectationsPassed = false;
                result.Message = redirectFailure;
                result.ProbeChecks.Add(redirectFailure);
                result.ExpectationFailures.Add(redirectFailure);
                return false;
            }

            int sampleRows;
            try
            {
                sampleRows = _vbaService.CreateSampleWorksheet(
                    request.Probe.SourceWorksheetName,
                    ExcelStagingConventions.ProbeSourceSheetName,
                    request.Probe.Rows);
            }
            catch (Exception ex)
            {
                var message = "无法为金丝雀执行准备样本表：" + ex.Message;
                result.Success = false;
                result.ExpectationsPassed = false;
                result.Message = message;
                result.ProbeChecks.Add(message);
                result.ExpectationFailures.Add(message);
                return false;
            }

            if (sampleRows <= 0)
            {
                // 源表本来就没有数据行，金丝雀无从谈起；直接放行全量，让 expect 去暴露问题。
                result.ProbeChecks.Add("源表 \"" + request.Probe.SourceWorksheetName + "\" 没有数据行，跳过金丝雀执行。");
                CleanupProbeSheets();
                return true;
            }

            var probeResult = new ExcelVbaStageResult();
            try
            {
                var probeProtectionTargets = request.ProtectionAllowedWorksheetNames;
                if (!request.StagedOnly
                    && probeProtectionTargets != null
                    && probeProtectionTargets.Count > 0)
                {
                    probeProtectionTargets = new List<string>
                    {
                        ExcelStagingConventions.ProbeOutputSheetName
                    };
                }

                var probeExecution = _vbaService.ExecuteVba(new VbaExecutionRequest
                {
                    Code = probeCode,
                    ModuleName = request.ModuleName,
                    EntryPoint = request.EntryPoint,
                    RemoveModuleAfterRun = true,
                    TimeoutSeconds = request.TimeoutSeconds,
                    StagedOnly = request.StagedOnly,
                    ProtectionAllowedWorksheetNames = probeProtectionTargets
                });

                if (!probeExecution.Success)
                {
                    return FailProbe(result, "金丝雀执行失败（样本 " + sampleRows + " 行）：" + probeExecution.Message);
                }

                if (!string.IsNullOrWhiteSpace(request.OutputSheet))
                {
                    probeResult.OutputProfile = _aggregationService.ProfileRange(new ExcelProfileRangeRequest
                    {
                        WorksheetName = ExcelStagingConventions.ProbeOutputSheetName
                    });
                    probeResult.OutputSheet = ExcelStagingConventions.ProbeOutputSheetName;
                    EvaluateStageExpectations(BuildProbeExpectation(request.Expect), probeResult);

                    if (!probeResult.ExpectationsPassed)
                    {
                        foreach (var check in probeResult.ConserveChecks) result.ProbeChecks.Add(check);
                        return FailProbe(
                            result,
                            "金丝雀校验未通过（样本 " + sampleRows + " 行）：" + string.Join(" ", probeResult.ExpectationFailures));
                    }

                    foreach (var check in probeResult.ConserveChecks) result.ProbeChecks.Add(check);
                }
            }
            catch (Exception ex)
            {
                return FailProbe(result, "金丝雀执行异常（样本 " + sampleRows + " 行）：" + ex.Message);
            }

            result.ProbePassed = true;
            result.ProbeChecks.Add("金丝雀通过：样本 " + sampleRows + " 行执行并校验成功，开始对全量数据执行。");
            CleanupProbeSheets();
            return true;
        }

        /// <summary>失败时保留样本表与样本输出表，让用户能直接打开看错在哪。</summary>
        private static bool FailProbe(ExcelVbaStageResult result, string message)
        {
            result.Success = false;
            result.ExpectationsPassed = false;
            result.ProbePassed = false;
            result.Message = message + " 原始数据未被触碰；样本表 "
                + ExcelStagingConventions.ProbeSourceSheetName + " 与 "
                + ExcelStagingConventions.ProbeOutputSheetName + " 已保留供检查。请修正代码后重试。";
            result.ProbeChecks.Add(message);
            result.ExpectationFailures.Add(message);
            return false;
        }

        /// <summary>
        /// 样本上的期望：去掉行数上下限，并把守恒比对源从原表改指样本表，
        /// 否则样本 200 行去和原表 10 万行的总额比对，必然假失败。
        /// </summary>
        private static ExcelVbaExpectation BuildProbeExpectation(ExcelVbaExpectation expect)
        {
            if (expect == null)
            {
                return null;
            }

            var probeExpect = new ExcelVbaExpectation
            {
                MinRows = -1,
                MaxRows = -1,
                Columns = expect.Columns == null ? new List<string>() : new List<string>(expect.Columns),
                Conserve = new List<ExcelVbaConserveRule>(),
                Basis = CloneVerificationBasis(expect.Basis),
                Assertions = new List<ExcelAssertion>()
            };

            if (expect.Conserve != null)
            {
                foreach (var rule in expect.Conserve)
                {
                    if (rule == null || rule.CompareTo == null)
                    {
                        // 比的是一个写死的期望值，样本上必然对不上，只能跳过。
                        continue;
                    }

                    probeExpect.Conserve.Add(new ExcelVbaConserveRule
                    {
                        Function = rule.Function,
                        Column = rule.Column,
                        Tolerance = rule.Tolerance,
                        CompareTo = new ExcelVbaConserveSource
                        {
                            WorksheetName = ExcelStagingConventions.ProbeSourceSheetName,
                            Address = string.Empty,
                            TableName = string.Empty,
                            Function = rule.CompareTo.Function,
                            Column = rule.CompareTo.Column
                        }
                    });
                }
            }

            if (expect.Assertions != null)
            {
                foreach (var assertion in expect.Assertions)
                {
                    if (assertion == null)
                    {
                        probeExpect.Assertions.Add(null);
                        continue;
                    }

                    if (assertion.Source == null
                        && !string.Equals(assertion.Kind, "sorted", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var probeAssertion = CloneAssertion(assertion);
                    if (probeAssertion.Source != null)
                    {
                        probeAssertion.Source.WorksheetName = ExcelStagingConventions.ProbeSourceSheetName;
                        probeAssertion.Source.Address = string.Empty;
                        probeAssertion.Source.TableName = string.Empty;
                    }

                    probeExpect.Assertions.Add(probeAssertion);
                }
            }

            return probeExpect;
        }

        private void CleanupProbeSheets()
        {
            try
            {
                _vbaService.DeleteWorksheetIfExists(ExcelStagingConventions.ProbeOutputSheetName);
                _vbaService.DeleteWorksheetIfExists(ExcelStagingConventions.ProbeSourceSheetName);
            }
            catch
            {
                // 清理失败不该让一次成功的执行变成失败；残留的 _wip_probe_* 表由 excel_cleanup_workspace 兜底。
            }
        }

        private void EvaluateStageExpectations(ExcelVbaExpectation expect, ExcelVbaStageResult result)
        {
            if (expect == null || result.OutputProfile == null)
            {
                return;
            }

            var profile = result.OutputProfile;
            var profileExpectationFailures = new List<string>();

            if (expect.MinRows >= 0 && profile.RowCount < expect.MinRows)
            {
                profileExpectationFailures.Add(
                    "期望至少 " + expect.MinRows + " 行数据，实际为 " + profile.RowCount + " 行。");
            }

            if (expect.MaxRows >= 0 && profile.RowCount > expect.MaxRows)
            {
                profileExpectationFailures.Add(
                    "期望至多 " + expect.MaxRows + " 行数据，实际为 " + profile.RowCount + " 行。");
            }

            if (expect.Columns != null && expect.Columns.Count > 0)
            {
                var actual = profile.Columns.Select(c => c.Name ?? string.Empty).ToList();
                var missing = expect.Columns
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Where(name => !actual.Any(a => string.Equals(a.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (missing.Count > 0)
                {
                    profileExpectationFailures.Add(
                        "缺少期望列：" + string.Join("、", missing)
                        + "；实际列为：" + string.Join("、", actual) + "。");
                }
            }

            result.ExpectationFailures.AddRange(profileExpectationFailures);
            EvaluateAssertions(expect, result);

            result.ExpectationsPassed = result.ExpectationFailures.Count == 0;
            FinalizeStageVerification(expect, result, profileExpectationFailures);
        }

        private void EvaluateAssertions(ExcelVbaExpectation expect, ExcelVbaStageResult result)
        {
            var plan = BuildStageVerificationPlan(expect, result.OutputSheet);
            if (plan.Assertions.Count == 0)
            {
                return;
            }

            ExcelVerificationReport report;
            try
            {
                report = _verificationEngine.Verify(plan);
            }
            catch (Exception exception)
            {
                var message = "断言框架无法完成验证：" + exception.Message;
                result.ExpectationFailures.Add(message);
                var failedReport = new ExcelVerificationReport
                {
                    Passed = false,
                    Tier = "C"
                };
                foreach (var assertion in plan.Assertions.Where(item => item != null))
                {
                    failedReport.Items.Add(new ExcelAssertionOutcome
                    {
                        Id = assertion.Id ?? string.Empty,
                        Kind = assertion.Kind ?? string.Empty,
                        Passed = false,
                        Tier = "C",
                        Independent = false,
                        TargetBackend = "unavailable"
                    });
                }

                failedReport.Notes.Add(message);
                result.Verification = ExcelVerificationResult.FromReport(
                    failedReport,
                    ToolResultVerificationFields.ProducerVba,
                    false,
                    plan.Basis);
                return;
            }

            foreach (var item in report.Items)
            {
                var check = (string.IsNullOrWhiteSpace(item.Id) ? item.Kind : item.Id)
                    + "：" + (item.Passed ? "通过" : "不通过")
                    + "，实际 " + (item.ActualValue.HasValue
                        ? item.ActualValue.Value.ToString("G", CultureInfo.InvariantCulture)
                        : "-")
                    + "，期望 " + (item.ExpectedValue.HasValue
                        ? item.ExpectedValue.Value.ToString("G", CultureInfo.InvariantCulture)
                        : "-")
                    + "，差额 " + (item.Difference.HasValue
                        ? item.Difference.Value.ToString("G", CultureInfo.InvariantCulture)
                        : "-")
                    + "，可信度 " + item.Tier + "。";
                result.ConserveChecks.Add(check);

                if (item.Passed
                    && ((item.TargetCoverage != null && item.TargetCoverage.TotalRows == 0)
                        || (item.SourceCoverage != null && item.SourceCoverage.TotalRows == 0)))
                {
                    item.Passed = false;
                    item.Tier = "C";
                    report.Passed = false;
                    report.Tier = "C";
                    report.Notes.Add(
                        (string.IsNullOrWhiteSpace(item.Id) ? string.Empty : "[" + item.Id + "] ")
                        + "守恒断言的数据源没有数据行；空表不得作为数值 0 通过。");
                }
            }

            result.Verification = ExcelVerificationResult.FromReport(
                report,
                ToolResultVerificationFields.ProducerVba,
                false,
                plan.Basis);

            if (!report.Passed)
            {
                result.ExpectationFailures.AddRange(report.Diffs);
                result.ExpectationFailures.AddRange(report.Notes);
                if (result.ExpectationFailures.Count == 0)
                {
                    result.ExpectationFailures.Add("一个或多个守恒断言未通过。");
                }
            }
        }

        private void FinalizeStageVerification(
            ExcelVbaExpectation expect,
            ExcelVbaStageResult result,
            IList<string> profileExpectationFailures)
        {
            var plan = BuildStageVerificationPlan(expect, result.OutputSheet);
            var report = result.Verification == null
                ? null
                : result.Verification.Details;
            var hasProfileExpectations = expect.MinRows >= 0
                || expect.MaxRows >= 0
                || (expect.Columns != null && expect.Columns.Count > 0);

            if (report == null && plan.Assertions.Count > 0)
            {
                report = new ExcelVerificationReport
                {
                    Passed = false,
                    Tier = "C"
                };
                foreach (var assertion in plan.Assertions.Where(item => item != null))
                {
                    report.Items.Add(new ExcelAssertionOutcome
                    {
                        Id = assertion.Id ?? string.Empty,
                        Kind = assertion.Kind ?? string.Empty,
                        Passed = false,
                        Tier = "C",
                        Independent = false,
                        TargetBackend = "unavailable"
                    });
                }

                report.Notes.Add("一个或多个断言没有生成可用的验证报告。");
            }

            if (report == null && !hasProfileExpectations)
            {
                return;
            }

            if (report == null)
            {
                report = new ExcelVerificationReport
                {
                    Passed = result.ExpectationsPassed,
                    Tier = "B"
                };
            }

            if (hasProfileExpectations)
            {
                var profilePassed = profileExpectationFailures.Count == 0;
                var profileOutcome = new ExcelAssertionOutcome
                {
                    Id = "output-profile",
                    Kind = "profileExpectation",
                    Passed = profilePassed,
                    Tier = "B",
                    Independent = false,
                    TargetBackend = result.OutputProfile.Coverage == null
                        ? "profile"
                        : (result.OutputProfile.Coverage.Backend ?? "profile"),
                    TargetCoverage = result.OutputProfile.Coverage,
                    DiffCount = profilePassed ? 0 : profileExpectationFailures.Count
                };
                report.Items.Add(profileOutcome);
                if (report.Tier == "A")
                {
                    report.Tier = "B";
                }

                if (!profilePassed)
                {
                    foreach (var failure in profileExpectationFailures)
                    {
                        if (report.Diffs.Count < 20)
                        {
                            report.Diffs.Add(failure);
                        }
                    }

                    report.DiffCount += profileExpectationFailures.Count;
                }
            }

            report.Passed = result.ExpectationsPassed;
            result.Verification = ExcelVerificationResult.FromReport(
                report,
                ToolResultVerificationFields.ProducerVba,
                false,
                CloneVerificationBasis(expect.Basis));
        }

        private static ExcelVerificationPlan BuildStageVerificationPlan(
            ExcelVbaExpectation expect,
            string outputSheet)
        {
            var basis = CloneVerificationBasis(expect == null ? null : expect.Basis);
            var plan = new ExcelVerificationPlan { Basis = basis };

            if (expect != null && expect.Assertions != null)
            {
                foreach (var assertion in expect.Assertions)
                {
                    var copy = CloneAssertion(assertion);
                    if (copy != null)
                    {
                        EnsureTargetDefaultsToSheet(copy, outputSheet);
                    }

                    plan.Assertions.Add(copy);
                }
            }

            if (expect == null || expect.Conserve == null)
            {
                return plan;
            }

            foreach (var rule in expect.Conserve)
            {
                if (rule == null)
                {
                    continue;
                }

                var function = string.IsNullOrWhiteSpace(rule.Function)
                    ? "sum"
                    : rule.Function.Trim();
                var sourceFunction = rule.CompareTo == null
                    || string.IsNullOrWhiteSpace(rule.CompareTo.Function)
                    ? function
                    : rule.CompareTo.Function.Trim();
                var isRowCount = string.Equals(function, "count", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(rule.Column);

                var assertion = new ExcelAssertion
                {
                    Id = "conserve-" + plan.Assertions.Count.ToString(CultureInfo.InvariantCulture),
                    Kind = isRowCount ? "rowCount" : "scalar",
                    Target = new ExcelAssertionSide
                    {
                        WorksheetName = outputSheet ?? string.Empty,
                        Column = rule.Column ?? string.Empty,
                        Function = function
                    },
                    ExpectedValue = rule.ExpectedValue,
                    Tolerance = rule.Tolerance > 0d ? (double?)rule.Tolerance : null
                };

                if (rule.CompareTo != null)
                {
                    assertion.Source = new ExcelAssertionSide
                    {
                        WorksheetName = rule.CompareTo.WorksheetName ?? string.Empty,
                        Address = rule.CompareTo.Address ?? string.Empty,
                        TableName = rule.CompareTo.TableName ?? string.Empty,
                        Column = rule.CompareTo.Column ?? string.Empty,
                        Function = sourceFunction
                    };
                }

                plan.Assertions.Add(assertion);
            }

            return plan;
        }

        private static ExcelVerificationBasis CloneVerificationBasis(ExcelVerificationBasis basis)
        {
            if (basis == null)
            {
                return new ExcelVerificationBasis();
            }

            return new ExcelVerificationBasis
            {
                Source = basis.Source == null
                    ? null
                    : new ExcelSourceRef
                    {
                        WorksheetName = basis.Source.WorksheetName,
                        Address = basis.Source.Address,
                        TableName = basis.Source.TableName
                    },
                Filters = basis.Filters == null
                    ? null
                    : basis.Filters.Select(filter => filter == null
                        ? null
                        : new CalcFilter
                        {
                            Column = filter.Column,
                            Operator = filter.Operator,
                            Value = filter.Value
                        }).ToList(),
                GroupBy = basis.GroupBy == null ? null : new List<string>(basis.GroupBy),
                Measures = basis.Measures == null
                    ? null
                    : basis.Measures.Select(measure => measure == null
                        ? null
                        : new ExcelVerificationMeasure
                        {
                            Column = measure.Column,
                            Function = measure.Function
                        }).ToList(),
                DerivedGroupBy = basis.DerivedGroupBy == null
                    ? null
                    : basis.DerivedGroupBy.Select(group => group == null
                        ? null
                        : new ExcelVerificationDerivedGroup
                        {
                            Column = group.Column,
                            Transform = group.Transform,
                            Alias = group.Alias,
                            Size = group.Size
                        }).ToList(),
                Sources = basis.Sources == null
                    ? null
                    : basis.Sources.Select(source => source == null
                        ? null
                        : new ExcelVerificationSource
                        {
                            Sheet = source.Sheet,
                            Range = source.Range,
                            Alias = source.Alias
                        }).ToList(),
                Lookups = basis.Lookups == null
                    ? null
                    : basis.Lookups.Select(lookup => lookup == null
                        ? null
                        : new ExcelVerificationLookup
                        {
                            Sheet = lookup.Sheet,
                            Range = lookup.Range,
                            TableName = lookup.TableName,
                            Key = lookup.Key,
                            ForeignKey = lookup.ForeignKey,
                            Columns = lookup.Columns == null
                                ? null
                                : new List<string>(lookup.Columns),
                            Join = lookup.Join,
                            OnDuplicateKey = lookup.OnDuplicateKey
                        }).ToList(),
                Topology = basis.Topology,
                Keys = basis.Keys == null ? null : new List<string>(basis.Keys),
                Join = basis.Join,
                HeaderMode = basis.HeaderMode,
                Note = basis.Note
            };
        }

        private static ExcelAssertion CloneAssertion(ExcelAssertion assertion)
        {
            if (assertion == null)
            {
                return null;
            }

            return new ExcelAssertion
            {
                Id = assertion.Id,
                Kind = assertion.Kind,
                Target = CloneAssertionSide(assertion.Target),
                Source = CloneAssertionSide(assertion.Source),
                ExpectedValue = assertion.ExpectedValue,
                Tolerance = assertion.Tolerance,
                Direction = assertion.Direction
            };
        }

        private static ExcelAssertionSide CloneAssertionSide(ExcelAssertionSide side)
        {
            if (side == null)
            {
                return null;
            }

            return new ExcelAssertionSide
            {
                WorksheetName = side.WorksheetName,
                Address = side.Address,
                TableName = side.TableName,
                Column = side.Column,
                Columns = side.Columns == null ? null : new List<string>(side.Columns),
                Function = side.Function,
                KeyColumn = side.KeyColumn,
                ValueColumn = side.ValueColumn,
                Filters = side.Filters == null
                    ? null
                    : side.Filters.Select(filter => filter == null
                        ? null
                        : new CalcFilter
                        {
                            Column = filter.Column,
                            Operator = filter.Operator,
                            Value = filter.Value
                        }).ToList(),
                ExcludeRows = side.ExcludeRows == null
                    ? null
                    : new List<int>(side.ExcludeRows),
                ExcludeLabelRows = side.ExcludeLabelRows
            };
        }

        private static void EnsureTargetDefaultsToSheet(ExcelAssertion assertion, string outputSheet)
        {
            if (assertion.Target == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(assertion.Target.Address)
                && string.IsNullOrWhiteSpace(assertion.Target.WorksheetName))
            {
                assertion.Target.WorksheetName = outputSheet ?? string.Empty;
            }
            else if (string.IsNullOrWhiteSpace(assertion.Target.Address)
                && string.IsNullOrWhiteSpace(assertion.Target.TableName)
                && string.IsNullOrWhiteSpace(assertion.Target.WorksheetName))
            {
                assertion.Target.WorksheetName = outputSheet ?? string.Empty;
            }
        }

        /// <summary>
        /// 守恒校验：形状对而数值错是数据处理里最难发现的错法，所以行数列名之外还要比对聚合量。
        /// </summary>
        /// <summary>删除中间过程表（默认前缀 _wip_），收尾时清理工作簿。</summary>
        public ExcelCleanupWorkspaceResult CleanupWorkspace(ExcelCleanupWorkspaceRequest request)
        {
            var prefix = request == null || string.IsNullOrWhiteSpace(request.Prefix)
                ? ExcelStagingConventions.WipPrefix
                : request.Prefix.Trim();
            var dryRun = request != null && request.DryRun;

            var result = new ExcelCleanupWorkspaceResult
            {
                Prefix = prefix,
                DryRun = dryRun
            };
            var registeredWorksheetNames = new HashSet<string>(
                request == null || request.RegisteredWorksheetNames == null
                    ? new string[0]
                    : request.RegisteredWorksheetNames,
                StringComparer.OrdinalIgnoreCase);

            var workbook = GetRequiredWorkbook();
            EnsureWorkbookWritable(workbook);

            var worksheets = workbook.Worksheets;
            try
            {
                var targets = new List<ExcelInterop.Worksheet>();
                for (var i = 1; i <= worksheets.Count; i++)
                {
                    var sheet = worksheets[i] as ExcelInterop.Worksheet;
                    if (sheet == null)
                    {
                        continue;
                    }

                    var name = GetWorksheetName(sheet);
                    if (name != null
                        && ExcelStagingConventions.IsRegisteredWipWorksheetForCleanup(
                            name,
                            prefix,
                            registeredWorksheetNames))
                    {
                        result.MatchedSheets.Add(name);
                        targets.Add(sheet);
                    }
                    else
                    {
                        ReleaseComObject(sheet);
                    }
                }

                if (result.MatchedSheets.Count == 0)
                {
                    result.Message = "没有找到以 \"" + prefix + "\" 开头且已登记为本会话中间表的工作表，无需清理。";
                    return result;
                }

                if (dryRun)
                {
                    result.Message = "预演模式：将删除 " + result.MatchedSheets.Count + " 个中间表，当前未做任何改动。";
                    return result;
                }

                // 工作簿至少要留一张可见工作表，否则 Excel 会拒绝删除。
                if (targets.Count >= worksheets.Count)
                {
                    result.Message = "拒绝清理：这些中间表已经是工作簿的全部工作表，删除后将没有剩余工作表。";
                    return result;
                }

                var displayAlerts = _application.DisplayAlerts;
                _application.DisplayAlerts = false;
                try
                {
                    foreach (var sheet in targets)
                    {
                        var name = GetWorksheetName(sheet);
                        sheet.Delete();
                        result.RemovedSheets.Add(name);
                    }
                }
                finally
                {
                    _application.DisplayAlerts = displayAlerts;
                }

                result.Message = "已删除 " + result.RemovedSheets.Count + " 个中间表。";
                return result;
            }
            finally
            {
                ReleaseComObject(worksheets);
            }
        }

        public ExcelListBackupsResult ListBackups(ExcelListBackupsRequest request)
        {
            return _backupService.ListBackups(request);
        }

        public ExcelRestoreBackupResult RestoreBackup(ExcelRestoreBackupRequest request)
        {
            return _backupService.RestoreBackup(request);
        }

        public ExcelPowerQueryValidationResult ValidatePowerQuery(ExcelPowerQueryValidationRequest request)
        {
            return _analysisService.ValidatePowerQuery(request);
        }

        public DocumentBackupResult CreateBackup(DocumentBackupRequest request)
        {
            return _backupService.CreateBackup(request);
        }

        public ExcelConnectionListResult ListConnections()
        {
            return _connectionService.ListConnections();
        }

        public ExcelConnectionViewResult ViewConnection(ExcelConnectionViewRequest request)
        {
            return _connectionService.ViewConnection(request);
        }

        public ExcelPowerQueryRefreshResult RefreshPowerQuery(ExcelPowerQueryRefreshRequest request)
        {
            return _connectionService.RefreshPowerQuery(request);
        }

        public ExcelPowerQueryErrorCheckResult CheckPowerQueryErrors(ExcelPowerQueryErrorCheckRequest request)
        {
            return _connectionService.CheckPowerQueryErrors(request);
        }

        public ExcelDataModelTableListResult ListDataModelTables()
        {
            return _dataModelService.ListDataModelTables();
        }

        public ExcelDataModelMeasureListResult ListDataModelMeasures()
        {
            return _dataModelService.ListDataModelMeasures();
        }

        public ExcelDataModelMeasureViewResult ViewDataModelMeasure(ExcelDataModelMeasureViewRequest request)
        {
            return _dataModelService.ViewDataModelMeasure(request);
        }

        public ExcelDataModelRelationshipListResult ListDataModelRelationships()
        {
            return _dataModelService.ListDataModelRelationships();
        }

        public ExcelDataModelCalculatedColumnListResult ListDataModelCalculatedColumns()
        {
            return _dataModelService.ListDataModelCalculatedColumns();
        }

        public ExcelDataModelCalculatedColumnViewResult ViewDataModelCalculatedColumn(ExcelDataModelCalculatedColumnViewRequest request)
        {
            return _dataModelService.ViewDataModelCalculatedColumn(request);
        }

        public ExcelRangeWriteResult WriteRange(ExcelRangeWriteRequest request)
        {
            return _rangeService.WriteRange(request);
        }

        public ExcelRangeClearResult ClearRange(ExcelRangeRequest request)
        {
            return _rangeService.ClearRange(request);
        }

        public ExcelRangeFormulaResult ReadRangeFormulas(ExcelRangeRequest request)
        {
            return _rangeService.ReadRangeFormulas(request);
        }

        public ExcelRangeInfoResult GetRangeInfo(ExcelRangeRequest request)
        {
            return _rangeService.GetRangeInfo(request);
        }

        public ExcelRangeHyperlinkResult GetRangeHyperlinks(ExcelRangeRequest request)
        {
            return _rangeService.GetRangeHyperlinks(request);
        }

        public ExcelWorksheetScreenshotResult CaptureWorksheetScreenshot(ExcelWorksheetScreenshotRequest request)
        {
            return _rangeService.CaptureWorksheetScreenshot(request);
        }

        public ExcelRangeValidationResult GetRangeValidation(ExcelRangeRequest request)
        {
            return _rangeService.GetRangeValidation(request);
        }

        public ExcelRangeNumberFormatResult GetRangeNumberFormats(ExcelRangeRequest request)
        {
            return _rangeService.GetRangeNumberFormats(request);
        }

        public ExcelRangeMergeInfoResult GetRangeMergeInfo(ExcelRangeRequest request)
        {
            return _rangeService.GetRangeMergeInfo(request);
        }

        public ExcelRangeLockInfoResult GetRangeLockInfo(ExcelRangeRequest request)
        {
            return _rangeService.GetRangeLockInfo(request);
        }

        public ExcelRangeStyleResult GetRangeStyle(ExcelRangeRequest request)
        {
            return _rangeService.GetRangeStyle(request);
        }

        public ExcelRangeFormulaWriteResult SetRangeFormulas(ExcelRangeFormulaWriteRequest request)
        {
            return _rangeService.SetRangeFormulas(request);
        }

        public ExcelRangeFormatWriteResult SetRangeFormat(ExcelRangeFormatWriteRequest request)
        {
            return _rangeService.SetRangeFormat(request);
        }

        public ExcelTableListResult ListTables()
        {
            return _tableService.ListTables();
        }

        public ExcelTableViewResult ViewTable(ExcelTableViewRequest request)
        {
            return _tableService.ViewTable(request);
        }

        public ExcelTableDataResult ReadTableData(ExcelTableDataRequest request)
        {
            return _tableService.ReadTableData(request);
        }
    }
}
