using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Runtime.Context
{
    /// <summary>
    /// 组合上下文窗口策略：按优先级依次检查 MicroCompact → AutoCompact → Snip。
    /// 这是 <see cref="IContextWindowPolicy"/> 的主力实现，替代阶段 0 的 <see cref="NullContextWindowPolicy"/>。
    ///
    /// 优先级设计：
    /// 1. <b>MicroCompact</b> — 单条 tool_result 超大时优先截断（开销最小）。
    /// 2. <b>AutoCompact</b> — 整体 token 超标时推荐摘要压缩。
    /// 3. <b>Snip</b> — 用户手动触发，通过独立的 <c>EvaluateSnip</c> / <c>ApplySnip</c> 入口。
    /// </summary>
    internal sealed class CompositeContextWindowPolicy : IContextWindowPolicy
    {
        private readonly AutoCompactPolicy _autoCompact;
        private readonly MicroCompactPolicy _microCompact;
        private readonly SnipCompactPolicy _snipCompact;
        private readonly ITokenEstimator _tokenEstimator;
        private readonly int _contextWindowSize;

        public CompositeContextWindowPolicy(
            ITokenEstimator tokenEstimator,
            int contextWindowSize = ContextWindowThresholds.DefaultContextWindowSize,
            IModelClient modelClient = null)
        {
            _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
            _contextWindowSize = contextWindowSize;
            _autoCompact = new AutoCompactPolicy(tokenEstimator, contextWindowSize, modelClient);
            _microCompact = new MicroCompactPolicy(contextWindowSize: contextWindowSize);
            _snipCompact = new SnipCompactPolicy();
        }

        /// <summary>
        /// 评估当前会话，返回推荐的操作。
        /// 优先级：MicroCompact > AutoCompact > None。
        /// </summary>
        public Task<ContextWindowEvaluation> EvaluateAsync(
            BrokerConversationState conversation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (conversation == null)
            {
                return Task.FromResult(ContextWindowEvaluation.NoAction);
            }

            // 1. 先检查 micro-compact（单条超大消息，无副作用）
            var microEval = _microCompact.Evaluate(conversation);
            if (microEval.RecommendedAction != ContextWindowAction.None)
            {
                return Task.FromResult(microEval);
            }

            // 2. 再检查 auto-compact（整体 token 超标）。
            // 无动作时也返回其评估结果，保留真实 token 估算供日志展示（而非静态 NoAction 的 0）。
            return Task.FromResult(_autoCompact.Evaluate(conversation));
        }

        /// <summary>
        /// 根据推荐动作执行对应的 compact 操作。
        /// </summary>
        public Task<BrokerConversationState> ApplyAsync(
            BrokerConversationState conversation,
            ContextWindowAction action,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (conversation == null)
            {
                return Task.FromResult(conversation);
            }

            switch (action)
            {
                case ContextWindowAction.MicroCompact:
                    return Task.FromResult(_microCompact.Apply(conversation));

                case ContextWindowAction.AutoCompact:
                    return Task.FromResult(_autoCompact.ApplyPrune(conversation)); // fallback when called without model options

                case ContextWindowAction.Snip:
                    // Snip 需要 start/end index，应通过 ApplySnipAsync 调用。
                    return Task.FromResult(conversation);

                case ContextWindowAction.None:
                default:
                    return Task.FromResult(conversation);
            }
        }

        // ---- AutoCompact 摘要模式 ----

        /// <summary>
        /// 使用模型生成摘要的方式执行 AutoCompact。需要 <see cref="AgentModelOptions"/>。
        /// </summary>
        public async Task<BrokerConversationState> ApplyAutoCompactSummaryAsync(
            BrokerConversationState conversation,
            AgentModelOptions modelOptions,
            CancellationToken cancellationToken)
        {
            return await _autoCompact.ApplyAsync(conversation, modelOptions, cancellationToken).ConfigureAwait(false);
        }

        // ---- Snip 专用入口 ----

        /// <summary>
        /// 评估 snip 请求。
        /// </summary>
        public ContextWindowEvaluation EvaluateSnip(BrokerConversationState conversation, int startIndex, int endIndex)
        {
            return _snipCompact.Evaluate(conversation, startIndex, endIndex);
        }

        /// <summary>
        /// 执行 snip。
        /// </summary>
        public BrokerConversationState ApplySnip(BrokerConversationState conversation, int startIndex, int endIndex)
        {
            return _snipCompact.Apply(conversation, startIndex, endIndex);
        }

        /// <summary>
        /// 导出 AutoCompact 策略，供外部记录 compact 结果。
        /// </summary>
        public AutoCompactPolicy AutoCompact => _autoCompact;

        /// <summary>
        /// 当前使用的 token 估算器。
        /// </summary>
        public ITokenEstimator TokenEstimator => _tokenEstimator;

        /// <summary>
        /// 当前上下文窗口大小。
        /// </summary>
        public int ContextWindowSize => _contextWindowSize;

        /// <summary>
        /// 更新上下文窗口大小（用于 per-turn 模型级别的覆盖，如从 MaxContextTokens 读取的值）。
        /// </summary>
        public void UpdateContextWindowSize(int newSize)
        {
            if (newSize > 0 && newSize != _contextWindowSize)
            {
                _autoCompact.UpdateContextWindowSize(newSize);
            }
        }
    }
}
