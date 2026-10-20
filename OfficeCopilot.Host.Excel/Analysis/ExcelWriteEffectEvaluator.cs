using System.Collections.Generic;
using OfficeCopilot.Agent.Abstractions;

namespace OfficeCopilot.Host.Excel.Analysis
{
    internal sealed class ExcelWriteEffectInput
    {
        public bool IsRegisteredWipSheet { get; set; }

        public bool IsNewRegisteredWipWorksheetCreation { get; set; }

        public bool IsNewFormalSheetThisTurn { get; set; }

        public bool IsOtherWorkbook { get; set; }

        public bool IsStructuralOperation { get; set; }

        public bool IsContractSource { get; set; }

        public bool IntersectsBaselineUsedRange { get; set; }

        public bool IsFormatOperation { get; set; }

        public int? NonEmptyCellCountInIntersection { get; set; }

        public int? FormulaCountInIntersection { get; set; }

        public string TargetWorkbook { get; set; }

        public string TargetWorksheet { get; set; }

        public string TargetAddress { get; set; }

        public IList<string> ListObjectNames { get; set; }

        public IList<string> PivotTableNames { get; set; }
    }

    internal sealed class ExcelWriteEffectEvaluation
    {
        public string Effect { get; set; }

        public int? OverwriteNonEmptyCellCount { get; set; }

        public int? OverwriteFormulaCount { get; set; }

        public string OperationSummary { get; set; }
    }

    internal static class ExcelWriteEffectEvaluator
    {
        public static ExcelWriteEffectEvaluation Evaluate(ExcelWriteEffectInput input)
        {
            var effect = EvaluateEffect(input);
            return new ExcelWriteEffectEvaluation
            {
                Effect = effect,
                OverwriteNonEmptyCellCount = input == null ? (int?)null : input.NonEmptyCellCountInIntersection,
                OverwriteFormulaCount = input == null ? (int?)null : input.FormulaCountInIntersection,
                OperationSummary = BuildOperationSummary(input, effect)
            };
        }

        private static string EvaluateEffect(ExcelWriteEffectInput input)
        {
            if (input == null)
            {
                return ToolWriteEffectLevels.Modify;
            }

            if ((input.IsRegisteredWipSheet || input.IsNewRegisteredWipWorksheetCreation)
                && !input.IsOtherWorkbook)
            {
                return ToolWriteEffectLevels.Scratch;
            }

            if (input.IsNewFormalSheetThisTurn && !input.IsOtherWorkbook)
            {
                return ToolWriteEffectLevels.Append;
            }

            if (input.IsOtherWorkbook
                || input.IsStructuralOperation
                || input.IsContractSource)
            {
                return ToolWriteEffectLevels.Modify;
            }

            if (!input.IntersectsBaselineUsedRange)
            {
                return ToolWriteEffectLevels.Append;
            }

            if (input.IsFormatOperation)
            {
                return ToolWriteEffectLevels.Modify;
            }

            return input.NonEmptyCellCountInIntersection.HasValue
                && input.NonEmptyCellCountInIntersection.Value == 0
                ? ToolWriteEffectLevels.Append
                : ToolWriteEffectLevels.Modify;
        }

        private static string BuildOperationSummary(ExcelWriteEffectInput input, string effect)
        {
            if (input == null)
            {
                return "无法解析目标区域；按 modify 级别保守处理。";
            }

            var parts = new List<string>();
            var target = JoinTarget(input.TargetWorkbook, input.TargetWorksheet, input.TargetAddress);
            if (!string.IsNullOrWhiteSpace(target))
            {
                parts.Add("目标：" + target);
            }

            parts.Add("级别：" + effect);
            parts.Add(input.NonEmptyCellCountInIntersection.HasValue
                ? "覆盖非空单元格：" + input.NonEmptyCellCountInIntersection.Value
                : "覆盖非空单元格：未知");
            parts.Add(input.FormulaCountInIntersection.HasValue
                ? "其中公式：" + input.FormulaCountInIntersection.Value
                : "其中公式：未知");
            AppendNames(parts, "表格", input.ListObjectNames);
            AppendNames(parts, "数据透视表", input.PivotTableNames);
            return string.Join("；", parts);
        }

        private static string JoinTarget(string workbook, string worksheet, string address)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(workbook))
            {
                parts.Add(workbook.Trim());
            }

            if (!string.IsNullOrWhiteSpace(worksheet))
            {
                parts.Add(worksheet.Trim());
            }

            if (!string.IsNullOrWhiteSpace(address))
            {
                parts.Add(address.Trim());
            }

            return string.Join(" / ", parts);
        }

        private static void AppendNames(IList<string> parts, string label, IList<string> names)
        {
            var values = names == null
                ? new List<string>()
                : new List<string>(names);
            parts.Add(values.Count == 0
                ? label + "：无或未知"
                : label + "：" + string.Join(", ", values));
        }
    }
}
