using System.Collections.Generic;
using System.Runtime.Serialization;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Abstractions
{
    /// <summary>数据源解析方式，回传给模型以便确认"到底算的是哪一块"。</summary>
    public static class ExcelTabularSourceKinds
    {
        public const string ListObject = "listObject";
        public const string ExplicitAddress = "explicitAddress";
        public const string Selection = "selection";
        public const string CurrentRegion = "currentRegion";
        public const string WorksheetUsedRange = "worksheetUsedRange";
    }

    public static class ExcelColumnValueKinds
    {
        public const string Empty = "empty";
        public const string Number = "number";
        public const string Text = "text";
        public const string Date = "date";
        public const string Boolean = "boolean";
        public const string Error = "error";
        public const string Mixed = "mixed";
    }

    [DataContract]
    public sealed class ExcelProfileRangeRequest
    {
        public ExcelProfileRangeRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            TableName = string.Empty;
            HeaderMode = "auto";
            TopK = 10;
            SampleRowCount = 5;
            Sources = new List<ExcelProfileRangeSource>();
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "tableName", Order = 3)]
        public string TableName { get; set; }

        /// <summary>auto / yes / no。</summary>
        [DataMember(Name = "headerMode", Order = 4)]
        public string HeaderMode { get; set; }

        [DataMember(Name = "topK", Order = 5)]
        public int TopK { get; set; }

        [DataMember(Name = "sampleRowCount", Order = 6)]
        public int SampleRowCount { get; set; }

        [DataMember(Name = "sources", Order = 7, EmitDefaultValue = false)]
        public List<ExcelProfileRangeSource> Sources { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            TableName = string.Empty;
            HeaderMode = "auto";
            TopK = 10;
            SampleRowCount = 5;
            Sources = new List<ExcelProfileRangeSource>();
        }
    }

    [DataContract]
    public sealed class ExcelProfileRangeSource
    {
        public ExcelProfileRangeSource()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            TableName = string.Empty;
            Alias = string.Empty;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "tableName", Order = 3)]
        public string TableName { get; set; }

        [DataMember(Name = "alias", Order = 4)]
        public string Alias { get; set; }
    }

    [DataContract]
    public sealed class ExcelValueFrequency
    {
        public ExcelValueFrequency()
        {
            Value = string.Empty;
        }

        [DataMember(Name = "value", Order = 1)]
        public string Value { get; set; }

        [DataMember(Name = "count", Order = 2)]
        public int Count { get; set; }
    }

    [DataContract]
    public sealed class ExcelColumnProfile
    {
        public ExcelColumnProfile()
        {
            Name = string.Empty;
            InferredType = ExcelColumnValueKinds.Empty;
            MinText = string.Empty;
            MaxText = string.Empty;
            TopValues = new List<ExcelValueFrequency>();
        }

        [DataMember(Name = "index", Order = 1)]
        public int Index { get; set; }

        [DataMember(Name = "name", Order = 2)]
        public string Name { get; set; }

        [DataMember(Name = "inferredType", Order = 3)]
        public string InferredType { get; set; }

        [DataMember(Name = "nonEmptyCount", Order = 4)]
        public int NonEmptyCount { get; set; }

        [DataMember(Name = "emptyCount", Order = 5)]
        public int EmptyCount { get; set; }

        [DataMember(Name = "distinctCount", Order = 6)]
        public int DistinctCount { get; set; }

        [DataMember(Name = "errorCount", Order = 7)]
        public int ErrorCount { get; set; }

        [DataMember(Name = "hasNumericStats", Order = 8)]
        public bool HasNumericStats { get; set; }

        [DataMember(Name = "sum", Order = 9)]
        public double Sum { get; set; }

        [DataMember(Name = "average", Order = 10)]
        public double Average { get; set; }

        [DataMember(Name = "minText", Order = 11)]
        public string MinText { get; set; }

        [DataMember(Name = "maxText", Order = 12)]
        public string MaxText { get; set; }

        [DataMember(Name = "topValues", Order = 13)]
        public List<ExcelValueFrequency> TopValues { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataQualityFlag
    {
        public ExcelDataQualityFlag()
        {
            Name = string.Empty;
            SampleRows = new List<int>();
            Impact = string.Empty;
            Recommendation = string.Empty;
        }

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }

        [DataMember(Name = "count", Order = 2, EmitDefaultValue = false)]
        public int? Count { get; set; }

        [DataMember(Name = "sampleRows", Order = 3)]
        public List<int> SampleRows { get; set; }

        [DataMember(Name = "impact", Order = 4)]
        public string Impact { get; set; }

        [DataMember(Name = "recommendation", Order = 5)]
        public string Recommendation { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Name = string.Empty;
            SampleRows = new List<int>();
            Impact = string.Empty;
            Recommendation = string.Empty;
        }
    }

    [DataContract]
    public sealed class ExcelProfileRangeResult
    {
        public ExcelProfileRangeResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            SourceKind = string.Empty;
            TableName = string.Empty;
            HeaderInferenceReason = string.Empty;
            TruncationNote = string.Empty;
            Columns = new List<ExcelColumnProfile>();
            SampleRows = new List<string[]>();
            Coverage = new ExcelCoverageReport();
            DataQualityFlags = new List<ExcelDataQualityFlag>();
            DataQualityNotes = new List<string>();
            Topology = "single";
            SourceProfiles = new List<ExcelProfileSourceResult>();
            HeaderConsistency = null;
        }

        [DataMember(Name = "workbookName", Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Name = "worksheetName", Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 3)]
        public string Address { get; set; }

        [DataMember(Name = "sourceKind", Order = 4)]
        public string SourceKind { get; set; }

        [DataMember(Name = "tableName", Order = 5)]
        public string TableName { get; set; }

        [DataMember(Name = "rowCount", Order = 6)]
        public int RowCount { get; set; }

        [DataMember(Name = "columnCount", Order = 7)]
        public int ColumnCount { get; set; }

        [DataMember(Name = "headerInferred", Order = 8)]
        public bool HeaderInferred { get; set; }

        [DataMember(Name = "headerInferenceReason", Order = 9)]
        public string HeaderInferenceReason { get; set; }

        [DataMember(Name = "isTruncated", Order = 10)]
        public bool IsTruncated { get; set; }

        [DataMember(Name = "truncationNote", Order = 11)]
        public string TruncationNote { get; set; }

        [DataMember(Name = "columns", Order = 12)]
        public List<ExcelColumnProfile> Columns { get; set; }

        [DataMember(Name = "sampleRows", Order = 13)]
        public List<string[]> SampleRows { get; set; }

        [DataMember(Name = "coverage", Order = 14)]
        public ExcelCoverageReport Coverage { get; set; }

        [DataMember(Name = "dataQualityFlags", Order = 15)]
        public List<ExcelDataQualityFlag> DataQualityFlags { get; set; }

        [DataMember(Name = "dataQualityNotes", Order = 16, EmitDefaultValue = false)]
        public List<string> DataQualityNotes { get; set; }

        [DataMember(Name = "topology", Order = 17, EmitDefaultValue = false)]
        public string Topology { get; set; }

        [DataMember(Name = "sourceProfiles", Order = 18, EmitDefaultValue = false)]
        public List<ExcelProfileSourceResult> SourceProfiles { get; set; }

        [DataMember(Name = "headerConsistency", Order = 19, EmitDefaultValue = false)]
        public ExcelProfileHeaderConsistency HeaderConsistency { get; set; }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            DataQualityFlags = new List<ExcelDataQualityFlag>();
            DataQualityNotes = new List<string>();
            Topology = "single";
            SourceProfiles = new List<ExcelProfileSourceResult>();
        }

    }

    [DataContract]
    public sealed class ExcelProfileSourceResult
    {
        public ExcelProfileSourceResult()
        {
            Alias = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            SourceKind = string.Empty;
            TableName = string.Empty;
            HeaderInferenceReason = string.Empty;
            TruncationNote = string.Empty;
            Columns = new List<ExcelColumnProfile>();
            SampleRows = new List<string[]>();
            Coverage = new ExcelCoverageReport();
            DataQualityFlags = new List<ExcelDataQualityFlag>();
            DataQualityNotes = new List<string>();
        }

        [DataMember(Name = "alias", Order = 1)]
        public string Alias { get; set; }

        [DataMember(Name = "worksheetName", Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 3)]
        public string Address { get; set; }

        [DataMember(Name = "sourceKind", Order = 4)]
        public string SourceKind { get; set; }

        [DataMember(Name = "tableName", Order = 5)]
        public string TableName { get; set; }

        [DataMember(Name = "rowCount", Order = 6)]
        public int RowCount { get; set; }

        [DataMember(Name = "columnCount", Order = 7)]
        public int ColumnCount { get; set; }

        [DataMember(Name = "headerInferred", Order = 8)]
        public bool HeaderInferred { get; set; }

        [DataMember(Name = "headerInferenceReason", Order = 9)]
        public string HeaderInferenceReason { get; set; }

        [DataMember(Name = "isTruncated", Order = 10)]
        public bool IsTruncated { get; set; }

        [DataMember(Name = "truncationNote", Order = 11)]
        public string TruncationNote { get; set; }

        [DataMember(Name = "columns", Order = 12)]
        public List<ExcelColumnProfile> Columns { get; set; }

        [DataMember(Name = "sampleRows", Order = 13)]
        public List<string[]> SampleRows { get; set; }

        [DataMember(Name = "coverage", Order = 14)]
        public ExcelCoverageReport Coverage { get; set; }

        [DataMember(Name = "dataQualityFlags", Order = 15)]
        public List<ExcelDataQualityFlag> DataQualityFlags { get; set; }

        [DataMember(Name = "dataQualityNotes", Order = 16, EmitDefaultValue = false)]
        public List<string> DataQualityNotes { get; set; }
    }

    [DataContract]
    public sealed class ExcelProfileHeaderConsistency
    {
        public ExcelProfileHeaderConsistency()
        {
            Differences = new List<string>();
        }

        [DataMember(Name = "namesConsistent", Order = 1)]
        public bool NamesConsistent { get; set; }

        [DataMember(Name = "orderConsistent", Order = 2)]
        public bool OrderConsistent { get; set; }

        [DataMember(Name = "typesConsistent", Order = 3)]
        public bool TypesConsistent { get; set; }

        [DataMember(Name = "headerInferenceConsistent", Order = 4)]
        public bool HeaderInferenceConsistent { get; set; }

        [DataMember(Name = "differences", Order = 5)]
        public List<string> Differences { get; set; }
    }

    [DataContract]
    public sealed class ExcelAggregateMeasure
    {
        public ExcelAggregateMeasure()
        {
            Column = string.Empty;
            Function = "sum";
            Alias = string.Empty;
        }

        [DataMember(Name = "column", Order = 1)]
        public string Column { get; set; }

        /// <summary>sum / count / countDistinct / average / min / max / first / last。</summary>
        [DataMember(Name = "function", Order = 2)]
        public string Function { get; set; }

        [DataMember(Name = "alias", Order = 3)]
        public string Alias { get; set; }
    }

    [DataContract]
    public sealed class ExcelAggregateFilter
    {
        public ExcelAggregateFilter()
        {
            Column = string.Empty;
            Operator = "eq";
            Value = string.Empty;
        }

        [DataMember(Name = "column", Order = 1)]
        public string Column { get; set; }

        /// <summary>eq / ne / gt / ge / lt / le / contains / startsWith / in / isEmpty / notEmpty / isNativeNumber / isTextNumber。</summary>
        [DataMember(Name = "operator", Order = 2)]
        public string Operator { get; set; }

        /// <summary>in 运算符用逗号分隔多个取值。</summary>
        [DataMember(Name = "value", Order = 3)]
        public string Value { get; set; }
    }

    [DataContract]
    public sealed class ExcelAggregateSort
    {
        public ExcelAggregateSort()
        {
            Column = string.Empty;
        }

        /// <summary>分组列名或度量别名。</summary>
        [DataMember(Name = "column", Order = 1)]
        public string Column { get; set; }

        [DataMember(Name = "descending", Order = 2)]
        public bool Descending { get; set; }
    }

    [DataContract]
    public sealed class ExcelAggregateDerivedGroup
    {
        public ExcelAggregateDerivedGroup()
        {
            Column = string.Empty;
            Transform = string.Empty;
            Alias = string.Empty;
        }

        /// <summary>源列名或列字母。</summary>
        [DataMember(Name = "column", Order = 1)]
        public string Column { get; set; }

        /// <summary>
        /// firstCharacter / prefix / year / quarter / month / monthName / yearMonth / day / dayOfWeek / numberBucket。
        /// </summary>
        [DataMember(Name = "transform", Order = 2)]
        public string Transform { get; set; }

        /// <summary>结果中的派生分组列名。</summary>
        [DataMember(Name = "alias", Order = 3)]
        public string Alias { get; set; }

        /// <summary>prefix 的字符数、numberBucket 的桶宽。0 表示用该 transform 的默认值。</summary>
        [DataMember(Name = "size", Order = 4)]
        public double Size { get; set; }
    }

    [DataContract]
    public sealed class ExcelAggregateSource
    {
        public ExcelAggregateSource()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            TableName = string.Empty;
            Alias = string.Empty;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "tableName", Order = 3)]
        public string TableName { get; set; }

        [DataMember(Name = "alias", Order = 4)]
        public string Alias { get; set; }
    }

    [DataContract]
    public sealed class ExcelAggregateSourceSummary
    {
        public ExcelAggregateSourceSummary()
        {
            Alias = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
        }

        [DataMember(Name = "alias", Order = 1)]
        public string Alias { get; set; }

        [DataMember(Name = "worksheetName", Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 3)]
        public string Address { get; set; }

        [DataMember(Name = "totalRowCount", Order = 4)]
        public int TotalRowCount { get; set; }
    }

    [DataContract]
    public sealed class ExcelAggregateLookup
    {
        public ExcelAggregateLookup()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Source = new ExcelSourceRef();
            Key = string.Empty;
            ForeignKey = string.Empty;
            Columns = new List<string>();
            Join = "left";
            OnDuplicateKey = "error";
        }

        [DataMember(Name = "source", Order = 1)]
        public ExcelSourceRef Source { get; set; }

        [DataMember(Name = "key", Order = 2)]
        public string Key { get; set; }

        [DataMember(Name = "foreignKey", Order = 3)]
        public string ForeignKey { get; set; }

        [DataMember(Name = "columns", Order = 4)]
        public List<string> Columns { get; set; }

        [DataMember(Name = "join", Order = 5)]
        public string Join { get; set; }

        [DataMember(Name = "onDuplicateKey", Order = 6)]
        public string OnDuplicateKey { get; set; }
    }

    [DataContract]
    public sealed class ExcelAggregateLookupSummary
    {
        public ExcelAggregateLookupSummary()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            TableName = string.Empty;
            Key = string.Empty;
            ForeignKey = string.Empty;
            Columns = new List<string>();
            Join = string.Empty;
            OnDuplicateKey = string.Empty;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "key", Order = 3)]
        public string Key { get; set; }

        [DataMember(Name = "foreignKey", Order = 4)]
        public string ForeignKey { get; set; }

        [DataMember(Name = "columns", Order = 5)]
        public List<string> Columns { get; set; }

        [DataMember(Name = "join", Order = 6)]
        public string Join { get; set; }

        [DataMember(Name = "onDuplicateKey", Order = 7)]
        public string OnDuplicateKey { get; set; }

        [DataMember(Name = "duplicateKeyCount", Order = 8)]
        public int DuplicateKeyCount { get; set; }

        [DataMember(Name = "unmatchedRowCount", Order = 9)]
        public int UnmatchedRowCount { get; set; }

        [DataMember(Name = "droppedRowCount", Order = 10)]
        public int DroppedRowCount { get; set; }

        [DataMember(Name = "tableName", Order = 11, EmitDefaultValue = false)]
        public string TableName { get; set; }
    }

    [DataContract]
    public sealed class ExcelAggregateRequest
    {
        public ExcelAggregateRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            TableName = string.Empty;
            HeaderMode = "auto";
            GroupBy = new List<string>();
            Measures = new List<ExcelAggregateMeasure>();
            Filters = new List<ExcelAggregateFilter>();
            OrderBy = new List<ExcelAggregateSort>();
            DerivedGroupBy = new List<ExcelAggregateDerivedGroup>();
            Sources = new List<ExcelAggregateSource>();
            Lookups = new List<ExcelAggregateLookup>();
            MaxGroups = 200;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "tableName", Order = 3)]
        public string TableName { get; set; }

        [DataMember(Name = "headerMode", Order = 4)]
        public string HeaderMode { get; set; }

        [DataMember(Name = "groupBy", Order = 5)]
        public List<string> GroupBy { get; set; }

        [DataMember(Name = "measures", Order = 6)]
        public List<ExcelAggregateMeasure> Measures { get; set; }

        [DataMember(Name = "filters", Order = 7)]
        public List<ExcelAggregateFilter> Filters { get; set; }

        [DataMember(Name = "orderBy", Order = 8)]
        public List<ExcelAggregateSort> OrderBy { get; set; }

        [DataMember(Name = "maxGroups", Order = 9)]
        public int MaxGroups { get; set; }

        [DataMember(Name = "derivedGroupBy", Order = 10)]
        public List<ExcelAggregateDerivedGroup> DerivedGroupBy { get; set; }

        [DataMember(Name = "sources", Order = 11, EmitDefaultValue = false)]
        public List<ExcelAggregateSource> Sources { get; set; }

        [DataMember(Name = "lookups", Order = 12, EmitDefaultValue = false)]
        public List<ExcelAggregateLookup> Lookups { get; set; }

    }

    [DataContract]
    public sealed class ExcelAggregateResult
    {
        public ExcelAggregateResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            SourceKind = string.Empty;
            TableName = string.Empty;
            Columns = new List<string>();
            Rows = new List<string[]>();
            Warnings = new List<string>();
            Coverage = new ExcelCoverageReport();
            SourceSummaries = new List<ExcelAggregateSourceSummary>();
            LookupSummaries = new List<ExcelAggregateLookupSummary>();
        }

        [DataMember(Name = "workbookName", Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Name = "worksheetName", Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 3)]
        public string Address { get; set; }

        [DataMember(Name = "sourceKind", Order = 4)]
        public string SourceKind { get; set; }

        [DataMember(Name = "tableName", Order = 5)]
        public string TableName { get; set; }

        [DataMember(Name = "totalRowCount", Order = 6)]
        public int TotalRowCount { get; set; }

        [DataMember(Name = "filteredRowCount", Order = 7)]
        public int FilteredRowCount { get; set; }

        [DataMember(Name = "skippedRowCount", Order = 8)]
        public int SkippedRowCount { get; set; }

        [DataMember(Name = "groupCount", Order = 9)]
        public int GroupCount { get; set; }

        [DataMember(Name = "isTruncated", Order = 10)]
        public bool IsTruncated { get; set; }

        [DataMember(Name = "columns", Order = 11)]
        public List<string> Columns { get; set; }

        [DataMember(Name = "rows", Order = 12)]
        public List<string[]> Rows { get; set; }

        [DataMember(Name = "warnings", Order = 13)]
        public List<string> Warnings { get; set; }

        [DataMember(Name = "coverage", Order = 14)]
        public ExcelCoverageReport Coverage { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Verification, Order = 15, EmitDefaultValue = false)]
        public ExcelVerificationResult Verification { get; set; }

        [DataMember(Name = "sourceSummaries", Order = 16, EmitDefaultValue = false)]
        public List<ExcelAggregateSourceSummary> SourceSummaries { get; set; }

        [DataMember(Name = "lookupSummaries", Order = 17, EmitDefaultValue = false)]
        public List<ExcelAggregateLookupSummary> LookupSummaries { get; set; }

    }

    [DataContract]
    public sealed class ExcelReconcileRequest
    {
        public ExcelReconcileRequest()
        {
            DetailWorksheetName = string.Empty;
            DetailAddress = string.Empty;
            DetailTableName = string.Empty;
            SummaryWorksheetName = string.Empty;
            SummaryAddress = string.Empty;
            SummaryTableName = string.Empty;
            DetailGroupColumn = string.Empty;
            DetailValueColumn = string.Empty;
            SummaryGroupColumn = string.Empty;
            SummaryValueColumn = string.Empty;
            SampleCount = 5;
            Tolerance = 0.01;
        }

        [DataMember(Name = "detailWorksheetName", Order = 1)]
        public string DetailWorksheetName { get; set; }

        [DataMember(Name = "detailAddress", Order = 2)]
        public string DetailAddress { get; set; }

        [DataMember(Name = "detailTableName", Order = 3)]
        public string DetailTableName { get; set; }

        [DataMember(Name = "detailGroupColumn", Order = 4)]
        public string DetailGroupColumn { get; set; }

        [DataMember(Name = "detailValueColumn", Order = 5)]
        public string DetailValueColumn { get; set; }

        [DataMember(Name = "summaryWorksheetName", Order = 6)]
        public string SummaryWorksheetName { get; set; }

        [DataMember(Name = "summaryAddress", Order = 7)]
        public string SummaryAddress { get; set; }

        [DataMember(Name = "summaryTableName", Order = 8)]
        public string SummaryTableName { get; set; }

        [DataMember(Name = "summaryGroupColumn", Order = 9)]
        public string SummaryGroupColumn { get; set; }

        [DataMember(Name = "summaryValueColumn", Order = 10)]
        public string SummaryValueColumn { get; set; }

        [DataMember(Name = "sampleCount", Order = 11)]
        public int SampleCount { get; set; }

        [DataMember(Name = "tolerance", Order = 12)]
        public double Tolerance { get; set; }
    }

    [DataContract]
    public sealed class ExcelReconcileSample
    {
        public ExcelReconcileSample()
        {
            GroupKey = string.Empty;
            SelectionReason = string.Empty;
        }

        [DataMember(Name = "groupKey", Order = 1)]
        public string GroupKey { get; set; }

        [DataMember(Name = "detailValue", Order = 2)]
        public double DetailValue { get; set; }

        [DataMember(Name = "summaryValue", Order = 3)]
        public double SummaryValue { get; set; }

        [DataMember(Name = "difference", Order = 4)]
        public double Difference { get; set; }

        [DataMember(Name = "matched", Order = 5)]
        public bool Matched { get; set; }

        /// <summary>
        /// 这一组为何被抽中（最大值 / 最小值 / 零值 / 负值 / 均匀覆盖…）。
        /// 缺了它，「抽样通过」等于「我抽了几个恰好没问题的」，没有任何说服力。
        /// </summary>
        [DataMember(Name = "selectionReason", Order = 6)]
        public string SelectionReason { get; set; }
    }

    [DataContract]
    public sealed class ExcelReconcileResult
    {
        public ExcelReconcileResult()
        {
            WorkbookName = string.Empty;
            DetailAddress = string.Empty;
            SummaryAddress = string.Empty;
            Conclusion = string.Empty;
            Samples = new List<ExcelReconcileSample>();
            MissingInSummary = new List<string>();
            ExtraInSummary = new List<string>();
            DetailCoverage = new ExcelCoverageReport();
            SummaryCoverage = new ExcelCoverageReport();
        }

        [DataMember(Name = "workbookName", Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Name = "detailAddress", Order = 2)]
        public string DetailAddress { get; set; }

        [DataMember(Name = "summaryAddress", Order = 3)]
        public string SummaryAddress { get; set; }

        [DataMember(Name = "passed", Order = 4)]
        public bool Passed { get; set; }

        [DataMember(Name = "detailRowCount", Order = 5)]
        public int DetailRowCount { get; set; }

        [DataMember(Name = "detailGroupCount", Order = 6)]
        public int DetailGroupCount { get; set; }

        [DataMember(Name = "summaryRowCount", Order = 7)]
        public int SummaryRowCount { get; set; }

        [DataMember(Name = "detailTotal", Order = 8)]
        public double DetailTotal { get; set; }

        [DataMember(Name = "summaryTotal", Order = 9)]
        public double SummaryTotal { get; set; }

        [DataMember(Name = "totalDifference", Order = 10)]
        public double TotalDifference { get; set; }

        [DataMember(Name = "totalMatched", Order = 11)]
        public bool TotalMatched { get; set; }

        [DataMember(Name = "groupCountMatched", Order = 12)]
        public bool GroupCountMatched { get; set; }

        [DataMember(Name = "sampleMatched", Order = 13)]
        public bool SampleMatched { get; set; }

        [DataMember(Name = "samples", Order = 14)]
        public List<ExcelReconcileSample> Samples { get; set; }

        [DataMember(Name = "missingInSummary", Order = 15)]
        public List<string> MissingInSummary { get; set; }

        [DataMember(Name = "extraInSummary", Order = 16)]
        public List<string> ExtraInSummary { get; set; }

        [DataMember(Name = "conclusion", Order = 17)]
        public string Conclusion { get; set; }

        [DataMember(Name = "detailCoverage", Order = 18)]
        public ExcelCoverageReport DetailCoverage { get; set; }

        [DataMember(Name = "summaryCoverage", Order = 19)]
        public ExcelCoverageReport SummaryCoverage { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Verification, Order = 20, EmitDefaultValue = false)]
        public ExcelVerificationResult Verification { get; set; }
    }
}
