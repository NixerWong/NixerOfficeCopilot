using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelRangeFormatWriteRequest
    {
        public ExcelRangeFormatWriteRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            NumberFormat = string.Empty;
            StyleName = string.Empty;
            HorizontalAlignment = string.Empty;
            VerticalAlignment = string.Empty;
            FontName = string.Empty;
            FontColor = string.Empty;
            FillColor = string.Empty;
            UseSelectionWhenAddressEmpty = true;
            FallbackToVba = true;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "numberFormat", Order = 3)]
        public string NumberFormat { get; set; }

        [DataMember(Name = "styleName", Order = 4)]
        public string StyleName { get; set; }

        [DataMember(Name = "horizontalAlignment", Order = 5)]
        public string HorizontalAlignment { get; set; }

        [DataMember(Name = "verticalAlignment", Order = 6)]
        public string VerticalAlignment { get; set; }

        [DataMember(Name = "wrapText", Order = 7, EmitDefaultValue = false)]
        public bool? WrapText { get; set; }

        [DataMember(Name = "shrinkToFit", Order = 8, EmitDefaultValue = false)]
        public bool? ShrinkToFit { get; set; }

        [DataMember(Name = "fontName", Order = 9)]
        public string FontName { get; set; }

        [DataMember(Name = "fontSize", Order = 10, EmitDefaultValue = false)]
        public double? FontSize { get; set; }

        [DataMember(Name = "bold", Order = 11, EmitDefaultValue = false)]
        public bool? Bold { get; set; }

        [DataMember(Name = "italic", Order = 12, EmitDefaultValue = false)]
        public bool? Italic { get; set; }

        [DataMember(Name = "underline", Order = 13, EmitDefaultValue = false)]
        public bool? Underline { get; set; }

        [DataMember(Name = "fontColor", Order = 14)]
        public string FontColor { get; set; }

        [DataMember(Name = "fillColor", Order = 15)]
        public string FillColor { get; set; }

        [DataMember(Name = "useSelectionWhenAddressEmpty", Order = 16)]
        public bool UseSelectionWhenAddressEmpty { get; set; }

        [DataMember(Name = "fallbackToVba", Order = 17)]
        public bool FallbackToVba { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeFormatWriteResult
    {
        public ExcelRangeFormatWriteResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            Summary = string.Empty;
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
        public int AppliedPropertyCount { get; set; }

        [DataMember(Order = 7)]
        public string Summary { get; set; }

        [DataMember(Order = 8)]
        public string AppliedBy { get; set; }

        [DataMember(Order = 9)]
        public bool FallbackUsed { get; set; }

        [DataMember(Order = 10)]
        public string VbaModuleName { get; set; }

        [DataMember(Order = 11)]
        public string VbaEntryPoint { get; set; }
    }
}