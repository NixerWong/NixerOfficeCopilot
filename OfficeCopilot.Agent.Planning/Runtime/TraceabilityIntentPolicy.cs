using System;

namespace OfficeCopilot.Agent.Planning
{
    /// <summary>
    /// 识别用户对分析过程的「可验证 / 留痕 / 分步」意图。
    /// 命中后应默认把关键中间结果物化到 _wip_NN_ 中间表，而不是纯内存算完只给结论。
    /// 只认「强词」；裸的「步骤」「过程」这类常见词不触发，避免每次分析都徒增中间表噪音。
    /// </summary>
    public static class TraceabilityIntentPolicy
    {
        private static readonly string[] TraceabilityMarkers = new[]
        {
            // 可验证性 / 可追溯性（出现即命中）
            "可验证", "留痕", "可追溯", "追溯", "审计", "可复查", "可复现", "复核",
            "可核查", "留档", "过程留痕", "步骤留痕", "展示步骤",
            // 分步 / 过程（要求比裸「步骤」更强的词组）
            "每一步", "分步骤", "逐步", "计算过程", "推导过程", "验证过程", "中间步骤", "计算步骤",
            // 英文
            "verifiable", "traceable", "auditable", "reproducible", "show your work", "step by step"
        };

        /// <summary>用户是否明确要求分析过程可验证 / 留痕 / 分步。</summary>
        public static bool RequiresTraceableSteps(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
            {
                return false;
            }

            foreach (var marker in TraceabilityMarkers)
            {
                if (userInput.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
