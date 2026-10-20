using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelWorksheetCreationRequest
    {
        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }
    }

    [DataContract]
    public sealed class ExcelWorksheetCreationResult
    {
        public ExcelWorksheetCreationResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            CodeName = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string CodeName { get; set; }
    }

    [DataContract]
    public sealed class ExcelWorksheetInfo
    {
        public ExcelWorksheetInfo()
        {
            Name = string.Empty;
            CodeName = string.Empty;
            UsedRangeAddress = string.Empty;
            HeaderSignature = string.Empty;
            HeaderColumns = new List<string>();
            RoleGuess = "other";
            InspectionError = string.Empty;
        }

        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public int Index { get; set; }

        [DataMember(Order = 3)]
        public bool IsVisible { get; set; }

        [DataMember(Order = 4)]
        public string UsedRangeAddress { get; set; }

        [DataMember(Order = 5)]
        public int UsedRangeRows { get; set; }

        [DataMember(Order = 6)]
        public int UsedRangeColumns { get; set; }

        [DataMember(Order = 7)]
        public string HeaderSignature { get; set; }

        [DataMember(Order = 8)]
        public bool HasFormulas { get; set; }

        [DataMember(Order = 9)]
        public bool IsProtected { get; set; }

        [DataMember(Order = 10)]
        public string RoleGuess { get; set; }

        [DataMember(Order = 11)]
        public string InspectionError { get; set; }

        [DataMember(Order = 12)]
        public List<string> HeaderColumns { get; set; }

        [DataMember(Order = 13)]
        public string CodeName { get; set; }
    }

    [DataContract]
    public sealed class ExcelWorksheetHeaderGroup
    {
        public ExcelWorksheetHeaderGroup()
        {
            GroupId = string.Empty;
            WorksheetNames = new List<string>();
        }

        [DataMember(Order = 1)]
        public string GroupId { get; set; }

        [DataMember(Order = 2)]
        public decimal MinimumHeaderSimilarity { get; set; }

        [DataMember(Order = 3)]
        public List<string> WorksheetNames { get; set; }
    }

    [DataContract]
    public sealed class ExcelSharedKeyCandidate
    {
        public ExcelSharedKeyCandidate()
        {
            ColumnName = string.Empty;
            WorksheetNames = new List<string>();
        }

        [DataMember(Order = 1)]
        public string ColumnName { get; set; }

        [DataMember(Order = 2)]
        public List<string> WorksheetNames { get; set; }

        [DataMember(Order = 3)]
        public bool UniquenessVerified { get; set; }
    }

    [DataContract]
    public sealed class ExcelWorksheetListResult
    {
        public ExcelWorksheetListResult()
        {
            WorkbookName = string.Empty;
            Worksheets = new List<ExcelWorksheetInfo>();
            WorksheetGroups = new List<ExcelWorksheetHeaderGroup>();
            SharedKeyCandidates = new List<ExcelSharedKeyCandidate>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelWorksheetInfo> Worksheets { get; set; }

        [DataMember(Order = 3)]
        public int TotalWorksheetCount { get; set; }

        [DataMember(Order = 4)]
        public bool IsTruncated { get; set; }

        [DataMember(Order = 5)]
        public List<ExcelWorksheetHeaderGroup> WorksheetGroups { get; set; }

        [DataMember(Order = 6)]
        public List<ExcelSharedKeyCandidate> SharedKeyCandidates { get; set; }
    }

    [DataContract]
    public sealed class ExcelWorksheetDataRequest
    {
        public ExcelWorksheetDataRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            MaxRows = 50;
            MaxColumns = 20;
            UseUsedRangeWhenAddressEmpty = true;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "maxRows", Order = 3)]
        public int MaxRows { get; set; }

        [DataMember(Name = "maxColumns", Order = 4)]
        public int MaxColumns { get; set; }

        [DataMember(Name = "useUsedRangeWhenAddressEmpty", Order = 5)]
        public bool UseUsedRangeWhenAddressEmpty { get; set; }
    }

    [DataContract]
    public sealed class ExcelWorksheetDataResult
    {
        public ExcelWorksheetDataResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            Preview = string.Empty;
            Headers = new List<string>();
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
        public List<string> Headers { get; set; }

        [DataMember(Order = 9)]
        public List<string[]> Rows { get; set; }
    }
}