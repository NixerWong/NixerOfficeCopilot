using System;
using System.Collections.Generic;

namespace OfficeCopilot.OpenXml.Excel
{
    /// <summary>
    /// Result of a rectangular range read from a worksheet. <see cref="Cells"/>
    /// is row-major: <c>Cells[r][c]</c> addresses row r, column c (both 0-based
    /// relative to the requested range). Empty cells are represented as
    /// empty strings so the returned grid is always rectangular.
    /// </summary>
    public sealed class ExcelReadRangeResult
    {
        public IReadOnlyList<IReadOnlyList<string>> Cells { get; set; }

        public int RowCount { get; set; }

        public int ColumnCount { get; set; }
    }
}
