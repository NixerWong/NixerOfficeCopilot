using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelRangeRequest
    {
        public ExcelRangeRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            MaxRows = 50;
            MaxColumns = 20;
            UseSelectionWhenAddressEmpty = true;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "maxRows", Order = 3)]
        public int MaxRows { get; set; }

        [DataMember(Name = "maxColumns", Order = 4)]
        public int MaxColumns { get; set; }

        [DataMember(Name = "useSelectionWhenAddressEmpty", Order = 5)]
        public bool UseSelectionWhenAddressEmpty { get; set; }

        [DataMember(Name = "includeHiddenRows", Order = 6)]
        public bool IncludeHiddenRows { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeFormulaResult
    {
        public ExcelRangeFormulaResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            Formulas = new List<string[]>();
            Values = new List<string[]>();
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
        public List<string[]> Formulas { get; set; }

        [DataMember(Order = 8)]
        public List<string[]> Values { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeWriteResult
    {
        public ExcelRangeWriteResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            ValueKind = string.Empty;
            Preview = string.Empty;
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
        public string ValueKind { get; set; }

        [DataMember(Order = 8)]
        public string Preview { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeClearResult
    {
        public ExcelRangeClearResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
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
        public int ClearedCellCount { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeInfoResult
    {
        public ExcelRangeInfoResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            NumberFormat = string.Empty;
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
        public string NumberFormat { get; set; }

        [DataMember(Order = 7)]
        public double Left { get; set; }

        [DataMember(Order = 8)]
        public double Top { get; set; }

        [DataMember(Order = 9)]
        public double Width { get; set; }

        [DataMember(Order = 10)]
        public double Height { get; set; }

        [DataMember(Name = "hiddenRowsScanComplete", Order = 11)]
        public bool HiddenRowsScanComplete { get; set; }

        [DataMember(Name = "hiddenRowsScanned", Order = 12, EmitDefaultValue = false)]
        public int HiddenRowsScanned { get; set; }

        [DataMember(Name = "hiddenRowNumbers", Order = 13, EmitDefaultValue = false)]
        public List<int> HiddenRowNumbers { get; set; }

        [DataMember(Name = "hiddenRowCount", Order = 14, EmitDefaultValue = false)]
        public int HiddenRowCount { get; set; }

        [DataMember(Name = "verification", Order = 15, EmitDefaultValue = false)]
        public ExcelVerificationResult Verification { get; set; }
    }

    [DataContract]
    public sealed class ExcelHyperlinkInfo
    {
        public ExcelHyperlinkInfo()
        {
            CellAddress = string.Empty;
            Address = string.Empty;
            SubAddress = string.Empty;
            DisplayText = string.Empty;
            ScreenTip = string.Empty;
        }

        [DataMember(Order = 1)]
        public string CellAddress { get; set; }

        [DataMember(Order = 2)]
        public string Address { get; set; }

        [DataMember(Order = 3)]
        public string SubAddress { get; set; }

        [DataMember(Order = 4)]
        public string DisplayText { get; set; }

        [DataMember(Order = 5)]
        public string ScreenTip { get; set; }

        [DataMember(Order = 6)]
        public bool IsInternal { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeHyperlinkResult
    {
        public ExcelRangeHyperlinkResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            Hyperlinks = new List<ExcelHyperlinkInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string Address { get; set; }

        [DataMember(Order = 4)]
        public List<ExcelHyperlinkInfo> Hyperlinks { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeValidationResult
    {
        public ExcelRangeValidationResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            ValidationType = string.Empty;
            ValidationOperator = string.Empty;
            Formula1 = string.Empty;
            Formula2 = string.Empty;
            InputTitle = string.Empty;
            InputMessage = string.Empty;
            ErrorStyle = string.Empty;
            ErrorTitle = string.Empty;
            ValidationErrorMessage = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string Address { get; set; }

        [DataMember(Order = 4)]
        public bool HasValidation { get; set; }

        [DataMember(Order = 5)]
        public string ValidationType { get; set; }

        [DataMember(Order = 6)]
        public string ValidationOperator { get; set; }

        [DataMember(Order = 7)]
        public string Formula1 { get; set; }

        [DataMember(Order = 8)]
        public string Formula2 { get; set; }

        [DataMember(Order = 9)]
        public bool IgnoreBlank { get; set; }

        [DataMember(Order = 10)]
        public bool ShowInputMessage { get; set; }

        [DataMember(Order = 11)]
        public string InputTitle { get; set; }

        [DataMember(Order = 12)]
        public string InputMessage { get; set; }

        [DataMember(Order = 13)]
        public bool ShowErrorAlert { get; set; }

        [DataMember(Order = 14)]
        public string ErrorStyle { get; set; }

        [DataMember(Order = 15)]
        public string ErrorTitle { get; set; }

        [DataMember(Order = 16)]
        public string ValidationErrorMessage { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeNumberFormatResult
    {
        public ExcelRangeNumberFormatResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            Formats = new List<string[]>();
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
        public List<string[]> Formats { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeMergeInfoResult
    {
        public ExcelRangeMergeInfoResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            MergeAreaAddress = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string Address { get; set; }

        [DataMember(Order = 4)]
        public bool IsMerged { get; set; }

        [DataMember(Order = 5)]
        public string MergeAreaAddress { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeLockInfoResult
    {
        public ExcelRangeLockInfoResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string Address { get; set; }

        [DataMember(Order = 4)]
        public bool IsLocked { get; set; }

        [DataMember(Order = 5)]
        public bool IsMixed { get; set; }
    }

    [DataContract]
    public sealed class ExcelRangeStyleResult
    {
        public ExcelRangeStyleResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            StyleName = string.Empty;
            StyleDescription = string.Empty;
            FillColor = string.Empty;
            FontColor = string.Empty;
            FontName = string.Empty;
            FontSize = string.Empty;
            Bold = string.Empty;
            Italic = string.Empty;
            Underline = string.Empty;
            NumberFormat = string.Empty;
            HorizontalAlignment = string.Empty;
            VerticalAlignment = string.Empty;
            Borders = string.Empty;
            MixedProperties = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string WorksheetName { get; set; }

        [DataMember(Order = 3)]
        public string Address { get; set; }

        [DataMember(Order = 4)]
        public string StyleName { get; set; }

        [DataMember(Order = 5)]
        public bool IsBuiltInStyle { get; set; }

        [DataMember(Order = 6)]
        public string StyleDescription { get; set; }

        /// <summary>区域内单元格数量，用于判断 mixed 的含义（单格永远不会 mixed）。</summary>
        [DataMember(Order = 7)]
        public int CellCount { get; set; }

        // 以下回读字段一律用字符串承载三态：具体值 / "mixed"（区域内不一致）/ "unknown"（读取失败）。
        // 用可空布尔或空字符串表达 mixed 会让模型把"读不到"当成"没有设置"，而这正是 D8 的成因。

        [DataMember(Order = 8)]
        public string FillColor { get; set; }

        [DataMember(Order = 9)]
        public string FontColor { get; set; }

        [DataMember(Order = 10)]
        public string FontName { get; set; }

        [DataMember(Order = 11)]
        public string FontSize { get; set; }

        [DataMember(Order = 12)]
        public string Bold { get; set; }

        [DataMember(Order = 13)]
        public string Italic { get; set; }

        [DataMember(Order = 14)]
        public string Underline { get; set; }

        [DataMember(Order = 15)]
        public string NumberFormat { get; set; }

        [DataMember(Order = 16)]
        public string HorizontalAlignment { get; set; }

        [DataMember(Order = 17)]
        public string VerticalAlignment { get; set; }

        /// <summary>边框摘要，如 "left,right,top,bottom" / "none" / "mixed"。</summary>
        [DataMember(Order = 18)]
        public string Borders { get; set; }

        /// <summary>取值为 mixed 的字段名清单，逗号分隔；便于模型一眼看出哪些结论不能下。</summary>
        [DataMember(Order = 19)]
        public string MixedProperties { get; set; }
    }

    [DataContract]
    public sealed class ExcelWorksheetScreenshotRequest
    {
        public ExcelWorksheetScreenshotRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            ImageFileName = string.Empty;
            UseSelectionWhenAddressEmpty = false;
            UseUsedRangeWhenAddressEmpty = true;
            MaxImageWidth = 1600;
            MaxImageHeight = 1200;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "imageFileName", Order = 3)]
        public string ImageFileName { get; set; }

        [DataMember(Name = "useSelectionWhenAddressEmpty", Order = 4)]
        public bool UseSelectionWhenAddressEmpty { get; set; }

        [DataMember(Name = "useUsedRangeWhenAddressEmpty", Order = 5)]
        public bool UseUsedRangeWhenAddressEmpty { get; set; }

        [DataMember(Name = "maxImageWidth", Order = 6)]
        public int MaxImageWidth { get; set; }

        [DataMember(Name = "maxImageHeight", Order = 7)]
        public int MaxImageHeight { get; set; }
    }

    [DataContract]
    public sealed class ExcelWorksheetScreenshotResult
    {
        public ExcelWorksheetScreenshotResult()
        {
            WorkbookName = string.Empty;
            WorksheetName = string.Empty;
            Address = string.Empty;
            CaptureSource = string.Empty;
            MimeType = "image/png";
            ImageBase64 = string.Empty;
            SavedImagePath = string.Empty;
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
        public string CaptureSource { get; set; }

        [DataMember(Order = 7)]
        public string MimeType { get; set; }

        [DataMember(Order = 8)]
        public int ImageWidth { get; set; }

        [DataMember(Order = 9)]
        public int ImageHeight { get; set; }

        [DataMember(Order = 10)]
        public int ByteCount { get; set; }

        [DataMember(Order = 11)]
        public string ImageBase64 { get; set; }

        [DataMember(Order = 12)]
        public string SavedImagePath { get; set; }
    }
}