using System;
using System.Collections.Generic;

namespace OfficeCopilot.OpenXml.Excel
{
    /// <summary>
    /// Full readback of an Excel ListObject table: its identity, range, header
    /// labels and data rows (row-major). Headers are omitted from
    /// <see cref="Rows"/>; the header labels live in <see cref="Headers"/>.
    /// </summary>
    public sealed class ExcelTableData
    {
        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Reference { get; set; }

        public bool HasHeader { get; set; }

        public IReadOnlyList<string> Headers { get; set; }

        public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; }
    }
}
