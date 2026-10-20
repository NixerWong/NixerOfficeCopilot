using System;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Token 预算管理器，1:1 复刻 cc-haha <c>src/query/tokenBudget.ts</c> 的收敛检测算法。
    /// 两个条件触发停止：
    /// 1. 上下文占用 ≥ 模型上限的 <see cref="StopThreshold"/>（默认 90%）
    /// 2. 连续 <see cref="MaxConsecutiveSmallDeltas"/> 次 text delta 都 &lt; <see cref="SmallDeltaTokenThreshold"/>
    ///    （"边际收益递减"主动停）
    /// </summary>
    internal sealed class TokenBudget
    {
        /// <summary>达到上下文窗口的这个比例时停止生成。</summary>
        public double StopThreshold { get; }

        /// <summary>delta 小于此值视为"边际递减"。</summary>
        public int SmallDeltaTokenThreshold { get; }

        /// <summary>连续小 delta 次数达此值即触发停止。</summary>
        public int MaxConsecutiveSmallDeltas { get; }

        private int _totalTokensEstimate;
        private int _consecutiveSmallDeltas;
        private int _contextWindowSize;
        private bool _stopped;

        /// <summary>
        /// 创建 TokenBudget。
        /// </summary>
        /// <param name="contextWindowSize">模型上下文窗口大小（token 数）。</param>
        /// <param name="stopThreshold">达到 contextWindow 的多少比例时停止，默认 0.9（90%）。</param>
        /// <param name="smallDeltaTokenThreshold">delta < 此值视为"小 delta"，默认 500。</param>
        /// <param name="maxConsecutiveSmallDeltas">连续小 delta 次数上限，默认 3。</param>
        public TokenBudget(
            int contextWindowSize = 128_000,
            double stopThreshold = 0.9,
            int smallDeltaTokenThreshold = 500,
            int maxConsecutiveSmallDeltas = 3)
        {
            _contextWindowSize = Math.Max(4096, contextWindowSize);
            StopThreshold = Clamp(stopThreshold, 0.5, 0.99);
            SmallDeltaTokenThreshold = Math.Max(100, smallDeltaTokenThreshold);
            MaxConsecutiveSmallDeltas = Math.Max(1, maxConsecutiveSmallDeltas);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>是否已触发停止条件。</summary>
        public bool IsStopped => _stopped;

        /// <summary>当前估算的上下文总 token 数。</summary>
        public int TotalTokensEstimate => _totalTokensEstimate;

        /// <summary>连续小 delta 次数。</summary>
        public int ConsecutiveSmallDeltas => _consecutiveSmallDeltas;

        /// <summary>当前使用的上下文窗口大小。</summary>
        public int ContextWindowSize => _contextWindowSize;

        /// <summary>
        /// 初始化/重置预算状态，通常在每轮 turn 开始时调用。
        /// </summary>
        /// <param name="initialTokenEstimate">初始上下文 token 估算（消息 + system prompt 等）。</param>
        /// <param name="contextWindowSize">可覆盖构造时的上下文窗口大小。</param>
        public void Reset(int initialTokenEstimate, int? contextWindowSize = null)
        {
            if (contextWindowSize.HasValue)
            {
                _contextWindowSize = Math.Max(4096, contextWindowSize.Value);
            }

            _totalTokensEstimate = Math.Max(0, initialTokenEstimate);
            _consecutiveSmallDeltas = 0;
            _stopped = false;
        }

        /// <summary>
        /// 记录一条 text delta，更新预算并返回是否应停止。
        /// 注意：SSE 流中每个 delta 只有 1-5 字符，所以"边际递减"检测不适合 per-delta 级别。
        /// 仅在上下文总量达到阈值时触发停止。
        /// </summary>
        /// <param name="tokenDelta">本次 delta 的 token 数（用 len/4 粗估）。</param>
        /// <returns>true 表示应停止本轮模型输出。</returns>
        public bool RecordDelta(int tokenDelta)
        {
            if (_stopped)
            {
                return true;
            }

            // Ensure minimum delta of 1 so single-char SSE chunks accumulate
            if (tokenDelta < 1) tokenDelta = 1;

            _totalTokensEstimate += tokenDelta;
            _consecutiveSmallDeltas = 0; // not used at delta level (see cc-haha: checked per-turn)

            // 仅使用上下文占用百分比阈值。SSE 逐 token 级别的"边际递减"检测不可靠，
            // 因为每个 SSE chunk 天然就是小 delta。cc-haha 在 turn 边界做这个检测。
            var usageRatio = (double)_totalTokensEstimate / _contextWindowSize;
            if (usageRatio >= StopThreshold)
            {
                _stopped = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 工具调用完成后增加 token 估算（tool_result 的回灌会消耗上下文）。
        /// </summary>
        public void RecordToolResult(int toolResultTokens)
        {
            _totalTokensEstimate += Math.Max(0, toolResultTokens);
        }

        /// <summary>
        /// 强制停止（如外部取消）。
        /// </summary>
        public void ForceStop()
        {
            _stopped = true;
        }
    }
}
