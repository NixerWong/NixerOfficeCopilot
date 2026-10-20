using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Runtime.Context
{
    /// <summary>
    /// Auto-compact 策略：当会话 token 占用超过触发阈值时，调用模型生成滚动摘要替代旧消息。
    /// 实际压缩逻辑迁入 <see cref="ContextCompactionEngine"/>（设计文档 §9 的 PrepareContext），
    /// 本类保留「评估 → 执行」入口与熔断计数，作为 <see cref="CompositeContextWindowPolicy"/> 的部件。
    /// </summary>
    internal sealed class AutoCompactPolicy
    {
        private readonly ITokenEstimator _tokenEstimator;
        private readonly ContextCompactionEngine _engine;
        private int _contextWindowSize;
        private int _consecutiveFailures;

        public AutoCompactPolicy(ITokenEstimator tokenEstimator, int contextWindowSize = ContextWindowThresholds.DefaultContextWindowSize, IModelClient modelClient = null)
        {
            _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
            _contextWindowSize = contextWindowSize;
            _engine = new ContextCompactionEngine(tokenEstimator, modelClient, contextWindowSize);
        }

        /// <summary>
        /// 评估当前会话是否需要 auto-compact（设计文档 §3 阈值触发）。
        /// 返回 <c>ContextWindowEvaluation</c>，其中 RecommendedAction 为 <c>AutoCompact</c> 或 <c>None</c>。
        /// </summary>
        public ContextWindowEvaluation Evaluate(BrokerConversationState conversation)
        {
            if (conversation == null)
            {
                return ContextWindowEvaluation.NoAction;
            }

            var effectiveWindow = ContextWindowThresholds.GetEffectiveCap(_contextWindowSize);
            var compactRatio = ContextWindowThresholds.GetCompactRatio(_contextWindowSize);
            var triggerThreshold = Math.Max(1, (int)(effectiveWindow * compactRatio));
            var warningThreshold = Math.Max(1, (int)(triggerThreshold * 0.70));

            // 熔断：连续失败 N 次后不再推荐
            if (_consecutiveFailures >= ContextWindowThresholds.MaxConsecutiveAutoCompactFailures)
            {
                return new ContextWindowEvaluation(
                    ContextWindowAction.None,
                    0,
                    effectiveWindow,
                    $"Auto-compact circuit breaker open after {_consecutiveFailures} consecutive failures.");
            }

            var estimatedTokens = _tokenEstimator.EstimateConversationTokens(conversation);
            var percentLeft = triggerThreshold > 0
                ? Math.Max(0, (int)Math.Round(((double)(triggerThreshold - estimatedTokens) / triggerThreshold) * 100))
                : 0;

            if (estimatedTokens >= triggerThreshold)
            {
                return new ContextWindowEvaluation(
                    ContextWindowAction.AutoCompact,
                    estimatedTokens,
                    effectiveWindow,
                    $"Token usage ({estimatedTokens}) exceeds trigger threshold ({triggerThreshold}). "
                    + $"Effective cap: {effectiveWindow}, percent left: {percentLeft}%.");
            }

            if (estimatedTokens >= warningThreshold)
            {
                return new ContextWindowEvaluation(
                    ContextWindowAction.None,
                    estimatedTokens,
                    effectiveWindow,
                    $"Token usage ({estimatedTokens}) approaching warning threshold ({warningThreshold}). "
                    + $"No action needed yet, percent left: {percentLeft}%.");
            }

            // 未到警告阈值：仍返回真实 token 估算，便于日志展示实际占用而非 0。
            return new ContextWindowEvaluation(
                ContextWindowAction.None,
                estimatedTokens,
                effectiveWindow,
                string.Empty);
        }

        /// <summary>
        /// 记录一次 compact 结果，用于熔断计数。
        /// </summary>
        public void RecordCompactResult(bool success)
        {
            if (success)
            {
                _consecutiveFailures = 0;
            }
            else
            {
                _consecutiveFailures++;
            }
        }

        /// <summary>
        /// 重置熔断计数。
        /// </summary>
        public void ResetCircuitBreaker()
        {
            _consecutiveFailures = 0;
        }

        /// <summary>
        /// 获取当前熔断状态。
        /// </summary>
        public bool IsCircuitBreakerOpen =>
            _consecutiveFailures >= ContextWindowThresholds.MaxConsecutiveAutoCompactFailures;

        /// <summary>
        /// 当前连续失败次数。
        /// </summary>
        public int ConsecutiveFailures => _consecutiveFailures;

        /// <summary>
        /// 更新上下文窗口大小（per-turn 覆盖）。
        /// </summary>
        public void UpdateContextWindowSize(int newSize)
        {
            if (newSize > 0)
            {
                _contextWindowSize = newSize;
                _engine.UpdateContextWindowSize(newSize);
            }
        }

        /// <summary>
        /// 计算有效的 auto-compact 触发阈值。
        /// 先用绝对缓冲量计算；若窗口太小导致阈值为负，则回退到统一百分比。
        /// 百分比来自 <see cref="ContextWindowThresholds.GetCompactThresholdRatio"/>。
        /// </summary>
        private static int GetEffectiveAutoCompactThreshold(int contextWindowSize)
        {
            var effective = ContextWindowThresholds.GetEffectiveContextWindowSize(contextWindowSize);
            var threshold = effective - ContextWindowThresholds.AutoCompactBufferTokens;
            if (threshold > 0)
                return threshold;

            return (int)(effective * ContextWindowThresholds.GetCompactThresholdRatio(contextWindowSize));
        }

        /// <summary>
        /// 执行 auto-compact：优先调用模型生成滚动摘要，回退到滑动窗口裁剪。
        /// 逻辑委托给 <see cref="ContextCompactionEngine.PrepareContextAsync"/>（设计文档 §9）。
        /// </summary>
        public async Task<BrokerConversationState> ApplyAsync(
            BrokerConversationState conversation,
            AgentModelOptions modelOptions,
            CancellationToken cancellationToken,
            int maxMessagesAfterCompact = ContextWindowThresholds.MaxMessagesAfterCompact)
        {
            if (conversation == null || conversation.Messages == null || conversation.Messages.Count <= 2)
                return conversation;

            var outcome = await _engine.PrepareContextAsync(conversation.Messages, modelOptions, cancellationToken).ConfigureAwait(false);
            if (outcome == null || !outcome.Changed || outcome.Messages == null)
                return conversation;

            RecordCompactResult(!outcome.SummaryFailed);

            RuntimeTrace.Write($"[AutoCompact] Compact done: usedSummary={outcome.UsedSummary}, "
                + $"before={outcome.BeforeTokens}t/{conversation.Messages.Count}msg, "
                + $"after={outcome.AfterTokens}t/{outcome.Messages.Count}msg, reason='{outcome.Reason}'");

            return new BrokerConversationState
            {
                ConversationId = conversation.ConversationId,
                Messages = new List<BrokerConversationMessage>(outcome.Messages),
                Metadata = conversation.Metadata == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(conversation.Metadata)
            };
        }

        /// <summary>
        /// 简单裁剪模式（当模型不可用时）。
        /// </summary>
        public BrokerConversationState ApplyPrune(BrokerConversationState conversation, int maxMessagesAfterCompact = ContextWindowThresholds.MaxMessagesAfterCompact)
        {
            if (conversation == null || conversation.Messages == null || conversation.Messages.Count <= 4)
                return conversation;

            var messages = conversation.Messages;
            var effectiveMax = Math.Max(2, Math.Min(maxMessagesAfterCompact, messages.Count / 2));
            if (messages.Count <= effectiveMax + 2)
            {
                // Guard: only skip if total tokens are also within limits.
                // When messages are few but individually huge (e.g., 8K+ chars each),
                // they can still overflow a small context window.
                var estimatedTokens = _tokenEstimator.EstimateConversationTokens(conversation);
                var threshold = GetEffectiveAutoCompactThreshold(_contextWindowSize);
                if (estimatedTokens < threshold)
                    return conversation;
                // Allow keeping just 1 message when individual messages are huge
                effectiveMax = 1;
            }

            var newMessages = new List<BrokerConversationMessage>();
            var systemMessages = messages
                .TakeWhile(m => m != null && string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                .ToList();
            // Only preserve real system messages, not old compact summaries/boundaries
            foreach (var sm in systemMessages)
            {
                if (!string.Equals(sm.MessageType, "compact_summary", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(sm.MessageType, "compact_boundary", StringComparison.OrdinalIgnoreCase))
                {
                    newMessages.Add(sm);
                }
            }

            var startOfTail = messages.Count - effectiveMax;
            var prunedCount = startOfTail - systemMessages.Count;
            if (prunedCount <= 0) return conversation;

            long prunedChars = 0;
            for (int i = systemMessages.Count; i < startOfTail; i++)
            { var m = messages[i]; if (m != null) prunedChars += (m.Content ?? string.Empty).Length; }
            var estimatedPrunedTokens = _tokenEstimator.EstimateTokens(new string('x', (int)Math.Min(prunedChars, int.MaxValue)));

            var boundaryMessage = new BrokerConversationMessage
            {
                MessageId = "compact-boundary-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                MessageType = "compact_boundary",
                Role = "system",
                Content = $"[Earlier context compacted — {prunedCount} messages / ~{estimatedPrunedTokens} tokens pruned. Remaining: {effectiveMax} most recent messages.]",
            };
            newMessages.Add(boundaryMessage);
            for (int i = startOfTail; i < messages.Count; i++) newMessages.Add(messages[i]);

            return new BrokerConversationState
            {
                ConversationId = conversation.ConversationId,
                Messages = newMessages,
                Metadata = conversation.Metadata == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(conversation.Metadata)
            };
        }
    }
}
