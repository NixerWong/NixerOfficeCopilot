using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelCoverageReport
    {
        public ExcelCoverageReport()
        {
            Backend = "managed";
            CellCountsAvailable = true;
        }

        [DataMember(Name = "totalRows", Order = 1)]
        public int TotalRows { get; set; }

        [DataMember(Name = "scannedRows", Order = 2)]
        public int ScannedRows { get; set; }

        /// <summary>非空且不是原生数值的单元格数；包含可解析数字文本和错误值。</summary>
        [DataMember(Name = "nonNumericCount", Order = 3)]
        public int NonNumericCount { get; set; }

        /// <summary>NonNumericCount 中可解析为数字的文本单元格数。</summary>
        [DataMember(Name = "textNumberCount", Order = 4)]
        public int TextNumberCount { get; set; }

        /// <summary>NonNumericCount 中的 Excel 错误值单元格数。</summary>
        [DataMember(Name = "errorCount", Order = 5)]
        public int ErrorCount { get; set; }

        [DataMember(Name = "blankCount", Order = 6)]
        public int BlankCount { get; set; }

        [DataMember(Name = "backend", Order = 7)]
        public string Backend { get; set; }

        [DataMember(Name = "cellCountsAvailable", Order = 8)]
        public bool? CellCountsAvailable { get; set; }
    }
}
