using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Analysis
{
    /// <summary>
    /// 一块二维表数据的内存快照。由 COM 侧一次性 <c>Range.Value2</c> 取数后构造，
    /// 之后的画像、分组聚合、对账全部在本快照上完成，不再回到 COM。
    /// 本类型及 <see cref="ExcelAggregationEngine"/> 不引用 Interop，可独立单测。
    /// </summary>
    public sealed class ExcelTabularSnapshot
    {
        public ExcelTabularSnapshot(string[] headers, IList<object[]> rows, bool[] columnIsDateFormatted)
        {
            Headers = headers ?? new string[0];
            Rows = rows ?? new List<object[]>();
            ColumnIsDateFormatted = columnIsDateFormatted ?? new bool[Headers.Length];
        }

        public string[] Headers { get; private set; }

        /// <summary>原始 Value2 值，行主序。长度不足的行按空处理。</summary>
        public IList<object[]> Rows { get; private set; }

        /// <summary>按列的数字格式是否为日期/时间格式。用于把 OADate double 还原成日期文本。</summary>
        public bool[] ColumnIsDateFormatted { get; private set; }

        public int RowCount
        {
            get { return Rows.Count; }
        }

        public int ColumnCount
        {
            get { return Headers.Length; }
        }

        public object GetValue(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= Rows.Count || columnIndex < 0)
            {
                return null;
            }

            var row = Rows[rowIndex];
            return row == null || columnIndex >= row.Length ? null : row[columnIndex];
        }

        /// <summary>按列名查列序号，不区分大小写；找不到返回 -1。也接受 "A"/"B" 形式的列字母。</summary>
        public int IndexOfColumn(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return -1;
            }

            var name = columnName.Trim();
            for (var index = 0; index < Headers.Length; index++)
            {
                if (string.Equals(Headers[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            var letterIndex = TryParseColumnLetter(name);
            return letterIndex >= 0 && letterIndex < Headers.Length ? letterIndex : -1;
        }

        private static int TryParseColumnLetter(string value)
        {
            var result = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var c = char.ToUpperInvariant(value[i]);
                if (c < 'A' || c > 'Z')
                {
                    return -1;
                }

                result = (result * 26) + (c - 'A' + 1);
            }

            return result - 1;
        }

        public static string BuildDefaultHeader(int columnIndex)
        {
            var builder = new StringBuilder();
            var value = columnIndex + 1;
            while (value > 0)
            {
                var remainder = (value - 1) % 26;
                builder.Insert(0, (char)('A' + remainder));
                value = (value - 1) / 26;
            }

            return builder.ToString();
        }
    }

    /// <summary>Value2 原始值的类型判定与文本化。</summary>
    public static class ExcelValueSemantics
    {
        public static bool IsEmpty(object value)
        {
            if (value == null || value is DBNull)
            {
                return true;
            }

            var text = value as string;
            return text != null && text.Length == 0;
        }

        /// <summary>Excel 错误值经 Value2 读出为 Int32 错误码。</summary>
        public static bool IsError(object value)
        {
            return value is int;
        }

        public static string DescribeError(object value)
        {
            switch (Convert.ToInt32(value)) // 已由 IsError 保证是 int
            {
                case -2146826281: return "#DIV/0!";
                case -2146826246: return "#N/A";
                case -2146826259: return "#NAME?";
                case -2146826288: return "#NULL!";
                case -2146826252: return "#NUM!";
                case -2146826265: return "#REF!";
                case -2146826273: return "#VALUE!";
                default: return "#ERROR!";
            }
        }

        public static bool TryGetNumber(object value, out double number)
        {
            number = 0d;
            if (value is double)
            {
                number = (double)value;
                return true;
            }

            if (value is float
                || value is decimal
                || value is long
                || value is short
                || value is sbyte
                || value is byte
                || value is uint
                || value is ulong
                || value is ushort)
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }

            var text = value as string;
            return text != null
                && double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out number)
                && !double.IsNaN(number)
                && !double.IsInfinity(number);
        }

        public static bool IsNativeNumber(object value)
        {
            return value is double
                || value is float
                || value is decimal
                || value is long
                || value is short
                || value is sbyte
                || value is byte
                || value is uint
                || value is ulong
                || value is ushort;
        }

        public static bool IsTextNumber(object value)
        {
            var text = value as string;
            if (text == null)
            {
                return false;
            }

            double number;
            return TryGetNumber(text, out number);
        }

        public static string ClassifyKind(object value, bool isDateFormatted)
        {
            if (IsEmpty(value))
            {
                return ExcelColumnValueKinds.Empty;
            }

            if (IsError(value))
            {
                return ExcelColumnValueKinds.Error;
            }

            if (value is bool)
            {
                return ExcelColumnValueKinds.Boolean;
            }

            if (value is double || value is float || value is decimal)
            {
                return isDateFormatted ? ExcelColumnValueKinds.Date : ExcelColumnValueKinds.Number;
            }

            return ExcelColumnValueKinds.Text;
        }

        /// <summary>把原始值转成用于展示与分组的稳定文本。</summary>
        public static string ToText(object value, bool isDateFormatted)
        {
            if (IsEmpty(value))
            {
                return string.Empty;
            }

            if (IsError(value))
            {
                return DescribeError(value);
            }

            if (value is bool)
            {
                return ((bool)value) ? "TRUE" : "FALSE";
            }

            if (value is DateTime)
            {
                return FormatDateTime((DateTime)value);
            }

            if (value is double || value is float || value is decimal)
            {
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (isDateFormatted && number > 0d && number < 2958466d)
                {
                    try
                    {
                        return FormatDateTime(DateTime.FromOADate(number));
                    }
                    catch (ArgumentException)
                    {
                        // 超出 OADate 范围时退回数值文本。
                    }
                }

                return FormatNumber(number);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return string.Empty;
            }

            var rounded = Math.Round(value, 10);
            return rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e15
                ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
                : rounded.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        private static string FormatDateTime(DateTime value)
        {
            return value.TimeOfDay == TimeSpan.Zero
                ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>判断单元格数字格式是否表示日期或时间。</summary>
        public static bool IsDateNumberFormat(string numberFormat)
        {
            if (string.IsNullOrWhiteSpace(numberFormat))
            {
                return false;
            }

            var stripped = StripLiterals(numberFormat);
            for (var i = 0; i < stripped.Length; i++)
            {
                var c = char.ToLowerInvariant(stripped[i]);
                if (c == 'y' || c == 'd' || c == 'h')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>去掉引号内文本、反斜杠转义字符与方括号段，避免把 "\d" 之类误判为日期。</summary>
        private static string StripLiterals(string format)
        {
            var builder = new StringBuilder(format.Length);
            var inQuote = false;
            var inBracket = false;

            for (var i = 0; i < format.Length; i++)
            {
                var c = format[i];
                if (c == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (inQuote)
                {
                    continue;
                }

                if (c == '[')
                {
                    inBracket = true;
                    continue;
                }

                if (c == ']')
                {
                    inBracket = false;
                    continue;
                }

                if (inBracket)
                {
                    continue;
                }

                if (c == '\\')
                {
                    i++;
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }
    }
}
