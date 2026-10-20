using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelRangeReadResult
    {
        public ExcelRangeReadResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            Preview = string.Empty;
            Rows = new List<string[]>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string Address { get; set; }

        [DataMember(Order = 4)]
        public int RowCount { get; set; }

        [DataMember(Order = 5)]
        public int ColumnCount { get; set; }

        [DataMember(Order = 6)]
        public bool IsTruncated { get; set; }

        [DataMember(Order = 7)]
        public string Preview { get; set; }

        [DataMember(Order = 8)]
        public List<string[]> Rows { get; set; }
    }
}