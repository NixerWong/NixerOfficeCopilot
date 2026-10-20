using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelRangeFormulaWriteRequest
    {
        public ExcelRangeFormulaWriteRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            Formula = string.Empty;
            Formulas = new List<string[]>();
            UseSelectionWhenAddressEmpty = true;
            FallbackToVba = true;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "formula", Order = 3)]
        public string Formula { get; set; }

        [DataMember(Name = "formulas", Order = 4)]
        public List<string[]> Formulas { get; set; }

        [DataMember(Name = "useSelectionWhenAddressEmpty", Order = 5)]
        public bool UseSelectionWhenAddressEmpty { get; set; }

        [DataMember(Name = "fallbackToVba", Order = 6)]
        public bool FallbackToVba { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeFormulaWriteResult
    {
        public ExcelRangeFormulaWriteResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            FormulaKind = string.Empty;
            Preview = string.Empty;
            AppliedBy = string.Empty;
            VbaModuleName = string.Empty;
            VbaEntryPoint = string.Empty;
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
        public int WrittenCellCount { get; set; }

        [DataMember(Order = 7)]
        public string FormulaKind { get; set; }

        [DataMember(Order = 8)]
        public string Preview { get; set; }

        [DataMember(Order = 9)]
        public string AppliedBy { get; set; }

        [DataMember(Order = 10)]
        public bool FallbackUsed { get; set; }

        [DataMember(Order = 11)]
        public string VbaModuleName { get; set; }

        [DataMember(Order = 12)]
        public string VbaEntryPoint { get; set; }
    }
}