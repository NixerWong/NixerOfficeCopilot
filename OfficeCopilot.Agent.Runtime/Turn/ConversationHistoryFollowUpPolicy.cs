using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OfficeCopilot.Agent.Persistence;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    internal static class ConversationHistoryFollowUpPolicy
    {
        private const int MaxToolEntries = 16;
        private const int MaxResultPreviewLength = 500;

        public static bool IsProcessFollowUp(string userInput)
        {
            var text = (userInput ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            var refersToPreviousTurn = ContainsAny(
                text,
                "刚才",
                "刚刚",
                "上一步",
                "上一轮",
                "前面",
                "之前");
            var asksAboutPreviousProcess = ContainsAny(
                    text,
                    "步骤是什么",
                    "过程是什么",
                    "做了什么",
                    "怎么做",
                    "如何做",
                    "依据是什么",
                    "用了哪些工具",
                    "调用了哪些工具",
                    "工具调用是怎么",
                    "工具调用过程")
                || (ContainsAny(text, "复盘", "回顾")
                    && ContainsAny(text, "步骤", "过程", "操作", "工具"));

            return refersToPreviousTurn && asksAboutPreviousProcess;
        }

        public static bool IsSkillPlanContinuation(string userInput)
        {
            var text = (userInput ?? string.Empty).Trim();
            if (text.Length == 0 || text.Length > 30)
            {
                return false;
            }

            if (new[] { "好", "好的", "可以", "行", "继续", "同意", "continue", "go ahead", "do it" }
                .Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            return ContainsAny(text, "写进去", "就这样", "按这个", "照做", "没问题", "继续吧", "改成");
        }

        public static string BuildPriorTurnToolTrail(
            IEnumerable<BrokerConversationMessage> messages,
            string currentMessageId)
        {
            var history = (messages ?? Enumerable.Empty<BrokerConversationMessage>()).ToList();
            var currentIndex = history.FindIndex(message => message != null
                && string.Equals(message.MessageId, currentMessageId, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                currentIndex = history.Count;
            }

            var startIndex = 0;
            for (var index = currentIndex - 1; index >= 0; index--)
            {
                if (history[index] != null
                    && string.Equals(history[index].Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    startIndex = index + 1;
                    break;
                }
            }

            var toolMessages = history
                .Skip(startIndex)
                .Take(Math.Max(0, currentIndex - startIndex))
                .Where(message => message != null
                    && (string.Equals(message.MessageType, "tool_call", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(message.MessageType, "tool_result", StringComparison.OrdinalIgnoreCase)))
                .Take(MaxToolEntries * 2)
                .ToList();
            if (toolMessages.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine("用户正在询问上一轮的过程。以下是已完成的工具轨迹，只用于解释，不要重新调用工具：");
            var step = 0;
            foreach (var message in toolMessages)
            {
                if (string.Equals(message.MessageType, "tool_call", StringComparison.OrdinalIgnoreCase))
                {
                    step++;
                    builder.Append(step)
                        .Append(". 调用 ")
                        .Append(string.IsNullOrWhiteSpace(message.Name) ? message.Content : message.Name)
                        .Append("，参数：")
                        .AppendLine(Compact(message.StructuredContentJson, MaxResultPreviewLength));
                    continue;
                }

                builder.Append("   结果：")
                    .Append(message.Success ? "成功。" : "失败。")
                    .AppendLine(Compact(message.Content, MaxResultPreviewLength));
            }

            return builder.ToString().TrimEnd();
        }

        private static bool ContainsAny(string text, params string[] terms)
        {
            return terms.Any(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string Compact(string text, int maximumLength)
        {
            var compact = (text ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            if (compact.Length <= maximumLength)
            {
                return compact;
            }

            return compact.Substring(0, maximumLength) + "...";
        }
    }
}