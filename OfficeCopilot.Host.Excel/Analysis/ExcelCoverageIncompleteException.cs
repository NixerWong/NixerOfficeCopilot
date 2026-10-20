using System;

namespace OfficeCopilot.Host.Excel.Analysis
{
    internal sealed class ExcelCoverageIncompleteException : InvalidOperationException
    {
        public const string ErrorCode = "excel.incomplete_coverage";

        public ExcelCoverageIncompleteException(string message)
            : base(message)
        {
        }
    }
}
