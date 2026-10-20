using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Com;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel.Verification.Backends.Com
{
    public sealed class DataQualityFingerprint
    {
        public DataQualityFingerprint()
        {
            Column = string.Empty;
            CalculationMode = string.Empty;
            CalculationState = string.Empty;
            SubtotalFormulaRows = new List<int>();
            Notes = new List<string>();
        }

        public string Column { get; set; }

        public int RowCount { get; set; }

        public int NonNumericCount { get; set; }

        public int ErrorCount { get; set; }

        public bool? HasNestedSubtotalRows { get; set; }

        public double? NestedSubtotalDifference { get; set; }

        public int SubtotalFormulaCount { get; set; }

        public List<int> SubtotalFormulaRows { get; set; }

        public bool? HasMergedCells { get; set; }

        public bool HasManuallyHiddenRows { get; set; }

        public bool AutoFilterMode { get; set; }

        public bool FilterMode { get; set; }

        public string CalculationMode { get; set; }

        public string CalculationState { get; set; }

        public bool CalculationMayBeStale { get; set; }

        public List<string> Notes { get; set; }

        internal static int CalculateNonNumericCount(
            int nonEmptyCount,
            int numericCount,
            int errorCount)
        {
            if (nonEmptyCount < 0 || numericCount < 0 || errorCount < 0
                || numericCount + errorCount > nonEmptyCount)
            {
                throw new InvalidOperationException("Excel 单元格计数不一致，无法计算非数值指纹。");
            }

            return nonEmptyCount - numericCount - errorCount;
        }

        internal static bool HasSignificantDifference(double left, double right)
        {
            var tolerance = Math.Max(1e-9, Math.Max(Math.Abs(left), Math.Abs(right)) * 1e-12);
            return Math.Abs(left - right) > tolerance;
        }

        internal static bool? DetectNestedSubtotal(
            double? sum,
            double? subtotalSum,
            bool filterMode)
        {
            if (!sum.HasValue || !subtotalSum.HasValue || filterMode)
            {
                return null;
            }

            return HasSignificantDifference(sum.Value, subtotalSum.Value);
        }

        internal static List<int> FindSubtotalFormulaRows(object formulas, int firstWorksheetRow, out int count)
        {
            count = 0;
            var sampleRows = new List<int>();
            var matrix = formulas as object[,];
            if (matrix != null)
            {
                for (var row = matrix.GetLowerBound(0); row <= matrix.GetUpperBound(0); row++)
                {
                    for (var column = matrix.GetLowerBound(1); column <= matrix.GetUpperBound(1); column++)
                    {
                        AddSubtotalFormulaRow(
                            matrix[row, column],
                            firstWorksheetRow + row - matrix.GetLowerBound(0),
                            sampleRows,
                            ref count);
                    }
                }
            }
            else
            {
                AddSubtotalFormulaRow(formulas, firstWorksheetRow, sampleRows, ref count);
            }

            return sampleRows;
        }

        private static void AddSubtotalFormulaRow(
            object formulaValue,
            int worksheetRow,
            IList<int> sampleRows,
            ref int count)
        {
            var formula = formulaValue as string;
            if (formula == null || !Regex.IsMatch(formula, @"\bSUBTOTAL\s*\(", RegexOptions.IgnoreCase))
            {
                return;
            }

            count++;
            if (sampleRows.Count < 10)
            {
                sampleRows.Add(worksheetRow);
            }
        }
    }

    internal sealed class DataQualityFingerprintReader : ExcelServiceBase
    {
        private const int MaximumFormulaAddressLength = 255;
        private const int MaximumDataRows = 100000;

        public DataQualityFingerprintReader(ExcelInterop.Application application)
            : base(application)
        {
        }

        public DataQualityFingerprint Read(ExcelSourceRef source, string column)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (string.IsNullOrWhiteSpace(column))
            {
                throw new ArgumentException("必须指定要检查的数据列。", nameof(column));
            }

            var sourceService = new ExcelAggregationService(_application);
            ExcelInterop.Range sourceRange = null;
            try
            {
                string sourceKind;
                string resolvedTableName;
                string worksheetName;
                sourceRange = sourceService.ResolveSource(
                    source,
                    out sourceKind,
                    out resolvedTableName,
                    out worksheetName);

                var layout = new ExcelColumnReader(_application).ResolveLayout(
                    sourceRange,
                    new ExcelColumnReadRequest
                    {
                        Columns = new List<string> { column },
                        HeaderMode = ExcelAggregationService.ResolveHeaderMode("auto", sourceKind)
                    });
                return Read(sourceRange, layout, 0);
            }
            finally
            {
                ReleaseComObject(sourceRange);
            }
        }

        internal DataQualityFingerprint Read(
            ExcelInterop.Range sourceRange,
            ExcelColumnReadLayout layout,
            int columnIndex)
        {
            if (sourceRange == null)
            {
                throw new ArgumentNullException(nameof(sourceRange));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (columnIndex < 0
                || columnIndex >= layout.Columns.Length
                || columnIndex >= layout.SourceColumnIndexes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(columnIndex));
            }

            if (layout.TotalRows <= 0)
            {
                throw new InvalidOperationException("数据质量指纹没有可检查的数据行。");
            }

            if (layout.TotalRows > MaximumDataRows)
            {
                throw new InvalidOperationException(
                    "COM 数据质量指纹单列最多处理 100,000 行，应改用托管检查。");
            }

            ExcelInterop.Range dataRange = null;
            ExcelInterop.Worksheet worksheet = null;
            object worksheetFunction = null;
            try
            {
                dataRange = ComCalcBackend.CreateDataRange(
                    sourceRange,
                    layout.SourceColumnIndexes[columnIndex],
                    layout.HasHeader,
                    layout.TotalRows);
                worksheet = dataRange.Worksheet as ExcelInterop.Worksheet;
                if (worksheet == null)
                {
                    throw new InvalidOperationException("无法确定数据质量指纹区域所属的工作表。");
                }

                var address = ComCalcBackend.GetRangeAddress(dataRange);
                if (address.Length > MaximumFormulaAddressLength)
                {
                    throw new InvalidOperationException(
                        "数据质量指纹区域地址超过 255 个字符，COM 后端不支持该区域。");
                }

                worksheetFunction = _application.WorksheetFunction;
                var nonEmptyCount = ReadFunctionCount(worksheetFunction, "CountA", dataRange);
                var numericCount = ReadFunctionCount(worksheetFunction, "Count", dataRange);
                var errorCount = ReadErrorCount(dataRange, address);
                var nonNumericCount = DataQualityFingerprint.CalculateNonNumericCount(
                    nonEmptyCount,
                    numericCount,
                    errorCount);
                var visibleCount = ReadSubtotalCount(worksheetFunction, dataRange, 3);
                var visibleCountIgnoringHidden = ReadSubtotalCount(worksheetFunction, dataRange, 103);
                var hasManuallyHiddenRows = visibleCount != visibleCountIgnoringHidden;

                var calculationMode = _application.Calculation;
                var calculationState = _application.CalculationState;
                var result = new DataQualityFingerprint
                {
                    Column = layout.Columns[columnIndex],
                    RowCount = layout.TotalRows,
                    NonNumericCount = nonNumericCount,
                    ErrorCount = errorCount,
                    HasManuallyHiddenRows = hasManuallyHiddenRows,
                    AutoFilterMode = worksheet.AutoFilterMode,
                    FilterMode = worksheet.FilterMode,
                    CalculationMode = calculationMode.ToString(),
                    CalculationState = calculationState.ToString(),
                    CalculationMayBeStale = calculationMode == ExcelInterop.XlCalculation.xlCalculationManual
                        || calculationState != ExcelInterop.XlCalculationState.xlDone,
                    HasMergedCells = ReadMergedCellState(dataRange)
                };

                int subtotalFormulaCount;
                result.SubtotalFormulaRows = DataQualityFingerprint.FindSubtotalFormulaRows(
                    dataRange.Formula,
                    Convert.ToInt32(dataRange.Row, CultureInfo.InvariantCulture),
                    out subtotalFormulaCount);
                result.SubtotalFormulaCount = subtotalFormulaCount;

                if (errorCount == 0)
                {
                    var sum = ComCalcBackend.ToFiniteNumber(
                        ComCalcBackend.InvokeWorksheetFunction(
                            worksheetFunction,
                            "Sum",
                            new object[] { dataRange }),
                        "sum");
                    var subtotal = ReadSubtotal(worksheetFunction, dataRange, 9);
                    result.NestedSubtotalDifference = sum - subtotal;
                    result.HasNestedSubtotalRows = DataQualityFingerprint.DetectNestedSubtotal(
                        sum,
                        subtotal,
                        result.FilterMode);
                }
                else
                {
                    result.HasNestedSubtotalRows = DataQualityFingerprint.DetectNestedSubtotal(
                        null,
                        null,
                        result.FilterMode);
                    result.Notes.Add("存在错误值，跳过 SUM 与 SUBTOTAL 差异判断。");
                }

                if (result.FilterMode)
                {
                    result.Notes.Add("当前存在活动筛选，SUM 与 SUBTOTAL 的差异无法单独归因于嵌套小计。");
                }

                if (result.HasNestedSubtotalRows == true)
                {
                    result.Notes.Add("SUM 与 SUBTOTAL(9) 的差异提示可能存在嵌套小计行；这不是公式逐行识别证明。");
                }

                if (result.HasManuallyHiddenRows)
                {
                    result.Notes.Add("SUBTOTAL(3) 与 SUBTOTAL(103) 的差异提示所选列中存在手动隐藏的非空行。");
                }

                if (result.AutoFilterMode || result.FilterMode)
                {
                    result.Notes.Add(
                        "工作表筛选状态：AutoFilterMode=" + result.AutoFilterMode
                        + "，FilterMode=" + result.FilterMode + "。");
                }

                if (result.CalculationMayBeStale)
                {
                    result.Notes.Add(
                        "Excel 计算状态为 " + result.CalculationMode + "/" + result.CalculationState
                        + "；未强制重算，公式结果可能过期。");
                }

                if (nonNumericCount > 0)
                {
                    result.Notes.Add(
                        "COUNTA-COUNT-错误值数包含文本及逻辑值；该计数本身不能区分文本型数字。");
                }

                return result;
            }
            finally
            {
                // These application-owned RCWs can be reused by subsequent column scans.
                ReleaseComObject(dataRange);
            }
        }

        private static bool? ReadMergedCellState(ExcelInterop.Range range)
        {
            var value = range.MergeCells;
            if (value is bool)
            {
                return (bool)value;
            }

            return value == DBNull.Value ? (bool?)true : null;
        }

        private static int ReadFunctionCount(
            object worksheetFunction,
            string function,
            ExcelInterop.Range range)
        {
            var value = ComCalcBackend.InvokeWorksheetFunction(
                worksheetFunction,
                function,
                new object[] { range });
            return Convert.ToInt32(
                ComCalcBackend.ToFiniteNumber(value, function),
                CultureInfo.InvariantCulture);
        }

        private static int ReadErrorCount(ExcelInterop.Range range, string address)
        {
            var value = ComCalcBackend.EvaluateOnWorksheet(
                range,
                "SUMPRODUCT(--ISERROR(" + address + "))");
            return Convert.ToInt32(
                ComCalcBackend.ToFiniteNumber(value, "errorCount"),
                CultureInfo.InvariantCulture);
        }

        private static int ReadSubtotalCount(
            object worksheetFunction,
            ExcelInterop.Range range,
            int functionNumber)
        {
            var value = ComCalcBackend.InvokeWorksheetFunction(
                worksheetFunction,
                "Subtotal",
                new object[] { functionNumber, range });
            return Convert.ToInt32(
                ComCalcBackend.ToFiniteNumber(value, "subtotal count"),
                CultureInfo.InvariantCulture);
        }

        private static double ReadSubtotal(
            object worksheetFunction,
            ExcelInterop.Range range,
            int functionNumber)
        {
            var value = ComCalcBackend.InvokeWorksheetFunction(
                worksheetFunction,
                "Subtotal",
                new object[] { functionNumber, range });
            return ComCalcBackend.ToFiniteNumber(value, "subtotal sum");
        }
    }
}
