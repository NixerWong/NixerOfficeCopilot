using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Analysis.Accumulators
{
    internal sealed class CoverageAccumulator
    {
        public int ScannedRows { get; private set; }

        public int NonNumericCount { get; private set; }

        public int TextNumberCount { get; private set; }

        public int ErrorCount { get; private set; }

        public int BlankCount { get; private set; }

        public void Accept(object[] row, int columnCount)
        {
            ScannedRows++;

            for (var column = 0; column < columnCount; column++)
            {
                var value = row != null && column < row.Length ? row[column] : null;
                if (ExcelValueSemantics.IsEmpty(value))
                {
                    BlankCount++;
                    continue;
                }

                var isError = ExcelValueSemantics.IsError(value);
                double number;
                var isNumeric = !isError && ExcelValueSemantics.TryGetNumber(value, out number);
                var isTextNumber = value is string && isNumeric;

                if (!isNumeric || isTextNumber)
                {
                    NonNumericCount++;
                }

                if (isTextNumber)
                {
                    TextNumberCount++;
                }

                if (isError)
                {
                    ErrorCount++;
                }
            }
        }

        public ExcelCoverageReport ToReport(int totalRows)
        {
            if (ScannedRows != totalRows)
            {
                throw new ExcelCoverageIncompleteException(
                    "Excel 覆盖不完整：已扫描 " + ScannedRows + " / " + totalRows
                    + " 行数据，拒绝返回不完整结果。");
            }

            return new ExcelCoverageReport
            {
                TotalRows = totalRows,
                ScannedRows = ScannedRows,
                NonNumericCount = NonNumericCount,
                TextNumberCount = TextNumberCount,
                ErrorCount = ErrorCount,
                BlankCount = BlankCount
            };
        }
    }
}
