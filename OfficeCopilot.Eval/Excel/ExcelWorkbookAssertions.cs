using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Eval.Excel
{
    internal static class ExcelWorkbookAssertions
    {
        private static readonly Regex _singleColumnRange = new Regex(
            @"^\$?(?<column>[A-Za-z]{1,3})\$?(?<first>\d+):\$?(?<endColumn>[A-Za-z]{1,3})\$?(?<last>\d+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IList<ExcelEvalCheck> Evaluate(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> answerRoot,
            IList assertions)
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook));
            }

            var checks = new List<ExcelEvalCheck>();
            foreach (var value in assertions)
            {
                var assertion = value as IDictionary<string, object>;
                if (assertion == null)
                {
                    throw new InvalidDataException("Workbook assertions must be objects.");
                }

                var type = GetString(assertion, "type");
                var sheetName = GetString(assertion, "sheet");
                if (!WorksheetExists(workbook, sheetName))
                {
                    checks.Add(Failure("worksheet-exists", sheetName, "not found"));
                    continue;
                }

                switch (type)
                {
                    case "formulaRange":
                        checks.Add(CheckFormulaRange(workbook, assertion));
                        break;
                    case "rangeSumEquals":
                        checks.Add(CheckRangeSum(workbook, answerRoot, assertion));
                        break;
                    case "columnSumEquals":
                        checks.Add(CheckColumnSum(workbook, answerRoot, assertion));
                        break;
                    case "rangeValuesEqual":
                        checks.Add(CheckRangeValues(workbook, answerRoot, assertion));
                        break;
                    case "sheetRowCount":
                        checks.Add(CheckSheetRowCount(workbook, assertion));
                        break;
                    case "valueCount":
                        checks.Add(CheckValueCount(workbook, answerRoot, assertion));
                        break;
                    case "keySetEquals":
                        checks.Add(CheckKeySet(workbook, answerRoot, assertion));
                        break;
                    case "numberFormat":
                        checks.Add(CheckNumberFormat(workbook, assertion));
                        break;
                    case "numericCellsNumberFormat":
                        checks.Add(CheckNumericCellsNumberFormat(workbook, assertion));
                        break;
                    case "errorCellsRetained":
                        checks.Add(CheckErrorCellsRetained(workbook, answerRoot, assertion));
                        break;
                    case "boldRange":
                        checks.Add(CheckBoldRange(workbook, assertion));
                        break;
                    default:
                        throw new InvalidDataException("Unsupported workbook assertion type: " + type);
                }
            }

            return checks;
        }

        public static string ComputeCellValuesFingerprint(ExcelWorkbookSession workbook)
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook));
            }

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
            using (var sha256 = SHA256.Create())
            {
                dynamic workbookObject = workbook.WorkbookObject;
                dynamic worksheets = workbookObject.Worksheets;
                var worksheetCount = Convert.ToInt32(worksheets.Count, CultureInfo.InvariantCulture);
                writer.Write(worksheetCount);
                for (var index = 1; index <= worksheetCount; index++)
                {
                    object worksheetObject = null;
                    object usedRangeObject = null;
                    try
                    {
                        worksheetObject = worksheets.Item[index];
                        dynamic worksheet = worksheetObject;
                        dynamic usedRange = worksheet.UsedRange;
                        usedRangeObject = usedRange;
                        writer.Write(Convert.ToString(worksheet.Name, CultureInfo.InvariantCulture) ?? string.Empty);
                        WriteComValues(writer, usedRange.Value2);
                    }
                    finally
                    {
                        ReleaseComObject(usedRangeObject);
                        ReleaseComObject(worksheetObject);
                    }
                }

                writer.Flush();
                return BitConverter.ToString(sha256.ComputeHash(stream.ToArray())).Replace("-", string.Empty);
            }
        }

        private static ExcelEvalCheck CheckFormulaRange(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> assertion)
        {
            var sheetName = GetString(assertion, "sheet");
            var address = GetString(assertion, "range");
            var formulaTemplates = GetFormulaTemplates(assertion);
            var match = _singleColumnRange.Match(address);
            if (!match.Success
                || !string.Equals(match.Groups["column"].Value, match.Groups["endColumn"].Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("formulaRange requires a single-column range: " + address);
            }

            var firstRow = int.Parse(match.Groups["first"].Value, CultureInfo.InvariantCulture);
            var lastRow = int.Parse(match.Groups["last"].Value, CultureInfo.InvariantCulture);
            var formulas = GetRangeProperty(workbook, sheetName, address, "Formula");
            var actualFormulas = ToValues(formulas).ToList();
            if (actualFormulas.Count != lastRow - firstRow + 1)
            {
                return Failure("formula-range", string.Join(" OR ", formulaTemplates), "unexpected cell count");
            }

            for (var index = 0; index < actualFormulas.Count; index++)
            {
                var row = (firstRow + index).ToString(CultureInfo.InvariantCulture);
                var expected = formulaTemplates.Select(template => template.Replace("{row}", row));
                var actual = NormalizeFormula(
                    Convert.ToString(actualFormulas[index], CultureInfo.InvariantCulture));
                if (!expected.Contains(actual, StringComparer.OrdinalIgnoreCase))
                {
                    return Failure(
                        "formula-range",
                        string.Join(" OR ", expected),
                        actual);
                }
            }

            return Pass("formula-range", string.Join(" OR ", formulaTemplates), address);
        }

        private static IList<string> GetFormulaTemplates(IDictionary<string, object> assertion)
        {
            object alternatives;
            if (assertion.TryGetValue("formulaTemplatesAny", out alternatives))
            {
                var templates = (alternatives as IList)
                    ?.Cast<object>()
                    .Select(value => NormalizeFormula(Convert.ToString(value, CultureInfo.InvariantCulture)))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
                if (templates == null || templates.Count == 0)
                {
                    throw new InvalidDataException(
                        "formulaRange formulaTemplatesAny must contain at least one formula template.");
                }

                return templates;
            }

            return new[] { NormalizeFormula(GetString(assertion, "formulaTemplate")) };
        }

        private static bool WorksheetExists(ExcelWorkbookSession workbook, string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                throw new InvalidDataException("Workbook assertion must specify a worksheet name.");
            }

            object worksheetsObject = null;
            object worksheetObject = null;
            try
            {
                dynamic workbookObject = workbook.WorkbookObject;
                dynamic worksheets = workbookObject.Worksheets;
                worksheetsObject = worksheets;
                worksheetObject = worksheets.Item[sheetName];
                return worksheetObject != null;
            }
            catch (COMException exception)
            {
                if (exception.ErrorCode == unchecked((int)0x8002000B))
                {
                    return false;
                }

                throw;
            }
            finally
            {
                ReleaseComObject(worksheetObject);
                ReleaseComObject(worksheetsObject);
            }
        }

        private static ExcelEvalCheck CheckRangeSum(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> answerRoot,
            IDictionary<string, object> assertion)
        {
            var actual = SumValues(GetRangeValues(
                workbook,
                GetString(assertion, "sheet"),
                GetString(assertion, "range")));
            var expected = SumExpected(answerRoot, GetString(assertion, "answer"));
            return CompareNumbers("range-sum", expected, actual, GetTolerance(assertion));
        }

        private static ExcelEvalCheck CheckColumnSum(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> answerRoot,
            IDictionary<string, object> assertion)
        {
            var sheetName = GetString(assertion, "sheet");
            var column = GetString(assertion, "column");
            var firstDataRow = GetInt(assertion, "firstDataRow");
            var lastRow = GetLastUsedRow(workbook, sheetName);
            var actual = SumValues(GetRangeValues(
                workbook,
                sheetName,
                column + firstDataRow.ToString(CultureInfo.InvariantCulture)
                    + ":" + column + lastRow.ToString(CultureInfo.InvariantCulture)));
            var expected = SumExpected(answerRoot, GetString(assertion, "answer"));
            return CompareNumbers("column-sum", expected, actual, GetTolerance(assertion));
        }

        private static ExcelEvalCheck CheckRangeValues(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> answerRoot,
            IDictionary<string, object> assertion)
        {
            var expected = ResolveAnswerValues(answerRoot, assertion);
            var actual = GetRangeValues(
                workbook,
                GetString(assertion, "sheet"),
                GetString(assertion, "range"));
            if (actual.Count != expected.Count)
            {
                return Failure(
                    "range-values",
                    expected.Count.ToString(CultureInfo.InvariantCulture) + " values",
                    actual.Count.ToString(CultureInfo.InvariantCulture) + " values");
            }

            var tolerance = GetTolerance(assertion);
            for (var index = 0; index < expected.Count; index++)
            {
                if (!ValuesEqual(expected[index], actual[index], tolerance))
                {
                    return Failure(
                        "range-values",
                        Describe(expected[index]),
                        Describe(actual[index]));
                }
            }

            return Pass("range-values", string.Join(", ", expected.Select(Describe)), "matched");
        }

        private static ExcelEvalCheck CheckSheetRowCount(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> assertion)
        {
            var actual = GetLastUsedRow(workbook, GetString(assertion, "sheet"));
            var expected = GetInt(assertion, "rowCount");
            return actual == expected
                ? Pass("sheet-row-count", expected.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture))
                : Failure("sheet-row-count", expected.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture));
        }

        private static ExcelEvalCheck CheckValueCount(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> answerRoot,
            IDictionary<string, object> assertion)
        {
            var sheetName = GetString(assertion, "sheet");
            var values = GetRangeValues(workbook, sheetName, GetString(assertion, "range"));
            var expectedValue = GetRequired(assertion, "value");
            var expectedCount = Convert.ToInt32(
                ResolveAnswerValues(answerRoot, assertion, "countAnswer").Single(),
                CultureInfo.InvariantCulture);
            var actualCount = values.Count(value => ValuesEqual(expectedValue, value, 0d));
            return actualCount == expectedCount
                ? Pass("value-count", expectedCount.ToString(CultureInfo.InvariantCulture), actualCount.ToString(CultureInfo.InvariantCulture))
                : Failure("value-count", expectedCount.ToString(CultureInfo.InvariantCulture), actualCount.ToString(CultureInfo.InvariantCulture));
        }

        private static ExcelEvalCheck CheckKeySet(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> answerRoot,
            IDictionary<string, object> assertion)
        {
            var sheetName = GetString(assertion, "sheet");
            var column = GetString(assertion, "column");
            var lastRow = GetLastUsedRow(workbook, sheetName);
            var actual = GetRangeValues(
                workbook,
                sheetName,
                column + "2:" + column + lastRow.ToString(CultureInfo.InvariantCulture))
                .Where(value => value != null && !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
                .Select(CanonicalKey)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var expected = ResolveAnswerValues(answerRoot, assertion)
                .Select(CanonicalKey)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var matches = actual.SequenceEqual(expected, StringComparer.Ordinal);
            return matches
                ? Pass("key-set", expected.Length.ToString(CultureInfo.InvariantCulture) + " keys", "matched")
                : Failure(
                    "key-set",
                    string.Join(", ", expected.Take(8)),
                    string.Join(", ", actual.Take(8)));
        }

        private static ExcelEvalCheck CheckNumberFormat(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> assertion)
        {
            var expected = GetString(assertion, "format");
            var actual = Convert.ToString(
                GetRangeProperty(
                    workbook,
                    GetString(assertion, "sheet"),
                    GetString(assertion, "range"),
                    "NumberFormatLocal"),
                CultureInfo.InvariantCulture);
            var matches = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)
                || IsExcelEscapedCurrencyFormat(expected, actual);
            return matches
                ? Pass("number-format", expected, actual)
                : Failure("number-format", expected, actual ?? "(mixed formats)");
        }

        private static ExcelEvalCheck CheckNumericCellsNumberFormat(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> assertion)
        {
            object worksheetObject = null;
            object rangeObject = null;
            object numericRangeObject = null;
            object cellsObject = null;
            try
            {
                dynamic workbookObject = workbook.WorkbookObject;
                dynamic worksheets = workbookObject.Worksheets;
                worksheetObject = worksheets.Item[GetString(assertion, "sheet")];
                dynamic worksheet = worksheetObject;
                dynamic range = worksheet.Range[GetString(assertion, "range")];
                rangeObject = range;
                dynamic numericRange = range.SpecialCells(2, 1);
                numericRangeObject = numericRange;
                dynamic cells = numericRange.Cells;
                cellsObject = cells;
                var numericCount = Convert.ToInt32(
                    cells.Count,
                    CultureInfo.InvariantCulture);
                var expected = GetString(assertion, "format");
                var actual = Convert.ToString(
                    numericRange.NumberFormatLocal,
                    CultureInfo.InvariantCulture);
                var matches = numericCount > 0
                    && (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)
                        || IsExcelEscapedCurrencyFormat(expected, actual));
                return matches
                    ? Pass(
                        "numeric-cells-number-format",
                        expected,
                        actual + " (" + numericCount.ToString(CultureInfo.InvariantCulture) + " numeric cells)")
                    : Failure(
                        "numeric-cells-number-format",
                        expected,
                        (actual ?? "(mixed formats)")
                        + " (" + numericCount.ToString(CultureInfo.InvariantCulture) + " numeric cells)");
            }
            finally
            {
                ReleaseComObject(cellsObject);
                ReleaseComObject(numericRangeObject);
                ReleaseComObject(rangeObject);
                ReleaseComObject(worksheetObject);
            }
        }

        private static bool IsExcelEscapedCurrencyFormat(string expected, string actual)
        {
            return !string.IsNullOrEmpty(expected)
                && "$¥€£".IndexOf(expected[0]) >= 0
                && string.Equals("!" + expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        private static ExcelEvalCheck CheckErrorCellsRetained(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> answerRoot,
            IDictionary<string, object> assertion)
        {
            var expected = Convert.ToInt32(
                ResolveAnswerValues(answerRoot, assertion, "countAnswer").Single(),
                CultureInfo.InvariantCulture);
            var actual = GetRangeValues(
                    workbook,
                    GetString(assertion, "sheet"),
                    GetString(assertion, "range"))
                .Count(IsErrorValue);
            return actual == expected
                ? Pass("error-cells-retained", expected.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture))
                : Failure("error-cells-retained", expected.ToString(CultureInfo.InvariantCulture), actual.ToString(CultureInfo.InvariantCulture));
        }

        private static ExcelEvalCheck CheckBoldRange(
            ExcelWorkbookSession workbook,
            IDictionary<string, object> assertion)
        {
            var value = GetRangeProperty(
                workbook,
                GetString(assertion, "sheet"),
                GetString(assertion, "range"),
                "Font.Bold");
            var isBold = value is bool
                ? (bool)value
                : Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
            return isBold
                ? Pass("bold-range", "bold", "bold")
                : Failure("bold-range", "bold", Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static IList<object> ResolveAnswerValues(
            IDictionary<string, object> answerRoot,
            IDictionary<string, object> assertion,
            string referenceName = "answer")
        {
            var reference = GetString(assertion, referenceName);
            return ExcelEvalTaskSetLoader.ResolveAnswerPath(answerRoot, reference)
                .SelectMany(value => value is IList && !(value is string)
                    ? ((IList)value).Cast<object>()
                    : new[] { value })
                .ToList();
        }

        private static decimal SumExpected(
            IDictionary<string, object> answerRoot,
            string answerPath)
        {
            decimal sum = 0m;
            foreach (var value in ExcelEvalTaskSetLoader.ResolveAnswerPath(answerRoot, answerPath))
            {
                sum += Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }

            return sum;
        }

        private static double SumValues(IEnumerable<object> values)
        {
            double sum = 0d;
            foreach (var value in values)
            {
                if (value == null || value is string && string.IsNullOrWhiteSpace((string)value))
                {
                    continue;
                }

                if (IsErrorValue(value))
                {
                    throw new InvalidDataException("A sum assertion range contains an Excel error value.");
                }

                double number;
                if (!double.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out number))
                {
                    continue;
                }

                sum += number;
            }

            return sum;
        }

        private static bool ValuesEqual(object expected, object actual, double tolerance)
        {
            if (expected == null || actual == null)
            {
                return expected == null && actual == null;
            }

            double expectedNumber;
            double actualNumber;
            if (TryDouble(expected, out expectedNumber) && TryDouble(actual, out actualNumber))
            {
                return Math.Abs(expectedNumber - actualNumber) <= tolerance;
            }

            return string.Equals(
                Convert.ToString(expected, CultureInfo.InvariantCulture),
                Convert.ToString(actual, CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        private static bool TryDouble(object value, out double number)
        {
            if (value is bool || value is DateTime)
            {
                number = 0d;
                return false;
            }

            return double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out number);
        }

        private static bool IsErrorValue(object value)
        {
            if (value is ErrorWrapper)
            {
                return true;
            }

            if (value is int)
            {
                return IsExcelErrorHResult((int)value);
            }

            if (value is long && (long)value >= int.MinValue && (long)value <= int.MaxValue)
            {
                return IsExcelErrorHResult((int)(long)value);
            }

            var text = value as string;
            return text != null
                && (text.StartsWith("#REF!", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("#N/A", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("#VALUE!", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("#DIV/0!", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("#NAME?", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("#NUM!", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("#NULL!", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsExcelErrorHResult(int value)
        {
            switch (value)
            {
                case unchecked((int)0x800A07D0):
                case unchecked((int)0x800A07E3):
                case unchecked((int)0x800A07E7):
                case unchecked((int)0x800A07E8):
                case unchecked((int)0x800A07EC):
                case unchecked((int)0x800A07ED):
                case unchecked((int)0x800A07FA):
                    return true;
                default:
                    return false;
            }
        }

        private static string CanonicalKey(object value)
        {
            return (value == null ? string.Empty : value.GetType().FullName)
                + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string NormalizeFormula(string formula)
        {
            if (formula == null)
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(formula, @"\s+", string.Empty);
            return normalized.StartsWith("=", StringComparison.Ordinal)
                ? normalized.Substring(1)
                : normalized;
        }

        private static IList<object> GetRangeValues(
            ExcelWorkbookSession workbook,
            string sheetName,
            string address)
        {
            return ToValues(GetRangeProperty(workbook, sheetName, address, "Value2"));
        }

        private static object GetRangeProperty(
            ExcelWorkbookSession workbook,
            string sheetName,
            string address,
            string property)
        {
            object worksheetObject = null;
            object rangeObject = null;
            try
            {
                dynamic workbookObjectValue = workbook.WorkbookObject;
                dynamic worksheets = workbookObjectValue.Worksheets;
                worksheetObject = worksheets.Item[sheetName];
                dynamic worksheet = worksheetObject;
                dynamic range = worksheet.Range[address];
                rangeObject = range;
                if (property == "Font.Bold")
                {
                    dynamic font = range.Font;
                    return font.Bold;
                }

                return property == "Value2"
                    ? range.Value2
                    : property == "Formula"
                        ? range.Formula
                        : property == "NumberFormatLocal"
                            ? range.NumberFormatLocal
                            : throw new InvalidDataException("Unsupported range property: " + property);
            }
            finally
            {
                ReleaseComObject(rangeObject);
                ReleaseComObject(worksheetObject);
            }
        }

        private static int GetLastUsedRow(ExcelWorkbookSession workbook, string sheetName)
        {
            object worksheetObject = null;
            object usedRangeObject = null;
            try
            {
                dynamic workbookObject = workbook.WorkbookObject;
                dynamic worksheets = workbookObject.Worksheets;
                worksheetObject = worksheets.Item[sheetName];
                dynamic worksheet = worksheetObject;
                dynamic usedRange = worksheet.UsedRange;
                usedRangeObject = usedRange;
                var firstRow = Convert.ToInt32(usedRange.Row, CultureInfo.InvariantCulture);
                var rowCount = Convert.ToInt32(usedRange.Rows.Count, CultureInfo.InvariantCulture);
                return firstRow + rowCount - 1;
            }
            finally
            {
                ReleaseComObject(usedRangeObject);
                ReleaseComObject(worksheetObject);
            }
        }

        private static IList<object> ToValues(object value)
        {
            if (value is Array)
            {
                return ((Array)value).Cast<object>().ToList();
            }

            return new List<object> { value };
        }

        private static void WriteComValues(BinaryWriter writer, object value)
        {
            if (value is Array)
            {
                var array = (Array)value;
                writer.Write(array.Rank);
                for (var dimension = 0; dimension < array.Rank; dimension++)
                {
                    writer.Write(array.GetLength(dimension));
                }

                foreach (var item in array)
                {
                    WriteValue(writer, item);
                }

                return;
            }

            writer.Write(0);
            WriteValue(writer, value);
        }

        private static void WriteValue(BinaryWriter writer, object value)
        {
            writer.Write(value == null ? string.Empty : value.GetType().FullName);
            writer.Write(value == null
                ? string.Empty
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        private static string GetString(IDictionary<string, object> dictionary, string key)
        {
            return Convert.ToString(GetRequired(dictionary, key), CultureInfo.InvariantCulture);
        }

        private static int GetInt(IDictionary<string, object> dictionary, string key)
        {
            return Convert.ToInt32(GetRequired(dictionary, key), CultureInfo.InvariantCulture);
        }

        private static double GetTolerance(IDictionary<string, object> dictionary)
        {
            object value;
            return dictionary.TryGetValue("tolerance", out value)
                ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
                : 0d;
        }

        private static object GetRequired(IDictionary<string, object> dictionary, string key)
        {
            object value;
            if (!dictionary.TryGetValue(key, out value))
            {
                throw new InvalidDataException("Workbook assertion is missing '" + key + "'.");
            }

            return value;
        }

        private static ExcelEvalCheck CompareNumbers(
            string name,
            decimal expected,
            double actual,
            double tolerance)
        {
            return Math.Abs((double)expected - actual) <= tolerance
                ? Pass(name, expected.ToString(CultureInfo.InvariantCulture), actual.ToString("R", CultureInfo.InvariantCulture))
                : Failure(name, expected.ToString(CultureInfo.InvariantCulture), actual.ToString("R", CultureInfo.InvariantCulture));
        }

        private static ExcelEvalCheck Pass(string name, string expected, string actual)
        {
            return new ExcelEvalCheck
            {
                Name = name,
                Passed = true,
                Expected = expected,
                Actual = actual
            };
        }

        private static ExcelEvalCheck Failure(string name, string expected, string actual)
        {
            return new ExcelEvalCheck
            {
                Name = name,
                Passed = false,
                Expected = expected,
                Actual = actual
            };
        }

        private static string Describe(object value)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "(blank)";
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
    }
}
