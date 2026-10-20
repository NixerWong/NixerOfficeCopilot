using System;

namespace OfficeCopilot.OpenXml.Excel
{
    /// <summary>
    /// Outcome of creating or writing an Excel ListObject table.
    /// </summary>
    public sealed class ExcelTableResult
    {
        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Reference { get; set; }

        public int RowCount { get; set; }

        public int ColumnCount { get; set; }
    }
}
