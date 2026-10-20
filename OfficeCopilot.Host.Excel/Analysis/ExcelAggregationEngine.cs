using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis.Accumulators;
using OfficeCopilot.HostBridge.Contracts;
using OfficeCopilot.Host.Excel.Verification;

namespace OfficeCopilot.Host.Excel.Analysis
{
    /// <summary>
    /// 纯内存的表结构画像、分组聚合与对账实现。不引用 Interop，可独立单测。
    /// COM 侧只负责取数（一次性 Value2）与结果封装。
    /// </summary>
    public static class ExcelAggregationEngine
    {
        private const string GroupKeySeparator = "\u0001";

        // ---------- 表头推断 ----------

        /// <summary>
        /// 判断首行是否为表头。<paramref name="headerMode"/> 取 auto / yes / no。
        /// </summary>
        public static bool ShouldTreatFirstRowAsHeader(
            IList<object[]> rawRows,
            bool[] columnIsDateFormatted,
            string headerMode,
            out bool inferred,
            out string reason)
        {
            var rowCount = rawRows == null ? 0 : rawRows.Count;
            var firstRow = rowCount > 0 ? rawRows[0] : new object[0];
            var hasHeader = ShouldTreatHeaderRow(
                firstRow,
                rowCount,
                headerMode,
                out inferred,
                out reason);
            if (!hasHeader || !inferred || rowCount <= 1)
            {
                return hasHeader;
            }

            for (var rowIndex = 1; rowIndex < rawRows.Count && rowIndex <= 20; rowIndex++)
            {
                var row = rawRows[rowIndex] ?? new object[0];
                for (var col = 0; col < row.Length; col++)
                {
                    if (!ExcelValueSemantics.IsEmpty(row[col]) && !(row[col] is string))
                    {
                        reason = "首行全为文本、后续行存在非文本值，判定首行为表头。";
                        return true;
                    }
                }
            }

            reason = "首行全为文本且后续行同为文本，仍按表头处理（如判断有误请显式传 headerMode）。";
            return true;
        }

        public static bool ShouldTreatHeaderRow(
            object[] firstRow,
            int sourceRowCount,
            string headerMode,
            out bool inferred,
            out string reason)
        {
            inferred = false;
            var mode = (headerMode ?? "auto").Trim().ToLowerInvariant();

            if (mode == "yes" || mode == "true")
            {
                reason = "调用方显式声明首行为表头。";
                return true;
            }

            if (mode == "no" || mode == "false")
            {
                reason = "调用方显式声明无表头。";
                return false;
            }

            inferred = true;
            if (sourceRowCount <= 0)
            {
                reason = "无数据行，按无表头处理。";
                return false;
            }

            firstRow = firstRow ?? new object[0];
            for (var index = 0; index < firstRow.Length; index++)
            {
                if (ExcelValueSemantics.IsEmpty(firstRow[index]))
                {
                    reason = "首行存在空单元格，判定为数据行而非表头。";
                    return false;
                }

                if (!(firstRow[index] is string))
                {
                    reason = "首行存在非文本单元格，判定为数据行而非表头。";
                    return false;
                }
            }

            reason = sourceRowCount == 1
                ? "仅一行且全为文本，按表头处理。"
                : "首行全为文本，按自动规则视为表头。";
            return true;
        }

        public static ExcelTabularSnapshot BuildSnapshot(
            IList<object[]> rawRows,
            bool[] columnIsDateFormatted,
            int columnCount,
            bool firstRowIsHeader)
        {
            var headers = new string[columnCount];
            var dataRows = new List<object[]>();

            if (firstRowIsHeader && rawRows != null && rawRows.Count > 0)
            {
                var headerRow = rawRows[0] ?? new object[0];
                for (var i = 0; i < columnCount; i++)
                {
                    var raw = i < headerRow.Length ? headerRow[i] : null;
                    var text = ExcelValueSemantics.ToText(raw, false).Trim();
                    headers[i] = string.IsNullOrEmpty(text) ? ExcelTabularSnapshot.BuildDefaultHeader(i) : text;
                }

                for (var r = 1; r < rawRows.Count; r++)
                {
                    dataRows.Add(rawRows[r]);
                }
            }
            else
            {
                for (var i = 0; i < columnCount; i++)
                {
                    headers[i] = ExcelTabularSnapshot.BuildDefaultHeader(i);
                }

                if (rawRows != null)
                {
                    dataRows.AddRange(rawRows);
                }
            }

            EnsureUniqueHeaders(headers);
            return new ExcelTabularSnapshot(headers, dataRows, columnIsDateFormatted);
        }

        public static ExcelCoverageReport BuildCoverageReport(ExcelTabularSnapshot snapshot, int totalRows)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var coverage = new CoverageAccumulator();
            for (var row = 0; row < snapshot.RowCount; row++)
            {
                coverage.Accept(snapshot.Rows[row], snapshot.ColumnCount);
            }

            return coverage.ToReport(totalRows);
        }

        private static void EnsureUniqueHeaders(string[] headers)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
            {
                var name = headers[i];
                int count;
                if (seen.TryGetValue(name, out count))
                {
                    seen[name] = count + 1;
                    headers[i] = name + "_" + (count + 1).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    seen[name] = 1;
                }
            }
        }

        // ---------- 画像 ----------

        public static List<ExcelColumnProfile> BuildColumnProfiles(ExcelTabularSnapshot snapshot, int topK)
        {
            var profiles = new List<ExcelColumnProfile>();
            if (snapshot == null)
            {
                return profiles;
            }

            var accumulators = new ColumnProfileAccumulator[snapshot.ColumnCount];
            for (var column = 0; column < snapshot.ColumnCount; column++)
            {
                var isDate = column < snapshot.ColumnIsDateFormatted.Length
                    && snapshot.ColumnIsDateFormatted[column];
                accumulators[column] = new ColumnProfileAccumulator(isDate);
            }

            for (var row = 0; row < snapshot.RowCount; row++)
            {
                for (var column = 0; column < snapshot.ColumnCount; column++)
                {
                    accumulators[column].Accept(snapshot.GetValue(row, column));
                }
            }

            for (var column = 0; column < snapshot.ColumnCount; column++)
            {
                profiles.Add(accumulators[column].ToProfile(
                    column + 1,
                    snapshot.Headers[column],
                    topK));
            }

            return profiles;
        }

        internal static string ResolveInferredType(HashSet<string> kinds)
        {
            kinds.Remove(ExcelColumnValueKinds.Error);

            if (kinds.Count == 0)
            {
                return ExcelColumnValueKinds.Empty;
            }

            return kinds.Count == 1 ? kinds.First() : ExcelColumnValueKinds.Mixed;
        }

        public static List<string[]> BuildSampleRows(ExcelTabularSnapshot snapshot, int sampleRowCount)
        {
            var rows = new List<string[]>();
            if (snapshot == null)
            {
                return rows;
            }

            var count = Math.Min(sampleRowCount > 0 ? sampleRowCount : 5, snapshot.RowCount);
            for (var row = 0; row < count; row++)
            {
                var values = new string[snapshot.ColumnCount];
                for (var col = 0; col < snapshot.ColumnCount; col++)
                {
                    var isDate = col < snapshot.ColumnIsDateFormatted.Length && snapshot.ColumnIsDateFormatted[col];
                    values[col] = ExcelValueSemantics.ToText(snapshot.GetValue(row, col), isDate);
                }

                rows.Add(values);
            }

            return rows;
        }

        // ---------- 分组聚合 ----------

        public static void Aggregate(
            ExcelTabularSnapshot snapshot,
            ExcelAggregateRequest request,
            ExcelAggregateResult result)
        {
            if (snapshot == null || request == null || result == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            Aggregate(snapshot, request, result, snapshot.RowCount);
        }

        public static void Aggregate(
            ExcelTabularSnapshot snapshot,
            ExcelAggregateRequest request,
            ExcelAggregateResult result,
            int totalRows)
        {
            if (snapshot == null || request == null || result == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            var accumulator = new AggregateStreamAccumulator(
                snapshot.Headers,
                snapshot.ColumnIsDateFormatted,
                request,
                result,
                totalRows);
            for (var row = 0; row < snapshot.RowCount; row++)
            {
                accumulator.AcceptRow(snapshot.Rows[row]);
            }

            accumulator.Complete();
        }

        internal sealed class AggregateStreamAccumulator
        {
            private readonly ExcelTabularSnapshot _rowView;
            private readonly object[] _currentRow;
            private readonly ExcelAggregateRequest _request;
            private readonly ExcelAggregateResult _result;
            private readonly int _totalRows;
            private readonly List<string> _groupBy;
            private readonly List<DerivedGroupColumn> _derivedGroups;
            private readonly List<string> _groupColumns;
            private readonly List<ExcelAggregateMeasure> _measures;
            private readonly List<int> _groupIndices;
            private readonly List<int> _measureIndices;
            private readonly List<CompiledFilter> _filters;
            private readonly Dictionary<string, GroupAccumulator> _accumulators;
            private readonly List<string> _order;
            private readonly CoverageAccumulator _coverage;
            private bool _completed;

            public AggregateStreamAccumulator(
                string[] headers,
                bool[] columnIsDateFormatted,
                ExcelAggregateRequest request,
                ExcelAggregateResult result,
                int totalRows)
            {
                if (headers == null)
                {
                    headers = new string[0];
                }

                _request = request ?? throw new ArgumentNullException(nameof(request));
                _result = result ?? throw new ArgumentNullException(nameof(result));
                _totalRows = totalRows;
                _currentRow = new object[headers.Length];
                _rowView = new ExcelTabularSnapshot(
                    headers,
                    new List<object[]> { _currentRow },
                    columnIsDateFormatted);

                _groupBy = (request.GroupBy ?? new List<string>())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .ToList();
                _derivedGroups = NormalizeDerivedGroups(_rowView, request.DerivedGroupBy);
                _groupColumns = _groupBy.Concat(_derivedGroups.Select(group => group.Alias)).ToList();

                _measures = NormalizeMeasures(request.Measures);
                if (_measures.Count == 0)
                {
                    throw new InvalidOperationException("measures 不能为空，至少需要一个聚合度量。");
                }

                _groupIndices = _groupBy
                    .Select(name => ResolveColumn(_rowView, name, "groupBy"))
                    .ToList();
                _measureIndices = _measures
                    .Select(measure => string.Equals(measure.Function, "count", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(measure.Column)
                        ? -1
                        : ResolveColumn(_rowView, measure.Column, "measures"))
                    .ToList();
                _filters = BuildFilters(_rowView, request.Filters);
                _accumulators = new Dictionary<string, GroupAccumulator>(StringComparer.Ordinal);
                _order = new List<string>();
                _coverage = new CoverageAccumulator();

                _result.Columns.AddRange(_groupColumns);
                _result.Columns.AddRange(_measures.Select(measure => measure.Alias));
            }

            public void AcceptRow(object[] values)
            {
                AcceptRow(values, true);
            }

            public void AcceptRow(object[] values, bool includeInAggregation)
            {
                if (_completed)
                {
                    throw new InvalidOperationException("不能向已完成的聚合累加器继续写入数据。");
                }

                Array.Clear(_currentRow, 0, _currentRow.Length);
                if (values != null)
                {
                    Array.Copy(values, _currentRow, Math.Min(values.Length, _currentRow.Length));
                }

                _coverage.Accept(_currentRow, _currentRow.Length);
                if (!includeInAggregation)
                {
                    return;
                }

                if (IsRowEmpty(_rowView, 0))
                {
                    _result.SkippedRowCount++;
                    return;
                }

                if (!PassesFilters(_rowView, 0, _filters))
                {
                    return;
                }

                _result.FilteredRowCount++;
                var keyParts = new string[_groupIndices.Count + _derivedGroups.Count];
                for (var group = 0; group < _groupIndices.Count; group++)
                {
                    var column = _groupIndices[group];
                    keyParts[group] = ExcelValueSemantics.ToText(
                        _rowView.GetValue(0, column),
                        IsDateColumn(_rowView, column));
                }

                for (var group = 0; group < _derivedGroups.Count; group++)
                {
                    var derivedGroup = _derivedGroups[group];
                    keyParts[_groupIndices.Count + group] = ApplyDerivedGroupTransform(
                        derivedGroup,
                        _rowView.GetValue(0, derivedGroup.ColumnIndex),
                        IsDateColumn(_rowView, derivedGroup.ColumnIndex));
                }

                var key = string.Join(GroupKeySeparator, keyParts);
                GroupAccumulator accumulator;
                if (!_accumulators.TryGetValue(key, out accumulator))
                {
                    accumulator = new GroupAccumulator(keyParts, _measures.Count);
                    _accumulators.Add(key, accumulator);
                    _order.Add(key);
                }

                for (var measure = 0; measure < _measures.Count; measure++)
                {
                    var column = _measureIndices[measure];
                    var value = column < 0 ? null : _rowView.GetValue(0, column);
                    var text = column < 0
                        ? string.Empty
                        : ExcelValueSemantics.ToText(value, IsDateColumn(_rowView, column));
                    accumulator.Slots[measure].Accept(value, text, column < 0);
                }
            }

            public void Complete()
            {
                if (_completed)
                {
                    throw new InvalidOperationException("聚合累加器已完成。");
                }

                _completed = true;
                _result.Coverage = _coverage.ToReport(_totalRows);
                _result.TotalRowCount = _coverage.ScannedRows;
                _result.GroupCount = _accumulators.Count;

                var groupRows = _order.Select(key => _accumulators[key]).ToList();
                ApplySort(groupRows, _groupColumns, _measures, _request.OrderBy);

                var maxGroups = _request.MaxGroups > 0 ? _request.MaxGroups : 200;
                if (groupRows.Count > maxGroups)
                {
                    _result.IsTruncated = true;
                    groupRows = groupRows.Take(maxGroups).ToList();
                }

                foreach (var group in groupRows)
                {
                    var cells = new string[_groupColumns.Count + _measures.Count];
                    for (var index = 0; index < _groupColumns.Count; index++)
                    {
                        cells[index] = group.KeyParts[index];
                    }

                    for (var index = 0; index < _measures.Count; index++)
                    {
                        var column = _measureIndices[index];
                        cells[_groupColumns.Count + index] = group.Slots[index].Render(
                            _measures[index].Function,
                            column >= 0 && IsDateColumn(_rowView, column));
                    }

                    _result.Rows.Add(cells);
                }

                for (var index = 0; index < _measures.Count; index++)
                {
                    var nonNumeric = groupRows.Sum(group => group.Slots[index].NonNumericCount);
                    if (nonNumeric > 0 && RequiresNumeric(_measures[index].Function))
                    {
                        _result.Warnings.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "度量 \"{0}\" 有 {1} 个非数值单元格被忽略。",
                            _measures[index].Alias,
                            nonNumeric));
                    }
                }

                if (_result.SkippedRowCount > 0)
                {
                    _result.Warnings.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "跳过 {0} 个整行为空的数据行。",
                        _result.SkippedRowCount));
                }
            }
        }

        internal static AggregateStreamAccumulator CreateAggregateAccumulator(
            string[] headers,
            bool[] columnIsDateFormatted,
            ExcelAggregateRequest request,
            ExcelAggregateResult result,
            int totalRows)
        {
            return new AggregateStreamAccumulator(
                headers,
                columnIsDateFormatted,
                request,
                result,
                totalRows);
        }

        private static List<DerivedGroupColumn> NormalizeDerivedGroups(
            ExcelTabularSnapshot snapshot,
            IList<ExcelAggregateDerivedGroup> requestedGroups)
        {
            var result = new List<DerivedGroupColumn>();
            foreach (var requested in requestedGroups ?? new List<ExcelAggregateDerivedGroup>())
            {
                if (requested == null || string.IsNullOrWhiteSpace(requested.Column))
                {
                    throw new InvalidOperationException("derivedGroupBy 的每项都必须指定 column。");
                }

                var transform = (requested.Transform ?? string.Empty).Trim();
                if (!DerivedGroupTransforms.IsSupported(transform))
                {
                    throw new InvalidOperationException(
                        "derivedGroupBy.transform 不支持 \"" + transform + "\"。可用值："
                        + string.Join("、", DerivedGroupTransforms.All) + "。");
                }

                var columnIndex = ResolveColumn(snapshot, requested.Column, "derivedGroupBy");

                if (DerivedGroupTransforms.IsDateTransform(transform) && !IsDateColumn(snapshot, columnIndex))
                {
                    throw new InvalidOperationException(
                        "derivedGroupBy.transform \"" + transform + "\" 要求列 \"" + requested.Column
                        + "\" 是日期格式列，但它不是。请改用日期列，或先确认该列的数字格式。");
                }

                var size = requested.Size;
                if (DerivedGroupTransforms.IsNumberBucket(transform) && size <= 0)
                {
                    throw new InvalidOperationException("derivedGroupBy.transform 为 numberBucket 时必须提供正数 size 作为桶宽。");
                }

                var alias = string.IsNullOrWhiteSpace(requested.Alias)
                    ? snapshot.Headers[columnIndex] + DerivedGroupTransforms.DefaultAliasSuffix(transform)
                    : requested.Alias.Trim();

                if (result.Any(group => string.Equals(group.Alias, alias, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("derivedGroupBy 中存在重复的 alias：" + alias);
                }

                result.Add(new DerivedGroupColumn(columnIndex, transform, alias, size));
            }

            return result;
        }

        private static string ApplyDerivedGroupTransform(DerivedGroupColumn group, object value, bool isDateFormatted)
        {
            if (ExcelValueSemantics.IsEmpty(value)) return string.Empty;
            if (ExcelValueSemantics.IsError(value)) return ExcelValueSemantics.DescribeError(value);

            var transform = group.Transform ?? string.Empty;

            if (DerivedGroupTransforms.IsDateTransform(transform))
            {
                double serial;
                if (!ExcelValueSemantics.TryGetNumber(value, out serial)) return string.Empty;
                // Excel 的 1900 闰年 bug 让序列号 60 无对应真实日期，DateTime.FromOADate 会直接抛。
                if (serial < 1d || serial > 2958465d) return string.Empty;

                var date = DateTime.FromOADate(serial);
                switch (transform.ToLowerInvariant())
                {
                    case "year": return date.Year.ToString(CultureInfo.InvariantCulture);
                    case "quarter": return date.Year.ToString(CultureInfo.InvariantCulture) + "-Q" + ((date.Month - 1) / 3 + 1).ToString(CultureInfo.InvariantCulture);
                    case "month": return date.Month.ToString("00", CultureInfo.InvariantCulture) + "月";
                    case "monthname": return date.ToString("MMMM", CultureInfo.CurrentCulture);
                    case "yearmonth": return date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    case "day": return date.Day.ToString("00", CultureInfo.InvariantCulture);
                    case "dayofweek": return ((int)date.DayOfWeek == 0 ? 7 : (int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture) + "-" + date.ToString("dddd", CultureInfo.CurrentCulture);
                }
            }

            if (DerivedGroupTransforms.IsNumberBucket(transform))
            {
                double number;
                if (!ExcelValueSemantics.TryGetNumber(value, out number)) return string.Empty;
                var width = group.Size;
                var lower = Math.Floor(number / width) * width;
                return lower.ToString("0.####", CultureInfo.InvariantCulture)
                    + "~" + (lower + width).ToString("0.####", CultureInfo.InvariantCulture);
            }

            var text = ExcelValueSemantics.ToText(value, isDateFormatted);
            if (text.Length == 0) return string.Empty;

            var length = string.Equals(transform, "prefix", StringComparison.OrdinalIgnoreCase)
                ? (group.Size > 0 ? (int)group.Size : 1)
                : 1;
            return text.Length <= length ? text : text.Substring(0, length);
        }

        /// <summary>派生分组支持的 transform 集合。新增值需同步 <c>ExcelAggregateTool</c> 的 schema 枚举。</summary>
        internal static class DerivedGroupTransforms
        {
            public static readonly string[] All =
            {
                "firstCharacter", "prefix",
                "year", "quarter", "month", "monthName", "yearMonth", "day", "dayOfWeek",
                "numberBucket"
            };

            private static readonly string[] DateTransforms =
            {
                "year", "quarter", "month", "monthName", "yearMonth", "day", "dayOfWeek"
            };

            public static bool IsSupported(string transform)
                => All.Any(t => string.Equals(t, transform, StringComparison.OrdinalIgnoreCase));

            public static bool IsDateTransform(string transform)
                => DateTransforms.Any(t => string.Equals(t, transform, StringComparison.OrdinalIgnoreCase));

            public static bool IsNumberBucket(string transform)
                => string.Equals(transform, "numberBucket", StringComparison.OrdinalIgnoreCase);

            public static string DefaultAliasSuffix(string transform)
            {
                switch ((transform ?? string.Empty).ToLowerInvariant())
                {
                    case "year": return "年份";
                    case "quarter": return "季度";
                    case "month": return "月份";
                    case "monthname": return "月份名";
                    case "yearmonth": return "年月";
                    case "day": return "日";
                    case "dayofweek": return "星期";
                    case "numberbucket": return "分桶";
                    case "prefix": return "前缀";
                    default: return "首字符";
                }
            }
        }

        private static bool RequiresNumeric(string function)
        {
            switch ((function ?? string.Empty).ToLowerInvariant())
            {
                case "sum":
                case "average":
                case "min":
                case "max":
                    return true;
                default:
                    return false;
            }
        }

        private sealed class DerivedGroupColumn
        {
            public DerivedGroupColumn(int columnIndex, string transform, string alias, double size)
            {
                ColumnIndex = columnIndex;
                Transform = transform ?? string.Empty;
                Alias = alias ?? string.Empty;
                Size = size;
            }

            public int ColumnIndex { get; private set; }

            public string Transform { get; private set; }

            public string Alias { get; private set; }

            public double Size { get; private set; }
        }

        private static List<ExcelAggregateMeasure> NormalizeMeasures(IList<ExcelAggregateMeasure> measures)
        {
            var normalized = new List<ExcelAggregateMeasure>();
            if (measures == null)
            {
                return normalized;
            }

            var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var measure in measures)
            {
                if (measure == null)
                {
                    continue;
                }

                var function = string.IsNullOrWhiteSpace(measure.Function) ? "sum" : measure.Function.Trim().ToLowerInvariant();
                if (!IsSupportedFunction(function))
                {
                    throw new InvalidOperationException(
                        "不支持的聚合函数：" + measure.Function
                        + "。支持 sum / count / countDistinct / errorCount / average / min / max / first / last。");
                }

                var column = (measure.Column ?? string.Empty).Trim();
                var alias = (measure.Alias ?? string.Empty).Trim();
                if (alias.Length == 0)
                {
                    alias = column.Length == 0 ? function : column + "_" + function;
                }

                var candidate = alias;
                var suffix = 2;
                while (!usedAliases.Add(candidate))
                {
                    candidate = alias + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                    suffix++;
                }

                normalized.Add(new ExcelAggregateMeasure { Column = column, Function = function, Alias = candidate });
            }

            return normalized;
        }

        private static bool IsSupportedFunction(string function)
        {
            switch (function)
            {
                case "sum":
                case "count":
                case "countdistinct":
                case "errorcount":
                case "average":
                case "avg":
                case "min":
                case "max":
                case "first":
                case "last":
                    return true;
                default:
                    return false;
            }
        }

        private static int ResolveColumn(ExcelTabularSnapshot snapshot, string name, string parameterName)
        {
            var index = snapshot.IndexOfColumn(name);
            if (index < 0)
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} 中的列 \"{1}\" 不存在。可用列：{2}",
                    parameterName,
                    name,
                    string.Join("、", snapshot.Headers)));
            }

            return index;
        }

        private static bool IsDateColumn(ExcelTabularSnapshot snapshot, int columnIndex)
        {
            return columnIndex >= 0
                && columnIndex < snapshot.ColumnIsDateFormatted.Length
                && snapshot.ColumnIsDateFormatted[columnIndex];
        }

        private static bool IsRowEmpty(ExcelTabularSnapshot snapshot, int row)
        {
            for (var col = 0; col < snapshot.ColumnCount; col++)
            {
                if (!ExcelValueSemantics.IsEmpty(snapshot.GetValue(row, col)))
                {
                    return false;
                }
            }

            return true;
        }

        // ---------- 过滤 ----------

        internal sealed class CompiledFilter
        {
            public int ColumnIndex;
            public bool IsDateColumn;
            public string Operator;
            public string Value;
            public string[] InValues;
            public double NumericValue;
            public bool HasNumericValue;
        }

        internal static List<CompiledFilter> BuildFilters(
            ExcelTabularSnapshot snapshot,
            IList<ExcelAggregateFilter> filters)
        {
            var compiled = new List<CompiledFilter>();
            if (filters == null)
            {
                return compiled;
            }

            foreach (var filter in filters)
            {
                if (filter == null || string.IsNullOrWhiteSpace(filter.Column))
                {
                    continue;
                }

                var op = (string.IsNullOrWhiteSpace(filter.Operator) ? "eq" : filter.Operator.Trim()).ToLowerInvariant();
                var index = ResolveColumn(snapshot, filter.Column, "filters");
                var value = filter.Value ?? string.Empty;
                if ((op == "isnativenumber" || op == "istextnumber")
                    && value.Length > 0)
                {
                    throw new InvalidOperationException(
                        "isNativeNumber 和 isTextNumber 过滤条件不接受 value。");
                }

                double numeric;
                var hasNumeric = double.TryParse(value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric);

                compiled.Add(new CompiledFilter
                {
                    ColumnIndex = index,
                    IsDateColumn = IsDateColumn(snapshot, index),
                    Operator = op,
                    Value = value,
                    InValues = op == "in"
                        ? value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray()
                        : null,
                    NumericValue = numeric,
                    HasNumericValue = hasNumeric
                });
            }

            return compiled;
        }

        private static bool PassesFilters(
            ExcelTabularSnapshot snapshot,
            int row,
            IList<CompiledFilter> filters)
        {
            for (var i = 0; i < filters.Count; i++)
            {
                if (!PassesFilter(snapshot, row, filters[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool PassesFilters(
            object[][] columns,
            int row,
            IList<CompiledFilter> filters)
        {
            for (var index = 0; index < (filters == null ? 0 : filters.Count); index++)
            {
                var filter = filters[index];
                if (filter.ColumnIndex < 0
                    || filter.ColumnIndex >= (columns == null ? 0 : columns.Length)
                    || columns[filter.ColumnIndex] == null
                    || row < 0
                    || row >= columns[filter.ColumnIndex].Length
                    || !PassesFilterValue(columns[filter.ColumnIndex][row], filter))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PassesFilter(ExcelTabularSnapshot snapshot, int row, CompiledFilter filter)
        {
            return PassesFilterValue(snapshot.GetValue(row, filter.ColumnIndex), filter);
        }

        private static bool PassesFilterValue(object raw, CompiledFilter filter)
        {
            var isEmpty = ExcelValueSemantics.IsEmpty(raw);

            if (filter.Operator == "isempty")
            {
                return isEmpty;
            }

            if (filter.Operator == "notempty")
            {
                return !isEmpty;
            }

            if (filter.Operator == "isnativenumber")
            {
                return ExcelValueSemantics.IsNativeNumber(raw);
            }

            if (filter.Operator == "istextnumber")
            {
                return ExcelValueSemantics.IsTextNumber(raw);
            }

            var text = ExcelValueSemantics.ToText(raw, filter.IsDateColumn);

            switch (filter.Operator)
            {
                case "eq":
                    return string.Equals(text, filter.Value, StringComparison.OrdinalIgnoreCase);
                case "ne":
                    return !string.Equals(text, filter.Value, StringComparison.OrdinalIgnoreCase);
                case "contains":
                    return text.IndexOf(filter.Value, StringComparison.OrdinalIgnoreCase) >= 0;
                case "startswith":
                    return text.StartsWith(filter.Value, StringComparison.OrdinalIgnoreCase);
                case "in":
                    return filter.InValues != null
                        && filter.InValues.Any(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase));
                case "gt":
                case "ge":
                case "lt":
                case "le":
                    return CompareOrdered(text, raw, filter);
                default:
                    throw new InvalidOperationException("不支持的过滤运算符：" + filter.Operator);
            }
        }

        private static bool CompareOrdered(string text, object raw, CompiledFilter filter)
        {
            int comparison;

            double number;
            if (filter.HasNumericValue && ExcelValueSemantics.TryGetNumber(raw, out number))
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

        // ---------- 分组累加 ----------

        private static void ApplySort(
            List<GroupAccumulator> groups,
            List<string> groupBy,
            List<ExcelAggregateMeasure> measures,
            IList<ExcelAggregateSort> orderBy)
        {
            var sorts = (orderBy ?? new List<ExcelAggregateSort>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Column))
                .ToList();

            if (sorts.Count == 0)
            {
                groups.Sort((left, right) => CompareKeyParts(left, right, 0, groupBy.Count));
                return;
            }

            groups.Sort((left, right) =>
            {
                foreach (var sort in sorts)
                {
                    var name = sort.Column.Trim();
                    var comparison = 0;

                    var groupIndex = groupBy.FindIndex(g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase));
                    if (groupIndex >= 0)
                    {
                        comparison = string.CompareOrdinal(left.KeyParts[groupIndex], right.KeyParts[groupIndex]);
                    }
                    else
                    {
                        var measureIndex = measures.FindIndex(m => string.Equals(m.Alias, name, StringComparison.OrdinalIgnoreCase));
                        if (measureIndex < 0)
                        {
                            continue;
                        }

                        var function = measures[measureIndex].Function;
                        var leftValue = left.Slots[measureIndex].RenderNumeric(function);
                        var rightValue = right.Slots[measureIndex].RenderNumeric(function);

                        if (leftValue.HasValue && rightValue.HasValue)
                        {
                            comparison = leftValue.Value.CompareTo(rightValue.Value);
                        }
                        else
                        {
                            comparison = string.CompareOrdinal(
                                left.Slots[measureIndex].Render(function, false),
                                right.Slots[measureIndex].Render(function, false));
                        }
                    }

                    if (comparison != 0)
                    {
                        return sort.Descending ? -comparison : comparison;
                    }
                }

                return CompareKeyParts(left, right, 0, groupBy.Count);
            });
        }

        private static int CompareKeyParts(GroupAccumulator left, GroupAccumulator right, int start, int count)
        {
            for (var i = start; i < count && i < left.KeyParts.Length && i < right.KeyParts.Length; i++)
            {
                var comparison = string.CompareOrdinal(left.KeyParts[i], right.KeyParts[i]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        // ---------- 对账 ----------

        public static void Reconcile(
            ExcelTabularSnapshot detail,
            ExcelTabularSnapshot summary,
            ExcelReconcileRequest request,
            ExcelReconcileResult result)
        {
            Reconcile(detail, summary, request, result, detail.RowCount, summary.RowCount);
        }

        public static void Reconcile(
            ExcelTabularSnapshot detail,
            ExcelTabularSnapshot summary,
            ExcelReconcileRequest request,
            ExcelReconcileResult result,
            int detailTotalRows,
            int summaryTotalRows)
        {
            var detailGroupIndex = ResolveColumn(detail, request.DetailGroupColumn, "detailGroupColumn");
            var detailValueIndex = ResolveColumn(detail, request.DetailValueColumn, "detailValueColumn");

            var summaryGroupName = string.IsNullOrWhiteSpace(request.SummaryGroupColumn)
                ? request.DetailGroupColumn
                : request.SummaryGroupColumn;
            var summaryValueName = string.IsNullOrWhiteSpace(request.SummaryValueColumn)
                ? request.DetailValueColumn
                : request.SummaryValueColumn;

            var summaryGroupIndex = ResolveColumn(summary, summaryGroupName, "summaryGroupColumn");
            var summaryValueIndex = ResolveColumn(summary, summaryValueName, "summaryValueColumn");

            result.DetailCoverage = BuildCoverageReport(detail, detailTotalRows);
            result.SummaryCoverage = BuildCoverageReport(summary, summaryTotalRows);

            var detailAccumulator = new KeySetAccumulator();

            for (var row = 0; row < detail.RowCount; row++)
            {
                if (IsRowEmpty(detail, row))
                {
                    continue;
                }

                var key = ExcelValueSemantics.ToText(detail.GetValue(row, detailGroupIndex), IsDateColumn(detail, detailGroupIndex));
                detailAccumulator.Accept(key, detail.GetValue(row, detailValueIndex));
            }

            var summaryAccumulator = new KeySetAccumulator();

            for (var row = 0; row < summary.RowCount; row++)
            {
                if (IsRowEmpty(summary, row))
                {
                    continue;
                }

                var key = ExcelValueSemantics.ToText(summary.GetValue(row, summaryGroupIndex), IsDateColumn(summary, summaryGroupIndex));
                summaryAccumulator.Accept(key, summary.GetValue(row, summaryValueIndex));
            }

            CompleteReconcile(
                detailAccumulator,
                summaryAccumulator,
                request,
                result,
                result.DetailCoverage,
                result.SummaryCoverage);
        }

        internal static void CompleteReconcile(
            KeySetAccumulator detailAccumulator,
            KeySetAccumulator summaryAccumulator,
            ExcelReconcileRequest request,
            ExcelReconcileResult result,
            ExcelCoverageReport detailCoverage,
            ExcelCoverageReport summaryCoverage)
        {
            result.DetailCoverage = detailCoverage;
            result.SummaryCoverage = summaryCoverage;
            var tolerance = request.Tolerance > 0 ? request.Tolerance : 0.01;
            result.DetailRowCount = detailAccumulator.RowCount;
            result.DetailGroupCount = detailAccumulator.Totals.Count;
            result.SummaryRowCount = summaryAccumulator.RowCount;
            result.DetailTotal = Math.Round(detailAccumulator.Total, 10);
            result.SummaryTotal = Math.Round(summaryAccumulator.Total, 10);
            result.TotalDifference = Math.Round(detailAccumulator.Total - summaryAccumulator.Total, 10);
            result.TotalMatched = Math.Abs(result.TotalDifference) <= tolerance;
            result.GroupCountMatched = detailAccumulator.Totals.Count == summaryAccumulator.Totals.Count;

            foreach (var key in detailAccumulator.KeysInOrder)
            {
                if (!summaryAccumulator.Totals.ContainsKey(key) && result.MissingInSummary.Count < 20)
                {
                    result.MissingInSummary.Add(key);
                }
            }

            foreach (var key in summaryAccumulator.Totals.Keys)
            {
                if (!detailAccumulator.Totals.ContainsKey(key) && result.ExtraInSummary.Count < 20)
                {
                    result.ExtraInSummary.Add(key);
                }
            }

            var sampleCount = request.SampleCount > 0 ? request.SampleCount : 5;
            result.SampleMatched = true;

            foreach (var picked in PickStratifiedSamples(
                detailAccumulator.KeysInOrder,
                detailAccumulator.Totals,
                sampleCount))
            {
                var key = picked.Key;
                double summaryValue;
                summaryAccumulator.Totals.TryGetValue(key, out summaryValue);

                var detailValue = detailAccumulator.Totals[key];
                var difference = Math.Round(detailValue - summaryValue, 10);
                var matched = Math.Abs(difference) <= tolerance;

                if (!matched)
                {
                    result.SampleMatched = false;
                }

                result.Samples.Add(new ExcelReconcileSample
                {
                    GroupKey = key,
                    DetailValue = Math.Round(detailValue, 10),
                    SummaryValue = Math.Round(summaryValue, 10),
                    Difference = difference,
                    Matched = matched,
                    SelectionReason = picked.Value
                });
            }

            var verificationPlan = new VerificationAssertionPlan
            {
                AssertionId = "total",
                Kind = "scalar",
                ExpectedValue = result.SummaryTotal,
                Tolerance = tolerance
            };
            verificationPlan.Sides.Add(new PlannedCalcSide
            {
                Side = "target",
                BackendName = "managed",
                Independent = false
            });
            var totalExecution = new VerificationAssertionExecution
            {
                Plan = verificationPlan,
                TargetResult = new CalcResult
                {
                    Scalar = result.DetailTotal,
                    Coverage = detailCoverage
                }
            };
            var groupPlan = new VerificationAssertionPlan
            {
                AssertionId = "groups",
                Kind = "groupMatch",
                Tolerance = tolerance
            };
            groupPlan.Sides.Add(new PlannedCalcSide
            {
                Side = "target",
                BackendName = "managed",
                Independent = false
            });
            groupPlan.Sides.Add(new PlannedCalcSide
            {
                Side = "source",
                BackendName = "managed",
                Independent = false
            });
            var groupExecution = new VerificationAssertionExecution
            {
                Plan = groupPlan,
                TargetResult = new CalcResult
                {
                    Groups = detailAccumulator.Totals,
                    Coverage = detailCoverage
                },
                SourceResult = new CalcResult
                {
                    Groups = summaryAccumulator.Totals,
                    Coverage = summaryCoverage
                }
            };
            var verificationReport = new Comparator().Compare(
                new[] { totalExecution, groupExecution });
            var verificationBasis = new ExcelVerificationBasis
            {
                Source = new ExcelSourceRef
                {
                    WorksheetName = request.DetailWorksheetName ?? string.Empty,
                    Address = request.DetailAddress ?? string.Empty,
                    TableName = request.DetailTableName ?? string.Empty
                },
                Note = "对账汇总源：" + (request.SummaryWorksheetName ?? string.Empty)
                    + "!" + (request.SummaryAddress ?? request.SummaryTableName ?? string.Empty)
                    + "；分组列：" + (request.SummaryGroupColumn ?? request.DetailGroupColumn)
                    + "；金额列：" + (request.SummaryValueColumn ?? request.DetailValueColumn)
            };
            verificationBasis.GroupBy.Add(request.DetailGroupColumn ?? string.Empty);
            verificationBasis.Measures.Add(new ExcelVerificationMeasure
            {
                Column = request.DetailValueColumn ?? string.Empty,
                Function = "sum"
            });
            result.Verification = ExcelVerificationResult.FromReport(
                verificationReport,
                ToolResultVerificationFields.ProducerUnknown,
                false,
                verificationBasis);
            result.TotalMatched = verificationReport.Items[0].Passed;
            result.Passed = result.Verification.Passed
                && result.GroupCountMatched
                && result.SampleMatched
                && result.MissingInSummary.Count == 0
                && result.ExtraInSummary.Count == 0;

            result.Conclusion = BuildConclusion(result, tolerance);
            if (!result.Verification.Passed
                && result.Verification.Details.Diffs.Count > 0)
            {
                result.Conclusion += " 框架差异："
                    + string.Join("；", result.Verification.Details.Diffs) + "。";
            }
        }

        /// <summary>
        /// 分层抽样：均匀取样只能证明「平均情况没错」，而映射类错误几乎总是集中在边界上——
        /// 金额最大的组（一处错就吃掉全部容差）、为 0 或为负的组（符号/过滤写反）、
        /// 以及首尾组（越界与偏移一行）。所以先把这些必检层占满，剩余名额才用均匀覆盖补齐。
        /// 返回值同时带上抽中的理由，让「抽样通过」这句话可被审阅。
        /// </summary>
        private static List<KeyValuePair<string, string>> PickStratifiedSamples(
            List<string> keys,
            Dictionary<string, double> detailTotals,
            int sampleCount)
        {
            var picked = new List<KeyValuePair<string, string>>();
            if (keys == null || keys.Count == 0 || sampleCount <= 0)
            {
                return picked;
            }

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> add = (key, reason) =>
            {
                if (key == null || picked.Count >= sampleCount || !taken.Add(key))
                {
                    return;
                }

                picked.Add(new KeyValuePair<string, string>(key, reason));
            };

            string maxKey = null, minKey = null, zeroKey = null, negativeKey = null;
            var maxValue = double.NegativeInfinity;
            var minValue = double.PositiveInfinity;

            foreach (var key in keys)
            {
                double value;
                if (!detailTotals.TryGetValue(key, out value))
                {
                    continue;
                }

                if (value > maxValue) { maxValue = value; maxKey = key; }
                if (value < minValue) { minValue = value; minKey = key; }
                if (zeroKey == null && value == 0d) { zeroKey = key; }
                if (negativeKey == null && value < 0d) { negativeKey = key; }
            }

            add(maxKey, "金额最大的分组");
            add(negativeKey, "存在负值的分组");
            add(zeroKey, "合计为零的分组");
            add(minKey, "金额最小的分组");
            add(keys[0], "首个分组");
            add(keys[keys.Count - 1], "末个分组");

            foreach (var key in PickSampleKeys(keys, sampleCount))
            {
                if (picked.Count >= sampleCount)
                {
                    break;
                }

                add(key, "均匀覆盖");
            }

            return picked;
        }

        /// <summary>均匀取样，覆盖首尾与中间，避免总是抽到前 N 个分组。</summary>
        private static IEnumerable<string> PickSampleKeys(List<string> keys, int sampleCount)
        {
            if (keys.Count == 0)
            {
                yield break;
            }

            if (keys.Count <= sampleCount)
            {
                foreach (var key in keys)
                {
                    yield return key;
                }

                yield break;
            }

            var step = (double)(keys.Count - 1) / (sampleCount - 1);
            var emitted = new HashSet<int>();
            for (var i = 0; i < sampleCount; i++)
            {
                var index = (int)Math.Round(i * step);
                if (index >= keys.Count)
                {
                    index = keys.Count - 1;
                }

                if (emitted.Add(index))
                {
                    yield return keys[index];
                }
            }
        }

        private static string BuildConclusion(ExcelReconcileResult result, double tolerance)
        {
            if (result.Passed)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "对账通过：明细合计 {0} 与汇总合计 {1} 一致（容差 {2}），分组数均为 {3}，抽样 {4} 组全部匹配。",
                    ExcelValueSemantics.FormatNumber(result.DetailTotal),
                    ExcelValueSemantics.FormatNumber(result.SummaryTotal),
                    ExcelValueSemantics.FormatNumber(tolerance),
                    result.DetailGroupCount,
                    result.Samples.Count);
            }

            var reasons = new List<string>();
            if (!result.TotalMatched)
            {
                reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "合计不符（明细 {0}，汇总 {1}，差额 {2}）",
                    ExcelValueSemantics.FormatNumber(result.DetailTotal),
                    ExcelValueSemantics.FormatNumber(result.SummaryTotal),
                    ExcelValueSemantics.FormatNumber(result.TotalDifference)));
            }

            if (!result.GroupCountMatched)
            {
                reasons.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "分组数不符（明细 {0} 组，汇总 {1} 行）",
                    result.DetailGroupCount,
                    result.SummaryRowCount));
            }

            if (result.MissingInSummary.Count > 0)
            {
                reasons.Add("汇总缺失分组：" + string.Join("、", result.MissingInSummary));
            }

            if (result.ExtraInSummary.Count > 0)
            {
                reasons.Add("汇总多出分组：" + string.Join("、", result.ExtraInSummary));
            }

            if (!result.SampleMatched)
            {
                var mismatched = result.Samples.Where(s => !s.Matched).Select(s => s.GroupKey);
                reasons.Add("抽样不匹配分组：" + string.Join("、", mismatched));
            }

            return "对账未通过：" + string.Join("；", reasons) + "。";
        }
    }
}
