using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Analysis.Accumulators;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Verification;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel.Verification.Backends.Managed
{
    public sealed class ManagedCalcBackend : ExcelServiceBase, IExcelCalcBackend
    {
        public ManagedCalcBackend(ExcelInterop.Application application)
            : base(application)
        {
        }

        public string Name
        {
            get { return "managed"; }
        }

        public bool CanEvaluate(CalcQuery query, CalcCostEstimate cost)
        {
            return CanEvaluateQuery(query, cost);
        }

        internal static bool CanEvaluateQuery(CalcQuery query, CalcCostEstimate cost)
        {
            if (VerificationPlanner.IsMultiSourceQuery(query))
            {
                string multiSourceReason;
                if (!VerificationPlanner.ValidateMultiSourceQuery(query, out multiSourceReason))
                {
                    return false;
                }

                return CanEvaluateQuery(
                    VerificationPlanner.CreateSingleSourceQuery(query, query.Sources[0]),
                    cost);
            }

            int requiredColumnCount;
            string reason;
            var isRowCount = query != null
                && string.Equals(query.Function, "rowCount", StringComparison.OrdinalIgnoreCase);
            if (cost == null
                || !TryValidateQuery(query, out requiredColumnCount, out reason)
                || cost.RowCount < 0
                || cost.ReadColumnCount < requiredColumnCount
                || (isRowCount
                    ? cost.ReadColumnCount != 0
                    : requiredColumnCount > 0 && cost.ReadColumnCount <= 0)
                || cost.ProjectedCellCount != (long)cost.RowCount * cost.ReadColumnCount
                || cost.ProjectedCellCount > ExcelColumnReader.MaximumProjectedCellCount)
            {
                return false;
            }

            return true;
        }

        public CalcResult Evaluate(CalcQuery query)
        {
            if (VerificationPlanner.IsMultiSourceQuery(query))
            {
                return EvaluateMultiSource(query);
            }

            int requiredColumnCount;
            string reason;
            if (!TryValidateQuery(query, out requiredColumnCount, out reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (_application == null)
            {
                throw new InvalidOperationException("当前未连接 Excel 实例。");
            }

            var sourceService = new ExcelAggregationService(_application);
            ExcelInterop.Range sourceRange = null;
            try
            {
                string sourceKind;
                string resolvedTableName;
                string worksheetName;
                sourceRange = sourceService.ResolveSource(
                    query.Source,
                    out sourceKind,
                    out resolvedTableName,
                    out worksheetName);

                var reader = new ExcelColumnReader(_application);
                var headerMode = ExcelAggregationService.ResolveHeaderMode(
                    query.HeaderMode,
                    sourceKind);
                if (NormalizeFunction(query.Function) == "rowcount")
                {
                    var layout = reader.ResolveLayout(
                        sourceRange,
                        new ExcelColumnReadRequest { HeaderMode = headerMode });
                    var rowCountResult = CreateRowCountResult(layout.TotalRows);
                    if (layout.HeaderInferred)
                    {
                        rowCountResult.Notes.Add(
                            "源数据表头为自动推断：" + layout.HeaderInferenceReason);
                    }

                    return rowCountResult;
                }

                if (NormalizeFunction(query.Function) == "sorted")
                {
                    var sortedSink = new SortedCalcChunkSink(query.SortDirection);
                    var layout = reader.Read(
                        sourceRange,
                        new ExcelColumnReadRequest
                        {
                            Columns = GetRequestedColumns(query),
                            HeaderMode = headerMode
                        },
                        sortedSink);
                    var sortedResult = sortedSink.Result;
                    if (layout.HeaderInferred)
                    {
                        sortedResult.Notes.Add("源数据表头为自动推断：" + layout.HeaderInferenceReason);
                    }

                    return sortedResult;
                }

                var sink = new ManagedCalcChunkSink(query);
                var dataLayout = reader.Read(
                    sourceRange,
                    new ExcelColumnReadRequest
                    {
                        Columns = GetRequestedColumns(query),
                        HeaderMode = headerMode,
                        IncludeAllColumns = query.ExcludeLabelRows
                    },
                    sink);

                var result = sink.Result;
                if (dataLayout.HeaderInferred)
                {
                    result.Notes.Add("源数据表头为自动推断：" + dataLayout.HeaderInferenceReason);
                }

                return result;
            }
            finally
            {
                ReleaseComObject(sourceRange);
            }
        }

        private CalcResult EvaluateMultiSource(CalcQuery query)
        {
            string reason;
            if (!VerificationPlanner.ValidateMultiSourceQuery(query, out reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (_application == null)
            {
                throw new InvalidOperationException("当前未连接 Excel 实例。");
            }

            var sourceService = new ExcelAggregationService(_application);
            var results = new List<CalcResult>(query.Sources.Count);
            foreach (var source in query.Sources)
            {
                var sourceQuery = VerificationPlanner.CreateSingleSourceQuery(query, source);
                var estimate = sourceService.EstimateCalcCost(sourceQuery);
                results.Add(estimate.RowCount == 0
                    ? VerificationPlanner.CreateEmptyMultiSourceResult(Name)
                    : Evaluate(sourceQuery));
            }

            return VerificationPlanner.CombineMultiSourceResults(
                query.Sources,
                results,
                Name);
        }

        private static bool TryValidateQuery(
            CalcQuery query,
            out int requiredColumnCount,
            out string reason)
        {
            requiredColumnCount = 0;
            reason = "托管计算请求无效。";
            if (query == null || query.Source == null || query.Filters == null)
            {
                return false;
            }

            var function = NormalizeFunction(query.Function);
            var excludedRows = query.ExcludeRows ?? new List<int>();
            if (excludedRows.Count > VerificationPlanner.MaximumExplicitExcludedRows)
            {
                reason = "托管计算的 excludeRows 数量超过上限。";
                return false;
            }

            var seenExcludedRows = new HashSet<int>();
            foreach (var row in excludedRows)
            {
                if (row <= 0 || !seenExcludedRows.Add(row))
                {
                    reason = "托管计算的 excludeRows 必须是唯一的正整数工作表行号。";
                    return false;
                }
            }

            if (HasExclusions(query)
                && (function == "keyset" || function == "nonblank" || function == "sorted"))
            {
                reason = "托管行排除仅支持数值标量和分组聚合。";
                return false;
            }
            if (function == "rowcount")
            {
                if (query.Columns == null || query.Columns.Count != 0
                    || query.Filters.Count != 0
                    || !string.IsNullOrWhiteSpace(query.GroupByColumn)
                    || HasExclusions(query))
                {
                    reason = "托管 rowCount 只接受无列、无筛选、无分组、无行排除的请求。";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (query.Columns == null || query.Columns.Count == 0)
            {
                reason = "托管计算请求至少需要一个数据列。";
                return false;
            }

            if (function != "sum"
                && function != "count"
                && function != "counta"
                && function != "countdistinct"
                && function != "avg"
                && function != "average"
                && function != "min"
                && function != "max"
                && function != "errorcount"
                && function != "keyset"
                && function != "nonblank"
                && function != "sorted")
            {
                reason = "托管后端不支持函数：" + (query.Function ?? string.Empty);
                return false;
            }

            if (function != "sum" && function != "nonblank" && query.Columns.Count != 1)
            {
                reason = "托管后端的 " + function + " 函数只支持单列。";
                return false;
            }

            if (function == "nonblank" && !string.IsNullOrWhiteSpace(query.GroupByColumn))
            {
                reason = "托管 nonBlank 不支持分组。";
                return false;
            }

            if ((function == "keyset" || function == "sorted")
                && !string.IsNullOrWhiteSpace(query.GroupByColumn))
            {
                reason = "托管 " + function + " 不支持分组。";
                return false;
            }

            if (function == "sorted"
                && (query.Filters.Count != 0
                    || (!string.Equals(query.SortDirection, "ascending", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(query.SortDirection, "descending", StringComparison.OrdinalIgnoreCase))))
            {
                reason = "托管 sorted 仅支持无筛选的 ascending / descending 检查。";
                return false;
            }

            var requiredColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in query.Columns)
            {
                var name = (column ?? string.Empty).Trim();
                if (name.Length == 0 || !requiredColumns.Add(name))
                {
                    reason = "托管计算请求包含空列名或重复列。";
                    return false;
                }
            }

            foreach (var filter in query.Filters)
            {
                if (filter == null || string.IsNullOrWhiteSpace(filter.Column) || filter.Value == null)
                {
                    reason = "托管计算请求包含无效筛选条件。";
                    return false;
                }

                var filterOperator = NormalizeOperator(filter.Operator);
                if (filterOperator != "eq"
                    && filterOperator != "ne"
                    && filterOperator != "gt"
                    && filterOperator != "ge"
                    && filterOperator != "lt"
                    && filterOperator != "le"
                    && filterOperator != "contains"
                    && filterOperator != "blank"
                    && filterOperator != "notblank"
                    && filterOperator != "isempty"
                    && filterOperator != "notempty")
                {
                    reason = "托管后端不支持筛选运算符：" + (filter.Operator ?? string.Empty);
                    return false;
                }

                requiredColumns.Add(filter.Column.Trim());
            }

            if (!string.IsNullOrWhiteSpace(query.GroupByColumn))
            {
                if (function == "sum" && query.Columns.Count != 1)
                {
                    reason = "托管后端分组查询只支持一个求和列。";
                    return false;
                }

                requiredColumns.Add(query.GroupByColumn.Trim());
            }

            requiredColumnCount = requiredColumns.Count;
            reason = string.Empty;
            return true;
        }

        private static IList<string> GetRequestedColumns(CalcQuery query)
        {
            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in query.Columns)
            {
                AddColumn(columns, seen, column);
            }

            foreach (var filter in query.Filters)
            {
                AddColumn(columns, seen, filter.Column);
            }

            AddColumn(columns, seen, query.GroupByColumn);
            return columns;
        }

        private static bool HasExclusions(CalcQuery query)
        {
            return query != null
                && ((query.ExcludeRows != null && query.ExcludeRows.Count > 0)
                    || query.ExcludeLabelRows);
        }

        private static void AddColumn(IList<string> columns, ISet<string> seen, string column)
        {
            if (!string.IsNullOrWhiteSpace(column) && seen.Add(column.Trim()))
            {
                columns.Add(column.Trim());
            }
        }

        private static ExcelAggregateRequest CreateAggregateRequest(CalcQuery query)
        {
            var function = NormalizeFunction(query.Function);
            if (function == "counta" || function == "nonblank" || function == "keyset")
            {
                function = "count";
            }
            else if (function == "avg")
            {
                function = "average";
            }

            var request = new ExcelAggregateRequest
            {
                GroupBy = function == "count" && NormalizeFunction(query.Function) == "keyset"
                    ? new List<string> { query.Columns[0].Trim() }
                    : string.IsNullOrWhiteSpace(query.GroupByColumn)
                        ? new List<string>()
                        : new List<string> { query.GroupByColumn.Trim() },
                MaxGroups = int.MaxValue
            };

            for (var index = 0; index < query.Columns.Count; index++)
            {
                request.Measures.Add(new ExcelAggregateMeasure
                {
                    Column = query.Columns[index].Trim(),
                    Function = function,
                    Alias = "value" + index.ToString(CultureInfo.InvariantCulture)
                });
            }

            foreach (var filter in query.Filters)
            {
                var filterOperator = NormalizeOperator(filter.Operator);
                request.Filters.Add(new ExcelAggregateFilter
                {
                    Column = filter.Column.Trim(),
                    Operator = filterOperator == "blank"
                        ? "isEmpty"
                        : filterOperator == "notblank"
                            ? "notEmpty"
                            : filterOperator,
                    Value = filter.Value
                });
            }

            if (NormalizeFunction(query.Function) == "keyset")
            {
                request.Filters.Add(new ExcelAggregateFilter
                {
                    Column = query.Columns[0].Trim(),
                    Operator = "notEmpty",
                    Value = string.Empty
                });
            }

            return request;
        }

        private static string NormalizeFunction(string function)
        {
            return (function ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string NormalizeOperator(string filterOperator)
        {
            return (filterOperator ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static double? ParseNumericResult(string value, string function)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            double number;
            if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number)
                || double.IsNaN(number)
                || double.IsInfinity(number))
            {
                throw new InvalidOperationException(
                    "托管函数 " + function + " 返回了非数值结果，无法用于数值验证：" + value);
            }

            return number;
        }

        private static CalcResult ConvertResult(
            CalcQuery query,
            ExcelAggregateResult aggregate,
            int groupCount)
        {
            if (aggregate.IsTruncated)
            {
                throw new InvalidOperationException("托管验证结果被截断，拒绝返回不完整分组。");
            }

            var result = new CalcResult
            {
                Coverage = aggregate.Coverage
            };
            foreach (var warning in aggregate.Warnings)
            {
                result.Notes.Add(warning);
            }

            var function = NormalizeFunction(query.Function);
            if (function == "keyset")
            {
                foreach (var row in aggregate.Rows)
                {
                    if (row == null || row.Length == 0)
                    {
                        throw new InvalidOperationException("托管 keySet 结果结构不完整。");
                    }

                    result.Keys.Add(row[0]);
                }

                return result;
            }

            if (groupCount == 0)
            {
                if (aggregate.Rows.Count == 0)
                {
                    result.Scalar = function == "min" || function == "max"
                        || function == "average" || function == "avg"
                        ? (double?)null
                        : 0d;
                    return result;
                }

                var values = aggregate.Rows[0];
                if ((function == "sum" || function == "nonblank") && query.Columns.Count > 1)
                {
                    var total = 0d;
                    for (var index = 0; index < query.Columns.Count; index++)
                    {
                        total += ParseNumericResult(values[index], function).GetValueOrDefault();
                    }

                    result.Scalar = total;
                }
                else
                {
                    result.Scalar = ParseNumericResult(values[0], function);
                }

                return result;
            }

            foreach (var row in aggregate.Rows)
            {
                if (row == null || row.Length <= groupCount)
                {
                    throw new InvalidOperationException("托管分组结果结构不完整。");
                }

                var key = string.Join("\u001f", row, 0, groupCount);
                var value = ParseNumericResult(row[groupCount], function);
                if (!value.HasValue)
                {
                    throw new InvalidOperationException(
                        "托管分组函数 " + function + " 对组 \"" + key + "\" 没有数值结果。");
                }

                result.Groups.Add(key, value.Value);
            }

            return result;
        }

        internal static CalcResult CreateRowCountResult(int rowCount)
        {
            return new CalcResult
            {
                Scalar = rowCount,
                Coverage = new ExcelCoverageReport
                {
                    TotalRows = rowCount,
                    ScannedRows = rowCount,
                    Backend = "managed",
                    CellCountsAvailable = false
                },
                Notes = { "行数来自数据区域结构；未读取单元格值。" }
            };
        }

        internal sealed class ManagedCalcChunkSink : IColumnChunkSink
        {
            private readonly CalcQuery _query;
            private readonly HashSet<int> _excludedRows = new HashSet<int>();
            private ExcelColumnReadLayout _layout;
            private ExcelAggregationEngine.AggregateStreamAccumulator _accumulator;
            private ExcelAggregateResult _aggregate;
            private int _nextRowIndex;
            private int _excludedRowCount;

            public ManagedCalcChunkSink(CalcQuery query)
            {
                _query = query;
            }

            public CalcResult Result { get; private set; }

            public void Begin(ExcelColumnReadLayout layout)
            {
                if (layout == null)
                {
                    throw new ArgumentNullException(nameof(layout));
                }

                _layout = layout;
                _excludedRows.Clear();
                foreach (var row in _query.ExcludeRows ?? new List<int>())
                {
                    var firstDataRow = layout.SourceFirstRow + (layout.HasHeader ? 1 : 0);
                    var lastDataRow = firstDataRow + layout.TotalRows - 1;
                    if (row < firstDataRow || row > lastDataRow)
                    {
                        throw new InvalidOperationException(
                            "excludeRows 包含不在数据区域内的工作表行号：" + row + ".");
                    }

                    _excludedRows.Add(row);
                }

                _aggregate = new ExcelAggregateResult();
                _accumulator = ExcelAggregationEngine.CreateAggregateAccumulator(
                    layout.Columns,
                    layout.ColumnIsDateFormatted,
                    CreateAggregateRequest(_query),
                    _aggregate,
                    layout.TotalRows);
                _nextRowIndex = 0;
                _excludedRowCount = 0;
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                if (_layout == null || _accumulator == null)
                {
                    throw new InvalidOperationException("托管计算分块尚未开始。");
                }

                if (firstRowIndex != _nextRowIndex
                    || columns == null
                    || columns.Length != _layout.Columns.Length)
                {
                    throw new InvalidOperationException("托管计算分块行偏移或列数不连续。");
                }

                var rowCount = columns.Length == 0 ? 0 : (columns[0] == null ? -1 : columns[0].Length);
                if (rowCount < 0)
                {
                    throw new InvalidOperationException("托管计算分块包含空列。");
                }

                for (var column = 0; column < columns.Length; column++)
                {
                    if (columns[column] == null || columns[column].Length != rowCount)
                    {
                        throw new InvalidOperationException("托管计算分块列长度不一致。");
                    }
                }

                for (var row = 0; row < rowCount; row++)
                {
                    var values = new object[columns.Length];
                    for (var column = 0; column < columns.Length; column++)
                    {
                        values[column] = columns[column][row];
                    }

                    var worksheetRow = _layout.SourceFirstRow
                        + (_layout.HasHeader ? 1 : 0)
                        + firstRowIndex
                        + row;
                    var excluded = _excludedRows.Contains(worksheetRow)
                        || (_query.ExcludeLabelRows && ContainsSubtotalLabel(values));
                    if (excluded)
                    {
                        _excludedRowCount++;
                    }

                    _accumulator.AcceptRow(values, !excluded);
                }

                _nextRowIndex += rowCount;
            }

            public void End()
            {
                if (_layout == null || _accumulator == null)
                {
                    throw new InvalidOperationException("托管计算分块尚未开始。");
                }

                if (_nextRowIndex != _layout.TotalRows)
                {
                    throw new ExcelCoverageIncompleteException(
                        "托管计算覆盖不完整：已扫描 " + _nextRowIndex + " / "
                        + _layout.TotalRows + " 行，拒绝返回不完整结果。");
                }

                _accumulator.Complete();
                Result = ConvertResult(_query, _aggregate, string.IsNullOrWhiteSpace(_query.GroupByColumn) ? 0 : 1);
                if (HasExclusions(_query))
                {
                    var reasons = new List<string>();
                    if (_excludedRows.Count > 0)
                    {
                        reasons.Add("显式工作表行号");
                    }

                    if (_query.ExcludeLabelRows)
                    {
                        reasons.Add("小计/合计标签行");
                    }

                    Result.Notes.Add(
                        "聚合按" + string.Join("与", reasons)
                        + "排除了 " + _excludedRowCount
                        + " 行；覆盖报告仍统计完整扫描的数据区域。");
                }
            }

            private static bool ContainsSubtotalLabel(object[] values)
            {
                return values != null && values.Any(DataQualityInspector.IsSubtotalLabel);
            }
        }

        internal sealed class SortedCalcChunkSink : IColumnChunkSink
        {
            private readonly bool _descending;
            private ExcelColumnReadLayout _layout;
            private CoverageAccumulator _coverage;
            private object _previousValue;
            private int _nextRowIndex;
            private bool _hasPreviousValue;
            private bool _isSorted;
            private bool _comparisonIndeterminate;

            public SortedCalcChunkSink(string direction)
            {
                _descending = string.Equals(
                    direction,
                    "descending",
                    StringComparison.OrdinalIgnoreCase);
            }

            public CalcResult Result { get; private set; }

            public void Begin(ExcelColumnReadLayout layout)
            {
                if (layout == null)
                {
                    throw new ArgumentNullException(nameof(layout));
                }

                if (layout.Columns == null || layout.Columns.Length != 1)
                {
                    throw new InvalidOperationException("排序检查必须且只能读取一列。");
                }

                _layout = layout;
                _coverage = new CoverageAccumulator();
                _previousValue = null;
                _nextRowIndex = 0;
                _hasPreviousValue = false;
                _isSorted = true;
                _comparisonIndeterminate = false;
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                if (_layout == null || _coverage == null)
                {
                    throw new InvalidOperationException("排序检查分块尚未开始。");
                }

                if (firstRowIndex != _nextRowIndex
                    || columns == null
                    || columns.Length != 1
                    || columns[0] == null)
                {
                    throw new InvalidOperationException("排序检查分块行偏移或列数不连续。");
                }

                foreach (var value in columns[0])
                {
                    _coverage.Accept(new[] { value }, 1);
                    if (ExcelValueSemantics.IsEmpty(value))
                    {
                        continue;
                    }

                    if (!IsNativeNumber(value))
                    {
                        _comparisonIndeterminate = true;
                    }

                    if (_hasPreviousValue)
                    {
                        int comparison;
                        if (!TryCompare(_previousValue, value, out comparison))
                        {
                            _comparisonIndeterminate = true;
                        }
                        else if ((_descending && comparison < 0)
                            || (!_descending && comparison > 0))
                        {
                            _isSorted = false;
                        }
                    }

                    _previousValue = value;
                    _hasPreviousValue = true;
                }

                _nextRowIndex += columns[0].Length;
            }

            public void End()
            {
                if (_layout == null || _coverage == null)
                {
                    throw new InvalidOperationException("排序检查分块尚未开始。");
                }

                if (_nextRowIndex != _layout.TotalRows)
                {
                    throw new ExcelCoverageIncompleteException(
                        "排序检查覆盖不完整：已扫描 " + _nextRowIndex + " / "
                        + _layout.TotalRows + " 行，拒绝返回不完整结果。");
                }

                var coverage = _coverage.ToReport(_layout.TotalRows);
                Result = new CalcResult
                {
                    IsSorted = _comparisonIndeterminate ? (bool?)null : _isSorted,
                    Coverage = coverage
                };

                if (coverage.BlankCount > 0)
                {
                    Result.Notes.Add("排序检查忽略了所选列中的空白值。");
                }

                if (coverage.ErrorCount > 0 || _comparisonIndeterminate)
                {
                    Result.Notes.Add(
                        "存在文本、错误值或其他无法可靠比较的值；托管后端不模拟 Excel 区域排序语义，排序结论为未知。");
                }
            }

            private static bool TryCompare(object left, object right, out int comparison)
            {
                comparison = 0;
                if (!IsNativeNumber(left) || !IsNativeNumber(right))
                {
                    return false;
                }

                double leftNumber;
                double rightNumber;
                if (!ExcelValueSemantics.TryGetNumber(left, out leftNumber)
                    || !ExcelValueSemantics.TryGetNumber(right, out rightNumber)
                    || double.IsNaN(leftNumber)
                    || double.IsInfinity(leftNumber)
                    || double.IsNaN(rightNumber)
                    || double.IsInfinity(rightNumber))
                {
                    return false;
                }

                comparison = leftNumber.CompareTo(rightNumber);
                return true;
            }

            private static bool IsNativeNumber(object value)
            {
                return value is byte
                    || value is sbyte
                    || value is short
                    || value is ushort
                    || value is int
                    || value is uint
                    || value is long
                    || value is ulong
                    || value is float
                    || value is double
                    || value is decimal;
            }
        }
    }
}
