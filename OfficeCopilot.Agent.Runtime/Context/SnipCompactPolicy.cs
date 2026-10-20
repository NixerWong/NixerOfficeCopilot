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
    /// Snip-compact 策略：删除指定范围的消息（用户主动发起 /snip 或 UI 操作）。
    /// 参考 cc-haha <c>snipCompact.ts</c> + <c>commands.ts</c> 中 <c>/snip</c> 命令。
    ///
    /// 删除范围：从指定起始 index 到结束 index（含），index 为 0-based。
    /// </summary>
    internal sealed class SnipCompactPolicy
    {
        /// <summary>
        /// 评估 snip 请求是否合法。
        /// </summary>
        public ContextWindowEvaluation Evaluate(
            BrokerConversationState conversation,
            int startIndex,
            int endIndex)
        {
            if (conversation == null || conversation.Messages == null)
            {
                return ContextWindowEvaluation.NoAction;
            }

            var messageCount = conversation.Messages.Count;

            if (startIndex < 0 || endIndex < 0 || startIndex > endIndex || endIndex >= messageCount)
            {
                return new ContextWindowEvaluation(
                    ContextWindowAction.None,
                    0,
                    0,
                    $"Invalid snip range [{startIndex}..{endIndex}] for conversation with {messageCount} messages.");
            }

            var snipCount = endIndex - startIndex + 1;

            return new ContextWindowEvaluation(
                ContextWindowAction.Snip,
                0,
                0,
                $"User requested snip of {snipCount} message(s) at indices [{startIndex}..{endIndex}].");
        }

        /// <summary>
        /// 执行 snip：删除指定范围的消息。
        /// </summary>
        public BrokerConversationState Apply(
            BrokerConversationState conversation,
            int startIndex,
            int endIndex)
        {
            if (conversation == null || conversation.Messages == null)
            {
                return conversation;
            }

            var messageCount = conversation.Messages.Count;

            if (startIndex < 0 || endIndex < 0 || startIndex > endIndex || endIndex >= messageCount)
            {
                return conversation;
            }

            var newMessages = new List<BrokerConversationMessage>();
            for (var i = 0; i < messageCount; i++)
            {
                if (i < startIndex || i > endIndex)
                {
                    newMessages.Add(conversation.Messages[i]);
                }
            }

            var snipped = new BrokerConversationState
            {
                ConversationId = conversation.ConversationId,
                Metadata = conversation.Metadata == null
                    ? null
                    : new Dictionary<string, string>(conversation.Metadata)
            };

            snipped.Messages = newMessages;

            return snipped;
        }
    }
}
