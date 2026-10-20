using System;
using System.Collections.Generic;
using OfficeCopilot.Agent.Persistence;

namespace OfficeCopilot.Agent.Runtime.Context
{
    /// <summary>
    /// 一个完整「轮」的切分结果（设计文档 §5）。一轮是：
    /// user → assistant(tool_call) → tool_result → assistant(tool_call) → tool_result → assistant(final)
    /// 切割边界必须落在完整一轮上，绝不能在 tool_call 与其 tool_result 之间断开。
    /// </summary>
    internal sealed class TurnSegment
    {
        public TurnSegment(
            int startIndex,
            int endIndex,
            IList<BrokerConversationMessage> messages,
            int estimatedTokens)
        {
            StartIndex = startIndex;
            EndIndex = endIndex;
            Messages = messages;
            EstimatedTokens = estimatedTokens;
        }

        /// <summary>在源消息列表中的起始下标（含）。</summary>
        public int StartIndex { get; private set; }

        /// <summary>在源消息列表中的结束下标（含）。</summary>
        public int EndIndex { get; private set; }

        /// <summary>本轮包含的所有消息。</summary>
        public IList<BrokerConversationMessage> Messages { get; private set; }

        /// <summary>本轮的估算 token 数。</summary>
        public int EstimatedTokens { get; private set; }
    }

    /// <summary>
    /// 把消息序列按「完整 user 回合」切分为轮。
    /// </summary>
    internal static class TurnSegmenter
    {
        public static bool IsUserPrompt(BrokerConversationMessage m)
        {
            return m != null
                && string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(m.ToolCallId)
                && !string.Equals(m.MessageType, "tool_result", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(m.MessageType, "tool_call", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsToolResult(BrokerConversationMessage m)
        {
            return m != null
                && (string.Equals(m.MessageType, "tool_result", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(m.Role, "tool_result", StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(m.ToolCallId)));
        }

        public static bool IsAssistant(BrokerConversationMessage m)
        {
            return m != null && string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSystem(BrokerConversationMessage m)
        {
            return m != null && string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCompactSummary(BrokerConversationMessage m)
        {
            return m != null
                && (string.Equals(m.MessageType, "compact_summary", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(m.MessageType, "compact_boundary", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsToolCall(BrokerConversationMessage m)
        {
            return m != null
                && (string.Equals(m.MessageType, "tool_call", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(m.Role, "tool_call", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 把消息列表切分为完整轮。每个 user prompt 开启新一轮，
        /// 其后所有 assistant / tool_call / tool_result 都属于本轮，直到下一个 user prompt。
        /// 调用方负责处理第一个 user prompt 之前的孤儿消息。
        /// </summary>
        public static List<TurnSegment> Segment(IList<BrokerConversationMessage> messages, ITokenEstimator estimator)
        {
            var result = new List<TurnSegment>();
            if (messages == null || messages.Count == 0)
            {
                return result;
            }

            int? turnStart = null;
            for (var i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                if (m == null)
                {
                    continue;
                }

                if (IsUserPrompt(m))
                {
                    if (turnStart.HasValue)
                    {
                        result.Add(BuildSegment(messages, turnStart.Value, i - 1, estimator));
                    }

                    turnStart = i;
                }
            }

            if (turnStart.HasValue)
            {
                result.Add(BuildSegment(messages, turnStart.Value, messages.Count - 1, estimator));
            }

            return result;
        }

        private static TurnSegment BuildSegment(
            IList<BrokerConversationMessage> messages,
            int startIndex,
            int endIndex,
            ITokenEstimator estimator)
        {
            var segmentMessages = new List<BrokerConversationMessage>();
            var tokens = 0;
            var last = startIndex;

            for (var i = startIndex; i <= endIndex && i < messages.Count; i++)
            {
                if (messages[i] == null) continue;
                segmentMessages.Add(messages[i]);
                tokens += estimator != null ? estimator.EstimateMessageTokens(messages[i]) : 0;
                last = i;
            }

            return new TurnSegment(startIndex, last, segmentMessages, tokens);
        }
    }
}
