using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelNamedRangeInfo
    {
        public ExcelNamedRangeInfo()
        {
            Name = string.Empty;
            Scope = string.Empty;
            RefersTo = string.Empty;
            ValueText = string.Empty;
            ValueType = string.Empty;
        }

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string Scope { get; set; }

        [DataMember(Order = 3)]
        public string RefersTo { get; set; }

        [DataMember(Order = 4)]
        public string ValueText { get; set; }

        [DataMember(Order = 5)]
        public string ValueType { get; set; }

        [DataMember(Order = 6)]
        public bool IsVisible { get; set; }
    }

    [DataContract]
    public sealed class ExcelNamedRangeListResult
    {
        public ExcelNamedRangeListResult()
        {
            WorkbookName = string.Empty;
            NamedRanges = new List<ExcelNamedRangeInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelNamedRangeInfo> NamedRanges { get; set; }
    }

    [DataContract]
    public sealed class ExcelNamedRangeValueRequest
    {
        public ExcelNamedRangeValueRequest()
        {
            Name = string.Empty;
        }

        [DataMember(Order = 1)]
        public string Name { get; set; }
    }

    [DataContract]
    public sealed class ExcelNamedRangeValueResult
    {
        public ExcelNamedRangeValueResult()
        {
            WorkbookName = string.Empty;
            Name = string.Empty;
            Scope = string.Empty;
            RefersTo = string.Empty;
            ValueText = string.Empty;
            ValueType = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string Name { get; set; }

        [DataMember(Order = 3)]
        public string Scope { get; set; }

        [DataMember(Order = 4)]
        public string RefersTo { get; set; }

        [DataMember(Order = 5)]
        public string ValueText { get; set; }

        [DataMember(Order = 6)]
        public string ValueType { get; set; }

        [DataMember(Order = 7)]
        public bool IsVisible { get; set; }
    }
}