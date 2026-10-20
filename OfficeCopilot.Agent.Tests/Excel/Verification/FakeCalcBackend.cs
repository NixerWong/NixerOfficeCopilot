using System;
using System.Collections.Generic;
using System.Globalization;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Verification.Backends;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    internal sealed class FakeCalcBackend : IExcelCalcBackend
    {
        private readonly string[] _columns;
        private readonly Dictionary<string, int> _columnIndexes;
        private readonly object[,] _rows;
        private readonly int _rowStart;
        private readonly int _columnStart;
        private readonly IList<string> _notes;
        private readonly Exception _evaluationException;

        public FakeCalcBackend(
            string[] columns,
            object[,] rows,
            IEnumerable<string> notes = null,
            Exception evaluationException = null)
        {
            if (columns == null)
            {
                throw new ArgumentNullException(nameof(columns));
            }

            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            if (columns.Length != rows.GetLength(1))
            {
                throw new ArgumentException("列名数量必须与数据矩阵列数相同。", nameof(columns));
            }

            _columns = new string[columns.Length];
            _columnIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < columns.Length; index++)
            {
                var column = (columns[index] ?? string.Empty).Trim();
                if (column.Length == 0 || _columnIndexes.ContainsKey(column))
                {
                    throw new ArgumentException("列名不能为空或重复。", nameof(columns));
                }

                _columns[index] = column;
                _columnIndexes.Add(column, index);
            }

            _rows = rows;
            _rowStart = rows.GetLowerBound(0);
            _columnStart = rows.GetLowerBound(1);
            _notes = new List<string>();
            if (notes != null)
            {
                foreach (var note in notes)
                {
                    if (note == null)
                    {
                        throw new ArgumentException("注记不能包含 null。", nameof(notes));
                    }

                    _notes.Add(note);
                }
            }

            _evaluationException = evaluationException;
        }

        public string Name
        {
            get { return "fake"; }
        }

        public bool CanEvaluate(CalcQuery query, CalcCostEstimate cost)
        {
            QueryPlan plan;
            string reason;
            if (cost == null || !TryBuildPlan(query, out plan, out reason))
            {
                return false;
            }

            var isRowCount = plan.Function == "rowcount";
            return (isRowCount || _rows.GetLength(0) > 0)
                && cost.RowCount == _rows.GetLength(0)
                && (isRowCount
                    ? cost.ReadColumnCount == 0
                    : cost.ReadColumnCount >= plan.RequiredColumnCount)
                && cost.ReadColumnCount <= _columns.Length
                && cost.ProjectedCellCount == (long)cost.RowCount * cost.ReadColumnCount;
        }

        public CalcResult Evaluate(CalcQuery query)
        {
            QueryPlan plan;
            string reason;
            if (!TryBuildPlan(query, out plan, out reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (_rows.GetLength(0) == 0 && plan.Function != "rowcount")
            {
                throw new InvalidOperationException("Fake 计算没有可处理的数据行。");
            }

            if (_evaluationException != null)
            {
                throw _evaluationException;
            }

            var matchingRows = GetMatchingRows(plan);
            var result = new CalcResult
            {
                Coverage = BuildCoverageReport()
            };
            foreach (var note in _notes)
            {
                result.Notes.Add(note);
            }

            if (plan.Function == "rowcount")
            {
                result.Scalar = matchingRows.Count;
                result.Coverage.CellCountsAvailable = false;
                result.Notes.Add("行数来自数据区域结构；未读取单元格值。");
                return result;
            }

            if (plan.Function == "nonblank")
            {
                var nonBlankCount = 0;
                foreach (var row in matchingRows)
                {
                    foreach (var column in plan.MeasureIndexes)
                    {
                        if (!ExcelValueSemantics.IsEmpty(GetValue(row, column)))
                        {
                            nonBlankCount++;
                        }
                    }
                }

                result.Scalar = nonBlankCount;
                return result;
            }

            if (plan.Function == "keyset")
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in matchingRows)
                {
                    var value = GetValue(row, plan.MeasureIndexes[0]);
                    if (!ExcelValueSemantics.IsEmpty(value))
                    {
                        var key = ExcelValueSemantics.ToText(value, false);
                        if (keys.Add(key))
                        {
                            result.Keys.Add(key);
                        }
                    }
                }

                return result;
            }

            if (plan.Function == "sorted")
            {
                var previousValue = (object)null;
                var hasPreviousValue = false;
                var isDescending = string.Equals(
                    plan.SortDirection,
                    "descending",
                    StringComparison.OrdinalIgnoreCase);
                var isSorted = true;
                var isIndeterminate = false;
                foreach (var row in matchingRows)
                {
                    var value = GetValue(row, plan.MeasureIndexes[0]);
                    if (ExcelValueSemantics.IsEmpty(value))
                    {
                        continue;
                    }

                    if (!TryGetNativeNumber(value, out var currentNumber))
                    {
                        isIndeterminate = true;
                    }

                    if (hasPreviousValue)
                    {
                        double previousNumber;
                        if (!TryGetNativeNumber(previousValue, out previousNumber)
                            || !TryGetNativeNumber(value, out currentNumber))
                        {
                            isIndeterminate = true;
                        }
                        else if ((isDescending && previousNumber < currentNumber)
                            || (!isDescending && previousNumber > currentNumber))
                        {
                            isSorted = false;
                        }
                    }

                    previousValue = value;
                    hasPreviousValue = true;
                }

                result.IsSorted = isIndeterminate ? (bool?)null : isSorted;
                if (isIndeterminate)
                {
                    result.Notes.Add(
                        "存在文本、错误值或其他无法可靠比较的值；托管后端不模拟 Excel 区域排序语义，排序结论为未知。");
                }

                return result;
            }

            if (plan.GroupIndex < 0)
            {
                result.Scalar = Calculate(plan, matchingRows);
                return result;
            }

            var groupedRows = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var row in matchingRows)
            {
                var key = ExcelValueSemantics.ToText(
                    GetValue(row, plan.GroupIndex),
                    false);
                List<int> groupRows;
                if (!groupedRows.TryGetValue(key, out groupRows))
                {
                    groupRows = new List<int>();
                    groupedRows.Add(key, groupRows);
                }

                groupRows.Add(row);
            }

            foreach (var group in groupedRows)
            {
                result.Groups.Add(group.Key, Calculate(plan, group.Value));
            }

            return result;
        }

        private bool TryBuildPlan(CalcQuery query, out QueryPlan plan, out string reason)
        {
            plan = null;
            reason = "Fake 计算请求无效。";
            if (query == null || query.Source == null
                || query.Columns == null
                || query.Filters == null)
            {
                return false;
            }

            if ((query.ExcludeRows != null && query.ExcludeRows.Count > 0)
                || query.ExcludeLabelRows)
            {
                reason = "Fake 后端未实现行排除，不能用于验证带 excludeRows / excludeLabelRows 的请求。";
                return false;
            }

            var function = (query.Function ?? string.Empty).Trim().ToLowerInvariant();
            var isRowCount = function == "rowcount";
            if (!isRowCount && query.Columns.Count == 0)
            {
                reason = "Fake 计算请求至少需要一个数据列。";
                return false;
            }

            if (!isRowCount
                && function != "sum"
                && function != "count"
                && function != "counta"
                && function != "countdistinct"
                && function != "min"
                && function != "max"
                && function != "errorcount"
                && function != "keyset"
                && function != "nonblank"
                && function != "sorted")
            {
                reason = "Fake 后端不支持函数：" + (query.Function ?? string.Empty);
                return false;
            }

            if (isRowCount
                && (query.Columns.Count != 0
                    || query.Filters.Count != 0
                    || !string.IsNullOrWhiteSpace(query.GroupByColumn)))
            {
                reason = "Fake rowCount 只支持无列、无筛选、无分组的请求。";
                return false;
            }

            if (!isRowCount
                && function != "sum"
                && function != "nonblank"
                && query.Columns.Count != 1)
            {
                reason = "Fake 后端的 " + function + " 函数只支持单列。";
                return false;
            }

            if ((function == "keyset" || function == "sorted")
                && !string.IsNullOrWhiteSpace(query.GroupByColumn))
            {
                reason = "Fake " + function + " 不支持分组。";
                return false;
            }

            if (function == "nonblank"
                && !string.IsNullOrWhiteSpace(query.GroupByColumn))
            {
                reason = "Fake nonBlank 不支持分组。";
                return false;
            }

            if (function == "sorted"
                && (query.Filters.Count != 0
                    || (!string.Equals(query.SortDirection, "ascending", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(query.SortDirection, "descending", StringComparison.OrdinalIgnoreCase))))
            {
                reason = "Fake sorted 仅支持无筛选的 ascending / descending 检查。";
                return false;
            }

            var requiredColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var measureIndexes = new List<int>();
            foreach (var column in query.Columns)
            {
                int columnIndex;
                var name = (column ?? string.Empty).Trim();
                if (name.Length == 0 || !requiredColumnNames.Add(name)
                    || !_columnIndexes.TryGetValue(name, out columnIndex))
                {
                    reason = "Fake 计算请求包含空、重复或不存在的数据列。";
                    return false;
                }

                measureIndexes.Add(columnIndex);
            }

            var filters = new List<CompiledFilter>();
            foreach (var filter in query.Filters)
            {
                int columnIndex;
                if (filter == null || string.IsNullOrWhiteSpace(filter.Column)
                    || filter.Value == null
                    || !_columnIndexes.TryGetValue(filter.Column.Trim(), out columnIndex))
                {
                    reason = "Fake 计算请求包含无效筛选列或筛选值。";
                    return false;
                }

                var filterOperator = (filter.Operator ?? string.Empty).Trim().ToLowerInvariant();
                if (filterOperator != "eq"
                    && filterOperator != "ne"
                    && filterOperator != "gt"
                    && filterOperator != "ge"
                    && filterOperator != "lt"
                    && filterOperator != "le"
                    && filterOperator != "blank"
                    && filterOperator != "notblank"
                    && filterOperator != "contains")
                {
                    reason = "Fake 后端不支持筛选运算符：" + (filter.Operator ?? string.Empty);
                    return false;
                }

                requiredColumnNames.Add(filter.Column.Trim());
                filters.Add(new CompiledFilter
                {
                    ColumnIndex = columnIndex,
                    Operator = filterOperator,
                    Value = filter.Value,
                    HasNumericValue = double.TryParse(
                        filter.Value,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out var numericValue),
                    NumericValue = numericValue
                });
            }

            var groupIndex = -1;
            if (!string.IsNullOrWhiteSpace(query.GroupByColumn))
            {
                if (function == "sum" && measureIndexes.Count != 1)
                {
                    reason = "Fake 后端分组查询只支持一个数值列。";
                    return false;
                }

                if (!_columnIndexes.TryGetValue(query.GroupByColumn.Trim(), out groupIndex))
                {
                    reason = "Fake 计算请求引用了不存在的分组列。";
                    return false;
                }

                requiredColumnNames.Add(query.GroupByColumn.Trim());
            }

            plan = new QueryPlan
            {
                Function = function,
                MeasureIndexes = measureIndexes,
                Filters = filters,
                GroupIndex = groupIndex,
                RequiredColumnCount = requiredColumnNames.Count,
                SortDirection = query.SortDirection
            };
            reason = string.Empty;
            return true;
        }

        private List<int> GetMatchingRows(QueryPlan plan)
        {
            var rows = new List<int>();
            var rowCount = _rows.GetLength(0);
            for (var offset = 0; offset < rowCount; offset++)
            {
                var row = _rowStart + offset;
                if (PassesFilters(row, plan.Filters))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        private bool PassesFilters(int row, IList<CompiledFilter> filters)
        {
            foreach (var filter in filters)
            {
                var value = GetValue(row, filter.ColumnIndex);
                var isEmpty = ExcelValueSemantics.IsEmpty(value);
                if (filter.Operator == "blank")
                {
                    if (!isEmpty)
                    {
                        return false;
                    }

                    continue;
                }

                if (filter.Operator == "notblank")
                {
                    if (isEmpty)
                    {
                        return false;
                    }

                    continue;
                }

                var text = ExcelValueSemantics.ToText(value, false);
                if (filter.Operator == "eq"
                    && !string.Equals(text, filter.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (filter.Operator == "ne"
                    && string.Equals(text, filter.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (filter.Operator == "contains"
                    && text.IndexOf(filter.Value, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }

                if (filter.Operator == "gt" || filter.Operator == "ge"
                    || filter.Operator == "lt" || filter.Operator == "le")
                {
                    if (!CompareOrdered(value, text, filter))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool CompareOrdered(object value, string text, CompiledFilter filter)
        {
            int comparison;
            double number;
            if (filter.HasNumericValue && ExcelValueSemantics.TryGetNumber(value, out number))
            {
                comparison = number.CompareTo(filter.NumericValue);
            }
            else
            {
                comparison = string.Compare(text, filter.Value, StringComparison.OrdinalIgnoreCase);
            }

            switch (filter.Operator)
            {
                case "gt": return comparison > 0;
                case "ge": return comparison >= 0;
                case "lt": return comparison < 0;
                default: return comparison <= 0;
            }
        }

        private double Calculate(QueryPlan plan, IList<int> rows)
        {
            if (plan.Function == "sum")
            {
                var total = 0d;
                foreach (var row in rows)
                {
                    foreach (var column in plan.MeasureIndexes)
                    {
                        double number;
                        var value = GetValue(row, column);
                        if (!ExcelValueSemantics.IsError(value)
                            && ExcelValueSemantics.TryGetNumber(value, out number))
                        {
                            total += number;
                        }
                    }
                }

                return total;
            }

            var measureIndex = plan.MeasureIndexes[0];
            var nonEmptyCount = 0;
            var errorCount = 0;
            var distinctValues = plan.Function == "countdistinct"
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;
            double? minimum = null;
            double? maximum = null;
            foreach (var row in rows)
            {
                var value = GetValue(row, measureIndex);
                if (ExcelValueSemantics.IsEmpty(value))
                {
                    continue;
                }

                nonEmptyCount++;
                if (ExcelValueSemantics.IsError(value))
                {
                    errorCount++;
                }

                if (distinctValues != null)
                {
                    distinctValues.Add(ExcelValueSemantics.ToText(value, false));
                }

                double number;
                if (!ExcelValueSemantics.IsError(value)
                    && ExcelValueSemantics.TryGetNumber(value, out number))
                {
                    if (!minimum.HasValue || number < minimum.Value)
                    {
                        minimum = number;
                    }

                    if (!maximum.HasValue || number > maximum.Value)
                    {
                        maximum = number;
                    }
                }
            }

            switch (plan.Function)
            {
                case "count":
                case "counta":
                    return nonEmptyCount;
                case "countdistinct":
                    return distinctValues.Count;
                case "errorcount":
                    return errorCount;
                case "min":
                    return minimum ?? 0d;
                case "max":
                    return maximum ?? 0d;
                default:
                    throw new InvalidOperationException("Fake 后端无法执行函数：" + plan.Function);
            }
        }

        private ExcelCoverageReport BuildCoverageReport()
        {
            var rowCount = _rows.GetLength(0);
            var columnCount = _rows.GetLength(1);
            var report = new ExcelCoverageReport
            {
                TotalRows = rowCount,
                ScannedRows = rowCount,
                Backend = Name,
                CellCountsAvailable = true
            };

            for (var rowOffset = 0; rowOffset < rowCount; rowOffset++)
            {
                for (var columnOffset = 0; columnOffset < columnCount; columnOffset++)
                {
                    var value = _rows[_rowStart + rowOffset, _columnStart + columnOffset];
                    if (ExcelValueSemantics.IsEmpty(value))
                    {
                        report.BlankCount++;
                        continue;
                    }

                    var isError = ExcelValueSemantics.IsError(value);
                    double number;
                    var isNumeric = !isError && ExcelValueSemantics.TryGetNumber(value, out number);
                    var isTextNumber = value is string && isNumeric;
                    if (!isNumeric || isTextNumber)
                    {
                        report.NonNumericCount++;
                    }

                    if (isTextNumber)
                    {
                        report.TextNumberCount++;
                    }

                    if (isError)
                    {
                        report.ErrorCount++;
                    }
                }
            }

            return report;
        }

        private object GetValue(int row, int column)
        {
            return _rows[row, _columnStart + column];
        }

        private static bool TryGetNativeNumber(object value, out double number)
        {
            number = 0d;
            if (!(value is byte)
                && !(value is sbyte)
                && !(value is short)
                && !(value is ushort)
                && !(value is int)
                && !(value is uint)
                && !(value is long)
                && !(value is ulong)
                && !(value is float)
                && !(value is double)
                && !(value is decimal))
            {
                return false;
            }

            return ExcelValueSemantics.TryGetNumber(value, out number)
                && !double.IsNaN(number)
                && !double.IsInfinity(number);
        }

        private sealed class QueryPlan
        {
            public string Function { get; set; }

            public IList<int> MeasureIndexes { get; set; }

            public IList<CompiledFilter> Filters { get; set; }

            public int GroupIndex { get; set; }

            public int RequiredColumnCount { get; set; }

            public string SortDirection { get; set; }
        }

        private sealed class CompiledFilter
        {
            public int ColumnIndex { get; set; }

            public string Operator { get; set; }

            public string Value { get; set; }

            public bool HasNumericValue { get; set; }

            public double NumericValue { get; set; }
        }
    }
}
