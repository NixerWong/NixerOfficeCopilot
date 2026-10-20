using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Planning
{
    internal sealed class StructuredExcelIntent
    {
        public StructuredExcelIntent(string skillName, double confidence, string reason)
        {
            SkillName = skillName ?? string.Empty;
            Confidence = confidence;
            Reason = reason ?? string.Empty;
        }

        public string SkillName { get; private set; }

        public double Confidence { get; private set; }

        public string Reason { get; private set; }
    }

    internal static class StructuredExcelIntentClassifier
    {
        private static readonly string[] FormattingTerms =
        {
            "格式", "样式", "字体", "背景", "颜色", "填充色", "边框", "对齐", "标红", "加粗", "斜体",
            "数字格式", "条件格式", "列宽", "行高", "冻结窗格", "美化", "排版", "两位小数",
            "format", "formatting", "style", "font", "background", "color", "colour", "fill color",
            "border", "alignment", "highlight", "bold", "italic", "number format", "conditional formatting",
            "column width", "row height", "freeze panes"
        };

        private static readonly string[] FormulaTerms =
        {
            "公式", "函数", "查找函数", "联动公式", "动态公式", "引用明细表", "随明细更新", "自动更新公式",
            "xlookup", "vlookup", "sumifs", "countifs", "sumproduct",
            "formula", "formulas", "formulae", "sum formula", "linked formula", "live formula",
            "dynamic formula", "recalculate", "link the summary", "link to source"
        };

        private static readonly string[] DataShapeTerms =
        {
            "复制", "拷贝", "提取到", "筛选到新表", "迁移", "移动", "搬运", "删除", "清除", "清空",
            "全部替换", "去重", "拆分", "排序", "重排", "转置", "展开", "改造",
            "提取", "宽表转成长表", "重命名工作表", "调整顺序", "转换为数值", "文本数字转换",
            "copy", "extract", "filter into", "migrate", "move", "delete", "clear",
            "replace all", "remove duplicate", "deduplicate", "split",
            "sort", "reorder", "transpose", "unpivot", "reshape", "convert to numbers"
        };

        private static readonly string[] OverwriteTerms =
        {
            "覆盖", "overwrite"
        };

        private static readonly string[] CoverageAnalysisTerms =
        {
            "覆盖几个", "覆盖多少", "覆盖哪些", "覆盖范围", "coverage", "cover how many"
        };

        private static readonly string[] AmbiguousMergeTerms =
        {
            "合并", "merge"
        };

        private static readonly string[] VirtualUnionTerms =
        {
            "union", "sources"
        };

        private static readonly string[] NewOutputTerms =
        {
            "新建工作表", "新增工作表", "创建工作表", "新建表", "创建新表", "新工作表",
            "汇总到新表", "写入新表", "输出到新表", "新建结果表",
            "new worksheet", "new sheet", "create a worksheet", "create a sheet", "add a worksheet",
            "add a new sheet", "output to a new sheet", "save to a new sheet"
        };

        private static readonly string[] CellValueWriteTerms =
        {
            "写入单元格", "填入单元格", "填写到单元格", "录入单元格", "设置单元格的值", "将值写入",
            "write to cell", "write a value to", "fill in cell", "set cell to", "set the cell to",
            "enter value in cell", "populate cell"
        };

        private static readonly string[] AnalysisTerms =
        {
            "查看", "读取", "分析", "统计", "检查", "计算", "求和", "求平均", "平均值", "总额", "合计", "总共",
            "加起来", "合起来", "几个", "几单", "没找到客户", "最大值", "最小值",
            "覆盖几个", "覆盖多少", "覆盖哪些", "覆盖范围",
            "排名", "分类汇总", "分组", "汇总", "占比", "对账", "核对", "多少", "哪个", "对比",
            "差异", "分布", "趋势", "查找", "找出", "列出", "报告",
            "inspect", "analyze", "analyse", "review", "read", "count", "calculate", "sum", "average",
            "highest", "lowest", "rank", "group by", "subtotal", "aggregate", "profile", "reconcile",
            "how many", "which", "compare", "difference", "distribution", "trend", "find", "list", "report",
            "show", "what is", "coverage", "cover how many"
        };

        private static readonly Regex NegatedFormulaMention = new Regex(
            @"(?:不要|不需要|不必|无需|不得|禁止|避免|do\s+not|don't|without|\bno\b)"
            + @"[^。！？.!?；;，,\r\n]{0,80}?(?:公式|函数|formul(?:a|ae)s?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static StructuredExcelIntent Classify(SkillPlannerRequest request)
        {
            if (request == null
                || !string.Equals(
                    SkillHostMatcher.Normalize(SkillHostMatcher.ResolveActual(request)),
                    "excel",
                    StringComparison.Ordinal))
            {
                return null;
            }

            var text = Normalize(request.UserInput);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var isFormatting = ContainsAny(text, FormattingTerms);
            var formulaIntentText = Normalize(
                NegatedFormulaMention.Replace(request.UserInput ?? string.Empty, string.Empty));
            var isFormula = ContainsAny(formulaIntentText, FormulaTerms);
            var targetsNewOutput = ContainsAny(text, NewOutputTerms);
            var writesCellValue = ContainsAny(text, CellValueWriteTerms);
            var hasAnalysisIntent = ContainsAny(text, AnalysisTerms);
            var describesVirtualUnionAnalysis = ContainsAny(text, VirtualUnionTerms)
                && hasAnalysisIntent
                && !targetsNewOutput;
            var isOverwrite = ContainsAny(text, OverwriteTerms)
                && !ContainsAny(text, CoverageAnalysisTerms);
            var isDataShapeChange = ContainsAny(text, DataShapeTerms)
                || isOverwrite
                || (ContainsAny(text, AmbiguousMergeTerms) && !describesVirtualUnionAnalysis);

            // Select the primary operation first; a single skill still carries all requested Excel tools.
            if (isDataShapeChange || targetsNewOutput)
            {
                return new StructuredExcelIntent(
                    "structural-transform",
                    0.90d,
                    "Classified an explicit data-shape or worksheet-output operation.");
            }

            if (isFormula)
            {
                return new StructuredExcelIntent(
                    "live-result",
                    0.91d,
                    "Classified a formula or source-linked result request from explicit formula evidence.");
            }

            if (writesCellValue)
            {
                return new StructuredExcelIntent(
                    "live-result",
                    0.86d,
                    "Classified an explicit cell-value write request.");
            }

            if (isFormatting)
            {
                return new StructuredExcelIntent(
                    "bulk-format",
                    0.92d,
                    "Classified a formatting request from explicit appearance or number-format terms.");
            }

            if (hasAnalysisIntent)
            {
                return new StructuredExcelIntent(
                    "read-analysis",
                    0.80d,
                    "Classified a read-only analysis or inspection request.");
            }

            return null;
        }

        private static bool ContainsAny(string text, string[] terms)
        {
            return terms.Any(term => ContainsTerm(text, term));
        }

        private static bool ContainsTerm(string text, string term)
        {
            if (term.Any(IsCjkCharacter))
            {
                return text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return Regex.IsMatch(
                text,
                @"(?<![a-z0-9])" + Regex.Escape(term) + @"(?![a-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            var previousWasWhitespace = false;
            foreach (var raw in value.Trim())
            {
                var character = char.ToLowerInvariant(raw);
                if ((character < 128 && char.IsLetterOrDigit(character)) || IsCjkCharacter(character))
                {
                    builder.Append(character);
                    previousWasWhitespace = false;
                }
                else if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }
            }

            return builder.ToString().Trim();
        }

        private static bool IsCjkCharacter(char value)
        {
            return (value >= 0x4E00 && value <= 0x9FFF)
                || (value >= 0x3400 && value <= 0x4DBF);
        }
    }
}
