using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelSourceRef
    {
        public ExcelSourceRef()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            TableName = string.Empty;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "tableName", Order = 3)]
        public string TableName { get; set; }
    }

    [DataContract]
    public sealed class CalcFilter
    {
        public CalcFilter()
        {
            Column = string.Empty;
            Operator = "eq";
            Value = string.Empty;
        }

        [DataMember(Name = "column", Order = 1)]
        public string Column { get; set; }

        [DataMember(Name = "operator", Order = 2)]
        public string Operator { get; set; }

        [DataMember(Name = "value", Order = 3)]
        public string Value { get; set; }
    }

    [DataContract]
    public sealed class CalcCostEstimate
    {
        [DataMember(Name = "rowCount", Order = 1)]
        public int RowCount { get; set; }

        [DataMember(Name = "readColumnCount", Order = 2)]
        public int ReadColumnCount { get; set; }

        [DataMember(Name = "projectedCellCount", Order = 3)]
        public long ProjectedCellCount { get; set; }
    }

    [DataContract]
    public sealed class CalcQuery
    {
        public CalcQuery()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Source = new ExcelSourceRef();
            Function = "sum";
            Columns = new List<string>();
            Filters = new List<CalcFilter>();
            GroupByColumn = string.Empty;
            SortDirection = "ascending";
            HeaderMode = "auto";
            ExcludeRows = new List<int>();
            ExcludeLabelRows = false;
            Sources = new List<ExcelVerificationSource>();
        }

        [DataMember(Name = "source", Order = 1)]
        public ExcelSourceRef Source { get; set; }

        [DataMember(Name = "function", Order = 2)]
        public string Function { get; set; }

        [DataMember(Name = "columns", Order = 3)]
        public List<string> Columns { get; set; }

        [DataMember(Name = "filters", Order = 4)]
        public List<CalcFilter> Filters { get; set; }

        [DataMember(Name = "groupByColumn", Order = 5)]
        public string GroupByColumn { get; set; }

        [DataMember(Name = "sortDirection", Order = 6, EmitDefaultValue = false)]
        public string SortDirection { get; set; }

        [DataMember(Name = "headerMode", Order = 7, EmitDefaultValue = false)]
        public string HeaderMode { get; set; }

        [DataMember(Name = "excludeRows", Order = 8, EmitDefaultValue = false)]
        public List<int> ExcludeRows { get; set; }

        [DataMember(Name = "excludeLabelRows", Order = 9, EmitDefaultValue = false)]
        public bool ExcludeLabelRows { get; set; }

        [DataMember(Name = "sources", Order = 10, EmitDefaultValue = false)]
        public List<ExcelVerificationSource> Sources { get; set; }
    }

    [DataContract]
    public sealed class CalcResult
    {
        public CalcResult()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Groups = new Dictionary<string, double>();
            Coverage = new ExcelCoverageReport();
            Notes = new List<string>();
            Keys = new List<string>();
            IsSorted = null;
        }

        [DataMember(Name = "scalar", Order = 1)]
        public double? Scalar { get; set; }

        [DataMember(Name = "groups", Order = 2)]
        public Dictionary<string, double> Groups { get; set; }

        [DataMember(Name = "coverage", Order = 3)]
        public ExcelCoverageReport Coverage { get; set; }

        [DataMember(Name = "notes", Order = 4)]
        public List<string> Notes { get; set; }

        [DataMember(Name = "keys", Order = 5, EmitDefaultValue = false)]
        public List<string> Keys { get; set; }

        [DataMember(Name = "isSorted", Order = 6, EmitDefaultValue = false)]
        public bool? IsSorted { get; set; }
    }
}
