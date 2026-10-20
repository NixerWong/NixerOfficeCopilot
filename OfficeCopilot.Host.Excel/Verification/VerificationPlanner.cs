using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Verification.Backends;

namespace OfficeCopilot.Host.Excel.Verification
{
    public sealed class VerificationPlanner
    {
        internal const int MaximumExplicitExcludedRows = 10000;

        private readonly IExcelCalcBackend _comBackend;
        private readonly IExcelCalcBackend _managedBackend;

        public VerificationPlanner(
            IExcelCalcBackend comBackend,
            IExcelCalcBackend managedBackend)
        {
            _comBackend = comBackend ?? throw new ArgumentNullException(nameof(comBackend));
            _managedBackend = managedBackend ?? throw new ArgumentNullException(nameof(managedBackend));
        }

        public VerificationAssertionPlan PlanAssertion(
            ExcelVerificationPlan verificationPlan,
            ExcelAssertion assertion,
            CalcCostEstimate targetCost,
            CalcCostEstimate sourceCost,
            bool targetProducedByManaged,
            bool sourceProducedByManaged)
        {
            if (verificationPlan == null)
            {
                throw new ArgumentNullException(nameof(verificationPlan));
            }

            if (assertion == null)
            {
                throw new ArgumentNullException(nameof(assertion));
            }

            if (verificationPlan.Basis == null || assertion.Target == null)
            {
                throw new InvalidOperationException("验证计划缺少 basis 或 target。");
            }

            var kind = Normalize(assertion.Kind);
            ValidateAssertionShape(assertion, kind, verificationPlan.Basis);
            if (targetCost == null)
            {
                throw new ArgumentNullException(nameof(targetCost));
            }

            if (assertion.Source != null && sourceCost == null)
            {
                throw new ArgumentNullException(nameof(sourceCost));
            }

            var result = new VerificationAssertionPlan
            {
                AssertionId = assertion.Id ?? string.Empty,
                Kind = assertion.Kind.Trim(),
                ExpectedValue = assertion.ExpectedValue,
                Tolerance = assertion.Tolerance,
                SampleCount = assertion.SampleCount ?? 3
            };
            var targetQuery = BuildQuery(
                verificationPlan.Basis,
                assertion,
                assertion.Target,
                kind,
                true);
            result.Sides.Add(SelectBackend(
                "target",
                kind,
                targetQuery,
                targetCost,
                targetProducedByManaged));

            if (assertion.Source != null)
            {
                var sourceQuery = BuildQuery(
                    verificationPlan.Basis,
                    assertion,
                    assertion.Source,
                    kind,
                    false);
                result.Sides.Add(SelectBackend(
                    "source",
                    kind,
                    sourceQuery,
                    sourceCost,
                    sourceProducedByManaged));
            }

            return result;
        }

        private PlannedCalcSide SelectBackend(
            string side,
            string assertionKind,
            CalcQuery query,
            CalcCostEstimate cost,
            bool producedByManaged)
        {
            var comCanEvaluate = _comBackend.CanEvaluate(query, cost);
            var managedCanEvaluate = _managedBackend.CanEvaluate(query, cost);
            IExcelCalcBackend selected;
            string reason;

            if (producedByManaged)
            {
                if (comCanEvaluate)
                {
                    selected = _comBackend;
                    reason = "托管产出要求使用独立 COM 复核。";
                }
                else if (managedCanEvaluate)
                {
                    selected = _managedBackend;
                    reason = "COM 不支持此查询，回退托管后端；该结果不具独立性。";
                }
                else
                {
                    throw UnsupportedQuery(side, query);
                }
            }
            else if (assertionKind == "scalar" || assertionKind == "rowcount")
            {
                if (comCanEvaluate)
                {
                    selected = _comBackend;
                    reason = "标量查询优先使用 Excel 原生 COM 后端。";
                }
                else if (managedCanEvaluate)
                {
                    selected = _managedBackend;
                    reason = "COM 不支持此查询，使用托管后端。";
                }
                else
                {
                    throw UnsupportedQuery(side, query);
                }
            }
            else if (managedCanEvaluate)
            {
                selected = _managedBackend;
                reason = "非标量查询默认使用托管后端。";
            }
            else if (comCanEvaluate)
            {
                selected = _comBackend;
                reason = "托管后端不支持此查询，回退到 COM 后端。";
            }
            else
            {
                throw UnsupportedQuery(side, query);
            }

            return new PlannedCalcSide
            {
                Side = side,
                Query = query,
                Backend = selected,
                BackendName = selected.Name,
                Independent = !producedByManaged
                    || !ReferenceEquals(selected, _managedBackend),
                Reason = reason
            };
        }

        private static InvalidOperationException UnsupportedQuery(string side, CalcQuery query)
        {
            return new InvalidOperationException(
                "验证 " + side + " 不受任何计算后端支持（函数 "
                + (query.Function ?? string.Empty) + "）。");
        }

        internal static CalcQuery BuildQuery(
            ExcelVerificationBasis basis,
            ExcelAssertion assertion,
            ExcelAssertionSide side,
            string kind,
            bool isTarget)
        {
            var query = new CalcQuery
            {
                Source = BuildSource(basis, side, !isTarget),
                Function = "sum",
                HeaderMode = string.IsNullOrWhiteSpace(basis.HeaderMode)
                    ? "auto"
                    : basis.HeaderMode,
                ExcludeRows = side.ExcludeRows == null
                    ? new List<int>()
                    : new List<int>(side.ExcludeRows),
                ExcludeLabelRows = side.ExcludeLabelRows
            };
            if (!isTarget
                && !HasSourceSelector(query.Source)
                && string.Equals(basis.Topology, "union", StringComparison.OrdinalIgnoreCase)
                && basis.Sources != null
                && basis.Sources.Count > 0)
            {
                query.Source = null;
                query.Sources = basis.Sources.Select(source => source == null
                    ? null
                    : new ExcelVerificationSource
                    {
                        Sheet = source.Sheet,
                        Range = source.Range,
                        Alias = source.Alias
                    }).ToList();
            }

            switch (kind)
            {
                case "scalar":
                    query.Function = ResolveFunction(side, basis, "sum");
                    query.Columns.AddRange(ResolveColumns(side, basis));
                    break;
                case "groupmatch":
                    query.Function = ResolveFunction(side, basis, "sum");
                    query.Columns.Add(Require(side.ValueColumn, "valueColumn"));
                    query.GroupByColumn = ResolveGroupColumn(side, basis);
                    break;
                case "keyset":
                case "keysetdifference":
                    query.Function = "keySet";
                    query.Columns.Add(Require(side.KeyColumn, "keyColumn"));
                    break;
                case "sorted":
                    query.Function = "sorted";
                    query.Columns.Add(Require(side.Column, "column"));
                    query.SortDirection = string.IsNullOrWhiteSpace(assertion.Direction)
                        ? "ascending"
                        : assertion.Direction.Trim();
                    break;
                case "rowcount":
                    if (isTarget)
                    {
                        query.Function = "rowCount";
                    }
                    else
                    {
                        var sourceColumns = ResolveColumns(side, basis);
                        query.Function = string.Equals(
                            ResolveFunction(side, basis, "count"),
                            "count",
                            StringComparison.OrdinalIgnoreCase)
                            && sourceColumns.Count == 0
                            ? "rowCount"
                            : ResolveFunction(side, basis, "count");
                        query.Columns.AddRange(sourceColumns);
                    }

                    break;
                case "nonblank":
                    query.Function = "nonBlank";
                    query.Columns.AddRange(ResolveColumns(side, basis));
                    break;
                default:
                    throw new InvalidOperationException("不支持的断言类型：" + assertion.Kind);
            }

            var filters = side.Filters ?? new List<CalcFilter>();
            if (!isTarget && filters.Count == 0)
            {
                filters = basis.Filters ?? new List<CalcFilter>();
            }

            foreach (var filter in filters)
            {
                if (filter == null)
                {
                    throw new InvalidOperationException("验证筛选条件不能为 null。");
                }

                query.Filters.Add(new CalcFilter
                {
                    Column = filter.Column,
                    Operator = filter.Operator,
                    Value = filter.Value
                });
            }

            if (IsMultiSourceQuery(query))
            {
                string reason;
                if (!ValidateMultiSourceQuery(query, out reason))
                {
                    throw new InvalidOperationException(reason);
                }
            }

            return query;
        }

        internal static bool IsMultiSourceQuery(CalcQuery query)
        {
            return query != null && query.Sources != null && query.Sources.Count > 0;
        }

        internal static bool IsAdditiveMultiSourceFunction(string function)
        {
            var normalized = Normalize(function);
            return normalized == "sum"
                || normalized == "count"
                || normalized == "counta"
                || normalized == "rowcount"
                || normalized == "nonblank"
                || normalized == "errorcount";
        }

        internal static bool ValidateMultiSourceQuery(CalcQuery query, out string reason)
        {
            reason = string.Empty;
            if (!IsMultiSourceQuery(query))
            {
                reason = "多源计算请求缺少 sources。";
                return false;
            }

            if (query.Source != null && HasSourceSelector(query.Source))
            {
                reason = "多源计算不能同时指定单一 source。";
                return false;
            }

            if (!IsAdditiveMultiSourceFunction(query.Function))
            {
                reason = "多源守恒只支持可加函数 sum、count、counta、rowCount、nonBlank 和 errorCount。";
                return false;
            }

            if ((query.ExcludeRows != null && query.ExcludeRows.Count > 0)
                || query.ExcludeLabelRows)
            {
                reason = "多源守恒不支持按行号或小计标签排除；请为每个来源单独构造一致的数据范围。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(query.GroupByColumn))
            {
                reason = "多源守恒不支持分组查询。";
                return false;
            }

            if (Normalize(query.Function) == "rowcount"
                && query.Filters != null
                && query.Filters.Count > 0)
            {
                reason = "多源 rowCount 不支持筛选；请改用可加的 count/nonBlank 指标或显式限定来源范围。";
                return false;
            }

            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in query.Sources)
            {
                if (source == null
                    || string.IsNullOrWhiteSpace(source.Sheet)
                    || string.IsNullOrWhiteSpace(source.Range)
                    || string.IsNullOrWhiteSpace(source.Alias))
                {
                    reason = "多源守恒的每个来源必须包含 sheet、range 和 alias。";
                    return false;
                }

                if (!aliases.Add(source.Alias.Trim()))
                {
                    reason = "多源守恒的来源 alias 不能重复：" + source.Alias;
                    return false;
                }
            }

            return true;
        }

        internal static CalcQuery CreateSingleSourceQuery(
            CalcQuery query,
            ExcelVerificationSource source)
        {
            if (query == null || source == null)
            {
                throw new ArgumentNullException(query == null ? nameof(query) : nameof(source));
            }

            return new CalcQuery
            {
                Source = new ExcelSourceRef
                {
                    WorksheetName = source.Sheet,
                    Address = source.Range
                },
                Function = query.Function,
                Columns = query.Columns == null
                    ? new List<string>()
                    : new List<string>(query.Columns),
                Filters = query.Filters == null
                    ? new List<CalcFilter>()
                    : query.Filters.Where(filter => filter != null)
                        .Select(filter => new CalcFilter
                        {
                            Column = filter.Column,
                            Operator = filter.Operator,
                            Value = filter.Value
                        }).ToList(),
                GroupByColumn = query.GroupByColumn,
                SortDirection = query.SortDirection,
                HeaderMode = query.HeaderMode,
                ExcludeRows = query.ExcludeRows == null
                    ? new List<int>()
                    : new List<int>(query.ExcludeRows),
                ExcludeLabelRows = query.ExcludeLabelRows
            };
        }

        internal static CalcResult CombineMultiSourceResults(
            IList<ExcelVerificationSource> sources,
            IList<CalcResult> sourceResults,
            string backendName)
        {
            if (sources == null || sourceResults == null
                || sources.Count == 0 || sources.Count != sourceResults.Count)
            {
                throw new InvalidOperationException(
                    "多源验证结果必须与来源列表一一对应。");
            }

            var total = 0d;
            var coverageReports = new List<ExcelCoverageReport>(sourceResults.Count);
            var notes = new List<string>();
            for (var index = 0; index < sourceResults.Count; index++)
            {
                var sourceResult = sourceResults[index];
                var source = sources[index];
                if (sourceResult == null || !sourceResult.Scalar.HasValue
                    || double.IsNaN(sourceResult.Scalar.Value)
                    || double.IsInfinity(sourceResult.Scalar.Value)
                    || sourceResult.Coverage == null)
                {
                    throw new InvalidOperationException(
                        "多源验证来源 \"" + source.Alias + "\" 未返回完整的有限标量与覆盖报告。");
                }

                total += sourceResult.Scalar.Value;
                if (double.IsNaN(total) || double.IsInfinity(total))
                {
                    throw new InvalidOperationException("多源可加指标求和溢出，无法完成守恒验证。");
                }

                coverageReports.Add(sourceResult.Coverage);
                foreach (var note in sourceResult.Notes ?? new List<string>())
                {
                    notes.Add(source.Alias + "：" + note);
                }
            }

            var allCellCountsAvailable = coverageReports.All(
                coverage => coverage.CellCountsAvailable == true);
            return new CalcResult
            {
                Scalar = total,
                Coverage = new ExcelCoverageReport
                {
                    TotalRows = coverageReports.Sum(coverage => coverage.TotalRows),
                    ScannedRows = coverageReports.Sum(coverage => coverage.ScannedRows),
                    Backend = backendName ?? "unknown",
                    CellCountsAvailable = allCellCountsAvailable,
                    NonNumericCount = allCellCountsAvailable
                        ? coverageReports.Sum(coverage => coverage.NonNumericCount)
                        : 0,
                    TextNumberCount = allCellCountsAvailable
                        ? coverageReports.Sum(coverage => coverage.TextNumberCount)
                        : 0,
                    ErrorCount = allCellCountsAvailable
                        ? coverageReports.Sum(coverage => coverage.ErrorCount)
                        : 0,
                    BlankCount = allCellCountsAvailable
                        ? coverageReports.Sum(coverage => coverage.BlankCount)
                        : 0
                },
                Notes = notes
            };
        }

        internal static CalcResult CreateEmptyMultiSourceResult(string backendName)
        {
            return new CalcResult
            {
                Scalar = 0d,
                Coverage = new ExcelCoverageReport
                {
                    Backend = backendName ?? "unknown",
                    CellCountsAvailable = true
                }
            };
        }

        internal static void ValidateAssertionShape(
            ExcelAssertion assertion,
            string kind,
            ExcelVerificationBasis basis)
        {
            if (string.IsNullOrWhiteSpace(assertion.Kind))
            {
                throw new InvalidOperationException("断言 kind 不能为空。");
            }

            if (assertion.ExpectedValue.HasValue
                && (double.IsNaN(assertion.ExpectedValue.Value)
                    || double.IsInfinity(assertion.ExpectedValue.Value)))
            {
                throw new InvalidOperationException("expectedValue 必须是有限数值。");
            }

            if (assertion.Tolerance.HasValue
                && (double.IsNaN(assertion.Tolerance.Value)
                    || double.IsInfinity(assertion.Tolerance.Value)))
            {
                throw new InvalidOperationException("tolerance 必须是有限数值。");
            }

            ValidateExclusions(assertion.Target, "target", kind);
            ValidateExclusions(assertion.Source, "source", kind);

            switch (kind)
            {
                case "scalar":
                    RequireScalarSide(assertion.Target, "target", basis);
                    if ((assertion.Source == null && !assertion.ExpectedValue.HasValue)
                        || (assertion.Source != null && assertion.ExpectedValue.HasValue))
                    {
                        throw new InvalidOperationException(
                            "scalar 必须且只能提供 source 或 expectedValue。");
                    }

                    if (assertion.Source != null
                        && ResolveColumns(assertion.Source, basis).Count > 0)
                    {
                        RequireScalarSide(assertion.Source, "source", basis);
                    }

                    break;
                case "groupmatch":
                    if (assertion.ExpectedValue.HasValue)
                    {
                        throw new InvalidOperationException(
                            "groupMatch 不接受 expectedValue，必须提供 target 和 source。");
                    }

                    RequireGroupSide(assertion.Target, "target");
                    RequireGroupSide(assertion.Source, "source");
                    break;
                case "keyset":
                case "keysetdifference":
                    if (assertion.ExpectedValue.HasValue)
                    {
                        throw new InvalidOperationException(
                            "keySet 和 keySetDifference 不接受 expectedValue，必须提供 target 和 source。");
                    }

                    RequireKeySide(assertion.Target, "target");
                    RequireKeySide(assertion.Source, "source");
                    if (kind == "keysetdifference"
                        && assertion.SampleCount.HasValue
                        && (assertion.SampleCount.Value < 1 || assertion.SampleCount.Value > 10))
                    {
                        throw new InvalidOperationException(
                            "keySetDifference sampleCount 必须在 1 到 10 之间。");
                    }
                    if (kind == "keyset" && assertion.SampleCount.HasValue)
                    {
                        throw new InvalidOperationException(
                            "sampleCount 只适用于 keySetDifference。");
                    }

                    break;
                case "sorted":
                    Require(assertion.Target.Column, "target.column");
                    if (assertion.Source != null
                        || assertion.ExpectedValue.HasValue
                        || (assertion.Target.Filters != null && assertion.Target.Filters.Count > 0))
                    {
                        throw new InvalidOperationException("sorted 只接受 target 列和 direction。");
                    }

                    var direction = string.IsNullOrWhiteSpace(assertion.Direction)
                        ? "ascending"
                        : assertion.Direction.Trim();
                    if (!string.Equals(direction, "ascending", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(direction, "descending", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("sorted direction 必须为 ascending 或 descending。");
                    }

                    break;
                case "rowcount":
                    if (assertion.Target.Filters != null
                        && assertion.Target.Filters.Count > 0)
                    {
                        throw new InvalidOperationException(
                            "rowCount 的 target 不支持筛选；请使用正确的数据范围，或由调用方提供 expectedValue。");
                    }

                    if ((assertion.Source == null && !assertion.ExpectedValue.HasValue)
                        || (assertion.Source != null && assertion.ExpectedValue.HasValue))
                    {
                        throw new InvalidOperationException(
                            "rowCount 必须且只能提供 source 或 expectedValue。");
                    }

                    if (assertion.Source != null)
                    {
                        RequireScalarSide(assertion.Source, "source", basis);
                    }

                    break;
                case "nonblank":
                    RequireColumns(assertion.Target, "target");
                    if ((assertion.Source == null && !assertion.ExpectedValue.HasValue)
                        || (assertion.Source != null && assertion.ExpectedValue.HasValue))
                    {
                        throw new InvalidOperationException(
                            "nonBlank 必须且只能提供 source 或 expectedValue。");
                    }

                    if (assertion.Source != null)
                    {
                        RequireColumns(assertion.Source, "source");
                    }

                    break;
                default:
                    throw new InvalidOperationException("不支持的断言类型：" + assertion.Kind);
            }
        }

        private static void ValidateExclusions(
            ExcelAssertionSide side,
            string label,
            string kind)
        {
            if (side == null)
            {
                return;
            }

            var excludedRows = side.ExcludeRows ?? new List<int>();
            var hasExclusions = excludedRows.Count > 0 || side.ExcludeLabelRows;
            if (!hasExclusions)
            {
                return;
            }

            if (kind != "scalar" && kind != "groupmatch")
            {
                throw new InvalidOperationException(
                    label + " 的行排除选项仅适用于 scalar 和 groupMatch。");
            }

            if (excludedRows.Count > MaximumExplicitExcludedRows)
            {
                throw new InvalidOperationException(
                    label + ".excludeRows 最多允许 "
                    + MaximumExplicitExcludedRows + " 个工作表行号。");
            }

            var seen = new HashSet<int>();
            foreach (var row in excludedRows)
            {
                if (row <= 0 || !seen.Add(row))
                {
                    throw new InvalidOperationException(
                        label + ".excludeRows 必须包含唯一的正整数工作表行号。");
                }
            }
        }

        private static void RequireScalarSide(
            ExcelAssertionSide side,
            string label,
            ExcelVerificationBasis basis)
        {
            if (side == null || ResolveColumns(side, basis).Count == 0)
            {
                throw new InvalidOperationException(label + " 必须指定用于数值计算的列。");
            }
        }

        private static void RequireGroupSide(ExcelAssertionSide side, string label)
        {
            if (side == null)
            {
                throw new InvalidOperationException(label + " 不能为空。");
            }

            Require(side.KeyColumn, label + ".keyColumn");
            Require(side.ValueColumn, label + ".valueColumn");
        }

        private static void RequireKeySide(ExcelAssertionSide side, string label)
        {
            if (side == null)
            {
                throw new InvalidOperationException(label + " 不能为空。");
            }

            Require(side.KeyColumn, label + ".keyColumn");
        }

        private static void RequireColumns(ExcelAssertionSide side, string label)
        {
            if (side == null)
            {
                throw new InvalidOperationException(label + " 不能为空。");
            }

            if ((side.Columns == null || side.Columns.Count == 0)
                && string.IsNullOrWhiteSpace(side.Column))
            {
                throw new InvalidOperationException(label + " 必须指定 column 或 columns。");
            }
        }

        private static IList<string> ResolveColumns(
            ExcelAssertionSide side,
            ExcelVerificationBasis basis)
        {
            if (side.Columns != null && side.Columns.Count > 0)
            {
                return new List<string>(side.Columns);
            }

            if (!string.IsNullOrWhiteSpace(side.Column))
            {
                return new List<string> { side.Column.Trim() };
            }

            if (basis.Measures != null && basis.Measures.Count == 1)
            {
                var measure = basis.Measures[0];
                if (measure != null && !string.IsNullOrWhiteSpace(measure.Column))
                {
                    return new List<string> { measure.Column.Trim() };
                }
            }

            return new List<string>();
        }

        private static string ResolveFunction(
            ExcelAssertionSide side,
            ExcelVerificationBasis basis,
            string defaultFunction)
        {
            if (!string.IsNullOrWhiteSpace(side.Function))
            {
                return side.Function.Trim();
            }

            if (basis.Measures != null && basis.Measures.Count == 1
                && basis.Measures[0] != null
                && !string.IsNullOrWhiteSpace(basis.Measures[0].Function))
            {
                return basis.Measures[0].Function.Trim();
            }

            return defaultFunction;
        }

        private static string ResolveGroupColumn(
            ExcelAssertionSide side,
            ExcelVerificationBasis basis)
        {
            if (!string.IsNullOrWhiteSpace(side.KeyColumn))
            {
                return side.KeyColumn.Trim();
            }

            if (basis.GroupBy != null && basis.GroupBy.Count == 1)
            {
                return basis.GroupBy[0];
            }

            throw new InvalidOperationException("groupMatch 必须指定一个 keyColumn。");
        }

        private static ExcelSourceRef BuildSource(
            ExcelVerificationBasis basis,
            ExcelAssertionSide side,
            bool inheritBasisSource)
        {
            var basisSource = inheritBasisSource ? basis.Source : null;
            return new ExcelSourceRef
            {
                WorksheetName = FirstNonEmpty(side.WorksheetName, basisSource == null ? null : basisSource.WorksheetName),
                Address = FirstNonEmpty(side.Address, basisSource == null ? null : basisSource.Address),
                TableName = FirstNonEmpty(side.TableName, basisSource == null ? null : basisSource.TableName)
            };
        }

        private static bool HasSourceSelector(ExcelSourceRef source)
        {
            return source != null
                && (!string.IsNullOrWhiteSpace(source.WorksheetName)
                    || !string.IsNullOrWhiteSpace(source.Address)
                    || !string.IsNullOrWhiteSpace(source.TableName));
        }

        private static string FirstNonEmpty(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? (fallback ?? string.Empty) : value.Trim();
        }

        private static string Require(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(parameter + " 不能为空。");
            }

            return value.Trim();
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }
    }

    public sealed class VerificationAssertionPlan
    {
        public VerificationAssertionPlan()
        {
            AssertionId = string.Empty;
            Kind = string.Empty;
            SampleCount = 3;
            Sides = new List<PlannedCalcSide>();
        }

        public string AssertionId { get; set; }

        public string Kind { get; set; }

        public double? ExpectedValue { get; set; }

        public double? Tolerance { get; set; }

        public int SampleCount { get; set; } = 3;

        public IList<PlannedCalcSide> Sides { get; private set; }
    }

    public sealed class PlannedCalcSide
    {
        public string Side { get; set; }

        public CalcQuery Query { get; set; }

        public IExcelCalcBackend Backend { get; set; }

        public string BackendName { get; set; }

        public bool Independent { get; set; }

        public string Reason { get; set; }
    }
}
