using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Verification.Backends.Com;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel.Verification
{
    internal sealed class DataQualityColumnStatistics
    {
        public string Name { get; set; }

        public int NumericCount { get; set; }

        public int NonEmptyCount { get; set; }

        public int TextNumberCount { get; set; }

        public IList<int> TextNumberSampleRows { get; set; }
    }

    internal sealed class DataQualityProfileSnapshot
    {
        public bool HasHeader { get; set; }

        public object[] RawHeaderValues { get; set; }

        public int HeaderRow { get; set; }

        public int DataRowCount { get; set; }

        public IList<DataQualityColumnStatistics> Columns { get; set; }

        public int SubtotalLabelCount { get; set; }

        public IList<int> SubtotalLabelRows { get; set; }

        public int FirstDataRowNonEmptyCount { get; set; }

        public int FirstDataRowTextCount { get; set; }

        public int LaterDataRowsNonEmptyCount { get; set; }

        public int LaterDataRowsNumericCount { get; set; }
    }

    internal sealed class DataQualityInspectionResult
    {
        public DataQualityInspectionResult()
        {
            Flags = new List<ExcelDataQualityFlag>();
            Notes = new List<string>();
        }

        public List<ExcelDataQualityFlag> Flags { get; private set; }

        public List<string> Notes { get; private set; }
    }

    internal static class DataQualityInspector
    {
        private const int MaximumComColumnsToInspect = 8;

        internal static bool IsSubtotalLabel(object value)
        {
            var text = value as string;
            if (text == null)
            {
                return false;
            }

            text = text.Trim();
            return text.EndsWith("小计", StringComparison.Ordinal)
                || text.EndsWith("合计", StringComparison.Ordinal)
                || text.EndsWith("总计", StringComparison.Ordinal)
                || string.Equals(text, "subtotal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "total", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "grand total", StringComparison.OrdinalIgnoreCase)
                || text.EndsWith(" total", StringComparison.OrdinalIgnoreCase);
        }

        internal static List<ExcelDataQualityFlag> InspectManaged(DataQualityProfileSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var flags = new List<ExcelDataQualityFlag>();
            if (snapshot.SubtotalLabelCount > 0)
            {
                flags.Add(new ExcelDataQualityFlag
                {
                    Name = "subtotalLabelRows",
                    Count = snapshot.SubtotalLabelCount,
                    SampleRows = LimitRows(snapshot.SubtotalLabelRows),
                    Impact = "标签行可能是人工汇总或小计，不应与明细行重复累计。",
                    Recommendation = "确认汇总口径；计算明细总额时排除这些小计/合计行。"
                });
            }

            var numericDominantColumns = (snapshot.Columns ?? new List<DataQualityColumnStatistics>())
                .Where(column => column != null
                    && column.NonEmptyCount > 0
                    && column.NumericCount > 0
                    && (long)column.NumericCount * 2 >= column.NonEmptyCount
                    && column.TextNumberCount > 0)
                .ToList();
            var textNumberCount = numericDominantColumns.Sum(column => column.TextNumberCount);
            if (textNumberCount > 0)
            {
                flags.Add(new ExcelDataQualityFlag
                {
                    Name = "textNumbers",
                    Count = textNumberCount,
                    SampleRows = LimitRows(numericDominantColumns
                        .SelectMany(column => column.TextNumberSampleRows ?? new int[0])),
                    Impact = "数值主导列含有以文本存储的数字；排序、筛选或公式可能不会按数值处理。",
                    Recommendation = "将这些单元格转换为真正的数值，并核对区域/小数点格式。"
                });
            }

            var suspiciousHeaderCount = 0;
            var suspiciousHeaderRows = new List<int>();
            if (snapshot.HasHeader)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rawHeader in snapshot.RawHeaderValues ?? new object[0])
                {
                    var text = rawHeader as string;
                    var normalized = text == null ? string.Empty : text.Trim();
                    if (normalized.Length == 0 || !seen.Add(normalized))
                    {
                        suspiciousHeaderCount++;
                        AddRow(suspiciousHeaderRows, snapshot.HeaderRow);
                    }
                }
            }

            if (snapshot.DataRowCount > 1
                && snapshot.FirstDataRowNonEmptyCount > 0
                && (long)snapshot.FirstDataRowTextCount * 2 > snapshot.FirstDataRowNonEmptyCount
                && snapshot.LaterDataRowsNonEmptyCount > 0
                && (long)snapshot.LaterDataRowsNumericCount * 2 > snapshot.LaterDataRowsNonEmptyCount)
            {
                suspiciousHeaderCount++;
                AddRow(suspiciousHeaderRows, snapshot.HeaderRow + (snapshot.HasHeader ? 1 : 0));
            }

            if (suspiciousHeaderCount > 0)
            {
                flags.Add(new ExcelDataQualityFlag
                {
                    Name = "suspiciousHeader",
                    Count = suspiciousHeaderCount,
                    SampleRows = LimitRows(suspiciousHeaderRows),
                    Impact = "表头可能为空、重复，或首条数据行看起来像被误判为表头。",
                    Recommendation = "检查表头行与 headerMode；必要时显式指定 headerMode 为 yes 或 no。"
                });
            }

            return flags;
        }

        internal static DataQualityInspectionResult InspectCom(
            ExcelInterop.Application application,
            ExcelInterop.Range sourceRange,
            ExcelColumnReadLayout layout,
            DataQualityProfileSnapshot snapshot)
        {
            if (application == null)
            {
                throw new ArgumentNullException(nameof(application));
            }

            if (sourceRange == null)
            {
                throw new ArgumentNullException(nameof(sourceRange));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var result = new DataQualityInspectionResult();
            if (layout.TotalRows > 0)
            {
                if (layout.TotalRows > 100000)
                {
                    result.Notes.Add(
                        "COM 质量指纹最多检查 100,000 行；本区域超限，公式小计及隐藏行指纹未检查。");
                }
                else
                {
                    var address = ComCalcBackend.GetRangeAddress(sourceRange);
                    if (address.Length > 255)
                    {
                        result.Notes.Add(
                            "COM 质量指纹的区域地址超过 255 个字符；公式小计及隐藏行指纹未检查。");
                    }
                    else
                    {
                        InspectFingerprints(application, sourceRange, layout, snapshot, result);
                    }
                }
            }

            var calculationMode = application.Calculation;
            var calculationState = application.CalculationState;
            if (calculationMode == ExcelInterop.XlCalculation.xlCalculationManual
                || calculationState != ExcelInterop.XlCalculationState.xlDone)
            {
                result.Flags.Add(new ExcelDataQualityFlag
                {
                    Name = "staleCalculation",
                    Impact = "工作簿处于手动计算模式或计算尚未完成，公式缓存值可能不是最新结果。",
                    Recommendation = "确认公式依赖并在 Excel 中重算后，再使用分析结果。"
                });
            }

            var mergedCells = ReadMergedCellState(sourceRange);
            if (mergedCells == true)
            {
                result.Flags.Add(new ExcelDataQualityFlag
                {
                    Name = "mergedCells",
                    Impact = "数据区域包含合并单元格；合并区域中的空白位置可能被误读为缺失值。",
                    Recommendation = "分析前取消合并并填充每行所需的键值；或将合并布局转换为规范表格。"
                });
            }
            else if (!mergedCells.HasValue)
            {
                result.Notes.Add("无法确定数据区域是否包含合并单元格，未将未知状态当作无风险。");
            }

            return result;
        }

        internal static DataQualityProfileSnapshot CreateSnapshot(ExcelAggregationService.ProfileChunkSink sink)
        {
            if (sink == null || sink.Layout == null || sink.ColumnAccumulators == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            var columns = new List<DataQualityColumnStatistics>();
            for (var index = 0; index < sink.ColumnAccumulators.Length; index++)
            {
                var accumulator = sink.ColumnAccumulators[index];
                columns.Add(new DataQualityColumnStatistics
                {
                    Name = sink.Layout.Columns[index],
                    NumericCount = accumulator.NumericCount,
                    NonEmptyCount = accumulator.NonEmptyCount,
                    TextNumberCount = accumulator.TextNumberCount,
                    TextNumberSampleRows = accumulator.TextNumberSampleRows.ToList()
                });
            }

            return new DataQualityProfileSnapshot
            {
                HasHeader = sink.Layout.HasHeader,
                RawHeaderValues = sink.Layout.RawHeaderValues,
                HeaderRow = sink.Layout.SourceFirstRow,
                DataRowCount = sink.Layout.TotalRows,
                Columns = columns,
                SubtotalLabelCount = sink.SubtotalLabelRows.Count,
                SubtotalLabelRows = sink.SubtotalLabelRows,
                FirstDataRowNonEmptyCount = sink.FirstDataRowNonEmptyCount,
                FirstDataRowTextCount = sink.FirstDataRowTextCount,
                LaterDataRowsNonEmptyCount = sink.LaterDataRowsNonEmptyCount,
                LaterDataRowsNumericCount = sink.LaterDataRowsNumericCount
            };
        }

        private static void InspectFingerprints(
            ExcelInterop.Application application,
            ExcelInterop.Range sourceRange,
            ExcelColumnReadLayout layout,
            DataQualityProfileSnapshot snapshot,
            DataQualityInspectionResult result)
        {
            var candidates = (snapshot.Columns ?? new List<DataQualityColumnStatistics>())
                .Select((column, index) => new { Column = column, Index = index })
                .Where(item => item.Column != null && item.Column.NonEmptyCount > 0)
                .OrderByDescending(item => item.Column.NonEmptyCount)
                .ThenByDescending(item => item.Column.NumericCount)
                .ThenBy(item => item.Index)
                .ToList();
            if (candidates.Count == 0 && layout.Columns.Length > 0)
            {
                candidates.Add(new { Column = (DataQualityColumnStatistics)null, Index = 0 });
            }

            var inspector = new DataQualityFingerprintReader(application);
            var formulaRows = new List<int>();
            var formulaCount = 0;
            var hasNestedSubtotal = false;
            var nestedSubtotalUnknown = false;
            var hasHiddenRows = false;
            var hasFilter = false;
            var columnsToInspect = Math.Min(candidates.Count, MaximumComColumnsToInspect);
            for (var index = 0; index < columnsToInspect; index++)
            {
                var fingerprint = inspector.Read(sourceRange, layout, candidates[index].Index);
                formulaCount += fingerprint.SubtotalFormulaCount;
                formulaRows.AddRange(fingerprint.SubtotalFormulaRows);
                hasNestedSubtotal |= fingerprint.HasNestedSubtotalRows == true;
                nestedSubtotalUnknown |= !fingerprint.HasNestedSubtotalRows.HasValue;
                hasHiddenRows |= fingerprint.HasManuallyHiddenRows;
                hasFilter |= fingerprint.FilterMode;
            }

            if (candidates.Count > MaximumComColumnsToInspect)
            {
                result.Notes.Add(
                    "为控制 COM 开销，仅检查了数值最多的 "
                    + MaximumComColumnsToInspect
                    + " 列；其余 "
                    + (candidates.Count - MaximumComColumnsToInspect)
                    + " 个数值列未检查公式小计或隐藏行指纹。");
            }

            if (nestedSubtotalUnknown && formulaCount == 0 && !hasNestedSubtotal)
            {
                result.Notes.Add(
                    "SUM 与 SUBTOTAL(9) 差异无法归因或不可计算；嵌套小计状态未知，不代表未发现小计。");
            }

            formulaRows = LimitRows(formulaRows);
            if (formulaCount > 0 || hasNestedSubtotal)
            {
                result.Flags.Add(new ExcelDataQualityFlag
                {
                    Name = "subtotalFormulaRows",
                    Count = formulaCount > 0 ? formulaCount : (int?)null,
                    SampleRows = formulaRows,
                    Impact = "发现 SUBTOTAL 公式行或 SUM 与 SUBTOTAL(9) 存在差异；将其作为明细累计可能造成重复计算。",
                    Recommendation = "检查列出的公式行并确认汇总口径；SUM/SUBTOTAL 差异仅是风险信号，并非逐行证明。"
                });
            }

            if (hasHiddenRows || hasFilter)
            {
                result.Flags.Add(new ExcelDataQualityFlag
                {
                    Name = "hiddenOrFilteredRows",
                    Impact = "区域存在手动隐藏行或筛选状态；全量数据与当前可见数据的统计结果可能不同。",
                    Recommendation = "确认分析应使用所有记录还是仅使用当前可见记录，并在问题中明确口径。"
                });
            }
        }

        private static bool? ReadMergedCellState(ExcelInterop.Range range)
        {
            var value = range.MergeCells;
            if (value is bool)
            {
                return (bool)value;
            }

            return value == DBNull.Value ? (bool?)true : null;
        }

        private static List<int> LimitRows(IEnumerable<int> rows)
        {
            return (rows ?? Enumerable.Empty<int>())
                .Distinct()
                .OrderBy(row => row)
                .Take(10)
                .ToList();
        }

        private static void AddRow(ICollection<int> rows, int row)
        {
            if (row > 0 && !rows.Contains(row))
            {
                rows.Add(row);
            }
        }
    }
}
