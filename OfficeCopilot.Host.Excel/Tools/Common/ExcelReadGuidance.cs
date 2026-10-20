namespace OfficeCopilot.Host.Excel.Tools
{
    /// <summary>读取类工具在结果被截断时回传给模型的统一指引，避免其退化为分页遍历全表。</summary>
    internal static class ExcelReadGuidance
    {
        public const string TruncationHint =
            "结果已超出直读上限。如需统计、分组汇总或排名，请改用 excel_aggregate；"
            + "如需了解列结构、类型与取值分布，请改用 excel_profile_range；"
            + "如需校验汇总结果，请改用 excel_reconcile；"
            + "如需比较两表键集合差异，请改用 excel_compare_key_sets。不要分页遍历全表。";
    }
}
