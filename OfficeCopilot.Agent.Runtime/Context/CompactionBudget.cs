using System;

namespace OfficeCopilot.Agent.Runtime.Context
{
    /// <summary>
    /// 一次 PrepareContext 计算出的预算分区（设计文档 §4、§7、§10）。
    /// 每个分区都用 <c>clamp(percentage * Max, absoluteFloor, absoluteCeiling)</c>，
    /// 避免纯比例在 5K ~ 1M 两端失效。
    /// </summary>
    internal sealed class CompactionBudget
    {
        /// <summary>模型窗口硬上限。</summary>
        public int MaxContextTokens { get; private set; }

        /// <summary>有效工作上限（含成本护栏）。</summary>
        public int EffectiveCap { get; private set; }

        /// <summary>触发系数。</summary>
        public double CompactRatio { get; private set; }

        /// <summary>触发阈值：<c>EffectiveCap * CompactRatio</c>。</summary>
        public int TriggerThreshold { get; private set; }

        /// <summary>为模型回复预留的输出空间。</summary>
        public int ReservedForResponse { get; private set; }

        /// <summary>摘要块 token 上限。</summary>
        public int SummaryBudget { get; private set; }

        /// <summary>System Prompt 实测 token（不按比例）。</summary>
        public int SystemTokens { get; private set; }

        /// <summary>Recent Window 上限（剩余预算）。</summary>
        public int RecentBudget { get; private set; }

        /// <summary>最少保留轮数（下限）。</summary>
        public int MinKeepTurns { get; private set; }

        /// <summary>最多保留轮数（软上限）。</summary>
        public int MaxKeepTurns { get; private set; }

        /// <summary>单条消息截断阈值。</summary>
        public int SingleMessageCap { get; private set; }

        /// <summary>成本护栏。</summary>
        public int CostCeiling { get; private set; }

        public static CompactionBudget Compute(
            int maxContextTokens,
            int systemTokens,
            int avgTurnTokens,
            int? costCeiling = null)
        {
            var b = new CompactionBudget();
            b.MaxContextTokens = Math.Max(4096, maxContextTokens);
            b.CostCeiling = costCeiling ?? ContextWindowThresholds.DefaultGlobalCostCeiling;
            b.EffectiveCap = ContextWindowThresholds.GetEffectiveCap(b.MaxContextTokens, b.CostCeiling);
            b.CompactRatio = ContextWindowThresholds.GetCompactRatio(b.MaxContextTokens);
            b.TriggerThreshold = Math.Max(1, (int)(b.EffectiveCap * b.CompactRatio));
            b.ReservedForResponse = ContextWindowThresholds.GetReservedForResponse(b.EffectiveCap);
            b.SummaryBudget = ContextWindowThresholds.GetSummaryBudget(b.EffectiveCap);
            b.SystemTokens = Math.Max(0, systemTokens);
            b.MinKeepTurns = ContextWindowThresholds.GetMinKeepTurns(b.MaxContextTokens);
            b.MaxKeepTurns = ContextWindowThresholds.GetMaxKeepTurns(b.MaxContextTokens, avgTurnTokens);
            b.SingleMessageCap = ContextWindowThresholds.GetSingleMessageCap(b.EffectiveCap);
            b.RecentBudget = ComputeRecentBudget(b);
            return b;
        }

        private static int ComputeRecentBudget(CompactionBudget b)
        {
            var recent = b.EffectiveCap - b.SystemTokens - b.ReservedForResponse - b.SummaryBudget;

            // 防止极小窗口把 Recent 挤成 0：至少保留 1 轮的绝对下限（设计文档 §8 恒定不变量）
            var floor = Math.Max(256, b.MinKeepTurns * 256);
            return Math.Max(floor, recent);
        }
    }
}
