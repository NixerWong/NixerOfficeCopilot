using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelSelectionInfo
    {
        public ExcelSelectionInfo()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            Preview = string.Empty;
        }

        [DataMember(Order = 1)]
        public bool IsAvailable { get; set; }

        [DataMember(Order = 2)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 3)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 4)]
        public string Address { get; set; }

        [DataMember(Order = 5)]
        public int RowCount { get; set; }

        [DataMember(Order = 6)]
        public int ColumnCount { get; set; }

        [DataMember(Order = 7)]
        public string Preview { get; set; }
    }
}