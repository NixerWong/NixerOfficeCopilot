using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelTableInfo
    {
        public ExcelTableInfo()
        {
            Name = string.Empty;
            WorksheetName = string.Empty;
            RangeAddress = string.Empty;
            TableStyle = string.Empty;
            Columns = new List<string>();
        }

        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string RangeAddress { get; set; }

        [DataMember(Order = 4)]
        public bool HasHeaders { get; set; }

        [DataMember(Order = 5)]
        public bool ShowTotals { get; set; }

        [DataMember(Order = 6)]
        public string TableStyle { get; set; }

        [DataMember(Order = 7)]
        public int RowCount { get; set; }

        [DataMember(Order = 8)]
        public int ColumnCount { get; set; }

        [DataMember(Order = 9)]
        public List<string> Columns { get; set; }
    }

    [DataContract]
    public sealed class ExcelTableListResult
    {
        public ExcelTableListResult()
        {
            WorkbookName = string.Empty;
            Tables = new List<ExcelTableInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelTableInfo> Tables { get; set; }
    }

    [DataContract]
    public sealed class ExcelTableViewRequest
    {
        public ExcelTableViewRequest()
        {
            TableName = string.Empty;
        }

        [DataMember(Name = "tableName", Order = 1)]
        public string TableName { get; set; }
    }

    [DataContract]
    public sealed class ExcelTableViewResult
    {
        public ExcelTableViewResult()
        {
            WorkbookName = string.Empty;
            Table = new ExcelTableInfo();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public ExcelTableInfo Table { get; set; }
    }

    [DataContract]
    public sealed class ExcelTableDataRequest
    {
        public ExcelTableDataRequest()
        {
            TableName = string.Empty;
            MaxRows = 100;
            MaxColumns = 30;
        }

        [DataMember(Name = "tableName", Order = 1)]
        public string TableName { get; set; }

        [DataMember(Name = "maxRows", Order = 2)]
        public int MaxRows { get; set; }

        [DataMember(Name = "maxColumns", Order = 3)]
        public int MaxColumns { get; set; }
    }

    [DataContract]
    public sealed class ExcelTableDataResult
    {
        public ExcelTableDataResult()
        {
            WorkbookName = string.Empty;
            TableName = string.Empty;
            WorksheetName = string.Empty;
            RangeAddress = string.Empty;
            Headers = new List<string>();
            Rows = new List<string[]>();
            Preview = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string TableName { get; set; }

        [DataMember(Order = 3)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 4)]
        public string RangeAddress { get; set; }

        [DataMember(Order = 5)]
        public List<string> Headers { get; set; }

        [DataMember(Order = 6)]
        public List<string[]> Rows { get; set; }

        [DataMember(Order = 7)]
        public int RowCount { get; set; }

        [DataMember(Order = 8)]
        public int ColumnCount { get; set; }

        [DataMember(Order = 9)]
        public bool IsTruncated { get; set; }

        [DataMember(Order = 10)]
        public string Preview { get; set; }
    }
}