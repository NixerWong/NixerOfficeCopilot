using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Verification.Backends.Com;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel.Verification
{
    internal sealed class ExcelAggregateVerificationService : ExcelServiceBase
    {
        private const int MaximumGroups = 500;

        private readonly ExcelAggregationService _sourceService;
        private readonly ComCalcBackend _comBackend;

        public ExcelAggregateVerificationService(
            ExcelInterop.Application application,
            ExcelAggregationService sourceService,
            ComCalcBackend comBackend)
            : base(application)
        {
            _sourceService = sourceService ?? throw new ArgumentNullException(nameof(sourceService));
            _comBackend = comBackend ?? throw new ArgumentNullException(nameof(comBackend));
        }

        public ExcelVerificationReport TryVerify(
            ExcelAggregateRequest request,
            ExcelAggregateResult result,
            ExcelVerificationBasis basis)
        {
            if (request != null && request.Sources != null && request.Sources.Count > 0)
            {
                return TryVerifyUnionScalar(request, result, basis);
            }

            IList<ExcelAggregateMeasure> scalarMeasures;
            IList<ExcelAggregateFilter> scalarFilters;
            if (TryResolveSupportedScalarShape(
                request,
                result,
                out scalarMeasures,
                out scalarFilters))
            {
                return TryVerifyScalar(
                    result,
                    basis,
                    scalarMeasures,
                    scalarFilters);
            }

            GroupDefinition groupDefinition;
            IList<ExcelAggregateMeasure> measures;
            if (!TryResolveSupportedShape(request, result, out groupDefinition, out measures))
            {
                return null;
            }

            ExcelInterop.Range sourceRange = null;
            try
            {
                string sourceKind;
                string tableName;
                string worksheetName;
                sourceRange = _sourceService.ResolveSource(
                    basis.Source,
                    out sourceKind,
                    out tableName,
                    out worksheetName);

                var reader = new ExcelColumnReader(_application);
                var readRequest = new ExcelColumnReadRequest
                {
                    Columns = GetVerificationReadColumns(groupDefinition),
                    HeaderMode = ExcelAggregationService.ResolveHeaderMode(
                        basis.HeaderMode,
                        sourceKind)
                };
                var preview = reader.ResolveLayout(sourceRange, readRequest);
                if ((long)preview.TotalRows * Math.Max(2, readRequest.Columns.Count)
                    > ComCalcBackend.MaximumEstimatedCells)
                {
                    return CreateUnverifiedReport(
                        "源数据行数与分组/筛选列数的乘积超过 COM 分组复核的安全读取上限。");
                }

                var sink = new GroupKeySink(groupDefinition);
                var layout = reader.Read(sourceRange, readRequest, sink);

                if (!string.IsNullOrWhiteSpace(sink.UnsupportedFilterReason))
                {
                    return CreateUnverifiedReport(sink.UnsupportedFilterReason);
                }

                if (sink.HasUnsupportedValue)
                {
                    return CreateUnverifiedReport(
                        "分组字段含错误值、无效日期或无法安全还原的空值，COM 复核无法可靠还原派生分组键。");
                }

                var groupKeys = sink.GroupKeys;
                if (groupKeys.Count == 0)
                {
                    return CreateUnverifiedReport("源数据没有可独立复核的分组。");
                }

                if (groupKeys.Count > MaximumGroups)
                {
                    return CreateUnverifiedReport(
                        "独立分组数 " + groupKeys.Count
                        + " 超过 COM 排名复核上限 " + MaximumGroups + "。");
                }

                if (HasCaseInsensitiveCollision(groupKeys))
                {
                    return CreateUnverifiedReport(
                        "分组键仅大小写不同；Excel 条件计算不区分大小写，无法安全独立区分这些组。");
                }

                var formulaPassesPerGroup = measures.Sum(measure =>
                {
                    var function = NormalizeFunction(measure.Function);
                    var isAverage = function == "average" || function == "avg";
                    return (function == "average" || function == "avg" ? 2 : 1)
                        + (function == "sum" || isAverage ? 1 : 0);
                });
                var work = (decimal)groupKeys.Count
                    * layout.TotalRows
                    * formulaPassesPerGroup
                    * (GetGroupFilterCount(groupDefinition)
                        + groupDefinition.VerificationFilters.Count);
                if (work > ComCalcBackend.MaximumConditionalComparisons)
                {
                    return CreateUnverifiedReport(
                        "完整分组复核的条件比较量超过 COM 安全上限。");
                }

                var groups = new List<CalculatedGroup>(groupKeys.Count);
                ExcelCoverageReport sharedCoverage = null;
                var costs = measures.Select(measure => new CalcCostEstimate
                {
                    RowCount = layout.TotalRows,
                    ReadColumnCount = GetMeasureQueryReadColumnCount(groupDefinition, measure),
                    ProjectedCellCount = (long)layout.TotalRows
                        * GetMeasureQueryReadColumnCount(groupDefinition, measure)
                }).ToList();
                foreach (var groupKey in groupKeys)
                {
                    var group = new CalculatedGroup(groupKey, measures.Count);
                    for (var measureIndex = 0; measureIndex < measures.Count; measureIndex++)
                    {
                        var measure = measures[measureIndex];
                        var query = CreateMeasureQuery(basis, groupDefinition, groupKey, measure);
                        if (!_comBackend.CanEvaluate(query, costs[measureIndex]))
                        {
                            return CreateUnverifiedReport(
                                "COM 后端不支持完整分组复核中的度量 "
                                + (measure.Alias ?? measure.Function) + "。");
                        }

                        var calculated = _comBackend.Evaluate(query);
                        if (!calculated.Scalar.HasValue
                            || double.IsNaN(calculated.Scalar.Value)
                            || double.IsInfinity(calculated.Scalar.Value))
                        {
                            return CreateUnverifiedReport(
                                "COM 对分组 \"" + groupKey + "\" 的度量 "
                                + (measure.Alias ?? measure.Function)
                                + " 未返回有限数值。");
                        }

                        if (sharedCoverage == null)
                        {
                            sharedCoverage = calculated.Coverage;
                        }
                        else
                        {
                            calculated.Coverage = sharedCoverage;
                        }

                        group.Values[measureIndex] = calculated.Scalar.Value;
                        group.Results[measureIndex] = calculated;
                    }

                    groups.Add(group);
                }

                return CompareAggregateOutput(
                    request,
                    result,
                    basis,
                    groupDefinition,
                    measures,
                    groups,
                    sharedCoverage,
                    layout.TotalRows);
            }
            finally
            {
                ReleaseComObject(sourceRange);
            }
        }

        private ExcelVerificationReport TryVerifyUnionScalar(
            ExcelAggregateRequest request,
            ExcelAggregateResult result,
            ExcelVerificationBasis basis)
        {
            if (!TryResolveSupportedUnionScalarShape(
                request,
                result,
                basis,
                out var measure,
                out var sourceRowCount,
                out var reason))
            {
                return CreateUnverifiedReport(reason);
            }

            var query = CreateUnionScalarMeasureQuery(basis, measure);
            var cost = _sourceService.EstimateCalcCost(query);
            if (!_comBackend.CanEvaluate(query, cost))
            {
                return CreateUnverifiedReport(
                    "COM 后端不支持完整复核此 union sum 查询。");
            }

            var calculated = _comBackend.Evaluate(query);
            if (!HasCompleteUnionComResult(calculated, sourceRowCount))
            {
                return CreateUnverifiedReport(
                    "COM union sum 未返回有限结果或未完整扫描全部来源行。");
            }

            return CompareScalarAggregateOutput(
                result,
                basis,
                _comBackend,
                new[] { measure },
                new List<CalcFilter>(),
                new[] { calculated },
                calculated.Coverage,
                sourceRowCount);
        }

        private ExcelVerificationReport TryVerifyScalar(
            ExcelAggregateResult result,
            ExcelVerificationBasis basis,
            IList<ExcelAggregateMeasure> measures,
            IList<ExcelAggregateFilter> filters)
        {
            if (result.IsTruncated
                || result.GroupCount != 1
                || result.Rows.Count != 1
                || result.Rows[0] == null
                || result.Rows[0].Length != measures.Count)
            {
                return CreateUnverifiedReport(
                    "全局聚合结果应包含一个未截断的汇总行，实际结果形状不符合独立复核要求。");
            }

            ExcelInterop.Range sourceRange = null;
            try
            {
                string sourceKind;
                string tableName;
                string worksheetName;
                sourceRange = _sourceService.ResolveSource(
                    basis.Source,
                    out sourceKind,
                    out tableName,
                    out worksheetName);

                var readColumns = GetScalarVerificationReadColumns(measures, filters);
                var reader = new ExcelColumnReader(_application);
                var readRequest = new ExcelColumnReadRequest
                {
                    Columns = readColumns,
                    HeaderMode = ExcelAggregationService.ResolveHeaderMode(
                        basis.HeaderMode,
                        sourceKind)
                };
                var preview = reader.ResolveLayout(sourceRange, readRequest);
                if ((long)preview.TotalRows * Math.Max(1, readColumns.Count)
                    > ComCalcBackend.MaximumEstimatedCells)
                {
                    return CreateUnverifiedReport(
                        "源数据行数与全局聚合度量/筛选列数的乘积超过 COM 复核的安全读取上限。");
                }

                var sink = new ScalarProfileSink(measures, filters);
                var layout = reader.Read(sourceRange, readRequest, sink);
                if (!string.IsNullOrWhiteSpace(sink.UnsupportedFilterReason))
                {
                    return CreateUnverifiedReport(sink.UnsupportedFilterReason);
                }

                if (!string.IsNullOrWhiteSpace(sink.UnsupportedMeasureReason))
                {
                    return CreateUnverifiedReport(sink.UnsupportedMeasureReason);
                }

                var formulaWork = 0L;
                foreach (var measure in measures)
                {
                    var function = NormalizeFunction(measure.Function);
                    var isAverage = function == "average" || function == "avg";
                    var passes = (isAverage ? 2 : 1)
                        + ((function == "sum" || isAverage)
                            && sink.CompiledFilters.Count > 0
                            ? 1
                            : 0);
                    var predicateCount = ComCalcBackend.GetConditionalPredicateCount(
                        function,
                        sink.CompiledFilters.Count);
                    formulaWork += (long)layout.TotalRows * passes * predicateCount;
                }

                if (formulaWork > ComCalcBackend.MaximumConditionalComparisons)
                {
                    return CreateUnverifiedReport(
                        "全局聚合复核的条件比较量超过 COM 安全上限。");
                }

                var calculated = new CalcResult[measures.Count];
                var costs = measures.Select(measure =>
                {
                    var columnCount = GetScalarMeasureQueryReadColumnCount(measure, filters);
                    return new CalcCostEstimate
                    {
                        RowCount = layout.TotalRows,
                        ReadColumnCount = columnCount,
                        ProjectedCellCount = (long)layout.TotalRows * columnCount
                    };
                }).ToList();
                ExcelCoverageReport sharedCoverage = null;
                for (var index = 0; index < measures.Count; index++)
                {
                    var query = CreateScalarMeasureQuery(
                        basis,
                        measures[index],
                        sink.CompiledFilters);
                    if (!_comBackend.CanEvaluate(query, costs[index]))
                    {
                        return CreateUnverifiedReport(
                            "COM 后端不支持全局聚合度量 "
                            + GetMeasureAlias(measures[index]) + "。");
                    }

                    calculated[index] = _comBackend.Evaluate(query);
                    if (!calculated[index].Scalar.HasValue
                        || double.IsNaN(calculated[index].Scalar.Value)
                        || double.IsInfinity(calculated[index].Scalar.Value))
                    {
                        return CreateUnverifiedReport(
                            "COM 对全局度量 " + GetMeasureAlias(measures[index])
                            + " 未返回有限数值。");
                    }

                    if (sharedCoverage == null)
                    {
                        sharedCoverage = calculated[index].Coverage;
                    }
                    else
                    {
                        calculated[index].Coverage = sharedCoverage;
                    }
                }

                return CompareScalarAggregateOutput(
                    result,
                    basis,
                    _comBackend,
                    measures,
                    sink.CompiledFilters,
                    calculated,
                    sharedCoverage,
                    layout.TotalRows);
            }
            finally
            {
                ReleaseComObject(sourceRange);
            }
        }

        private ExcelVerificationReport CompareAggregateOutput(
            ExcelAggregateRequest request,
            ExcelAggregateResult result,
            ExcelVerificationBasis basis,
            GroupDefinition groupDefinition,
            IList<ExcelAggregateMeasure> measures,
            IList<CalculatedGroup> groups,
            ExcelCoverageReport coverage,
            int sourceRowCount)
        {
            var groupColumnCount = 1;
            var reportExecutions = new List<VerificationAssertionExecution>();
            var actualKeys = new List<string>();
            var invalidOutput = false;

            foreach (var row in result.Rows)
            {
                if (row == null || row.Length < groupColumnCount + measures.Count)
                {
                    invalidOutput = true;
                    continue;
                }

                var groupKey = row[0] ?? string.Empty;
                actualKeys.Add(groupKey);
                var calculatedGroup = groups.FirstOrDefault(
                    candidate => string.Equals(candidate.Key, groupKey, StringComparison.Ordinal));
                if (calculatedGroup == null)
                {
                    invalidOutput = true;
                    continue;
                }

                for (var measureIndex = 0; measureIndex < measures.Count; measureIndex++)
                {
                    double outputValue;
                    if (!TryParseFiniteNumber(row[groupColumnCount + measureIndex], out outputValue))
                    {
                        invalidOutput = true;
                        continue;
                    }

                    var measure = measures[measureIndex];
                    var query = CreateMeasureQuery(
                        basis,
                        groupDefinition,
                        groupKey,
                        measure);
                    var plan = new VerificationAssertionPlan
                    {
                        AssertionId = groupKey + "/" + GetMeasureAlias(measure),
                        Kind = "scalar",
                        ExpectedValue = outputValue,
                        Tolerance = Math.Max(1e-6d, Math.Abs(outputValue) * 1e-10d)
                    };
                    plan.Sides.Add(new PlannedCalcSide
                    {
                        Side = "target",
                        Query = query,
                        Backend = _comBackend,
                        BackendName = "com",
                        Independent = true
                    });

                    reportExecutions.Add(new VerificationAssertionExecution
                    {
                        Plan = plan,
                        TargetResult = calculatedGroup.Results[measureIndex]
                    });
                }
            }

            var comparator = new Comparator();
            var report = reportExecutions.Count == 0
                ? new ExcelVerificationReport()
                : comparator.Compare(reportExecutions);
            var sortedGroups = groups
                .OrderBy(group => group, new AggregateGroupComparer(request, result, measures))
                .ToList();
            var maximumGroups = request.MaxGroups > 0 ? request.MaxGroups : 200;
            var expectedOutputCount = Math.Min(sortedGroups.Count, maximumGroups);
            var expectedTruncated = sortedGroups.Count > maximumGroups;
            var rankingPassed = !invalidOutput
                && result.GroupCount == sortedGroups.Count
                && result.TotalRowCount == sourceRowCount
                && result.Rows.Count == expectedOutputCount
                && result.IsTruncated == expectedTruncated
                && MatchesExpectedGroupKeys(actualKeys, sortedGroups, expectedOutputCount);

            var rankingOutcome = new ExcelAssertionOutcome
            {
                Id = "aggregate_group_ranking",
                Kind = "aggregateRanking",
                Passed = rankingPassed,
                Tier = "A",
                Independent = true,
                TargetBackend = "com",
                TargetCoverage = coverage,
                ExpectedCount = sortedGroups.Count,
                ActualCount = result.GroupCount,
                ComparedCount = actualKeys.Count,
                DiffCount = rankingPassed ? 0 : 1
            };
            report.Items.Add(rankingOutcome);
            if (!rankingPassed)
            {
                report.DiffCount++;
                report.Diffs.Add(
                    "分组数量、输出截断形状或返回组顺序与独立 COM 全量排名不一致。");
            }

            report.Passed = report.Items.All(item => item.Passed);
            report.Tier = report.Items.All(item => string.Equals(item.Tier, "A", StringComparison.Ordinal))
                ? "A"
                : "B";
            report.Notes.Add(
                "COM 独立重算了全部 " + sortedGroups.Count
                + " 个分组，并核对了返回分组的度量与排名。");
            const string targetNotePrefix = "] target: ";
            var uniqueNotes = new List<string>();
            var seenNotes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var note in report.Notes)
            {
                var prefixIndex = note.IndexOf(targetNotePrefix, StringComparison.Ordinal);
                var normalizedNote = prefixIndex >= 0
                    ? note.Substring(prefixIndex + targetNotePrefix.Length)
                    : note;
                if (seenNotes.Add(normalizedNote))
                {
                    uniqueNotes.Add(normalizedNote);
                }
            }

            report.Notes.Clear();
            foreach (var note in uniqueNotes)
            {
                report.Notes.Add(note);
            }

            return report;
        }

        internal static ExcelVerificationReport CompareScalarAggregateOutput(
            ExcelAggregateResult result,
            ExcelVerificationBasis basis,
            ComCalcBackend backend,
            IList<ExcelAggregateMeasure> measures,
            IList<CalcFilter> filters,
            IList<CalcResult> calculated,
            ExcelCoverageReport coverage,
            int sourceRowCount)
        {
            var executions = new List<VerificationAssertionExecution>(measures.Count);
            for (var index = 0; index < measures.Count; index++)
            {
                double outputValue;
                if (!TryParseFiniteNumber(result.Rows[0][index], out outputValue))
                {
                    return CreateUnverifiedReport(
                        "全局聚合输出中的度量 "
                        + GetMeasureAlias(measures[index])
                        + " 不是有限数值，无法与 COM 结果比较。");
                }

                var plan = new VerificationAssertionPlan
                {
                    AssertionId = GetMeasureAlias(measures[index]),
                    Kind = "scalar",
                    ExpectedValue = outputValue,
                    Tolerance = Math.Max(1e-6d, Math.Abs(outputValue) * 1e-10d)
                };
                plan.Sides.Add(new PlannedCalcSide
                {
                    Side = "target",
                    Query = CreateScalarMeasureQuery(basis, measures[index], filters),
                    Backend = backend,
                    BackendName = "com",
                    Independent = true
                });
                executions.Add(new VerificationAssertionExecution
                {
                    Plan = plan,
                    TargetResult = calculated[index]
                });
            }

            var report = new Comparator().Compare(executions);
            var shapePassed = result.GroupCount == 1
                && result.TotalRowCount == sourceRowCount
                && !result.IsTruncated
                && result.Rows.Count == 1
                && result.Columns.Count == measures.Count;
            report.Items.Add(new ExcelAssertionOutcome
            {
                Id = "aggregate_scalar_shape",
                Kind = "aggregateShape",
                Passed = shapePassed,
                Tier = "A",
                Independent = true,
                TargetBackend = "com",
                TargetCoverage = coverage,
                ExpectedCount = 1,
                ActualCount = result.GroupCount,
                ComparedCount = result.Rows.Count,
                DiffCount = shapePassed ? 0 : 1
            });
            if (!shapePassed)
            {
                report.DiffCount++;
                report.Diffs.Add(
                    "全局汇总行数、分组数、数据覆盖或截断状态与源数据不一致。");
            }

            report.Passed = report.Items.All(item => item.Passed);
            report.Tier = report.Items.All(item =>
                string.Equals(item.Tier, "A", StringComparison.Ordinal))
                ? "A"
                : "B";
            report.Notes.Add(
                "COM 独立重算并核对了全局汇总的 "
                + measures.Count + " 个度量，覆盖源数据 "
                + sourceRowCount + " 行。");
            return report;
        }

        internal static bool MatchesExpectedGroupKeys(
            IList<string> actualKeys,
            IList<CalculatedGroup> sortedGroups,
            int expectedOutputCount)
        {
            if (actualKeys == null || sortedGroups == null
                || expectedOutputCount < 0
                || actualKeys.Count != expectedOutputCount
                || sortedGroups.Count < expectedOutputCount)
            {
                return false;
            }

            for (var index = 0; index < expectedOutputCount; index++)
            {
                if (!string.Equals(
                    actualKeys[index],
                    sortedGroups[index].Key,
                    StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool TryResolveSupportedScalarShape(
            ExcelAggregateRequest request,
            ExcelAggregateResult result,
            out IList<ExcelAggregateMeasure> measures,
            out IList<ExcelAggregateFilter> filters)
        {
            measures = null;
            filters = null;
            if (request == null
                || result == null
                || result.Rows == null
                || result.Columns == null
                || (request.GroupBy ?? new List<string>())
                    .Any(column => !string.IsNullOrWhiteSpace(column))
                || (request.DerivedGroupBy ?? new List<ExcelAggregateDerivedGroup>())
                    .Any(group => group != null))
            {
                return false;
            }

            measures = (request.Measures ?? new List<ExcelAggregateMeasure>())
                .Where(measure => measure != null)
                .ToList();
            if (measures.Count == 0 || result.Columns.Count != measures.Count)
            {
                return false;
            }

            foreach (var measure in measures)
            {
                var function = NormalizeFunction(measure.Function);
                if (string.IsNullOrWhiteSpace(measure.Column)
                    || (function != "sum"
                        && function != "count"
                        && function != "average"
                        && function != "avg"
                        && function != "min"
                        && function != "max"))
                {
                    return false;
                }
            }

            filters = new List<ExcelAggregateFilter>();
            foreach (var filter in (request.Filters ?? new List<ExcelAggregateFilter>())
                .Where(candidate => candidate != null))
            {
                ExcelAggregateFilter normalized;
                if (!TryResolveSupportedFilter(filter, out normalized))
                {
                    return false;
                }

                filters.Add(normalized);
            }

            return true;
        }

        internal static bool TryResolveSupportedUnionScalarShape(
            ExcelAggregateRequest request,
            ExcelAggregateResult result,
            ExcelVerificationBasis basis,
            out ExcelAggregateMeasure measure,
            out int sourceRowCount,
            out string reason)
        {
            measure = null;
            sourceRowCount = 0;
            reason = "union 聚合不符合自动独立复核的严格范围。";
            if (request == null
                || result == null
                || basis == null
                || request.Sources == null
                || request.Sources.Count < 2
                || request.Lookups != null && request.Lookups.Count > 0
                || request.Filters != null && request.Filters.Count > 0
                || basis.Lookups != null && basis.Lookups.Count > 0
                || basis.Filters != null && basis.Filters.Count > 0
                || basis.Keys != null && basis.Keys.Count > 0
                || !string.IsNullOrWhiteSpace(basis.Join)
                || basis.Source != null
                    && (!string.IsNullOrWhiteSpace(basis.Source.WorksheetName)
                        || !string.IsNullOrWhiteSpace(basis.Source.Address)
                        || !string.IsNullOrWhiteSpace(basis.Source.TableName))
                || (request.GroupBy ?? new List<string>())
                    .Any(column => !string.IsNullOrWhiteSpace(column))
                || (request.DerivedGroupBy ?? new List<ExcelAggregateDerivedGroup>())
                    .Any(group => group != null)
                || (basis.GroupBy ?? new List<string>())
                    .Any(column => !string.IsNullOrWhiteSpace(column))
                || (basis.DerivedGroupBy ?? new List<ExcelVerificationDerivedGroup>())
                    .Any(group => group != null)
                || request.Measures == null
                || request.Measures.Count != 1
                || request.Measures[0] == null
                || basis.Measures == null
                || basis.Measures.Count != 1
                || basis.Measures[0] == null
                || basis.Sources == null
                || basis.Sources.Count != request.Sources.Count
                || result.SourceSummaries == null
                || result.SourceSummaries.Count != request.Sources.Count
                || result.LookupSummaries != null && result.LookupSummaries.Count > 0
                || !string.Equals(
                    basis.Topology,
                    "union",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    result.SourceKind,
                    "union",
                    StringComparison.OrdinalIgnoreCase)
                || (!string.Equals(basis.HeaderMode, "yes", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(basis.HeaderMode, "no", StringComparison.OrdinalIgnoreCase))
                || !string.Equals(
                    request.HeaderMode,
                    basis.HeaderMode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            measure = request.Measures[0];
            if (string.IsNullOrWhiteSpace(measure.Column)
                || !string.Equals(
                    (measure.Function ?? string.Empty).Trim(),
                    "sum",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    basis.Measures[0].Column,
                    measure.Column,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    basis.Measures[0].Function,
                    measure.Function,
                    StringComparison.OrdinalIgnoreCase)
                || result.IsTruncated
                || result.Columns == null
                || result.Columns.Count != 1
                || !string.Equals(
                    result.Columns[0],
                    GetMeasureAlias(measure),
                    StringComparison.OrdinalIgnoreCase)
                || result.Rows == null
                || result.Rows.Count != 1
                || result.Rows[0] == null
                || result.Rows[0].Length != 1
                || result.GroupCount != 1
                || result.SourceSummaries.Any(source => source == null))
            {
                measure = null;
                return false;
            }

            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalRows = 0;
            for (var index = 0; index < request.Sources.Count; index++)
            {
                var requestedSource = request.Sources[index];
                var resultSource = result.SourceSummaries[index];
                var basisSource = basis.Sources[index];
                if (requestedSource == null
                    || resultSource == null
                    || basisSource == null
                    || string.IsNullOrWhiteSpace(requestedSource.WorksheetName)
                    || string.IsNullOrWhiteSpace(requestedSource.Address)
                    || !string.IsNullOrWhiteSpace(requestedSource.TableName)
                    || string.IsNullOrWhiteSpace(requestedSource.Alias)
                    || string.IsNullOrWhiteSpace(resultSource.WorksheetName)
                    || string.IsNullOrWhiteSpace(resultSource.Address)
                    || string.IsNullOrWhiteSpace(resultSource.Alias)
                    || !aliases.Add(requestedSource.Alias.Trim())
                    || !string.Equals(
                        requestedSource.Alias.Trim(),
                        resultSource.Alias.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        requestedSource.WorksheetName.Trim(),
                        resultSource.WorksheetName.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    || !AddressesMatch(requestedSource.Address, resultSource.Address)
                    || !string.Equals(
                        resultSource.WorksheetName.Trim(),
                        basisSource.Sheet.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    || !AddressesMatch(resultSource.Address, basisSource.Range)
                    || !string.Equals(
                        resultSource.Alias.Trim(),
                        basisSource.Alias.Trim(),
                        StringComparison.OrdinalIgnoreCase)
                    || resultSource.TotalRowCount < 0)
                {
                    measure = null;
                    return false;
                }

                totalRows += resultSource.TotalRowCount;
                if (totalRows > int.MaxValue)
                {
                    measure = null;
                    return false;
                }
            }

            sourceRowCount = (int)totalRows;
            if (sourceRowCount <= 0
                || result.TotalRowCount != sourceRowCount
                || result.FilteredRowCount < 0
                || result.SkippedRowCount < 0
                || (long)result.FilteredRowCount + result.SkippedRowCount != sourceRowCount
                || result.Coverage == null
                || result.Coverage.TotalRows != sourceRowCount
                || result.Coverage.ScannedRows != sourceRowCount)
            {
                measure = null;
                sourceRowCount = 0;
                return false;
            }

            reason = string.Empty;
            return true;
        }

        internal static CalcQuery CreateUnionScalarMeasureQuery(
            ExcelVerificationBasis basis,
            ExcelAggregateMeasure measure)
        {
            if (basis == null || basis.Sources == null || basis.Sources.Count < 2
                || measure == null || string.IsNullOrWhiteSpace(measure.Column))
            {
                throw new ArgumentException(
                    "union sum 查询必须包含至少两个来源和一个度量列。");
            }

            return new CalcQuery
            {
                Sources = basis.Sources.Select(source => source == null
                    ? null
                    : new ExcelVerificationSource
                    {
                        Sheet = source.Sheet,
                        Range = source.Range,
                        Alias = source.Alias
                    }).ToList(),
                Function = "sum",
                Columns = { measure.Column.Trim() },
                HeaderMode = basis.HeaderMode
            };
        }

        internal static bool HasCompleteUnionComResult(
            CalcResult result,
            int expectedSourceRowCount)
        {
            return result != null
                && result.Scalar.HasValue
                && !double.IsNaN(result.Scalar.Value)
                && !double.IsInfinity(result.Scalar.Value)
                && result.Coverage != null
                && string.Equals(
                    result.Coverage.Backend,
                    "com",
                    StringComparison.OrdinalIgnoreCase)
                && result.Coverage.TotalRows == expectedSourceRowCount
                && result.Coverage.ScannedRows == expectedSourceRowCount;
        }

        internal static bool TryResolveSupportedShape(
            ExcelAggregateRequest request,
            ExcelAggregateResult result,
            out GroupDefinition groupDefinition,
            out IList<ExcelAggregateMeasure> measures)
        {
            groupDefinition = null;
            measures = null;
            if (request == null
                || result == null
                || result.Rows == null
                || result.Columns == null)
            {
                return false;
            }

            var groupByCount = request.GroupBy == null ? 0 : request.GroupBy.Count;
            var derivedGroupCount = request.DerivedGroupBy == null
                ? 0
                : request.DerivedGroupBy.Count(group => group != null);
            if (groupByCount + derivedGroupCount != 1)
            {
                return false;
            }

            if (groupByCount == 1 && derivedGroupCount == 0)
            {
                if (string.IsNullOrWhiteSpace(request.GroupBy[0]))
                {
                    return false;
                }

                groupDefinition = new GroupDefinition(request.GroupBy[0].Trim(), string.Empty, 0d);
            }
            else
            {
                var derived = request.DerivedGroupBy.FirstOrDefault(group => group != null);
                var transform = (derived.Transform ?? string.Empty).Trim();
                var isPrefix = string.Equals(transform, "prefix", StringComparison.OrdinalIgnoreCase);
                var isFirstCharacter = string.Equals(
                    transform,
                    "firstCharacter",
                    StringComparison.OrdinalIgnoreCase);
                var isNumberBucket = ExcelAggregationEngine.DerivedGroupTransforms
                    .IsNumberBucket(transform);
                var isDateTransform = ExcelAggregationEngine.DerivedGroupTransforms
                    .IsDateTransform(transform);
                if (derived == null
                    || string.IsNullOrWhiteSpace(derived.Column)
                    || (!isFirstCharacter && !isPrefix && !isDateTransform && !isNumberBucket))
                {
                    return false;
                }

                if (isNumberBucket && derived.Size <= 0d)
                {
                    return false;
                }

                var size = isPrefix || isNumberBucket
                    ? (derived.Size > 0d ? derived.Size : 1d)
                    : 1d;
                if (double.IsNaN(size)
                    || double.IsInfinity(size)
                    || (isPrefix && size > SumIfsCriteriaBuilder.MaximumCriteriaLength)
                    || (isNumberBucket && size > 1e15))
                {
                    return false;
                }

                groupDefinition = new GroupDefinition(
                    derived.Column.Trim(),
                    transform,
                    isNumberBucket ? size : Math.Max(1, (int)size));
            }

            var filters = (request.Filters ?? new List<ExcelAggregateFilter>())
                .Where(filter => filter != null)
                .ToList();
            int? filteredMonth = null;
            var verificationFilters = new List<ExcelAggregateFilter>();
            if (filters.Count > 0)
            {
                foreach (var filter in filters)
                {
                    int parsedMonth;
                    if (string.Equals(
                            groupDefinition.Transform,
                            "month",
                            StringComparison.OrdinalIgnoreCase)
                        && TryParseMonthContainsFilter(
                            filter,
                            groupDefinition.SourceColumn,
                            out parsedMonth))
                    {
                        if (filteredMonth.HasValue)
                        {
                            return false;
                        }

                        filteredMonth = parsedMonth;
                        continue;
                    }

                    ExcelAggregateFilter normalizedFilter;
                    if (!TryResolveSupportedFilter(filter, out normalizedFilter))
                    {
                        return false;
                    }

                    verificationFilters.Add(normalizedFilter);
                }
            }

            groupDefinition = new GroupDefinition(
                groupDefinition.SourceColumn,
                groupDefinition.Transform,
                groupDefinition.Size,
                filteredMonth,
                verificationFilters);

            measures = (request.Measures ?? new List<ExcelAggregateMeasure>())
                .Where(measure => measure != null)
                .ToList();
            if (measures.Count == 0 || result.Columns.Count != measures.Count + 1)
            {
                return false;
            }

            foreach (var measure in measures)
            {
                var function = NormalizeFunction(measure.Function);
                if (string.IsNullOrWhiteSpace(measure.Column)
                    || (function != "sum" && function != "count" && function != "average" && function != "avg"))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolveSupportedFilter(
            ExcelAggregateFilter filter,
            out ExcelAggregateFilter normalizedFilter)
        {
            normalizedFilter = null;
            var operation = string.IsNullOrWhiteSpace(filter?.Operator)
                ? "eq"
                : filter.Operator.Trim().ToLowerInvariant();
            var value = filter?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filter?.Column)
                || !IsSupportedFilterOperator(operation))
            {
                return false;
            }

            if ((operation == "isnativenumber" || operation == "istextnumber")
                && value.Length > 0)
            {
                return false;
            }

            double numericValue;
            if ((operation == "gt" || operation == "ge" || operation == "lt" || operation == "le")
                && (!TryParseFiniteNumber(value.Trim(), out numericValue)))
            {
                return false;
            }

            var candidate = new ExcelAggregateFilter
            {
                Column = filter.Column.Trim(),
                Operator = operation,
                Value = value
            };
            string expression;
            if (!SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                CreateComFilter(candidate, false, false),
                "$A$1:$A$2",
                ".",
                out expression))
            {
                return false;
            }

            normalizedFilter = candidate;
            return true;
        }

        private static bool IsSupportedFilterOperator(string operation)
        {
            switch ((operation ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "eq":
                case "ne":
                case "contains":
                case "startswith":
                case "in":
                case "gt":
                case "ge":
                case "lt":
                case "le":
                case "isempty":
                case "notempty":
                case "isnativenumber":
                case "istextnumber":
                    return true;
                default:
                    return false;
            }
        }

        private static CalcFilter CreateComFilter(
            ExcelAggregateFilter filter,
            bool isNumericColumn,
            bool isDateColumn)
        {
            var operation = (filter.Operator ?? "eq").Trim().ToLowerInvariant();
            var comOperation = operation;
            if (operation == "eq")
            {
                double number;
                if (isDateColumn)
                {
                    comOperation = "dateTextEquals";
                }
                else if (isNumericColumn && TryParseFiniteNumber(filter.Value, out number)
                    && string.Equals(
                        ExcelValueSemantics.FormatNumber(number),
                        filter.Value,
                        StringComparison.Ordinal))
                {
                    comOperation = "eq";
                }
                else
                {
                    comOperation = "textEquals";
                }
            }
            else if (operation == "ne")
            {
                if (string.IsNullOrEmpty(filter.Value))
                {
                    comOperation = "notEmpty";
                }
                else if (isDateColumn)
                {
                    comOperation = "dateTextNotEquals";
                }
                else
                {
                    double number;
                    if (isNumericColumn && TryParseFiniteNumber(filter.Value, out number)
                        && string.Equals(
                            ExcelValueSemantics.FormatNumber(number),
                            filter.Value,
                            StringComparison.Ordinal))
                    {
                        comOperation = "ne";
                    }
                    else
                    {
                        comOperation = "textNotEquals";
                    }
                }
            }
            else if (operation == "contains")
            {
                comOperation = isDateColumn ? "dateTextContains" : "textContains";
            }
            else if (operation == "startswith")
            {
                comOperation = isDateColumn ? "dateTextStartsWith" : "textStartsWith";
            }
            else if (operation == "in")
            {
                comOperation = isDateColumn
                    ? "dateTextIn"
                    : isNumericColumn && IsCanonicalNumberList(filter.Value)
                            ? "numberIn"
                            : "textIn";
            }

            return new CalcFilter
            {
                Column = filter.Column,
                Operator = comOperation,
                Value = filter.Value
            };
        }

        private static bool IsCanonicalNumberList(string value)
        {
            foreach (var item in (value ?? string.Empty)
                .Split(',')
                .Select(candidate => candidate.Trim())
                .Where(candidate => candidate.Length > 0))
            {
                double number;
                if (!TryParseFiniteNumber(item, out number)
                    || !string.Equals(
                        ExcelValueSemantics.FormatNumber(number),
                        item,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCanonicalNumber(string value)
        {
            double number;
            return TryParseFiniteNumber(value, out number)
                && string.Equals(
                    ExcelValueSemantics.FormatNumber(number),
                    value,
                    StringComparison.Ordinal);
        }

        private static bool TryParseMonthContainsFilter(
            ExcelAggregateFilter filter,
            string sourceColumn,
            out int month)
        {
            month = 0;
            if (filter == null
                || !string.Equals(
                    filter.Column == null ? string.Empty : filter.Column.Trim(),
                    sourceColumn,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    filter.Operator == null ? string.Empty : filter.Operator.Trim(),
                    "contains",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = filter.Value ?? string.Empty;
            return value.Length == 4
                && value[0] == '-'
                && value[3] == '-'
                && int.TryParse(
                    value.Substring(1, 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out month)
                && month >= 1
                && month <= 12;
        }

        private static bool TryParseMonthGroupKey(string groupKey, out int month)
        {
            month = 0;
            return groupKey != null
                && groupKey.Length == 3
                && groupKey[2] == '月'
                && int.TryParse(
                    groupKey.Substring(0, 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out month)
                && month >= 1
                && month <= 12;
        }

        internal static CalcQuery CreateMeasureQuery(
            ExcelVerificationBasis basis,
            GroupDefinition definition,
            string groupKey,
            ExcelAggregateMeasure measure)
        {
            var query = new CalcQuery
            {
                Source = basis.Source,
                Function = NormalizeFunction(measure.Function) == "count"
                    ? "counta"
                    : measure.Function.Trim(),
                Columns = { measure.Column.Trim() },
                HeaderMode = basis.HeaderMode ?? "auto"
            };

            var verificationFilters = definition.CompiledVerificationFilters;
            if (verificationFilters == null)
            {
                verificationFilters = definition.VerificationFilters
                    .Select(filter => CreateComFilter(filter, false, false))
                    .ToList();
            }

            foreach (var filter in verificationFilters)
            {
                query.Filters.Add(new CalcFilter
                {
                    Column = filter.Column,
                    Operator = filter.Operator,
                    Value = filter.Value
                });
            }

            foreach (var groupFilter in CreateComGroupFilters(definition, groupKey))
            {
                query.Filters.Add(groupFilter);
            }

            return query;
        }

        internal static CalcQuery CreateScalarMeasureQuery(
            ExcelVerificationBasis basis,
            ExcelAggregateMeasure measure,
            IList<CalcFilter> compiledFilters)
        {
            var query = new CalcQuery
            {
                Source = basis.Source,
                Function = NormalizeFunction(measure.Function) == "count"
                    ? "counta"
                    : measure.Function.Trim(),
                Columns = { measure.Column.Trim() },
                HeaderMode = basis.HeaderMode ?? "auto"
            };
            foreach (var filter in compiledFilters ?? new List<CalcFilter>())
            {
                query.Filters.Add(new CalcFilter
                {
                    Column = filter.Column,
                    Operator = filter.Operator,
                    Value = filter.Value
                });
            }

            return query;
        }

        private static List<string> GetScalarVerificationReadColumns(
            IList<ExcelAggregateMeasure> measures,
            IList<ExcelAggregateFilter> filters)
        {
            var columns = new List<string>();
            foreach (var measure in measures)
            {
                AddUniqueColumn(columns, measure.Column);
            }

            foreach (var filter in filters)
            {
                AddUniqueColumn(columns, filter.Column);
            }

            return columns;
        }

        private static int GetScalarMeasureQueryReadColumnCount(
            ExcelAggregateMeasure measure,
            IList<ExcelAggregateFilter> filters)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                measure.Column.Trim()
            };
            foreach (var filter in filters)
            {
                columns.Add(filter.Column);
            }

            return columns.Count;
        }

        private static void AddUniqueColumn(IList<string> columns, string column)
        {
            if (!columns.Any(existing => string.Equals(
                existing,
                column,
                StringComparison.OrdinalIgnoreCase)))
            {
                columns.Add(column);
            }
        }

        private static IList<CalcFilter> CreateComGroupFilters(
            GroupDefinition definition,
            string groupKey)
        {
            if (ExcelAggregationEngine.DerivedGroupTransforms
                .IsNumberBucket(definition.Transform))
            {
                double[] range;
                if (definition.NumberBucketRanges == null
                    || !definition.NumberBucketRanges.TryGetValue(groupKey, out range))
                {
                    return new[]
                    {
                        new CalcFilter
                        {
                            Column = definition.SourceColumn,
                            Operator = "unsupportedNumberBucket",
                            Value = groupKey
                        }
                    };
                }

                return new[]
                {
                    new CalcFilter
                    {
                        Column = definition.SourceColumn,
                        Operator = "ge",
                        Value = range[0].ToString("R", CultureInfo.InvariantCulture)
                    },
                    new CalcFilter
                    {
                        Column = definition.SourceColumn,
                        Operator = "lt",
                        Value = range[1].ToString("R", CultureInfo.InvariantCulture)
                    }
                };
            }

            return new[] { CreateComGroupFilter(definition, groupKey) };
        }

        private static int GetGroupFilterCount(GroupDefinition definition)
        {
            return ExcelAggregationEngine.DerivedGroupTransforms
                .IsNumberBucket(definition.Transform)
                ? 2
                : 1;
        }

        private static CalcFilter CreateComGroupFilter(
            GroupDefinition definition,
            string groupKey)
        {
            var filter = CreateGroupFilter(definition, groupKey);
            var transform = (definition.Transform ?? string.Empty).Trim();
            if (string.Equals(transform, "firstCharacter", StringComparison.OrdinalIgnoreCase)
                || string.Equals(transform, "prefix", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(filter.Operator, "startsWith", StringComparison.OrdinalIgnoreCase))
                {
                    filter.Operator = "textStartsWith";
                }
                else if (string.Equals(filter.Operator, "eq", StringComparison.OrdinalIgnoreCase))
                {
                    filter.Operator = "textEquals";
                }

                return filter;
            }

            if (!ExcelAggregationEngine.DerivedGroupTransforms.IsDateTransform(transform)
                && string.Equals(filter.Operator, "eq", StringComparison.OrdinalIgnoreCase))
            {
                return CreateComFilter(
                    new ExcelAggregateFilter
                    {
                        Column = filter.Column,
                        Operator = filter.Operator,
                        Value = filter.Value
                    },
                    definition.SourceColumnIsNumeric,
                    definition.SourceColumnIsDate);
            }

            return filter;
        }

        private static List<string> GetVerificationReadColumns(GroupDefinition definition)
        {
            var columns = new List<string> { definition.SourceColumn };
            foreach (var filter in definition.VerificationFilters)
            {
                if (!columns.Any(column => string.Equals(
                    column,
                    filter.Column,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    columns.Add(filter.Column);
                }
            }

            return columns;
        }

        private static int GetMeasureQueryReadColumnCount(
            GroupDefinition definition,
            ExcelAggregateMeasure measure)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                definition.SourceColumn,
                measure.Column.Trim()
            };
            foreach (var filter in definition.VerificationFilters)
            {
                columns.Add(filter.Column);
            }

            return columns.Count;
        }

        internal static CalcFilter CreateGroupFilter(GroupDefinition definition, string groupKey)
        {
            var key = groupKey ?? string.Empty;
            if (key.Length == 0)
            {
                return new CalcFilter
                {
                    Column = definition.SourceColumn,
                    Operator = "isEmpty",
                    Value = key
                };
            }

            if (ExcelAggregationEngine.DerivedGroupTransforms.IsDateTransform(definition.Transform))
            {
                string operation;
                string value;
                if (!TryCreateDateGroupFilterValue(
                    definition.Transform,
                    key,
                    out operation,
                    out value))
                {
                    operation = "unsupportedDateGroupKey";
                    value = key;
                }

                return new CalcFilter
                {
                    Column = definition.SourceColumn,
                    Operator = operation,
                    Value = value
                };
            }

            var isPrefix = string.Equals(
                definition.Transform,
                "prefix",
                StringComparison.OrdinalIgnoreCase);
            var isShortPrefix = isPrefix && key.Length < (int)definition.Size;
            return new CalcFilter
            {
                Column = definition.SourceColumn,
                Operator = string.IsNullOrWhiteSpace(definition.Transform) || isShortPrefix
                    ? "eq"
                    : "startsWith",
                Value = key
            };
        }

        internal static bool TryGetDateGroupKey(
            string transform,
            object value,
            bool isDateColumn,
            out string groupKey)
        {
            groupKey = string.Empty;
            DateTime date;
            if (!TryGetNativeDate(value, isDateColumn, out date))
            {
                return false;
            }

            switch ((transform ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "year":
                    groupKey = date.Year.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "quarter":
                    groupKey = date.Year.ToString(CultureInfo.InvariantCulture)
                        + "-Q" + ((date.Month - 1) / 3 + 1).ToString(CultureInfo.InvariantCulture);
                    return true;
                case "month":
                    groupKey = date.Month.ToString("00", CultureInfo.InvariantCulture) + "月";
                    return true;
                case "monthname":
                    groupKey = date.ToString("MMMM", CultureInfo.CurrentCulture);
                    return !string.IsNullOrEmpty(groupKey);
                case "yearmonth":
                    groupKey = date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    return true;
                case "day":
                    groupKey = date.Day.ToString("00", CultureInfo.InvariantCulture);
                    return true;
                case "dayofweek":
                    var dayOfWeek = (int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek;
                    groupKey = dayOfWeek.ToString(CultureInfo.InvariantCulture)
                        + "-" + date.ToString("dddd", CultureInfo.CurrentCulture);
                    return !string.IsNullOrEmpty(groupKey);
                default:
                    return false;
            }
        }

        private static bool TryCreateDateGroupFilterValue(
            string transform,
            string groupKey,
            out string operation,
            out string value)
        {
            operation = string.Empty;
            value = string.Empty;
            var normalizedTransform = (transform ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalizedTransform)
            {
                case "year":
                    int year;
                    if (!TryParseYearGroupKey(groupKey, out year))
                    {
                        return false;
                    }

                    operation = "yearEquals";
                    value = year.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "quarter":
                    int quarterYear;
                    int quarter;
                    if (!TryParseQuarterGroupKey(groupKey, out quarterYear, out quarter))
                    {
                        return false;
                    }

                    operation = "quarterEquals";
                    value = quarterYear.ToString(CultureInfo.InvariantCulture)
                        + "-Q" + quarter.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "month":
                    int month;
                    if (!TryParseMonthGroupKey(groupKey, out month))
                    {
                        return false;
                    }

                    operation = "monthEquals";
                    value = month.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "monthname":
                    if (!TryParseMonthNameGroupKey(groupKey, out month))
                    {
                        return false;
                    }

                    operation = "monthEquals";
                    value = month.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "yearmonth":
                    int yearMonthYear;
                    int yearMonthMonth;
                    if (!TryParseYearMonthGroupKey(
                        groupKey,
                        out yearMonthYear,
                        out yearMonthMonth))
                    {
                        return false;
                    }

                    operation = "yearMonthEquals";
                    value = yearMonthYear.ToString("0000", CultureInfo.InvariantCulture)
                        + "-" + yearMonthMonth.ToString("00", CultureInfo.InvariantCulture);
                    return true;
                case "day":
                    int day;
                    if (!TryParseDayGroupKey(groupKey, out day))
                    {
                        return false;
                    }

                    operation = "dayEquals";
                    value = day.ToString(CultureInfo.InvariantCulture);
                    return true;
                case "dayofweek":
                    int dayOfWeek;
                    if (!TryParseDayOfWeekGroupKey(groupKey, out dayOfWeek))
                    {
                        return false;
                    }

                    operation = "dayOfWeekEquals";
                    value = dayOfWeek.ToString(CultureInfo.InvariantCulture);
                    return true;
                default:
                    return false;
            }
        }

        internal static bool TryGetMonthGroupKey(
            object value,
            bool isDateColumn,
            out string groupKey)
        {
            return TryGetDateGroupKey("month", value, isDateColumn, out groupKey);
        }

        internal static bool TryGetYearGroupKey(
            object value,
            bool isDateColumn,
            out string groupKey)
        {
            return TryGetDateGroupKey("year", value, isDateColumn, out groupKey);
        }

        private static bool TryGetNativeDate(
            object value,
            bool isDateColumn,
            out DateTime date)
        {
            date = default(DateTime);
            double serial;
            if (!isDateColumn
                || !ExcelValueSemantics.IsNativeNumber(value)
                || !ExcelValueSemantics.TryGetNumber(value, out serial)
                || serial < 1d
                || serial > 2958465d)
            {
                return false;
            }

            try
            {
                date = DateTime.FromOADate(serial);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryParseYearGroupKey(string groupKey, out int year)
        {
            year = 0;
            return !string.IsNullOrEmpty(groupKey)
                && int.TryParse(
                    groupKey,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out year)
                && year >= 1
                && year <= 9999
                && year.ToString(CultureInfo.InvariantCulture) == groupKey;
        }

        private static bool TryParseQuarterGroupKey(
            string groupKey,
            out int year,
            out int quarter)
        {
            year = 0;
            quarter = 0;
            if (string.IsNullOrEmpty(groupKey))
            {
                return false;
            }

            var marker = groupKey.IndexOf("-Q", StringComparison.Ordinal);
            if (marker <= 0
                || marker != groupKey.LastIndexOf("-Q", StringComparison.Ordinal)
                || !TryParseYearGroupKey(groupKey.Substring(0, marker), out year)
                || !int.TryParse(
                    groupKey.Substring(marker + 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out quarter)
                || quarter < 1
                || quarter > 4)
            {
                return false;
            }

            return year.ToString(CultureInfo.InvariantCulture)
                + "-Q" + quarter.ToString(CultureInfo.InvariantCulture) == groupKey;
        }

        private static bool TryParseMonthNameGroupKey(string groupKey, out int month)
        {
            month = 0;
            if (string.IsNullOrEmpty(groupKey))
            {
                return false;
            }

            var dateTimeFormat = CultureInfo.CurrentCulture.DateTimeFormat;
            for (var candidate = 1; candidate <= 12; candidate++)
            {
                if (!string.Equals(
                    dateTimeFormat.GetMonthName(candidate),
                    groupKey,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                if (month != 0)
                {
                    return false;
                }

                month = candidate;
            }

            return month != 0;
        }

        private static bool TryParseYearMonthGroupKey(
            string groupKey,
            out int year,
            out int month)
        {
            year = 0;
            month = 0;
            if (groupKey == null
                || groupKey.Length != 7
                || groupKey[4] != '-'
                || !int.TryParse(
                    groupKey.Substring(0, 4),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out year)
                || year < 1
                || year > 9999
                || !int.TryParse(
                    groupKey.Substring(5, 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out month)
                || month < 1
                || month > 12)
            {
                return false;
            }

            return year.ToString("0000", CultureInfo.InvariantCulture)
                + "-" + month.ToString("00", CultureInfo.InvariantCulture) == groupKey;
        }

        private static bool TryParseDayGroupKey(string groupKey, out int day)
        {
            day = 0;
            return groupKey != null
                && groupKey.Length == 2
                && int.TryParse(
                    groupKey,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out day)
                && day >= 1
                && day <= 31
                && day.ToString("00", CultureInfo.InvariantCulture) == groupKey;
        }

        private static bool TryParseDayOfWeekGroupKey(string groupKey, out int dayOfWeek)
        {
            dayOfWeek = 0;
            if (string.IsNullOrEmpty(groupKey))
            {
                return false;
            }

            var separator = groupKey.IndexOf('-');
            if (separator <= 0
                || !int.TryParse(
                    groupKey.Substring(0, separator),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out dayOfWeek)
                || dayOfWeek < 1
                || dayOfWeek > 7)
            {
                return false;
            }

            var dayName = CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(
                (DayOfWeek)(dayOfWeek % 7));
            return string.Equals(
                groupKey.Substring(separator + 1),
                dayName,
                StringComparison.Ordinal);
        }

        internal static bool HasCaseInsensitiveCollision(IList<string> keys)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                if (!seen.Add(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseFiniteNumber(string value, out double number)
        {
            return double.TryParse(
                    value,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out number)
                && !double.IsNaN(number)
                && !double.IsInfinity(number);
        }

        private static string NormalizeFunction(string function)
        {
            return (function ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string GetMeasureAlias(ExcelAggregateMeasure measure)
        {
            if (!string.IsNullOrWhiteSpace(measure.Alias))
            {
                return measure.Alias.Trim();
            }

            return measure.Column.Trim() + "_" + NormalizeFunction(measure.Function);
        }

        private static bool AddressesMatch(string left, string right)
        {
            return string.Equals(
                NormalizeAddress(left),
                NormalizeAddress(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAddress(string address)
        {
            return (address ?? string.Empty)
                .Trim()
                .Replace("$", string.Empty)
                .Replace(" ", string.Empty);
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

        private static bool IsFilterCompatibleWithColumnProfile(
            ExcelAggregateFilter sourceFilter,
            ExcelAggregationEngine.CompiledFilter compiledFilter,
            bool isTextColumn,
            bool isNumericColumn,
            bool isDateTextColumn)
        {
            var operation = compiledFilter.Operator;
            var value = sourceFilter.Value ?? string.Empty;
            if (operation == "isempty"
                || operation == "notempty"
                || operation == "isnativenumber"
                || operation == "istextnumber")
            {
                return true;
            }

            if (operation == "eq" || operation == "ne")
            {
                if (value.Length == 0)
                {
                    return true;
                }

                if (isTextColumn || isDateTextColumn)
                {
                    return true;
                }

                return isNumericColumn && IsCanonicalNumber(value);
            }

            if (operation == "contains" || operation == "startswith")
            {
                return value.Length == 0 || isTextColumn || isDateTextColumn;
            }

            if (operation == "in")
            {
                return isTextColumn || isDateTextColumn
                    || (isNumericColumn && IsCanonicalNumberList(value));
            }

            if (operation == "gt" || operation == "ge"
                || operation == "lt" || operation == "le")
            {
                return compiledFilter.HasNumericValue
                    && isNumericColumn
                    && !double.IsNaN(compiledFilter.NumericValue)
                    && !double.IsInfinity(compiledFilter.NumericValue);
            }

            return false;
        }

        private static void ObserveColumnTypeProfiles(
            object[][] columns,
            int rowIndex,
            int columnCount,
            bool[] allTextOrEmpty,
            bool[] allNativeNumbers,
            bool[] hasText,
            bool[] hasNativeNumber,
            bool[] hasOtherValue)
        {
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var value = columns[columnIndex][rowIndex];
                if (ExcelValueSemantics.IsEmpty(value))
                {
                    allNativeNumbers[columnIndex] = false;
                }
                else if (value is string)
                {
                    allNativeNumbers[columnIndex] = false;
                    hasText[columnIndex] = true;
                }
                else if (ExcelValueSemantics.IsNativeNumber(value))
                {
                    allTextOrEmpty[columnIndex] = false;
                    hasNativeNumber[columnIndex] = true;
                }
                else
                {
                    allTextOrEmpty[columnIndex] = false;
                    allNativeNumbers[columnIndex] = false;
                    hasOtherValue[columnIndex] = true;
                }
            }
        }

        internal sealed class GroupDefinition
        {
            public GroupDefinition(
                string sourceColumn,
                string transform,
                double size,
                int? filteredMonth = null,
                IList<ExcelAggregateFilter> verificationFilters = null)
            {
                SourceColumn = sourceColumn;
                Transform = transform;
                Size = size;
                FilteredMonth = filteredMonth;
                VerificationFilters = new List<ExcelAggregateFilter>(
                    verificationFilters ?? new List<ExcelAggregateFilter>());
                NumberBucketRanges = new Dictionary<string, double[]>(StringComparer.Ordinal);
            }

            public string SourceColumn { get; private set; }

            public string Transform { get; private set; }

            public double Size { get; private set; }

            public int? FilteredMonth { get; private set; }

            public IList<ExcelAggregateFilter> VerificationFilters { get; private set; }

            public IList<CalcFilter> CompiledVerificationFilters { get; set; }

            public bool SourceColumnIsText { get; set; }

            public bool SourceColumnIsNumeric { get; set; }

            public bool SourceColumnIsDate { get; set; }

            public IDictionary<string, double[]> NumberBucketRanges { get; private set; }
        }

        internal sealed class ScalarProfileSink : IColumnChunkSink
        {
            private readonly IList<ExcelAggregateMeasure> _measures;
            private readonly IList<ExcelAggregateFilter> _sourceFilters;
            private IList<ExcelAggregationEngine.CompiledFilter> _filters =
                new List<ExcelAggregationEngine.CompiledFilter>();
            private string[] _columns;
            private bool[] _dateFormatted;
            private bool[] _allTextOrEmpty;
            private bool[] _allNativeNumbers;
            private bool[] _hasText;
            private bool[] _hasNativeNumber;
            private bool[] _hasOtherValue;

            public ScalarProfileSink(
                IList<ExcelAggregateMeasure> measures,
                IList<ExcelAggregateFilter> filters)
            {
                _measures = measures;
                _sourceFilters = filters;
                CompiledFilters = new List<CalcFilter>();
            }

            public IList<CalcFilter> CompiledFilters { get; private set; }

            public string UnsupportedFilterReason { get; private set; }

            public string UnsupportedMeasureReason { get; private set; }

            public void Begin(ExcelColumnReadLayout layout)
            {
                if (layout.Columns == null || layout.Columns.Length == 0)
                {
                    throw new InvalidOperationException(
                        "全局聚合复核至少需要读取一个度量或筛选列。");
                }

                _columns = layout.Columns;
                _dateFormatted = layout.ColumnIsDateFormatted;
                _allTextOrEmpty = Enumerable.Repeat(true, _columns.Length).ToArray();
                _allNativeNumbers = Enumerable.Repeat(true, _columns.Length).ToArray();
                _hasText = new bool[_columns.Length];
                _hasNativeNumber = new bool[_columns.Length];
                _hasOtherValue = new bool[_columns.Length];
                var snapshot = new ExcelTabularSnapshot(
                    _columns,
                    new List<object[]>(),
                    _dateFormatted);
                _filters = ExcelAggregationEngine.BuildFilters(snapshot, _sourceFilters);
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                if (columns == null
                    || columns.Length != _columns.Length
                    || columns[0] == null)
                {
                    throw new InvalidOperationException(
                        "全局聚合类型画像读取返回了无效的数据块。");
                }

                for (var index = 0; index < columns.Length; index++)
                {
                    if (columns[index] == null
                        || columns[index].Length != columns[0].Length)
                    {
                        throw new InvalidOperationException(
                            "全局聚合类型画像读取包含无效列数据。");
                    }
                }

                for (var rowIndex = 0; rowIndex < columns[0].Length; rowIndex++)
                {
                    ObserveColumnTypeProfiles(
                        columns,
                        rowIndex,
                        _columns.Length,
                        _allTextOrEmpty,
                        _allNativeNumbers,
                        _hasText,
                        _hasNativeNumber,
                        _hasOtherValue);
                }
            }

            public void End()
            {
                var compiledFilters = new List<CalcFilter>();
                for (var index = 0; index < _sourceFilters.Count; index++)
                {
                    var sourceFilter = _sourceFilters[index];
                    var compiledFilter = _filters[index];
                    var columnIndex = compiledFilter.ColumnIndex;
                    var isDateTextColumn = compiledFilter.IsDateColumn
                        && !_hasOtherValue[columnIndex];
                    var isTextColumn = _allTextOrEmpty[columnIndex];
                    var isNumericColumn = _allNativeNumbers[columnIndex]
                        && _hasNativeNumber[columnIndex];
                    if (!IsFilterCompatibleWithColumnProfile(
                        sourceFilter,
                        compiledFilter,
                        isTextColumn,
                        isNumericColumn,
                        isDateTextColumn))
                    {
                        UnsupportedFilterReason = "筛选列 \"" + sourceFilter.Column
                            + "\" 的值类型与筛选条件 " + sourceFilter.Operator
                            + " 不匹配，无法保证 COM 独立复核与聚合筛选语义一致。";
                        return;
                    }

                    compiledFilters.Add(CreateComFilter(
                        sourceFilter,
                        isNumericColumn,
                        isDateTextColumn));
                }

                CompiledFilters = compiledFilters;
                foreach (var measure in _measures)
                {
                    var function = NormalizeFunction(measure.Function);
                    if (function != "min" && function != "max")
                    {
                        continue;
                    }

                    var columnIndex = FindColumnIndex(measure.Column);
                    if (_dateFormatted[columnIndex]
                        || !_hasNativeNumber[columnIndex]
                        || _hasText[columnIndex]
                        || _hasOtherValue[columnIndex])
                    {
                        UnsupportedMeasureReason = "全局 " + function
                            + " 度量 \"" + measure.Column
                            + "\" 不是纯原生数值列；托管极值包含文本数值的语义与 Excel MIN/MAX 不同。";
                        return;
                    }
                }
            }

            private int FindColumnIndex(string column)
            {
                for (var index = 0; index < _columns.Length; index++)
                {
                    if (string.Equals(
                        _columns[index],
                        column,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return index;
                    }
                }

                throw new InvalidOperationException(
                    "全局聚合类型画像中缺少度量列：" + column);
            }
        }

        internal sealed class GroupKeySink : IColumnChunkSink
        {
            private readonly GroupDefinition _definition;
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
            private IList<ExcelAggregationEngine.CompiledFilter> _filters =
                new List<ExcelAggregationEngine.CompiledFilter>();
            private bool _isDateColumn;
            private int _columnCount;
            private bool[] _allTextOrEmpty;
            private bool[] _allNativeNumbers;
            private bool[] _hasText;
            private bool[] _hasNativeNumber;
            private bool[] _hasOtherValue;

            public GroupKeySink(GroupDefinition definition)
            {
                _definition = definition;
                GroupKeys = new List<string>();
            }

            public IList<string> GroupKeys { get; private set; }

            public bool HasUnsupportedValue { get; private set; }

            public string UnsupportedFilterReason { get; private set; }

            public void Begin(ExcelColumnReadLayout layout)
            {
                if (layout.Columns == null
                    || layout.Columns.Length == 0
                    || !string.Equals(
                        layout.Columns[0],
                        _definition.SourceColumn,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("分组键读取必须先投影分组列。");
                }

                _columnCount = layout.Columns.Length;
                _allTextOrEmpty = Enumerable.Repeat(true, _columnCount).ToArray();
                _allNativeNumbers = Enumerable.Repeat(true, _columnCount).ToArray();
                _hasText = new bool[_columnCount];
                _hasNativeNumber = new bool[_columnCount];
                _hasOtherValue = new bool[_columnCount];
                _isDateColumn = layout.ColumnIsDateFormatted[0];
                var filterSnapshot = new ExcelTabularSnapshot(
                    layout.Columns,
                    new List<object[]>(),
                    layout.ColumnIsDateFormatted);
                _filters = ExcelAggregationEngine.BuildFilters(
                    filterSnapshot,
                    _definition.VerificationFilters);
                if (ExcelAggregationEngine.DerivedGroupTransforms
                    .IsDateTransform(_definition.Transform)
                    && !_isDateColumn)
                {
                    HasUnsupportedValue = true;
                }
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                if (columns == null
                    || columns.Length != _columnCount
                    || columns[0] == null)
                {
                    throw new InvalidOperationException("分组键读取返回了无效的数据块。");
                }

                for (var rowIndex = 0; rowIndex < columns[0].Length; rowIndex++)
                {
                    ObserveColumnTypeProfiles(
                        columns,
                        rowIndex,
                        _columnCount,
                        _allTextOrEmpty,
                        _allNativeNumbers,
                        _hasText,
                        _hasNativeNumber,
                        _hasOtherValue);
                    if (!ExcelAggregationEngine.PassesFilters(columns, rowIndex, _filters))
                    {
                        continue;
                    }

                    var value = columns[0][rowIndex];
                    if (ExcelValueSemantics.IsError(value))
                    {
                        HasUnsupportedValue = true;
                        continue;
                    }

                    string text;
                    if (ExcelAggregationEngine.DerivedGroupTransforms
                        .IsNumberBucket(_definition.Transform))
                    {
                        double number;
                        if (!ExcelValueSemantics.IsNativeNumber(value)
                            || !ExcelValueSemantics.TryGetNumber(value, out number))
                        {
                            HasUnsupportedValue = true;
                            continue;
                        }

                        var lower = Math.Floor(number / _definition.Size) * _definition.Size;
                        var upper = lower + _definition.Size;
                        if (double.IsNaN(lower)
                            || double.IsInfinity(lower)
                            || double.IsNaN(upper)
                            || double.IsInfinity(upper))
                        {
                            HasUnsupportedValue = true;
                            continue;
                        }

                        text = lower.ToString("0.####", CultureInfo.InvariantCulture)
                            + "~" + upper.ToString("0.####", CultureInfo.InvariantCulture);
                        double[] existingRange;
                        if (_definition.NumberBucketRanges.TryGetValue(text, out existingRange))
                        {
                            if (existingRange[0] != lower || existingRange[1] != upper)
                            {
                                HasUnsupportedValue = true;
                            }
                        }
                        else
                        {
                            _definition.NumberBucketRanges.Add(text, new[] { lower, upper });
                        }
                    }
                    else if (ExcelAggregationEngine.DerivedGroupTransforms
                        .IsDateTransform(_definition.Transform))
                    {
                        if (ExcelValueSemantics.IsEmpty(value))
                        {
                            if (_definition.FilteredMonth.HasValue)
                            {
                                continue;
                            }

                            HasUnsupportedValue = true;
                            continue;
                        }

                        if (!TryGetDateGroupKey(
                            _definition.Transform,
                            value,
                            _isDateColumn,
                            out text))
                        {
                            HasUnsupportedValue = true;
                            continue;
                        }

                        if (_definition.FilteredMonth.HasValue)
                        {
                            int month;
                            if (!TryParseMonthGroupKey(text, out month))
                            {
                                HasUnsupportedValue = true;
                                continue;
                            }

                            if (_definition.FilteredMonth.HasValue
                                && month != _definition.FilteredMonth.Value)
                            {
                                continue;
                            }
                        }
                    }
                    else
                    {
                        text = ExcelValueSemantics.ToText(value, _isDateColumn);
                        if (!string.IsNullOrWhiteSpace(_definition.Transform))
                        {
                            var length = string.Equals(
                                _definition.Transform,
                                "prefix",
                                StringComparison.OrdinalIgnoreCase)
                                ? Math.Max(1, (int)_definition.Size)
                                : 1;
                            if (text.Length > length)
                            {
                                text = text.Substring(0, length);
                            }
                        }
                    }

                    if (_seen.Add(text))
                    {
                        GroupKeys.Add(text);
                    }
                }
            }

            public void End()
            {
                _definition.SourceColumnIsText = _allTextOrEmpty[0];
                _definition.SourceColumnIsNumeric = _hasNativeNumber[0]
                    && !_hasText[0]
                    && !_hasOtherValue[0];
                _definition.SourceColumnIsDate = _isDateColumn;

                if (string.Equals(
                        _definition.Transform,
                        "firstCharacter",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        _definition.Transform,
                        "prefix",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!_definition.SourceColumnIsText)
                    {
                        HasUnsupportedValue = true;
                    }
                }
                else if (string.IsNullOrWhiteSpace(_definition.Transform)
                    && !_definition.SourceColumnIsText
                    && !_definition.SourceColumnIsNumeric)
                {
                    HasUnsupportedValue = true;
                }
                else if (ExcelAggregationEngine.DerivedGroupTransforms
                    .IsNumberBucket(_definition.Transform)
                    && !_allNativeNumbers[0])
                {
                    HasUnsupportedValue = true;
                }

                var compiledFilters = new List<CalcFilter>();
                for (var index = 0; index < _definition.VerificationFilters.Count; index++)
                {
                    var sourceFilter = _definition.VerificationFilters[index];
                    var compiledFilter = _filters[index];
                    var isDateTextColumn = compiledFilter.IsDateColumn
                        && !_hasOtherValue[compiledFilter.ColumnIndex];
                    var isTextColumn = _allTextOrEmpty[compiledFilter.ColumnIndex];
                    var isNumericColumn = _allNativeNumbers[compiledFilter.ColumnIndex]
                        && _hasNativeNumber[compiledFilter.ColumnIndex];
                    if (!IsFilterCompatibleWithColumnProfile(
                        sourceFilter,
                        compiledFilter,
                        isTextColumn,
                        isNumericColumn,
                        isDateTextColumn))
                    {
                        UnsupportedFilterReason = "筛选列 \"" + sourceFilter.Column
                            + "\" 的值类型与筛选条件 " + sourceFilter.Operator
                            + " 不匹配，无法保证 COM 独立复核与聚合筛选语义一致。";
                        return;
                    }

                    compiledFilters.Add(CreateComFilter(
                        sourceFilter,
                        isNumericColumn,
                        isDateTextColumn));
                }

                _definition.CompiledVerificationFilters = compiledFilters;
            }
        }

        internal sealed class CalculatedGroup
        {
            public CalculatedGroup(string key, int measureCount)
            {
                Key = key;
                Values = new double[measureCount];
                Results = new CalcResult[measureCount];
            }

            public string Key { get; private set; }

            public double[] Values { get; private set; }

            public CalcResult[] Results { get; private set; }
        }

        internal sealed class AggregateGroupComparer : IComparer<CalculatedGroup>
        {
            private readonly ExcelAggregateRequest _request;
            private readonly ExcelAggregateResult _result;
            private readonly IList<ExcelAggregateMeasure> _measures;

            public AggregateGroupComparer(
                ExcelAggregateRequest request,
                ExcelAggregateResult result,
                IList<ExcelAggregateMeasure> measures)
            {
                _request = request;
                _result = result;
                _measures = measures;
            }

            public int Compare(CalculatedGroup left, CalculatedGroup right)
            {
                foreach (var sort in _request.OrderBy ?? new List<ExcelAggregateSort>())
                {
                    if (sort == null || string.IsNullOrWhiteSpace(sort.Column))
                    {
                        continue;
                    }

                    var comparison = 0;
                    if (string.Equals(sort.Column.Trim(), _result.Columns[0], StringComparison.OrdinalIgnoreCase))
                    {
                        comparison = string.CompareOrdinal(left.Key, right.Key);
                    }
                    else
                    {
                        var measureIndex = -1;
                        for (var index = 0; index < _measures.Count; index++)
                        {
                            if (string.Equals(
                                sort.Column.Trim(),
                                _result.Columns[index + 1],
                                StringComparison.OrdinalIgnoreCase))
                            {
                                measureIndex = index;
                                break;
                            }
                        }

                        if (measureIndex >= 0)
                        {
                            comparison = left.Values[measureIndex].CompareTo(right.Values[measureIndex]);
                        }
                    }

                    if (comparison != 0)
                    {
                        return sort.Descending ? -comparison : comparison;
                    }
                }

                return string.CompareOrdinal(left.Key, right.Key);
            }
        }
    }
}
