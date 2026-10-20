using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Runtime.Context
{
    /// <summary>
    /// 一次 PrepareContext 的产出（设计文档 §9）。
    /// </summary>
    internal sealed class ContextCompactionOutcome
    {
        /// <summary>压缩链是否发生变化。</summary>
        public bool Changed { get; set; }

        /// <summary>是否使用了模型摘要（false 表示走了滑窗裁剪兜底）。</summary>
        public bool UsedSummary { get; set; }

        /// <summary>摘要调用是否失败（用于熔断计数）。</summary>
        public bool SummaryFailed { get; set; }

        /// <summary>新的压缩链（发送给模型的内容）。</summary>
        public IList<BrokerConversationMessage> Messages { get; set; }

        public int BeforeTokens { get; set; }

        public int AfterTokens { get; set; }

        public int PrunedMessageCount { get; set; }

        public string Reason { get; set; }
    }

    /// <summary>
    /// 上下文压缩引擎。实现设计文档的「锚点保留 + 中段压缩」结构：
    /// <code>[System Prompt] + [Summary Block] + [Recent Window]</code>。
    /// 触发基于阈值而非上限，Recent Window 由 token 预算反推轮数，摘要采用滚动累积。
    /// </summary>
    internal sealed class ContextCompactionEngine
    {
        private const int MaxShrinkIterations = 3;

        private readonly ITokenEstimator _tokenEstimator;
        private readonly CompactionSummarizer _summarizer;
        private int _contextWindowSize;
        private int _costCeiling;
        private int _avgTurnTokens;

        public ContextCompactionEngine(
            ITokenEstimator tokenEstimator,
            IModelClient modelClient = null,
            int contextWindowSize = ContextWindowThresholds.DefaultContextWindowSize,
            int costCeiling = ContextWindowThresholds.DefaultGlobalCostCeiling,
            int avgTurnTokens = ContextWindowThresholds.DefaultAverageTurnTokens)
        {
            _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
            _summarizer = new CompactionSummarizer(modelClient);
            _contextWindowSize = Math.Max(4096, contextWindowSize);
            _costCeiling = costCeiling > 0 ? costCeiling : ContextWindowThresholds.DefaultGlobalCostCeiling;
            _avgTurnTokens = avgTurnTokens > 0 ? avgTurnTokens : ContextWindowThresholds.DefaultAverageTurnTokens;
        }

        public int ContextWindowSize => _contextWindowSize;

        public int CostCeiling => _costCeiling;

        public int AverageTurnTokens => _avgTurnTokens;

        public void UpdateContextWindowSize(int newSize)
        {
            if (newSize > 0)
            {
                _contextWindowSize = Math.Max(4096, newSize);
            }
        }

        /// <summary>
        /// 准备发送给模型的压缩链。返回 <c>null</c> 表示当前无需压缩（未到触发阈值）。
        /// </summary>
        public async Task<ContextCompactionOutcome> PrepareContextAsync(
            IList<BrokerConversationMessage> compactMessages,
            AgentModelOptions modelOptions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (compactMessages == null || compactMessages.Count == 0)
            {
                return null;
            }

            var beforeTokens = EstimateMessages(compactMessages);
            var budget = CompactionBudget.Compute(_contextWindowSize, 0, _avgTurnTokens, _costCeiling);

            // §3 触发机制：基于阈值而非上限，并为回复预留输出空间
            if (beforeTokens + budget.ReservedForResponse <= budget.TriggerThreshold)
            {
                return null;
            }

            // 拆解：[System Prompt]（永不压缩） / [Summary Block]（滚动累积） / [Body]（可压缩）
            var realSystem = new List<BrokerConversationMessage>();
            var oldSummaryMessages = new List<BrokerConversationMessage>();
            var body = new List<BrokerConversationMessage>();
            foreach (var m in compactMessages)
            {
                if (m == null)
                {
                    continue;
                }

                if (TurnSegmenter.IsCompactSummary(m))
                {
                    oldSummaryMessages.Add(m);
                }
                else if (TurnSegmenter.IsSystem(m))
                {
                    realSystem.Add(m);
                }
                else
                {
                    body.Add(m);
                }
            }

            var systemTokens = EstimateMessages(realSystem);
            budget = CompactionBudget.Compute(_contextWindowSize, systemTokens, _avgTurnTokens, _costCeiling);
            var existingSummaryText = CombineOldSummaries(oldSummaryMessages);

            // §5 Recent Window：按 token 预算反推轮数（对齐完整轮）
            var split = SplitByBudget(body, budget);

            // §8.2 预收缩：把保留的最近轮按需并入中段，使「摘要 + recent + 输出预留」落入触发阈值以内。
            // 这一步也能修正「RecentBudget 装得下全部轮（middle 为空）但仍超阈值」的情况，
            // 避免陷入"summary self-compressed 但总量不变"的死循环。
            ShrinkRecentToFit(split, realSystem, oldSummaryMessages, budget);

            var middle = split.Middle;
            var recent = RecentMessagesOf(split);

            // 中段为空且仍超阈值 → 已无处可压，最后手段是压缩摘要自身
            if (middle.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(existingSummaryText) && _summarizer.CanSummarize)
                {
                    var recompressed = await TrySummarizeAsync(
                        existingSummaryText, null, modelOptions, budget, cancellationToken).ConfigureAwait(false);
                    if (recompressed != null)
                    {
                        var rebuilt = BuildResult(realSystem, recompressed, middle, recent);
                        return BuildOutcome(compactMessages, rebuilt, true, true, false, "summary self-compressed");
                    }
                }

                return null;
            }

            // §6 滚动累积摘要：NewSummary = Summarize(existingSummary, middle)
            string summaryText = null;
            var summaryFailed = false;
            var summaryAvailable = _summarizer.CanSummarize
                && modelOptions != null
                && !string.IsNullOrWhiteSpace(modelOptions.Endpoint);

            if (summaryAvailable)
            {
                try
                {
                    summaryText = await _summarizer.SummarizeAsync(
                        existingSummaryText, middle, modelOptions, budget.SummaryBudget, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    RuntimeTrace.Write($"[ContextCompaction] Summary failed: {ex.Message}. Falling back to sliding-window prune.");
                    summaryFailed = true;
                }
            }

            if (string.IsNullOrWhiteSpace(summaryText))
            {
                // §8.3 兜底：滑动窗口丢弃最老中段，保证会话不中断
                var pruned = BuildPruned(realSystem, existingSummaryText, middle, recent);
                return BuildOutcome(compactMessages, pruned, true, false, summaryFailed, "sliding-window prune");
            }

            var result = BuildResult(realSystem, summaryText, middle, recent);

            // §8.2 兜底：压缩后仍超限则继续收缩 Recent，把多出的轮卷入摘要
            result = await ShrinkToFitAsync(
                result, realSystem, summaryText, split, budget, modelOptions, cancellationToken).ConfigureAwait(false);

            // §6 摘要自身接近预算上限时二次压缩
            result = await EnsureSummaryWithinBudgetAsync(result, budget, modelOptions, cancellationToken).ConfigureAwait(false);

            return BuildOutcome(compactMessages, result, true, true, false, "rolling summary");
        }

        // =====================================================================
        // 拆分与重组
        // =====================================================================

        private TurnSplit SplitByBudget(IList<BrokerConversationMessage> body, CompactionBudget budget)
        {
            var turns = TurnSegmenter.Segment(body, _tokenEstimator);
            if (turns.Count == 0)
            {
                return new TurnSplit
                {
                    Turns = turns,
                    FirstKeptTurnIndex = 0,
                    Middle = body == null ? new List<BrokerConversationMessage>() : new List<BrokerConversationMessage>(body),
                    Recent = new List<BrokerConversationMessage>(),
                };
            }

            // 第一个 user prompt 之前的孤儿消息永远归入 middle
            var middle = new List<BrokerConversationMessage>();
            for (var i = 0; i < turns[0].StartIndex && i < body.Count; i++)
            {
                middle.Add(body[i]);
            }

            // 从最新往回累加 token，直到超过 RecentBudget 停止
            var keptCount = 0;
            var accumulated = 0;
            for (var i = turns.Count - 1; i >= 0; i--)
            {
                accumulated += turns[i].EstimatedTokens;
                keptCount++;
                if (keptCount >= budget.MinKeepTurns && accumulated >= budget.RecentBudget)
                {
                    break;
                }
                if (keptCount >= budget.MaxKeepTurns)
                {
                    break;
                }
            }

            // 护栏：至少 MinKeepTurns（受轮数总量约束）、至多 MaxKeepTurns
            keptCount = Math.Max(keptCount, Math.Min(budget.MinKeepTurns, turns.Count));
            keptCount = Math.Min(keptCount, turns.Count);

            var firstKeptTurnIndex = turns.Count - keptCount;
            for (var i = 0; i < firstKeptTurnIndex; i++)
            {
                foreach (var m in turns[i].Messages)
                {
                    middle.Add(m);
                }
            }

            var recent = new List<BrokerConversationMessage>();
            for (var i = firstKeptTurnIndex; i < turns.Count; i++)
            {
                foreach (var m in turns[i].Messages)
                {
                    recent.Add(m);
                }
            }

            // 运行时反馈：观测每轮平均 token，供 MaxKeepTurns 估算
            if (turns.Count > 0 && body != null)
            {
                var bodyTokens = EstimateMessages(body);
                var observed = bodyTokens / turns.Count;
                if (observed > 0)
                {
                    _avgTurnTokens = (_avgTurnTokens + observed) / 2;
                }
            }

            return new TurnSplit
            {
                Turns = turns,
                FirstKeptTurnIndex = firstKeptTurnIndex,
                Middle = middle,
                Recent = recent,
            };
        }

        /// <summary>
        /// 预收缩（设计文档 §8.2）：当「system + 已有摘要 + recent + 输出预留」仍超过触发阈值时，
        /// 把最老的保留轮并入 middle（随后会被卷入滚动摘要），直到投影总量落入阈值以内或触达
        /// <see cref="CompactionBudget.MinKeepTurns"/>。这保证即便 RecentBudget 能装下全部轮
        /// （middle 为空）也能继续压缩，而不是反复做无意义的摘要自压缩。
        /// </summary>
        private void ShrinkRecentToFit(
            TurnSplit split,
            List<BrokerConversationMessage> realSystem,
            List<BrokerConversationMessage> oldSummaryMessages,
            CompactionBudget budget)
        {
            var maxShrink = split.KeepTurns - budget.MinKeepTurns;
            for (var i = 0; i < maxShrink; i++)
            {
                var projected = EstimateMessages(realSystem)
                    + EstimateMessages(oldSummaryMessages)
                    + EstimateMessages(RecentMessagesOf(split))
                    + budget.ReservedForResponse;

                if (projected <= budget.TriggerThreshold)
                {
                    break;
                }

                foreach (var m in split.Turns[split.FirstKeptTurnIndex].Messages)
                {
                    split.Middle.Add(m);
                }

                split.FirstKeptTurnIndex++;
            }
        }

        private async Task<List<BrokerConversationMessage>> ShrinkToFitAsync(
            List<BrokerConversationMessage> result,
            List<BrokerConversationMessage> realSystem,
            string summaryText,
            TurnSplit split,
            CompactionBudget budget,
            AgentModelOptions modelOptions,
            CancellationToken cancellationToken)
        {
            var current = result;
            var currentSummary = summaryText;

            for (var iteration = 0; iteration < MaxShrinkIterations; iteration++)
            {
                if (EstimateMessages(current) + budget.ReservedForResponse <= budget.TriggerThreshold)
                {
                    break;
                }

                if (split.KeepTurns <= budget.MinKeepTurns)
                {
                    break;
                }

                // 把最老的一轮 recent 卷入 middle，重新组合
                foreach (var m in split.Turns[split.FirstKeptTurnIndex].Messages)
                {
                    split.Middle.Add(m);
                }

                var moved = new List<BrokerConversationMessage>(split.Turns[split.FirstKeptTurnIndex].Messages);
                split.FirstKeptTurnIndex++;

                if (_summarizer.CanSummarize
                    && modelOptions != null
                    && !string.IsNullOrWhiteSpace(modelOptions.Endpoint))
                {
                    try
                    {
                        var folded = await _summarizer.SummarizeAsync(
                            currentSummary, moved, modelOptions, budget.SummaryBudget, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(folded))
                        {
                            currentSummary = folded;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        RuntimeTrace.Write($"[ContextCompaction] Shrink re-summarize failed: {ex.Message}");
                    }
                }

                current = BuildResult(realSystem, currentSummary, split.Middle, RecentMessagesOf(split));
            }

            return current;
        }

        private async Task<List<BrokerConversationMessage>> EnsureSummaryWithinBudgetAsync(
            List<BrokerConversationMessage> result,
            CompactionBudget budget,
            AgentModelOptions modelOptions,
            CancellationToken cancellationToken)
        {
            var summaryIndex = FindSummaryIndex(result);
            if (summaryIndex < 0)
            {
                return result;
            }

            var summaryMsg = result[summaryIndex];
            if (_tokenEstimator.EstimateMessageTokens(summaryMsg) <= budget.SummaryBudget)
            {
                return result;
            }

            if (!_summarizer.CanSummarize
                || modelOptions == null
                || string.IsNullOrWhiteSpace(modelOptions.Endpoint))
            {
                var capChars = Math.Max(200, budget.SummaryBudget * 4);
                if (summaryMsg.Content.Length > capChars)
                {
                    summaryMsg.Content = summaryMsg.Content.Substring(0, capChars) + "\n[... truncated ...]";
                }

                return result;
            }

            try
            {
                var single = new List<BrokerConversationMessage> { summaryMsg };
                var recompressed = await _summarizer.SummarizeAsync(
                    null, single, modelOptions, budget.SummaryBudget, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(recompressed))
                {
                    summaryMsg.Content = BuildSummaryContent(recompressed, 0);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RuntimeTrace.Write($"[ContextCompaction] Summary self-compress failed: {ex.Message}");
            }

            return result;
        }

        private async Task<string> TrySummarizeAsync(
            string existingSummary,
            IReadOnlyList<BrokerConversationMessage> newContent,
            AgentModelOptions modelOptions,
            CompactionBudget budget,
            CancellationToken cancellationToken)
        {
            if (!_summarizer.CanSummarize
                || modelOptions == null
                || string.IsNullOrWhiteSpace(modelOptions.Endpoint))
            {
                return null;
            }

            try
            {
                return await _summarizer.SummarizeAsync(
                    existingSummary, newContent, modelOptions, budget.SummaryBudget, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RuntimeTrace.Write($"[ContextCompaction] Summarize failed: {ex.Message}");
                return null;
            }
        }

        // =====================================================================
        // 辅助
        // =====================================================================

        private List<BrokerConversationMessage> BuildResult(
            List<BrokerConversationMessage> realSystem,
            string summaryText,
            IList<BrokerConversationMessage> middle,
            IList<BrokerConversationMessage> recent)
        {
            var result = new List<BrokerConversationMessage>();
            result.AddRange(realSystem);

            var summaryMsg = new BrokerConversationMessage
            {
                MessageId = "compact-summary-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                MessageType = "compact_summary",
                Role = "system",
                Name = string.Empty,
                ToolCallId = string.Empty,
                Content = BuildSummaryContent(summaryText, middle == null ? 0 : middle.Count),
                CoveredMessageIdStart = middle != null && middle.Count > 0 ? (middle[0].MessageId ?? string.Empty) : string.Empty,
                CoveredMessageIdEnd = middle != null && middle.Count > 0 ? (middle[middle.Count - 1].MessageId ?? string.Empty) : string.Empty,
            };
            result.Add(summaryMsg);
            if (recent != null)
            {
                result.AddRange(recent);
            }

            return result;
        }

        private List<BrokerConversationMessage> BuildPruned(
            List<BrokerConversationMessage> realSystem,
            string existingSummaryText,
            IList<BrokerConversationMessage> middle,
            IList<BrokerConversationMessage> recent)
        {
            var result = new List<BrokerConversationMessage>();
            result.AddRange(realSystem);

            var note = string.IsNullOrWhiteSpace(existingSummaryText)
                ? $"[Earlier context pruned — {middle.Count} messages dropped.]"
                : $"[Earlier context pruned — {middle.Count} messages dropped. Prior summary retained below.]\n\n{existingSummaryText}";

            result.Add(new BrokerConversationMessage
            {
                MessageId = "compact-boundary-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                MessageType = "compact_boundary",
                Role = "system",
                Name = string.Empty,
                ToolCallId = string.Empty,
                Content = note,
                CoveredMessageIdStart = middle != null && middle.Count > 0 ? (middle[0].MessageId ?? string.Empty) : string.Empty,
                CoveredMessageIdEnd = middle != null && middle.Count > 0 ? (middle[middle.Count - 1].MessageId ?? string.Empty) : string.Empty,
            });

            if (recent != null)
            {
                result.AddRange(recent);
            }

            return result;
        }

        private static string BuildSummaryContent(string summaryText, int compressedCount)
        {
            return $"[Conversation summary — {compressedCount} earlier messages compressed.]\n\n{summaryText}";
        }

        private static List<BrokerConversationMessage> RecentMessagesOf(TurnSplit split)
        {
            var recent = new List<BrokerConversationMessage>();
            for (var i = split.FirstKeptTurnIndex; i < split.Turns.Count; i++)
            {
                recent.AddRange(split.Turns[i].Messages);
            }

            return recent;
        }

        private static int FindSummaryIndex(List<BrokerConversationMessage> messages)
        {
            if (messages == null)
            {
                return -1;
            }

            for (var i = 0; i < messages.Count; i++)
            {
                if (TurnSegmenter.IsCompactSummary(messages[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string CombineOldSummaries(IList<BrokerConversationMessage> summaries)
        {
            if (summaries == null || summaries.Count == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var s in summaries)
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Content))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.AppendLine().AppendLine();
                }

                sb.Append(s.Content);
            }

            return sb.Length == 0 ? null : sb.ToString();
        }

        private int EstimateMessages(IEnumerable<BrokerConversationMessage> messages)
        {
            var total = 0;
            if (messages == null)
            {
                return 0;
            }

            foreach (var m in messages)
            {
                if (m == null)
                {
                    continue;
                }

                total += _tokenEstimator.EstimateMessageTokens(m);
            }

            return total;
        }

        private ContextCompactionOutcome BuildOutcome(
            IList<BrokerConversationMessage> before,
            IList<BrokerConversationMessage> after,
            bool changed,
            bool usedSummary,
            bool summaryFailed,
            string reason)
        {
            var beforeCount = before == null ? 0 : before.Count;
            var afterCount = after == null ? 0 : after.Count;
            return new ContextCompactionOutcome
            {
                Changed = changed,
                UsedSummary = usedSummary,
                SummaryFailed = summaryFailed,
                Messages = after,
                BeforeTokens = EstimateMessages(before),
                AfterTokens = EstimateMessages(after),
                PrunedMessageCount = Math.Max(0, beforeCount - afterCount),
                Reason = reason ?? string.Empty,
            };
        }

        private sealed class TurnSplit
        {
            public List<TurnSegment> Turns { get; set; }

            public int FirstKeptTurnIndex { get; set; }

            public List<BrokerConversationMessage> Middle { get; set; }

            public List<BrokerConversationMessage> Recent { get; set; }

            public int KeepTurns => Turns.Count - FirstKeptTurnIndex;
        }
    }
}
