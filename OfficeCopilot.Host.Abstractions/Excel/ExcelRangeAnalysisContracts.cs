using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelRangeFindRequest
    {
        public ExcelRangeFindRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            SearchValue = string.Empty;
            MatchCase = false;
            MatchEntireCell = false;
            SearchFormulas = false;
            UseSelectionWhenAddressEmpty = true;
            MaxResults = 100;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "searchValue", Order = 3)]
        public string SearchValue { get; set; }

        [DataMember(Name = "matchCase", Order = 4)]
        public bool MatchCase { get; set; }

        [DataMember(Name = "matchEntireCell", Order = 5)]
        public bool MatchEntireCell { get; set; }

        [DataMember(Name = "searchFormulas", Order = 6)]
        public bool SearchFormulas { get; set; }

        [DataMember(Name = "useSelectionWhenAddressEmpty", Order = 7)]
        public bool UseSelectionWhenAddressEmpty { get; set; }

        [DataMember(Name = "maxResults", Order = 8)]
        public int MaxResults { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeCellMatch
    {
        public ExcelRangeCellMatch()
        {
            Address = string.Empty;
            Value = string.Empty;
            Formula = string.Empty;
        }

        [DataMember(Order = 1)]
        public string Address { get; set; }

        [DataMember(Order = 2)]
        public int Row { get; set; }

        [DataMember(Order = 3)]
        public int Column { get; set; }

        [DataMember(Order = 4)]
        public string Value { get; set; }

        [DataMember(Order = 5)]
        public string Formula { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeFindResult
    {
        public ExcelRangeFindResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            SearchValue = string.Empty;
            MatchingCells = new List<ExcelRangeCellMatch>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string Address { get; set; }

        [DataMember(Order = 4)]
        public string SearchValue { get; set; }

        [DataMember(Order = 5)]
        public bool SearchFormulas { get; set; }

        [DataMember(Order = 6)]
        public bool MatchCase { get; set; }

        [DataMember(Order = 7)]
        public bool MatchEntireCell { get; set; }

        [DataMember(Order = 8)]
        public bool IsTruncated { get; set; }

        [DataMember(Order = 9)]
        public List<ExcelRangeCellMatch> MatchingCells { get; set; }
    }

    [DataContract]
    public sealed class ExcelAnalyzeRangeRequest
    {
        public ExcelAnalyzeRangeRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            Operation = "aggregate";
            Function = "max";
            ReturnAddress = string.Empty;
            UseSelectionWhenAddressEmpty = true;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "operation", Order = 3)]
        public string Operation { get; set; }

        [DataMember(Name = "function", Order = 4)]
        public string Function { get; set; }

        [DataMember(Name = "returnAddress", Order = 5)]
        public string ReturnAddress { get; set; }

        [DataMember(Name = "useSelectionWhenAddressEmpty", Order = 6)]
        public bool UseSelectionWhenAddressEmpty { get; set; }
    }

    [DataContract]
    public sealed class ExcelAnalyzeRangeResult
    {
        public ExcelAnalyzeRangeResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            Operation = string.Empty;
            Function = string.Empty;
            ReturnAddress = string.Empty;
            ValueText = string.Empty;
            ValueType = string.Empty;
            MatchAddress = string.Empty;
            MatchedReturnAddress = string.Empty;
            Engine = string.Empty;
            MatchedReturnValues = new List<string>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string Address { get; set; }

        [DataMember(Order = 4)]
        public string Operation { get; set; }

        [DataMember(Order = 5)]
        public string Function { get; set; }

        [DataMember(Order = 6)]
        public string ReturnAddress { get; set; }

        [DataMember(Order = 7)]
        public string ValueText { get; set; }

        [DataMember(Order = 8)]
        public string ValueType { get; set; }

        [DataMember(Order = 9)]
        public int MatchIndex { get; set; }

        [DataMember(Order = 10)]
        public string MatchAddress { get; set; }

        [DataMember(Order = 11)]
        public string MatchedReturnAddress { get; set; }

        [DataMember(Order = 12)]
        public List<string> MatchedReturnValues { get; set; }

        [DataMember(Order = 13)]
        public string Engine { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeFormulaValidationRequest
    {
        public ExcelRangeFormulaValidationRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            UseSelectionWhenAddressEmpty = true;
            MaxRows = 100;
            MaxColumns = 30;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "useSelectionWhenAddressEmpty", Order = 3)]
        public bool UseSelectionWhenAddressEmpty { get; set; }

        [DataMember(Name = "maxRows", Order = 4)]
        public int MaxRows { get; set; }

        [DataMember(Name = "maxColumns", Order = 5)]
        public int MaxColumns { get; set; }
    }

    [DataContract]
    public sealed class ExcelFormulaValidationError
    {
        public ExcelFormulaValidationError()
        {
            CellAddress = string.Empty;
            Formula = string.Empty;
            Message = string.Empty;
            Suggestion = string.Empty;
            Category = string.Empty;
        }

        [DataMember(Order = 1)]
        public string CellAddress { get; set; }

        [DataMember(Order = 2)]
        public int Row { get; set; }

        [DataMember(Order = 3)]
        public int Column { get; set; }

        [DataMember(Order = 4)]
        public string Formula { get; set; }

        [DataMember(Order = 5)]
        public string Message { get; set; }

        [DataMember(Order = 6)]
        public string Suggestion { get; set; }

        [DataMember(Order = 7)]
        public string Category { get; set; }
    }

    [DataContract]
    public sealed class ExcelFormulaValidationWarning
    {
        public ExcelFormulaValidationWarning()
        {
            CellAddress = string.Empty;
            Formula = string.Empty;
            Message = string.Empty;
            Category = string.Empty;
        }

        [DataMember(Order = 1)]
        public string CellAddress { get; set; }

        [DataMember(Order = 2)]
        public string Formula { get; set; }

        [DataMember(Order = 3)]
        public string Message { get; set; }

        [DataMember(Order = 4)]
        public string Category { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeFormulaValidationResult
    {
        public ExcelRangeFormulaValidationResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            Formulas = new List<string[]>();
            Errors = new List<ExcelFormulaValidationError>();
            Warnings = new List<ExcelFormulaValidationWarning>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string Address { get; set; }

        [DataMember(Order = 4)]
        public bool IsValid { get; set; }

        [DataMember(Order = 5)]
        public int FormulaCount { get; set; }

        [DataMember(Order = 6)]
        public int ValidCount { get; set; }

        [DataMember(Order = 7)]
        public int ErrorCount { get; set; }

        [DataMember(Order = 8)]
        public bool IsTruncated { get; set; }

        [DataMember(Order = 9)]
        public List<string[]> Formulas { get; set; }

        [DataMember(Order = 10)]
        public List<ExcelFormulaValidationError> Errors { get; set; }

        [DataMember(Order = 11)]
        public List<ExcelFormulaValidationWarning> Warnings { get; set; }
    }
}