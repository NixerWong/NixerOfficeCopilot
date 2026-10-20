using System;

namespace OfficeCopilot.Agent.Runtime.Context
{
    /// <summary>
    /// 上下文窗口阈值常量与计算方法。
    /// 参考 cc-haha <c>autoCompact.ts</c> 的 <c>AUTOCOMPACT_BUFFER_TOKENS</c>、<c>WARNING_THRESHOLD_BUFFER_TOKENS</c> 等常量。
    /// </summary>
    internal static class ContextWindowThresholds
    {
        /// <summary>触发 auto-compact 的缓冲量：当估算 token 占用 ≥ (contextWindow - 此值) 时触发。</summary>
        public const int AutoCompactBufferTokens = 13_000;

        /// <summary>警告阈值缓冲量。</summary>
        public const int WarningThresholdBufferTokens = 20_000;

        /// <summary>错误阈值缓冲量。</summary>
        public const int ErrorThresholdBufferTokens = 20_000;

        /// <summary>用户手动 /compact 的缓冲量。</summary>
        public const int ManualCompactBufferTokens = 3_000;

        /// <summary>Auto-compact 连续失败熔断次数。与 cc-haha <c>MAX_CONSECUTIVE_AUTOCOMPACT_FAILURES = 3</c> 一致。</summary>
        public const int MaxConsecutiveAutoCompactFailures = 3;

        /// <summary>compact 摘要输出预留 token 数。与 cc-haha <c>MAX_OUTPUT_TOKENS_FOR_SUMMARY = 20_000</c> 一致。</summary>
        public const int MaxOutputTokensForSummary = 20_000;

        /// <summary>Micro-compact 单条 tool_result 截断阈值（字符数）。超过此值的 tool_result 会被截断。</summary>
        public const int MicroCompactCharThreshold = 50_000;

        /// <summary>截断后保留的前缀字符数。</summary>
        public const int MicroCompactPrefixLength = 2_000;

        /// <summary>Auto-compact 后保留的最近消息数。</summary>
        public const int MaxMessagesAfterCompact = 20;

        /// <summary>默认模型上下文窗口大小（token）。用于无明确模型信息时的回退值。</summary>
        public const int DefaultContextWindowSize = 200_000;

        // ---- 跨尺度参数（设计文档 §7、§10）：5K ~ 1M 窗口自适应 ----

        /// <summary>全局成本护栏：即使模型窗口 1M，有效工作区也不超过此值。</summary>
        public const int DefaultGlobalCostCeiling = 400_000;

        /// <summary>触发系数下限（小窗口更保守）。</summary>
        public const double CompactRatioMin = 0.6;

        /// <summary>触发系数上限（大窗口更宽松）。</summary>
        public const double CompactRatioMax = 0.85;

        /// <summary>输出预留 token 下限。</summary>
        public const int ReservedForResponseFloor = 1_024;

        /// <summary>输出预留 token 上限。</summary>
        public const int ReservedForResponseCeiling = 16_384;

        /// <summary>摘要块 token 预算下限。</summary>
        public const int SummaryBudgetFloor = 2_048;

        /// <summary>摘要块 token 预算上限。</summary>
        public const int SummaryBudgetCeiling = 32_768;

        /// <summary>最小保留轮数下限（5K 端允许降到 1）。</summary>
        public const int MinKeepTurnsFloor = 1;

        /// <summary>最小保留轮数上限。</summary>
        public const int MinKeepTurnsCeiling = 3;

        /// <summary>最大保留轮数下限。</summary>
        public const int MaxKeepTurnsFloor = 4;

        /// <summary>最大保留轮数上限。</summary>
        public const int MaxKeepTurnsCeiling = 30;

        /// <summary>默认每轮平均 token 数（运行时统计前的初始值）。</summary>
        public const int DefaultAverageTurnTokens = 800;

        /// <summary>单条消息截断阈值下限。</summary>
        public const int SingleMessageCapFloor = 4_096;

        /// <summary>单条消息截断阈值上限。</summary>
        public const int SingleMessageCapCeiling = 32_768;

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// 对数缩放：5K → 0，1M → 1（以 log2 归一化）。
        /// 用于跨尺度平滑插值，避免分段硬编码（设计文档 §7）。
        /// </summary>
        public static double LogScale(int contextWindowSize)
        {
            if (contextWindowSize <= 5_000) return 0.0;
            if (contextWindowSize >= 1_000_000) return 1.0;

            var min = Math.Log(5_000.0, 2);
            var max = Math.Log(1_000_000.0, 2);
            var v = Math.Log(contextWindowSize, 2);
            return (v - min) / (max - min);
        }

        /// <summary>
        /// 触发系数：小窗口更保守，大窗口更宽松。clamp(0.6 + 0.2 * log_scale, 0.6, 0.85)。
        /// </summary>
        public static double GetCompactRatio(int contextWindowSize)
        {
            return Clamp(CompactRatioMin + 0.2 * LogScale(contextWindowSize), CompactRatioMin, CompactRatioMax);
        }

        /// <summary>
        /// 有效工作上限：min(窗口上限, 成本护栏)。1M 窗口不会被用满。
        /// </summary>
        public static int GetEffectiveCap(int contextWindowSize, int costCeiling = DefaultGlobalCostCeiling)
        {
            if (costCeiling <= 0)
            {
                costCeiling = DefaultGlobalCostCeiling;
            }

            return Math.Min(contextWindowSize, costCeiling);
        }

        /// <summary>输出预留：clamp(EffectiveCap * 0.10, 1k, 16k)。</summary>
        public static int GetReservedForResponse(int effectiveCap)
        {
            return Clamp((int)(effectiveCap * 0.10), ReservedForResponseFloor, ReservedForResponseCeiling);
        }

        /// <summary>摘要块预算：clamp(EffectiveCap * 0.10, 2k, 32k)。</summary>
        public static int GetSummaryBudget(int effectiveCap)
        {
            return Clamp((int)(effectiveCap * 0.10), SummaryBudgetFloor, SummaryBudgetCeiling);
        }

        /// <summary>最少保留轮数：clamp(round(log2(Max/5k)), 1, 3)。</summary>
        public static int GetMinKeepTurns(int contextWindowSize)
        {
            var ratio = Math.Max(1.0, contextWindowSize / 5_000.0);
            var keep = (int)Math.Round(Math.Log(ratio, 2));
            return Clamp(keep, MinKeepTurnsFloor, MinKeepTurnsCeiling);
        }

        /// <summary>最多保留轮数：clamp(round(Max / AvgTurnTokens), 4, 30)。</summary>
        public static int GetMaxKeepTurns(int contextWindowSize, int avgTurnTokens = DefaultAverageTurnTokens)
        {
            if (avgTurnTokens <= 0) return MaxKeepTurnsCeiling;
            var keep = (int)Math.Round((double)contextWindowSize / avgTurnTokens);
            return Clamp(keep, MaxKeepTurnsFloor, MaxKeepTurnsCeiling);
        }

        /// <summary>单条消息截断阈值：clamp(EffectiveCap * 0.25, 4k, 32k)。</summary>
        public static int GetSingleMessageCap(int effectiveCap)
        {
            return Clamp((int)(effectiveCap * 0.25), SingleMessageCapFloor, SingleMessageCapCeiling);
        }

        /// <summary>
        /// 计算当前模型的有效上下文窗口大小（减去摘要输出预留）。
        /// 对应 cc-haha <c>getEffectiveContextWindowSize</c>。
        /// </summary>
        public static int GetEffectiveContextWindowSize(int contextWindowSize)
        {
            var reserved = Math.Min(MaxOutputTokensForSummary, contextWindowSize / 10);
            return contextWindowSize - reserved;
        }

        /// <summary>
        /// 计算触发 auto-compact 的 token 阈值。
        /// 对应 cc-haha <c>getAutoCompactThreshold</c>。
        /// </summary>
        public static int GetAutoCompactThreshold(int contextWindowSize)
        {
            var effective = GetEffectiveContextWindowSize(contextWindowSize);
            return effective - AutoCompactBufferTokens;
        }

        /// <summary>
        /// 计算警告阈值。
        /// </summary>
        public static int GetWarningThreshold(int contextWindowSize)
        {
            var effective = GetEffectiveContextWindowSize(contextWindowSize);
            return effective - WarningThresholdBufferTokens;
        }

        /// <summary>
        /// 计算阻塞阈值（达到此值时必须 compact 或 snip 才能继续）。
        /// </summary>
        public static int GetBlockingLimit(int contextWindowSize)
        {
            var effective = GetEffectiveContextWindowSize(contextWindowSize);
            return effective - ManualCompactBufferTokens;
        }

        /// <summary>
        /// 统一的 compact 触发阈值比例（对数缩放版，设计文档 §7）。
        /// 小窗口更保守、大窗口更宽松，不再分段硬编码。
        /// 两个消费者：
        /// - BrokerTurnExecutor pre-call：应用于 modelMessages（含 system prompt）
        /// - AutoCompactPolicy.Evaluate：应用于 CompactMessages（纯对话）
        /// </summary>
        public static double GetCompactThresholdRatio(int contextWindowSize)
        {
            return GetCompactRatio(contextWindowSize);
        }
    }
}
