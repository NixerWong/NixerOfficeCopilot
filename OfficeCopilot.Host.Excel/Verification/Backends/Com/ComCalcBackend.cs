using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Verification;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel.Verification.Backends.Com
{
    public sealed class ComCalcBackend : ExcelServiceBase, IExcelCalcBackend
    {
        internal const long MaximumConditionalComparisons = 20000000L;
        internal const long MaximumEstimatedCells = 500000L;
        private const int MaximumCriteriaPairs = 127;

        public ComCalcBackend(ExcelInterop.Application application)
            : base(application)
        {
        }

        public string Name
        {
            get { return "com"; }
        }

        public bool CanEvaluate(CalcQuery query, CalcCostEstimate cost)
        {
            return CanEvaluateQuery(query, cost);
        }

        public DataQualityFingerprint GetDataQualityFingerprint(ExcelSourceRef source, string column)
        {
            if (_application == null)
            {
                throw new InvalidOperationException("当前未连接 Excel 实例。");
            }

            return new DataQualityFingerprintReader(_application).Read(source, column);
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

            string reason;
            var isRowCount = query != null
                && string.Equals(query.Function, "rowCount", StringComparison.OrdinalIgnoreCase);
            if (!TryValidateQuery(query, out reason) || cost == null
                || cost.RowCount < 0
                || (isRowCount
                    ? cost.ReadColumnCount != 0
                    : cost.ReadColumnCount <= 0)
                || cost.ProjectedCellCount != (long)cost.RowCount * cost.ReadColumnCount
                || (!isRowCount && cost.ProjectedCellCount > MaximumEstimatedCells))
            {
                return false;
            }

            var requiredColumnCount = GetRequiredColumnCount(query);
            if (cost.ReadColumnCount < requiredColumnCount)
            {
                return false;
            }

            var filters = GetFilters(query);
            var function = query.Function.Trim().ToLowerInvariant();
            var isFilteredExtreme = filters.Count > 0
                && (function == "min" || function == "max");
            if (!IsConditionalWorkWithinLimit(
                GetConditionalPredicateCount(function, filters.Count),
                cost.RowCount))
            {
                return false;
            }

            var formulaCriteria = new List<string>();
            foreach (var filter in filters)
            {
                string criteria;
                var operation = (filter.Operator ?? string.Empty).Trim();
                var usesFormulaPredicate = operation.StartsWith(
                        "text",
                        StringComparison.OrdinalIgnoreCase)
                    || operation.StartsWith(
                        "dateText",
                        StringComparison.OrdinalIgnoreCase)
                    || SumIfsCriteriaBuilder.IsDatePartFilter(operation)
                    || string.Equals(operation, "numberIn", StringComparison.OrdinalIgnoreCase);
                var supported = string.Equals(
                        query.Function.Trim(),
                        "sum",
                        StringComparison.OrdinalIgnoreCase)
                    && !usesFormulaPredicate
                    ? SumIfsCriteriaBuilder.TryBuild(filter, ".", out criteria)
                    : SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                        filter,
                        "$A$1:$A$2",
                        ".",
                        out criteria);
                if (!supported)
                {
                    return false;
                }

                if (isFilteredExtreme)
                {
                    formulaCriteria.Add(criteria);
                }
            }

            if (isFilteredExtreme)
            {
                string formula;
                if (!TryBuildFilteredExtremeFormula(
                    "$A$1:$A$2",
                    formulaCriteria,
                    function == "max",
                    ",",
                    out formula))
                {
                    return false;
                }
            }

            return true;
        }

        public CalcResult Evaluate(CalcQuery query)
        {
            if (VerificationPlanner.IsMultiSourceQuery(query))
            {
                return EvaluateMultiSource(query);
            }

            string reason;
            if (!TryValidateQuery(query, out reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (_application == null)
            {
                throw new InvalidOperationException("当前未连接 Excel 实例。");
            }

            var sourceService = new ExcelAggregationService(_application);
            var sourceRange = (ExcelInterop.Range)null;
            var dataRanges = new List<ExcelInterop.Range>();
            object worksheetFunctionObject = null;
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

                var requestedColumns = GetRequestedColumns(query);
                var layout = new ExcelColumnReader(_application).ResolveLayout(
                    sourceRange,
                    new ExcelColumnReadRequest
                    {
                        Columns = requestedColumns,
                        HeaderMode = ExcelAggregationService.ResolveHeaderMode(
                            query.HeaderMode,
                            sourceKind)
                    });
                if (layout.TotalRows <= 0)
                {
                    if (!string.Equals(query.Function, "rowCount", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("COM 计算没有可处理的数据行。");
                    }
                }

                EnsureEstimatedCellCountWithinLimit(
                    (long)layout.TotalRows * layout.Columns.Length);
                var filters = GetFilters(query);
                var function = query.Function.Trim().ToLowerInvariant();
                EnsureConditionalWorkWithinLimit(
                    GetConditionalPredicateCount(function, filters.Count),
                    layout.TotalRows);
                if (string.Equals(query.Function, "rowCount", StringComparison.OrdinalIgnoreCase))
                {
                    var rowCountResult = CreateResult(
                        query,
                        (double)layout.TotalRows,
                        layout.TotalRows);
                    if (layout.HeaderInferred)
                    {
                        rowCountResult.Notes.Add(
                            "源数据表头为自动推断：" + layout.HeaderInferenceReason);
                    }

                    return rowCountResult;
                }

                for (var column = 0; column < layout.Columns.Length; column++)
                {
                    dataRanges.Add(CreateDataRange(
                        sourceRange,
                        layout.SourceColumnIndexes[column],
                        layout.HasHeader,
                        layout.TotalRows));
                }

                worksheetFunctionObject = _application.WorksheetFunction;
                var value = EvaluateValue(query, layout, dataRanges, filters, worksheetFunctionObject);
                return CreateResult(query, value, layout.TotalRows);
            }
            finally
            {
                for (var index = dataRanges.Count - 1; index >= 0; index--)
                {
                    ReleaseComObject(dataRanges[index]);
                }

                ReleaseComObject(worksheetFunctionObject);
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

        private object EvaluateValue(
            CalcQuery query,
            ExcelColumnReadLayout layout,
            IList<ExcelInterop.Range> dataRanges,
            IList<CalcFilter> filters,
            object worksheetFunctionObject)
        {
            var function = query.Function.Trim().ToLowerInvariant();
            var decimalSeparator = GetDecimalSeparator();

            if (function == "errorcount")
            {
                return EvaluateErrorCount(dataRanges[0]);
            }

            if (function == "nonblank" || (function == "counta" && filters.Count == 0))
            {
                var total = 0d;
                foreach (var column in query.Columns)
                {
                    var range = dataRanges[FindLayoutColumn(layout, column)];
                    total += ToFiniteNumber(
                        EvaluateOnWorksheet(
                            range,
                            BuildCountaFormula(GetRangeAddress(range))),
                        function);
                }

                return total;
            }

            if ((function == "count" || function == "counta") && filters.Count > 0)
            {
                return EvaluateFilteredCount(query, layout, dataRanges, filters, decimalSeparator);
            }

            if (function == "average" || function == "avg")
            {
                return EvaluateFilteredAverage(
                    query,
                    layout,
                    dataRanges,
                    filters,
                    decimalSeparator,
                    GetListSeparator());
            }

            if (function == "sum")
            {
                var total = 0d;
                var queryColumnCount = query.Columns.Count;
                var listSeparator = GetListSeparator();
                for (var column = 0; column < queryColumnCount; column++)
                {
                    var range = dataRanges[FindLayoutColumn(layout, query.Columns[column])];
                    var value = filters.Count == 0
                        ? EvaluateOnWorksheet(
                            range,
                            "SUMPRODUCT("
                                + BuildSumValueExpression(GetRangeAddress(range), listSeparator)
                                + ")")
                        : EvaluateFilteredSum(
                            range,
                            layout,
                            dataRanges,
                            filters,
                            decimalSeparator,
                            listSeparator);
                    total += ToFiniteNumber(value, function);
                }

                return total;
            }

            if ((function == "min" || function == "max") && filters.Count > 0)
            {
                return EvaluateFilteredExtreme(
                    query,
                    layout,
                    dataRanges,
                    filters,
                    decimalSeparator,
                    GetListSeparator(),
                    function == "max");
            }

            var targetRange = dataRanges[FindLayoutColumn(layout, query.Columns[0])];
            return InvokeWorksheetFunction(
                worksheetFunctionObject,
                GetWorksheetFunctionName(function, query.Function),
                new object[] { targetRange });
        }

        internal static string GetWorksheetFunctionName(string function, string originalFunction = null)
        {
            switch ((function ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "count":
                    return "Count";
                case "counta":
                    return "CountA";
                case "min":
                    return "Min";
                case "max":
                    return "Max";
                default:
                    throw new InvalidOperationException(
                        "COM 后端不支持函数：" + (originalFunction ?? function ?? string.Empty));
            }
        }

        internal static string BuildCountValueExpression(string function, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                throw new ArgumentException("A range address is required.", nameof(address));
            }

            switch ((function ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "count":
                    return "--ISNUMBER(" + address + ")";
                case "counta":
                    return "--(NOT(ISBLANK(" + address + ")))";
                default:
                    throw new InvalidOperationException(
                        "COM 后端不支持筛选计数函数：" + (function ?? string.Empty));
            }
        }

        internal static string BuildCountaFormula(string address)
        {
            return "SUMPRODUCT(" + BuildCountValueExpression("counta", address) + ")";
        }

        internal static string BuildSumValueExpression(string rangeAddress, string listSeparator)
        {
            if (string.IsNullOrWhiteSpace(rangeAddress))
            {
                throw new ArgumentException("A range address is required.", nameof(rangeAddress));
            }

            var separator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            var numberValue = "NUMBERVALUE("
                + rangeAddress + separator + "\".\""
                + separator + "\",\")";
            return "IFERROR(IF(ISNUMBER(" + rangeAddress + ")"
                + separator + rangeAddress
                + separator + "IF(ISTEXT(" + rangeAddress + ")"
                + separator + numberValue + separator + "0)),0)";
        }

        private double EvaluateFilteredSum(
            ExcelInterop.Range sumRange,
            ExcelColumnReadLayout layout,
            IList<ExcelInterop.Range> dataRanges,
            IList<CalcFilter> filters,
            string decimalSeparator,
            string listSeparator)
        {
            var filterExpressions = new List<string>(filters.Count);
            var sumIfsCriteriaRanges = new List<string>(filters.Count);
            var sumIfsCriteria = new List<string>(filters.Count);
            var canUseSumIfs = filters.Count > 0;
            for (var index = 0; index < filters.Count; index++)
            {
                string criteriaExpression;
                var filterRange = dataRanges[FindLayoutColumn(layout, filters[index].Column)];
                var operation = (filters[index].Operator ?? string.Empty).Trim();
                string criteria;
                if (string.Equals(operation, "isNativeNumber", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(operation, "isTextNumber", StringComparison.OrdinalIgnoreCase)
                    || !SumIfsCriteriaBuilder.TryBuild(
                        filters[index],
                        decimalSeparator,
                        out criteria))
                {
                    canUseSumIfs = false;
                }
                else
                {
                    sumIfsCriteriaRanges.Add(GetRangeAddress(filterRange));
                    sumIfsCriteria.Add(criteria);
                }

                if (!SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                    filters[index],
                    GetRangeAddress(filterRange),
                    decimalSeparator,
                    listSeparator,
                    out criteriaExpression))
                {
                    throw new InvalidOperationException(
                        "COM 后端不支持筛选条件：" + (filters[index].Operator ?? string.Empty));
                }

                filterExpressions.Add(criteriaExpression);
            }

            if (canUseSumIfs && HasNoParseableTextNumbers(sumRange, listSeparator))
            {
                var sumIfsFormula = BuildSumIfsFormula(
                    GetRangeAddress(sumRange),
                    sumIfsCriteriaRanges,
                    sumIfsCriteria,
                    listSeparator);
                double sumIfsResult;
                if (TryGetNativeNumber(
                        EvaluateOnWorksheet(sumRange, sumIfsFormula),
                        out sumIfsResult)
                    && !double.IsNaN(sumIfsResult)
                    && !double.IsInfinity(sumIfsResult))
                {
                    return sumIfsResult;
                }
            }

            var formula = BuildFilteredSumFormula(
                GetRangeAddress(sumRange),
                filterExpressions,
                listSeparator);
            return ToFiniteNumber(
                EvaluateOnWorksheet(sumRange, formula),
                "sum");
        }

        private static bool HasNoParseableTextNumbers(
            ExcelInterop.Range range,
            string listSeparator)
        {
            var formula = BuildTextNumberCountFormula(
                GetRangeAddress(range),
                listSeparator);
            double textNumberCount;
            return TryGetNativeNumber(
                    EvaluateOnWorksheet(range, formula),
                    out textNumberCount)
                && textNumberCount == 0d;
        }

        internal static string BuildFilteredSumFormula(
            string rangeAddress,
            IList<string> filterExpressions,
            string listSeparator)
        {
            var separator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            var formulaArguments = new List<string>
            {
                BuildSumValueExpression(rangeAddress, separator)
            };
            foreach (var filterExpression in filterExpressions ?? new List<string>())
            {
                formulaArguments.Add("--" + EnsureFormulaErrorFallback(
                    filterExpression,
                    separator));
            }

            return "SUMPRODUCT("
                + string.Join(separator, formulaArguments.ToArray()) + ")";
        }

        private double EvaluateFilteredAverage(
            CalcQuery query,
            ExcelColumnReadLayout layout,
            IList<ExcelInterop.Range> dataRanges,
            IList<CalcFilter> filters,
            string decimalSeparator,
            string listSeparator)
        {
            var valueRange = dataRanges[FindLayoutColumn(layout, query.Columns[0])];
            var total = EvaluateFilteredSum(
                valueRange,
                layout,
                dataRanges,
                filters,
                decimalSeparator,
                listSeparator);
            var filterExpressions = new List<string>();
            foreach (var filter in filters)
            {
                string expression;
                var filterRange = dataRanges[FindLayoutColumn(layout, filter.Column)];
                if (!SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                    filter,
                    GetRangeAddress(filterRange),
                    decimalSeparator,
                    listSeparator,
                    out expression))
                {
                    throw new InvalidOperationException(
                        "COM 后端不支持筛选条件：" + (filter.Operator ?? string.Empty));
                }

                filterExpressions.Add(expression);
            }

            var numericCount = ToFiniteNumber(
                EvaluateOnWorksheet(
                    valueRange,
                    BuildFilteredNumericCountFormula(
                        GetRangeAddress(valueRange),
                        filterExpressions,
                        listSeparator)),
                "average count");
            if (numericCount <= 0d)
            {
                throw new InvalidOperationException(
                    "COM average 过滤后没有可参与计算的数值。");
            }

            return total / numericCount;
        }

        private double EvaluateFilteredExtreme(
            CalcQuery query,
            ExcelColumnReadLayout layout,
            IList<ExcelInterop.Range> dataRanges,
            IList<CalcFilter> filters,
            string decimalSeparator,
            string listSeparator,
            bool maximum)
        {
            var valueRange = dataRanges[FindLayoutColumn(layout, query.Columns[0])];
            var filterExpressions = new List<string>();
            foreach (var filter in filters)
            {
                string expression;
                var filterRange = dataRanges[FindLayoutColumn(layout, filter.Column)];
                if (!SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                    filter,
                    GetRangeAddress(filterRange),
                    decimalSeparator,
                    listSeparator,
                    out expression))
                {
                    throw new InvalidOperationException(
                        "COM 后端不支持筛选条件：" + (filter.Operator ?? string.Empty));
                }

                filterExpressions.Add(expression);
            }

            string formula;
            if (!TryBuildFilteredExtremeFormula(
                GetRangeAddress(valueRange),
                filterExpressions,
                maximum,
                listSeparator,
                out formula))
            {
                throw new InvalidOperationException(
                    "COM 条件极值公式超过 Excel 公式长度上限或无法构造。");
            }

            return ToFiniteNumber(
                EvaluateOnWorksheet(valueRange, formula),
                maximum ? "max" : "min");
        }

        internal static bool TryBuildFilteredExtremeFormula(
            string rangeAddress,
            IList<string> filterExpressions,
            bool maximum,
            string listSeparator,
            out string formula)
        {
            formula = string.Empty;
            if (string.IsNullOrWhiteSpace(rangeAddress))
            {
                return false;
            }

            var separator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            var conditions = new List<string>
            {
                "ISNUMBER(" + rangeAddress + ")"
            };
            foreach (var expression in filterExpressions ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(expression))
                {
                    return false;
                }

                conditions.Add(EnsureFormulaErrorFallback(expression, separator));
            }

            var include = "(" + string.Join(")*(", conditions.ToArray()) + ")";
            var array = "IF(" + include + separator + rangeAddress
                + separator + "NA())";
            formula = "AGGREGATE(" + (maximum ? "14" : "15")
                + separator + "6" + separator + array + separator + "1)";
            return formula.Length <= 8192;
        }

        internal static string BuildFilteredNumericCountFormula(
            string rangeAddress,
            IList<string> filterExpressions,
            string listSeparator)
        {
            if (string.IsNullOrWhiteSpace(rangeAddress))
            {
                throw new ArgumentException("A range address is required.", nameof(rangeAddress));
            }

            var separator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            var formulaArguments = new List<string>
            {
                BuildNumericCountExpression(rangeAddress, separator)
            };

            foreach (var filterExpression in filterExpressions ?? new List<string>())
            {
                formulaArguments.Add("--" + EnsureFormulaErrorFallback(
                    filterExpression,
                    separator));
            }

            return "SUMPRODUCT("
                + string.Join(separator, formulaArguments.ToArray()) + ")";
        }

        internal static string BuildSumIfsFormula(
            string rangeAddress,
            IList<string> criteriaRangeAddresses,
            IList<string> criteria,
            string listSeparator)
        {
            if (criteriaRangeAddresses == null
                || criteria == null
                || criteriaRangeAddresses.Count != criteria.Count)
            {
                throw new ArgumentException(
                    "SUMIFS criteria ranges and criteria must have matching counts.");
            }

            var separator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            var arguments = new List<string> { rangeAddress };
            for (var index = 0; index < criteria.Count; index++)
            {
                arguments.Add(criteriaRangeAddresses[index]);
                arguments.Add("\"" + (criteria[index] ?? string.Empty)
                    .Replace("\"", "\"\"") + "\"");
            }

            return "SUMIFS(" + string.Join(separator, arguments.ToArray()) + ")";
        }

        private static string EnsureFormulaErrorFallback(
            string expression,
            string listSeparator)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new ArgumentException(
                    "A formula comparison expression is required.",
                    nameof(expression));
            }

            if (expression.TrimStart().StartsWith(
                "IFERROR(",
                StringComparison.OrdinalIgnoreCase))
            {
                return expression;
            }

            var separator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            return "IFERROR(" + expression + separator + "FALSE)";
        }

        internal static string BuildNumericCountExpression(string rangeAddress, string listSeparator)
        {
            if (string.IsNullOrWhiteSpace(rangeAddress))
            {
                throw new ArgumentException("A range address is required.", nameof(rangeAddress));
            }

            var separator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            var numberValue = "NUMBERVALUE("
                + rangeAddress + separator + "\".\""
                + separator + "\",\")";
            return "--((ISNUMBER(" + rangeAddress + ")+IFERROR(ISTEXT("
                + rangeAddress + ")*ISNUMBER(" + numberValue + ")" + separator
                + "FALSE))>0)";
        }

        internal static string BuildTextNumberCountFormula(
            string rangeAddress,
            string listSeparator)
        {
            var separator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            return "SUMPRODUCT("
                + BuildNumericCountExpression(rangeAddress, separator)
                + ")-COUNT(" + rangeAddress + ")";
        }

        private double EvaluateFilteredCount(
            CalcQuery query,
            ExcelColumnReadLayout layout,
            IList<ExcelInterop.Range> dataRanges,
            IList<CalcFilter> filters,
            string decimalSeparator)
        {
            var listSeparator = GetListSeparator();
            var valueRange = dataRanges[FindLayoutColumn(layout, query.Columns[0])];
            var formulaArguments = new List<string>
            {
                BuildCountValueExpression(query.Function, GetRangeAddress(valueRange))
            };
            foreach (var filter in filters)
            {
                string comparison;
                var range = dataRanges[FindLayoutColumn(layout, filter.Column)];
                if (!SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                    filter,
                    GetRangeAddress(range),
                    decimalSeparator,
                    listSeparator,
                    out comparison))
                {
                    throw new InvalidOperationException(
                        "COM 后端不支持筛选条件：" + (filter.Operator ?? string.Empty));
                }

                formulaArguments.Add("--" + EnsureFormulaErrorFallback(
                    comparison,
                    listSeparator));
            }

            var formula = "SUMPRODUCT("
                + string.Join(listSeparator, formulaArguments.ToArray()) + ")";
            return ToFiniteNumber(EvaluateOnWorksheet(valueRange, formula), query.Function);
        }

        private static object EvaluateErrorCount(ExcelInterop.Range range)
        {
            var address = GetRangeAddress(range);
            if (address.Length > SumIfsCriteriaBuilder.MaximumCriteriaLength)
            {
                throw new InvalidOperationException(
                    "错误值计数的区域地址超过 255 个字符，COM 后端不支持该区域。");
            }

            return EvaluateOnWorksheet(range, "SUMPRODUCT(--ISERROR(" + address + "))");
        }

        internal static object EvaluateOnWorksheet(ExcelInterop.Range range, string formula)
        {
            var worksheet = range.Worksheet as ExcelInterop.Worksheet;
            if (worksheet == null)
            {
                throw new InvalidOperationException("无法确定 COM 计算区域所属的工作表。");
            }

            // This Worksheet RCW is borrowed from the caller's Range and must not be final-released here.
            return worksheet.Evaluate(formula);
        }

        private string GetDecimalSeparator()
        {
            var separator = Convert.ToString(
                _application.International[ExcelInterop.XlApplicationInternational.xlDecimalSeparator],
                CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(separator) ? "." : separator;
        }

        private string GetListSeparator()
        {
            var separator = Convert.ToString(
                _application.International[ExcelInterop.XlApplicationInternational.xlListSeparator],
                CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(separator) ? "," : separator;
        }

        internal static ExcelInterop.Range CreateDataRange(
            ExcelInterop.Range sourceRange,
            int sourceColumnIndex,
            bool hasHeader,
            int rowCount)
        {
            ExcelInterop.Range cells = null;
            ExcelInterop.Range firstCell = null;
            ExcelInterop.Range dataRange = null;
            try
            {
                cells = sourceRange.Cells as ExcelInterop.Range;
                firstCell = cells[hasHeader ? 2 : 1, sourceColumnIndex + 1] as ExcelInterop.Range;
                if (firstCell == null)
                {
                    throw new InvalidOperationException("无法定位 COM 计算列的首个数据单元格。");
                }

                dataRange = firstCell.Resize[rowCount, 1] as ExcelInterop.Range;
                if (dataRange == null)
                {
                    throw new InvalidOperationException("无法创建 COM 计算列区域。");
                }

                if (ReferenceEquals(dataRange, firstCell))
                {
                    firstCell = null;
                }

                var result = dataRange;
                dataRange = null;
                return result;
            }
            finally
            {
                ReleaseComObject(dataRange);
                ReleaseComObject(firstCell);
                ReleaseComObject(cells);
            }
        }

        internal static string GetRangeAddress(ExcelInterop.Range range)
        {
            return range.Address[true, true, ExcelInterop.XlReferenceStyle.xlA1];
        }

        internal static object InvokeWorksheetFunction(
            object worksheetFunction,
            string methodName,
            object[] arguments)
        {
            dynamic functions = worksheetFunction;
            switch (methodName)
            {
                case "Sum":
                    return functions.Sum(arguments[0]);
                case "Count":
                    return functions.Count(arguments[0]);
                case "CountA":
                    return functions.CountA(arguments[0]);
                case "Min":
                    return functions.Min(arguments[0]);
                case "Max":
                    return functions.Max(arguments[0]);
                case "Subtotal":
                    return functions.Subtotal(arguments[0], arguments[1]);
            }

            try
            {
                return worksheetFunction.GetType().InvokeMember(
                    methodName,
                    BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.Public,
                    null,
                    worksheetFunction,
                    arguments,
                    CultureInfo.CurrentCulture);
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException == null)
                {
                    throw;
                }

                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        internal static double ToFiniteNumber(object value, string function)
        {
            double number;
            if (!TryGetNativeNumber(value, out number)
                || double.IsNaN(number)
                || double.IsInfinity(number))
            {
                throw new InvalidOperationException(
                    "Excel 原生函数 " + function + " 返回了非数值结果。");
            }

            return number;
        }

        private static bool TryGetNativeNumber(object value, out double number)
        {
            if (value is ErrorWrapper)
            {
                number = 0d;
                return false;
            }

            if (value is int)
            {
                var integer = (int)value;
                if (IsExcelErrorCode(integer)
                    || integer < 0
                    || integer > 1048576)
                {
                    number = 0d;
                    return false;
                }

                number = integer;
                return true;
            }

            number = 0d;
            return !ExcelValueSemantics.IsError(value)
                && ExcelValueSemantics.TryGetNumber(value, out number);
        }

        private static bool IsExcelErrorCode(int value)
        {
            switch (value)
            {
                case -2146826281:
                case -2146826246:
                case -2146826259:
                case -2146826288:
                case -2146826252:
                case -2146826265:
                case -2146826273:
                    return true;
                default:
                    return false;
            }
        }

        private static CalcResult CreateResult(CalcQuery query, object value, int rowCount)
        {
            var result = new CalcResult
            {
                Scalar = ToFiniteNumber(value, query.Function),
                Coverage = new ExcelCoverageReport
                {
                    TotalRows = rowCount,
                    ScannedRows = rowCount,
                    Backend = "com",
                    CellCountsAvailable = false
                }
            };
            result.Notes.Add("COM 原生函数覆盖全部数据行；为避免额外逐格读取，单元格分类计数未采集。");

            var function = query.Function.Trim().ToLowerInvariant();
            if (function == "sum")
            {
                result.Notes.Add(
                    "COM 求和显式包含可解析的文本数字并忽略错误值，与托管 sum 的分类口径一致。");
            }
            else if (function == "count")
            {
                result.Notes.Add("Excel COUNT 仅计原生数值；托管 count 可能将其他非空值计入。");
            }
            else if (function == "counta")
            {
                result.Notes.Add("Excel COUNTA 会计入返回空字符串的公式，可能与托管空白语义不同。");
            }
            else if (function == "min" || function == "max")
            {
                result.Notes.Add("Excel MIN/MAX 忽略文本；托管后端在无数值时可能采用文本极值。");
            }

            return result;
        }

        private static int FindLayoutColumn(ExcelColumnReadLayout layout, string requestedColumn)
        {
            for (var index = 0; index < layout.Columns.Length; index++)
            {
                if (string.Equals(
                    layout.Columns[index],
                    (requestedColumn ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            throw new InvalidOperationException(
                "COM 计算布局中缺少列：" + (requestedColumn ?? string.Empty));
        }

        private static IList<string> GetRequestedColumns(CalcQuery query)
        {
            var columns = new List<string>(query.Columns);
            foreach (var filter in GetFilters(query))
            {
                columns.Add(filter.Column);
            }

            return columns;
        }

        private static IList<CalcFilter> GetFilters(CalcQuery query)
        {
            return query.Filters ?? new List<CalcFilter>();
        }

        private static int GetRequiredColumnCount(CalcQuery query)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in query.Columns)
            {
                columns.Add(column.Trim());
            }

            foreach (var filter in GetFilters(query))
            {
                columns.Add(filter.Column.Trim());
            }

            return columns.Count;
        }

        private static bool TryValidateQuery(CalcQuery query, out string reason)
        {
            reason = string.Empty;
            if (query == null || query.Source == null)
            {
                reason = "COM 计算请求缺少数据源。";
                return false;
            }

            if ((query.ExcludeRows != null && query.ExcludeRows.Count > 0)
                || query.ExcludeLabelRows)
            {
                reason = "COM 后端不支持按工作表行号或标签排除；请使用托管后端。";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(query.GroupByColumn))
            {
                reason = "COM 后端不支持分组计算。";
                return false;
            }

            var function = (query.Function ?? string.Empty).Trim().ToLowerInvariant();
            var isRowCount = function == "rowcount";
            if (isRowCount)
            {
                if (query.Columns == null || query.Columns.Count != 0
                    || GetFilters(query).Count != 0
                    || !string.IsNullOrWhiteSpace(query.GroupByColumn))
                {
                    reason = "COM rowCount 只接受无列、无筛选、无分组的请求。";
                    return false;
                }

                return true;
            }

            if (query.Columns == null || query.Columns.Count == 0)
            {
                reason = "COM 计算请求至少需要一个数据列。";
                return false;
            }

            var queryColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in query.Columns)
            {
                if (string.IsNullOrWhiteSpace(column))
                {
                    reason = "COM 计算请求包含空列名。";
                    return false;
                }

                if (!queryColumns.Add(column.Trim()))
                {
                    reason = "COM 计算请求不能重复指定同一数据列。";
                    return false;
                }
            }

            var singleColumnFunction = function == "count"
                || function == "counta"
                || function == "average"
                || function == "avg"
                || function == "min"
                || function == "max"
                || function == "errorcount";
            if (function != "sum" && function != "nonblank" && !singleColumnFunction)
            {
                reason = "COM 后端不支持函数：" + (query.Function ?? string.Empty);
                return false;
            }

            if (singleColumnFunction && query.Columns.Count != 1)
            {
                reason = "COM 后端的 " + function + " 函数只支持单列。";
                return false;
            }

            var filters = GetFilters(query);
            if (function == "nonblank" && filters.Count > 0)
            {
                reason = "COM 后端的 nonBlank 不支持筛选。";
                return false;
            }

            if (filters.Count > MaximumCriteriaPairs)
            {
                reason = "COM 后端筛选条件数量超过 Excel 原生函数上限。";
                return false;
            }

            if (filters.Count > 0
                && function != "sum"
                && function != "average"
                && function != "avg"
                && function != "count"
                && function != "counta"
                && function != "min"
                && function != "max")
            {
                reason = "COM 后端未为 " + function + " 实现筛选计算。";
                return false;
            }

            foreach (var filter in filters)
            {
                if (filter == null || string.IsNullOrWhiteSpace(filter.Column))
                {
                    reason = "COM 计算请求包含无效筛选列。";
                    return false;
                }
            }

            var hasBoundedErrorCountSource =
                (!string.IsNullOrWhiteSpace(query.Source.Address)
                    && query.Source.Address.Length <= SumIfsCriteriaBuilder.MaximumCriteriaLength)
                || (!string.IsNullOrWhiteSpace(query.Source.TableName)
                    && query.Source.TableName.Length <= SumIfsCriteriaBuilder.MaximumCriteriaLength);
            if (function == "errorcount" && !hasBoundedErrorCountSource)
            {
                reason = "错误值计数仅支持表名或不超过 255 个字符的显式地址数据源。";
                return false;
            }

            return true;
        }

        private static void EnsureConditionalWorkWithinLimit(int filterCount, int rowCount)
        {
            if (!IsConditionalWorkWithinLimit(filterCount, rowCount))
            {
                throw new InvalidOperationException(
                    "COM 条件计算超过 20,000,000 次比较上限，应改用托管后端。");
            }
        }

        private static void EnsureEstimatedCellCountWithinLimit(long projectedCellCount)
        {
            if (projectedCellCount > MaximumEstimatedCells)
            {
                throw new InvalidOperationException(
                    "COM 计算估算读取格数超过 100,000 上限，应改用托管后端。");
            }
        }

        internal static bool IsConditionalWorkWithinLimit(int filterCount, int rowCount)
        {
            return filterCount >= 0
                && rowCount >= 0
                && (long)filterCount * rowCount <= MaximumConditionalComparisons;
        }

        internal static int GetConditionalPredicateCount(string function, int filterCount)
        {
            var normalizedFunction = (function ?? string.Empty).Trim().ToLowerInvariant();
            return filterCount > 0
                && (normalizedFunction == "min" || normalizedFunction == "max")
                ? filterCount + 1
                : filterCount;
        }
    }
}
