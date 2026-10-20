using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelDataModelTableInfo
    {
        public ExcelDataModelTableInfo()
        {
            Name = string.Empty;
            SourceName = string.Empty;
        }

        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string SourceName { get; set; }

        [DataMember(Order = 3)]
        public int RecordCount { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelTableListResult
    {
        public ExcelDataModelTableListResult()
        {
            WorkbookName = string.Empty;
            Tables = new List<ExcelDataModelTableInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelDataModelTableInfo> Tables { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelMeasureInfo
    {
        public ExcelDataModelMeasureInfo()
        {
            Name = string.Empty;
            TableName = string.Empty;
            FormulaPreview = string.Empty;
            Description = string.Empty;
        }

        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string TableName { get; set; }

        [DataMember(Order = 3)]
        public string FormulaPreview { get; set; }

        [DataMember(Order = 4)]
        public string Description { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelMeasureListResult
    {
        public ExcelDataModelMeasureListResult()
        {
            WorkbookName = string.Empty;
            Measures = new List<ExcelDataModelMeasureInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelDataModelMeasureInfo> Measures { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelMeasureViewRequest
    {
        public ExcelDataModelMeasureViewRequest()
        {
            MeasureName = string.Empty;
            TableName = string.Empty;
        }

        [DataMember(Name = "measureName", Order = 1)]
        public string MeasureName { get; set; }

        [DataMember(Name = "tableName", Order = 2)]
        public string TableName { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelMeasureViewResult
    {
        public ExcelDataModelMeasureViewResult()
        {
            WorkbookName = string.Empty;
            MeasureName = string.Empty;
            TableName = string.Empty;
            DaxFormula = string.Empty;
            Description = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string MeasureName { get; set; }

        [DataMember(Order = 3)]
        public string TableName { get; set; }

        [DataMember(Order = 4)]
        public string DaxFormula { get; set; }

        [DataMember(Order = 5)]
        public string Description { get; set; }

        [DataMember(Order = 6)]
        public int CharacterCount { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelRelationshipInfo
    {
        public ExcelDataModelRelationshipInfo()
        {
            FromTable = string.Empty;
            FromColumn = string.Empty;
            ToTable = string.Empty;
            ToColumn = string.Empty;
        }

        [DataMember(Order = 1)]
        public string FromTable { get; set; }

        [DataMember(Order = 2)]
        public string FromColumn { get; set; }

        [DataMember(Order = 3)]
        public string ToTable { get; set; }

        [DataMember(Order = 4)]
        public string ToColumn { get; set; }

        [DataMember(Order = 5)]
        public bool IsActive { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelRelationshipListResult
    {
        public ExcelDataModelRelationshipListResult()
        {
            WorkbookName = string.Empty;
            Relationships = new List<ExcelDataModelRelationshipInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelDataModelRelationshipInfo> Relationships { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelCalculatedColumnInfo
    {
        public ExcelDataModelCalculatedColumnInfo()
        {
            Name = string.Empty;
            TableName = string.Empty;
            FormulaPreview = string.Empty;
            DataType = string.Empty;
            Description = string.Empty;
        }

        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string TableName { get; set; }

        [DataMember(Order = 3)]
        public string FormulaPreview { get; set; }

        [DataMember(Order = 4)]
        public string DataType { get; set; }

        [DataMember(Order = 5)]
        public string Description { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelCalculatedColumnListResult
    {
        public ExcelDataModelCalculatedColumnListResult()
        {
            WorkbookName = string.Empty;
            CalculatedColumns = new List<ExcelDataModelCalculatedColumnInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelDataModelCalculatedColumnInfo> CalculatedColumns { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelCalculatedColumnViewRequest
    {
        public ExcelDataModelCalculatedColumnViewRequest()
        {
            TableName = string.Empty;
            ColumnName = string.Empty;
        }

        [DataMember(Name = "tableName", Order = 1)]
        public string TableName { get; set; }

        [DataMember(Name = "columnName", Order = 2)]
        public string ColumnName { get; set; }
    }

    [DataContract]
    public sealed class ExcelDataModelCalculatedColumnViewResult
    {
        public ExcelDataModelCalculatedColumnViewResult()
        {
            WorkbookName = string.Empty;
            TableName = string.Empty;
            ColumnName = string.Empty;
            DaxFormula = string.Empty;
            DataType = string.Empty;
            Description = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string TableName { get; set; }

        [DataMember(Order = 3)]
        public string ColumnName { get; set; }

        [DataMember(Order = 4)]
        public string DaxFormula { get; set; }

        [DataMember(Order = 5)]
        public string DataType { get; set; }

        [DataMember(Order = 6)]
        public string Description { get; set; }

        [DataMember(Order = 7)]
        public int CharacterCount { get; set; }
    }
}