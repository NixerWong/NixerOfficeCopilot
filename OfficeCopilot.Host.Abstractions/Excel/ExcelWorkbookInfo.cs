using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelWorkbookInfo
    {
        public ExcelWorkbookInfo()
        {
            Name = string.Empty;
            FullName = string.Empty;
            WorksheetName = string.Empty;
        }

        [DataMember(Order = 1)]
        public bool IsAvailable { get; set; }

        [DataMember(Order = 2)]
        public string Name { get; set; }

        [DataMember(Order = 3)]
        public string FullName { get; set; }

        [DataMember(Order = 4)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 5)]
        public bool IsReadOnly { get; set; }
    }
}