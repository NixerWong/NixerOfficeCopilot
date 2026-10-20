using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelPowerQueryInfo
    {
        public ExcelPowerQueryInfo()
        {
            Name = string.Empty;
            Formula = string.Empty;
            FormulaPreview = string.Empty;
        }

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string Formula { get; set; }

        [DataMember(Order = 3)]
        public string FormulaPreview { get; set; }

        [DataMember(Order = 4)]
        public bool IsConnectionOnly { get; set; }
    }

    [DataContract]
    public sealed class ExcelPowerQueryListResult
    {
        public ExcelPowerQueryListResult()
        {
            WorkbookName = string.Empty;
            Queries = new List<ExcelPowerQueryInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelPowerQueryInfo> Queries { get; set; }
    }

    [DataContract]
    public sealed class ExcelPowerQueryViewRequest
    {
        public ExcelPowerQueryViewRequest()
        {
            Name = string.Empty;
        }

        [DataMember(Order = 1)]
        public string Name { get; set; }
    }

    [DataContract]
    public sealed class ExcelPowerQueryViewResult
    {
        public ExcelPowerQueryViewResult()
        {
            WorkbookName = string.Empty;
            Name = string.Empty;
            Formula = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string Name { get; set; }

        [DataMember(Order = 3)]
        public string Formula { get; set; }

        [DataMember(Order = 4)]
        public int CharacterCount { get; set; }

        [DataMember(Order = 5)]
        public bool IsConnectionOnly { get; set; }
    }
}