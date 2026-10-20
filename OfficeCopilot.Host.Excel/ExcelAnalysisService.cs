using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel
{
    internal sealed class ExcelAnalysisService : ExcelServiceBase
    {
        public ExcelAnalysisService(ExcelInterop.Application application)
            : base(application)
        {
        }

        public ExcelAnalyzeRangeResult AnalyzeRange(ExcelAnalyzeRangeRequest request)
        {
            request = request ?? new ExcelAnalyzeRangeRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveAnalyzeRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可分析的区域。");
            }

            var operation = NormalizeAnalyzeOperation(request.Operation);
            var function = NormalizeAnalyzeFunction(request.Function);
            ExcelInterop.Range returnRange = null;
            ExcelInterop.Range matchedValueCell = null;
            ExcelInterop.Range matchedReturnRange = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(request.ReturnAddress))
                {
                    returnRange = ResolveAnalyzeReturnRange(worksheet, request.ReturnAddress);
                }

                var result = new ExcelAnalyzeRangeResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = GetWorksheetName(worksheet),
                    Address = GetAddress(range),
                    Operation = operation,
                    Function = function,
                    ReturnAddress = returnRange == null ? string.Empty : GetAddress(returnRange),
                    Engine = "excel-native"
                };

                switch (operation)
                {
                    case "aggregate":
                        PopulateAggregateAnalysisResult(range, function, result);
                        break;
                    case "lookup_by_extreme":
                        PopulateLookupByExtremeAnalysisResult(
                            range,
                            returnRange,
                            function,
                            result,
                            out matchedValueCell,
                            out matchedReturnRange);
                        break;
                    default:
                        throw new InvalidOperationException("不支持的分析操作：" + operation);
                }

                return result;
            }
            finally
            {
                ReleaseComObject(matchedReturnRange);
                ReleaseComObject(matchedValueCell);
                ReleaseComObject(returnRange);
            }
        }

        public ExcelRangeFindResult FindInRange(ExcelRangeFindRequest request)
        {
            request = request ?? new ExcelRangeFindRequest();
            if (string.IsNullOrWhiteSpace(request.SearchValue))
            {
                throw new InvalidOperationException("搜索内容不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveFindRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可搜索的区域。");
            }

            var result = new ExcelRangeFindResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = GetWorksheetName(worksheet),
                Address = GetAddress(range),
                SearchValue = request.SearchValue,
                SearchFormulas = request.SearchFormulas,
                MatchCase = request.MatchCase,
                MatchEntireCell = request.MatchEntireCell
            };

            ExcelInterop.Range currentFind = null;
            ExcelInterop.Range firstFind = null;
            var maxResults = NormalizePositive(request.MaxResults, 100);

            try
            {
                currentFind = range.Find(
                    request.SearchValue,
                    Type.Missing,
                    request.SearchFormulas ? ExcelInterop.XlFindLookIn.xlFormulas : ExcelInterop.XlFindLookIn.xlValues,
                    request.MatchEntireCell ? ExcelInterop.XlLookAt.xlWhole : ExcelInterop.XlLookAt.xlPart,
                    ExcelInterop.XlSearchOrder.xlByRows,
                    ExcelInterop.XlSearchDirection.xlNext,
                    request.MatchCase,
                    Type.Missing,
                    Type.Missing) as ExcelInterop.Range;

                while (currentFind != null)
                {
                    if (firstFind == null)
                    {
                        firstFind = currentFind;
                    }
                    else if (string.Equals(GetAddress(currentFind), GetAddress(firstFind), StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    result.MatchingCells.Add(new ExcelRangeCellMatch
                    {
                        Address = GetAddress(currentFind),
                        Row = SafeGetInt32(() => Convert.ToInt32(currentFind.Row)),
                        Column = SafeGetInt32(() => Convert.ToInt32(currentFind.Column)),
                        Value = Convert.ToString(currentFind.Text) ?? string.Empty,
                        Formula = Convert.ToString(currentFind.Formula) ?? string.Empty
                    });

                    if (result.MatchingCells.Count >= maxResults)
                    {
                        result.IsTruncated = true;
                        break;
                    }

                    var nextFind = range.FindNext(currentFind) as ExcelInterop.Range;
                    if (!ReferenceEquals(currentFind, firstFind))
                    {
                        ReleaseComObject(currentFind);
                    }

                    currentFind = nextFind;
                }
            }
            finally
            {
                if (!ReferenceEquals(currentFind, firstFind))
                {
                    ReleaseComObject(currentFind);
                }

                ReleaseComObject(firstFind);
            }

            return result;
        }

        public ExcelRangeFormulaValidationResult ValidateRangeFormulas(ExcelRangeFormulaValidationRequest request)
        {
            request = request ?? new ExcelRangeFormulaValidationRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveValidationRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可验证的区域。");
            }

            var maxRows = NormalizePositive(request.MaxRows, 100);
            var maxColumns = NormalizePositive(request.MaxColumns, 30);
            var totalRows = Convert.ToInt32(range.Rows.Count);
            var totalColumns = Convert.ToInt32(range.Columns.Count);

            var result = new ExcelRangeFormulaValidationResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = GetWorksheetName(worksheet),
                Address = GetAddress(range),
                IsValid = true,
                IsTruncated = totalRows > maxRows || totalColumns > maxColumns,
                Formulas = BuildFormulaRows(range, maxRows, maxColumns)
            };

            var rowCount = Math.Min(totalRows, maxRows);
            var columnCount = Math.Min(totalColumns, maxColumns);

            for (var row = 1; row <= rowCount; row++)
            {
                for (var column = 1; column <= columnCount; column++)
                {
                    ExcelInterop.Range cell = null;

                    try
                    {
                        cell = range.Cells[row, column] as ExcelInterop.Range;
                        if (cell == null)
                        {
                            continue;
                        }

                        var formula = Convert.ToString(cell.Formula) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(formula) || !formula.StartsWith("=", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        result.FormulaCount++;

                        var address = GetAddress(cell);
                        var displayText = Convert.ToString(cell.Text) ?? string.Empty;
                        var hasError = IsExcelFormulaError(displayText);
                        if (hasError)
                        {
                            result.IsValid = false;
                            result.ErrorCount++;

                            result.Errors.Add(new ExcelFormulaValidationError
                            {
                                CellAddress = address,
                                Row = SafeGetInt32(() => Convert.ToInt32(cell.Row)),
                                Column = SafeGetInt32(() => Convert.ToInt32(cell.Column)),
                                Formula = formula,
                                Message = "公式结果为 Excel 错误值：" + displayText,
                                Suggestion = BuildFormulaSuggestion(displayText),
                                Category = ClassifyFormulaError(displayText)
                            });

                            continue;
                        }

                        result.ValidCount++;

                        if (ContainsVolatileFunction(formula))
                        {
                            result.Warnings.Add(new ExcelFormulaValidationWarning
                            {
                                CellAddress = address,
                                Formula = formula,
                                Message = "检测到易变函数，可能导致频繁重算。",
                                Category = "volatile-function"
                            });
                        }

                        if (SafeGetBool(() => Convert.ToBoolean(cell.HasArray), false))
                        {
                            result.Warnings.Add(new ExcelFormulaValidationWarning
                            {
                                CellAddress = address,
                                Formula = formula,
                                Message = "检测到数组公式，请确认目标区域与依赖范围一致。",
                                Category = "array-formula"
                            });
                        }

                        if (formula.IndexOf("[", StringComparison.OrdinalIgnoreCase) >= 0
                            && formula.IndexOf("]", StringComparison.OrdinalIgnoreCase) > formula.IndexOf("[", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Warnings.Add(new ExcelFormulaValidationWarning
                            {
                                CellAddress = address,
                                Formula = formula,
                                Message = "公式包含外部工作簿引用，请确认外部链接可用。",
                                Category = "external-reference"
                            });
                        }
                    }
                    finally
                    {
                        ReleaseComObject(cell);
                    }
                }
            }

            return result;
        }

        public ExcelPowerQueryValidationResult ValidatePowerQuery(ExcelPowerQueryValidationRequest request)
        {
            request = request ?? new ExcelPowerQueryValidationRequest();

            if (string.IsNullOrWhiteSpace(request.MCode))
            {
                throw new InvalidOperationException("M 代码不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            if (workbook.ReadOnly)
            {
                throw new InvalidOperationException("当前工作簿为只读状态，无法执行临时 Power Query 校验。");
            }

            var result = new ExcelPowerQueryValidationResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                MCode = request.MCode,
                IsValid = true
            };

            object queriesObject = null;
            object temporaryQuery = null;
            var temporaryName = "__copilot_validation_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                queriesObject = workbook.Queries;
                dynamic queries = queriesObject;
                temporaryQuery = queries.Add(temporaryName, request.MCode);
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ValidationErrors.Add(ex.Message);
            }

            try
            {
                if (temporaryQuery != null)
                {
                    ((dynamic)temporaryQuery).Delete();
                    temporaryQuery = null;
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ValidationErrors.Add(
                    "无法清理临时 Power Query 校验对象 \"" + temporaryName + "\"：" + ex.Message);
            }
            finally
            {
                ReleaseComObject(temporaryQuery);
                ReleaseComObject(queriesObject);
            }

            return result;
        }

        private void PopulateAggregateAnalysisResult(
            ExcelInterop.Range range,
            string function,
            ExcelAnalyzeRangeResult result)
        {
            var value = ExecuteNativeAggregate(range, function);
            result.ValueText = ConvertAnalysisValueToText(value);
            result.ValueType = ClassifyAnalysisValue(value);
        }

        private void PopulateLookupByExtremeAnalysisResult(
            ExcelInterop.Range valueRange,
            ExcelInterop.Range returnRange,
            string function,
            ExcelAnalyzeRangeResult result,
            out ExcelInterop.Range matchedValueCell,
            out ExcelInterop.Range matchedReturnRange)
        {
            matchedValueCell = null;
            matchedReturnRange = null;

            if (!string.Equals(function, "max", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(function, "min", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("lookup_by_extreme 仅支持 max 或 min。");
            }

            if (!IsOneDimensionalRange(valueRange))
            {
                throw new InvalidOperationException("lookup_by_extreme 仅支持单行或单列的分析区域。");
            }

            if (returnRange != null && !IsCompatibleReturnRange(valueRange, returnRange))
            {
                throw new InvalidOperationException("returnAddress 必须与分析区域按行或按列对齐。");
            }

            var value = ExecuteNativeAggregate(valueRange, function);
            var matchIndex = ResolveAnalyzeMatchIndex(valueRange, value);
            if (matchIndex <= 0)
            {
                throw new InvalidOperationException("未能定位极值对应的位置。");
            }

            matchedValueCell = GetMatchedCell(valueRange, matchIndex);
            result.ValueText = matchedValueCell == null
                ? ConvertAnalysisValueToText(value)
                : GetCellDisplayText(matchedValueCell);
            result.ValueType = ClassifyAnalysisValue(value);
            result.MatchIndex = matchIndex;
            result.MatchAddress = GetAddress(matchedValueCell);

            if (returnRange != null)
            {
                matchedReturnRange = GetMatchedReturnRange(valueRange, returnRange, matchIndex);
                result.MatchedReturnAddress = GetAddress(matchedReturnRange);
                result.MatchedReturnValues = BuildMatchedReturnValues(matchedReturnRange);
            }
        }

        private object ExecuteNativeAggregate(ExcelInterop.Range range, string function)
        {
            if (_application == null)
            {
                throw new InvalidOperationException("当前未连接 Excel 实例。");
            }

            object worksheetFunctionObject = null;

            try
            {
                worksheetFunctionObject = _application.WorksheetFunction;
                dynamic worksheetFunction = worksheetFunctionObject;

                switch (function)
                {
                    case "max":
                        return worksheetFunction.Max(range);
                    case "min":
                        return worksheetFunction.Min(range);
                    case "average":
                        return worksheetFunction.Average(range);
                    case "sum":
                        return worksheetFunction.Sum(range);
                    case "count":
                        return worksheetFunction.Count(range);
                    case "counta":
                        return worksheetFunction.CountA(range);
                    default:
                        throw new InvalidOperationException("不支持的分析函数：" + function);
                }
            }
            finally
            {
                ReleaseComObject(worksheetFunctionObject);
            }
        }

        private int ResolveAnalyzeMatchIndex(ExcelInterop.Range range, object value)
        {
            var matchedIndex = TryExecuteNativeMatch(range, value);
            if (matchedIndex > 0)
            {
                return matchedIndex;
            }

            return FindRelativeIndexInRange(range, value);
        }

        private int TryExecuteNativeMatch(ExcelInterop.Range range, object value)
        {
            if (_application == null)
            {
                return 0;
            }

            object worksheetFunctionObject = null;

            try
            {
                worksheetFunctionObject = _application.WorksheetFunction;
                dynamic worksheetFunction = worksheetFunctionObject;
                var matchedIndex = worksheetFunction.Match(value, range, 0);
                return Convert.ToInt32(matchedIndex, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
            finally
            {
                ReleaseComObject(worksheetFunctionObject);
            }
        }

        private static int FindRelativeIndexInRange(ExcelInterop.Range range, object value)
        {
            if (range == null)
            {
                return 0;
            }

            var rowCount = Convert.ToInt32(range.Rows.Count);
            var columnCount = Convert.ToInt32(range.Columns.Count);

            if (rowCount == 1)
            {
                for (var column = 1; column <= columnCount; column++)
                {
                    var cell = range.Cells[1, column] as ExcelInterop.Range;

                    try
                    {
                        if (AreEquivalentAnalysisValues(cell == null ? null : cell.Value2, value))
                        {
                            return column;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(cell);
                    }
                }

                return 0;
            }

            for (var row = 1; row <= rowCount; row++)
            {
                var cell = range.Cells[row, 1] as ExcelInterop.Range;

                try
                {
                    if (AreEquivalentAnalysisValues(cell == null ? null : cell.Value2, value))
                    {
                        return row;
                    }
                }
                finally
                {
                    ReleaseComObject(cell);
                }
            }

            return 0;
        }

        private static bool AreEquivalentAnalysisValues(object left, object right)
        {
            if (left == null && right == null)
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            double leftNumber;
            double rightNumber;
            if (TryConvertToDouble(left, out leftNumber)
                && TryConvertToDouble(right, out rightNumber))
            {
                return Math.Abs(leftNumber - rightNumber) < 0.0000001d;
            }

            var leftText = Convert.ToString(left, CultureInfo.InvariantCulture) ?? string.Empty;
            var rightText = Convert.ToString(right, CultureInfo.InvariantCulture) ?? string.Empty;
            return string.Equals(leftText, rightText, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryConvertToDouble(object value, out double number)
        {
            if (value is double)
            {
                number = (double)value;
                return true;
            }

            if (value is float)
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (value is decimal)
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (value is int || value is long || value is short || value is byte)
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }

            return double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out number);
        }

        private static string ConvertAnalysisValueToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var formattable = value as IFormattable;
            return formattable != null
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        private static string ClassifyAnalysisValue(object value)
        {
            if (value == null)
            {
                return "blank";
            }

            if (value is bool)
            {
                return "boolean";
            }

            if (value is DateTime)
            {
                return "date";
            }

            double number;
            return TryConvertToDouble(value, out number) ? "number" : "text";
        }

        private static bool IsOneDimensionalRange(ExcelInterop.Range range)
        {
            if (range == null)
            {
                return false;
            }

            return Convert.ToInt32(range.Rows.Count) == 1
                || Convert.ToInt32(range.Columns.Count) == 1;
        }

        private static bool IsCompatibleReturnRange(ExcelInterop.Range valueRange, ExcelInterop.Range returnRange)
        {
            if (valueRange == null || returnRange == null)
            {
                return false;
            }

            var valueRowCount = Convert.ToInt32(valueRange.Rows.Count);
            var valueColumnCount = Convert.ToInt32(valueRange.Columns.Count);
            var returnRowCount = Convert.ToInt32(returnRange.Rows.Count);
            var returnColumnCount = Convert.ToInt32(returnRange.Columns.Count);

            if (valueRowCount == 1)
            {
                return returnColumnCount == valueColumnCount;
            }

            return returnRowCount == valueRowCount;
        }

        private static ExcelInterop.Range GetMatchedCell(ExcelInterop.Range range, int matchIndex)
        {
            if (range == null || matchIndex <= 0)
            {
                return null;
            }

            return Convert.ToInt32(range.Rows.Count) == 1
                ? range.Cells[1, matchIndex] as ExcelInterop.Range
                : range.Cells[matchIndex, 1] as ExcelInterop.Range;
        }

        private static ExcelInterop.Range GetMatchedReturnRange(
            ExcelInterop.Range valueRange,
            ExcelInterop.Range returnRange,
            int matchIndex)
        {
            if (valueRange == null || returnRange == null || matchIndex <= 0)
            {
                return null;
            }

            return Convert.ToInt32(valueRange.Rows.Count) == 1
                ? returnRange.Columns[matchIndex] as ExcelInterop.Range
                : returnRange.Rows[matchIndex] as ExcelInterop.Range;
        }

        private static List<string> BuildMatchedReturnValues(ExcelInterop.Range range)
        {
            var values = new List<string>();
            if (range == null)
            {
                return values;
            }

            var rowCount = Convert.ToInt32(range.Rows.Count);
            var columnCount = Convert.ToInt32(range.Columns.Count);

            if (rowCount == 1)
            {
                for (var column = 1; column <= columnCount; column++)
                {
                    var cell = range.Cells[1, column] as ExcelInterop.Range;

                    try
                    {
                        values.Add(GetCellDisplayText(cell));
                    }
                    finally
                    {
                        ReleaseComObject(cell);
                    }
                }

                return values;
            }

            for (var row = 1; row <= rowCount; row++)
            {
                var cell = range.Cells[row, 1] as ExcelInterop.Range;

                try
                {
                    values.Add(GetCellDisplayText(cell));
                }
                finally
                {
                    ReleaseComObject(cell);
                }
            }

            return values;
        }

        private static string GetCellDisplayText(ExcelInterop.Range cell)
        {
            return cell == null ? string.Empty : (Convert.ToString(cell.Text) ?? string.Empty);
        }

        private static string NormalizeAnalyzeOperation(string operation)
        {
            var normalized = string.IsNullOrWhiteSpace(operation)
                ? "aggregate"
                : operation.Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "aggregate":
                case "lookup_by_extreme":
                    return normalized;
                case "lookup":
                    return "lookup_by_extreme";
                default:
                    return normalized;
            }
        }

        private static string NormalizeAnalyzeFunction(string function)
        {
            var normalized = string.IsNullOrWhiteSpace(function)
                ? "max"
                : function.Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "avg":
                    return "average";
                default:
                    return normalized;
            }
        }

        private ExcelInterop.Range ResolveFindRange(ExcelInterop.Worksheet worksheet, ExcelRangeFindRequest request)
        {
            if (request.UseSelectionWhenAddressEmpty && string.IsNullOrWhiteSpace(request.Address))
            {
                var selection = _application != null ? _application.Selection as ExcelInterop.Range : null;
                if (selection != null)
                {
                    return selection;
                }
            }

            if (worksheet == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.Address))
            {
                return null;
            }

            try
            {
                return worksheet.Range[request.Address.Trim()] as ExcelInterop.Range;
            }
            catch
            {
                throw new InvalidOperationException("区域地址无效：" + request.Address);
            }
        }

        private ExcelInterop.Range ResolveAnalyzeRange(ExcelInterop.Worksheet worksheet, ExcelAnalyzeRangeRequest request)
        {
            if (request != null
                && request.UseSelectionWhenAddressEmpty
                && string.IsNullOrWhiteSpace(request.Address))
            {
                var selection = _application != null ? _application.Selection as ExcelInterop.Range : null;
                if (selection != null)
                {
                    return selection;
                }
            }

            if (worksheet == null)
            {
                return null;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Address))
            {
                return null;
            }

            try
            {
                return worksheet.Range[request.Address.Trim()] as ExcelInterop.Range;
            }
            catch
            {
                throw new InvalidOperationException("区域地址无效：" + request.Address);
            }
        }

        private static ExcelInterop.Range ResolveAnalyzeReturnRange(ExcelInterop.Worksheet worksheet, string address)
        {
            if (worksheet == null || string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            try
            {
                return worksheet.Range[address.Trim()] as ExcelInterop.Range;
            }
            catch
            {
                throw new InvalidOperationException("returnAddress 无效：" + address);
            }
        }

        private ExcelInterop.Range ResolveValidationRange(ExcelInterop.Worksheet worksheet, ExcelRangeFormulaValidationRequest request)
        {
            if (request.UseSelectionWhenAddressEmpty && string.IsNullOrWhiteSpace(request.Address))
            {
                var selection = _application != null ? _application.Selection as ExcelInterop.Range : null;
                if (selection != null)
                {
                    return selection;
                }
            }

            if (worksheet == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.Address))
            {
                return null;
            }

            try
            {
                return worksheet.Range[request.Address.Trim()] as ExcelInterop.Range;
            }
            catch
            {
                throw new InvalidOperationException("区域地址无效：" + request.Address);
            }
        }

        private static bool IsExcelFormulaError(string displayText)
        {
            if (string.IsNullOrWhiteSpace(displayText))
            {
                return false;
            }

            return displayText.StartsWith("#", StringComparison.Ordinal);
        }

        private static string ClassifyFormulaError(string displayText)
        {
            var text = displayText ?? string.Empty;

            if (text.IndexOf("#NAME?", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "undefined-function";
            }

            if (text.IndexOf("#REF!", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "invalid-reference";
            }

            if (text.IndexOf("#DIV/0!", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "division-by-zero";
            }

            if (text.IndexOf("#VALUE!", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "type-mismatch";
            }

            if (text.IndexOf("#N/A", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "lookup-failed";
            }

            if (text.IndexOf("#NUM!", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "numeric-error";
            }

            if (text.IndexOf("#NULL!", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "null-intersection";
            }

            return "excel-error";
        }

        private static string BuildFormulaSuggestion(string displayText)
        {
            var category = ClassifyFormulaError(displayText);

            switch (category)
            {
                case "undefined-function":
                    return "检查函数名拼写、区域名称或加载项函数是否可用。";
                case "invalid-reference":
                    return "检查被引用单元格、工作表或工作簿是否已删除或移动。";
                case "division-by-zero":
                    return "检查分母是否可能为 0，必要时增加保护条件。";
                case "type-mismatch":
                    return "检查参与计算的单元格类型是否一致。";
                case "lookup-failed":
                    return "检查查找键、查找区域和匹配模式。";
                case "numeric-error":
                    return "检查数值范围、参数合法性和幂运算。";
                default:
                    return "请在 Excel 中检查公式依赖项和中间结果。";
            }
        }

        private static bool ContainsVolatileFunction(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula))
            {
                return false;
            }

            var normalized = formula.ToUpperInvariant();
            return normalized.IndexOf("NOW(", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("TODAY(", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("RAND(", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("RANDBETWEEN(", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("OFFSET(", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("INDIRECT(", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("CELL(", StringComparison.Ordinal) >= 0
                || normalized.IndexOf("INFO(", StringComparison.Ordinal) >= 0;
        }
    }
}
