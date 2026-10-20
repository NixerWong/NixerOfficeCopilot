using System;

namespace OfficeCopilot.OpenXml.Excel
{
    /// <summary>
    /// Readback of a single chart on a worksheet: its 1-based position, type,
    /// title and series count.
    /// </summary>
    public sealed class ExcelChartInfo
    {
        public int Index { get; set; }

        public string Type { get; set; }

        public string Title { get; set; }

        public int SeriesCount { get; set; }
    }
}
