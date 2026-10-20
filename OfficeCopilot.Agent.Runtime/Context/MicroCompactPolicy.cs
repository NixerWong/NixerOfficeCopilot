using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Runtime.Context
{
    /// <summary>
    /// Micro-compact 策略：当单条 tool_result 消息的字符数超过阈值时，截断内容并用占位符替换。
    /// 参考 cc-haha <c>microCompact.ts</c> 的 <c>microcompactMessages</c>。
    ///
    /// 核心思路：
    /// - 遍历会话消息，找出字符数超过 <see cref="ContextWindowThresholds.MicroCompactCharThreshold"/> 的 tool_result。
    /// - 保留前 N 个字符（<see cref="ContextWindowThresholds.MicroCompactPrefixLength"/>）+ 尾部标记。
    /// - 全文可持久化到磁盘（阶段 1.2 后续补充），当前只做截断。
    /// </summary>
    internal sealed class MicroCompactPolicy
    {
        /// <summary>tool_result 最多占上下文窗口的比例（超过则截断）。</summary>
        private const double MaxToolResultContextRatio = 0.10;

        /// <summary>绝对最小截断阈值（字符），防止 ctxWindow 极小时过度截断。</summary>
        private const int MinCharThreshold = 2_000;

        private readonly int _charThreshold;
        private readonly int _prefixLength;

        /// <summary>
        /// 创建 MicroCompact 策略。
        /// </summary>
        /// <param name="contextWindowSize">上下文窗口大小（tokens），用于动态计算截断阈值。
        /// 传入 0 或负数则使用 <see cref="ContextWindowThresholds.MicroCompactCharThreshold"/> 作为回退。</param>
        /// <param name="prefixLength">截断后保留的前缀字符数。</param>
        /// <param name="charThreshold">显式字符阈值（&gt; 0 时优先于 contextWindowSize 动态计算，便于测试）。</param>
        public MicroCompactPolicy(
            int contextWindowSize = 0,
            int prefixLength = ContextWindowThresholds.MicroCompactPrefixLength,
            int charThreshold = 0)
        {
            _prefixLength = prefixLength > 0 ? prefixLength : ContextWindowThresholds.MicroCompactPrefixLength;
            _charThreshold = charThreshold > 0 ? charThreshold : ComputeCharThreshold(contextWindowSize);
        }

        /// <summary>
        /// 根据上下文窗口大小动态计算 tool_result 截断阈值。
        /// 公式：max(MinCharThreshold, contextWindowSize × MaxToolResultContextRatio × 4)。
        /// 保证单条 tool_result 不会消耗超过 10% 的上下文窗口。
        /// </summary>
        private static int ComputeCharThreshold(int contextWindowSize)
        {
            if (contextWindowSize <= 0)
                return ContextWindowThresholds.MicroCompactCharThreshold;

            var dynamicThreshold = (int)(contextWindowSize * MaxToolResultContextRatio * 4);
            return Math.Max(MinCharThreshold, dynamicThreshold);
        }
        /// <summary>
        /// 评估会话是否需要 micro-compact。
        /// 返回找到的第一条超大 tool_result 的上下文评估结果。
        /// </summary>
        public ContextWindowEvaluation Evaluate(BrokerConversationState conversation)
        {
            if (conversation == null || conversation.Messages == null)
            {
                return ContextWindowEvaluation.NoAction;
            }

            var oversizedCount = 0;
            long totalOversizedChars = 0;

            foreach (var message in conversation.Messages)
            {
                if (message == null)
                {
                    continue;
                }

                if (!IsToolResult(message))
                {
                    continue;
                }

                var contentLength = (message.Content ?? string.Empty).Length;
                if (contentLength > _charThreshold)
                {
                    oversizedCount++;
                    totalOversizedChars += contentLength;
                }
            }

            if (oversizedCount == 0)
            {
                return ContextWindowEvaluation.NoAction;
            }

            return new ContextWindowEvaluation(
                ContextWindowAction.MicroCompact,
                0, // token 估算由调用方负责
                0,
                $"Found {oversizedCount} oversized tool result(s) "
                + $"(total {totalOversizedChars} chars, threshold {_charThreshold} chars). "
                + "Micro-compact will truncate them.");
        }

        /// <summary>
        /// 对会话执行 micro-compact：截断所有超大 tool_result 消息。
        /// 返回新的会话状态（原会话不被修改）。
        /// </summary>
        public BrokerConversationState Apply(BrokerConversationState conversation)
        {
            if (conversation == null || conversation.Messages == null)
            {
                return conversation;
            }

            var compactedCount = 0;
            var newMessages = new List<BrokerConversationMessage>();

            foreach (var message in conversation.Messages)
            {
                if (message == null)
                {
                    newMessages.Add(message);
                    continue;
                }

                if (IsToolResult(message) && (message.Content ?? string.Empty).Length > _charThreshold)
                {
                    var truncated = TruncateToolResult(message);
                    newMessages.Add(truncated);
                    compactedCount++;
                }
                else
                {
                    newMessages.Add(message);
                }
            }

            if (compactedCount == 0)
            {
                return conversation;
            }

            var compacted = new BrokerConversationState
            {
                ConversationId = conversation.ConversationId,
                Metadata = conversation.Metadata == null
                    ? null
                    : new Dictionary<string, string>(conversation.Metadata)
            };

            compacted.Messages = newMessages;

            return compacted;
        }

        private static bool IsToolResult(BrokerConversationMessage message)
        {
            return message != null
                && (string.Equals(message.MessageType, "tool_result", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "tool_result", StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(message.ToolCallId)));
        }

        private BrokerConversationMessage TruncateToolResult(BrokerConversationMessage original)
        {
            var content = original.Content ?? string.Empty;
            var truncated = content.Length > _prefixLength
                ? content.Substring(0, _prefixLength)
                : content;

            return new BrokerConversationMessage
            {
                MessageId = original.MessageId,
                Role = original.Role,
                MessageType = original.MessageType,
                Content = truncated,
                ToolCallId = original.ToolCallId,
                Name = original.Name,
                StructuredContentJson = original.StructuredContentJson,
                CreatedAtUtc = original.CreatedAtUtc
            };
        }
    }
}
