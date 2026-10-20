using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 回合内上下文维护。本回合的工具调用/结果只存在于内存中的模型消息列表里，
    /// 持久化链上的压缩看不到也保不住它们，因此在这里单独处理。
    /// </summary>
    internal static class InTurnContextMaintenance
    {
        internal const string StaleReadMarker = "[已过期的读取结果]";

        private static readonly HashSet<string> FoldableReadTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "word_read_document",
            "word_read_paragraphs",
            "word_read_outline"
        };

        /// <summary>与模型实际收到的内容同口径：正文、名称和工具调用参数都计入（字符数 / 4）。</summary>
        public static int EstimateTokens(IEnumerable<ModelClientMessage> messages)
        {
            if (messages == null)
            {
                return 0;
            }

            long chars = 0;
            foreach (var message in messages)
            {
                if (message == null)
                {
                    continue;
                }

                chars += (message.Content?.Length ?? 0) + (message.Name?.Length ?? 0);
                if (message.ToolCalls != null)
                {
                    foreach (var call in message.ToolCalls)
                    {
                        chars += (call?.Name?.Length ?? 0) + (call?.ArgumentsJson?.Length ?? 0);
                    }
                }
            }

            return (int)Math.Min(int.MaxValue, chars / 4);
        }

        /// <summary>
        /// 压缩后从持久化链重建的消息不含本回合的工具调用/结果（它们只回放 user/assistant 文本），
        /// 直接替换会让模型丢掉本回合全部进度并从头开始。这里把旧列表中最后一条 user 消息之后的部分接回去。
        /// </summary>
        public static List<ModelClientMessage> PreserveInTurnTail(List<ModelClientMessage> rebuilt, IList<ModelClientMessage> previous)
        {
            if (rebuilt == null || previous == null)
            {
                return rebuilt;
            }

            var previousUser = LastUserIndex(previous);
            var rebuiltUser = LastUserIndex(rebuilt);
            if (previousUser < 0 || rebuiltUser < 0)
            {
                return rebuilt;
            }

            var result = rebuilt.Take(rebuiltUser + 1).ToList();
            result.AddRange(previous.Skip(previousUser + 1));
            return result;
        }

        /// <summary>
        /// 折叠本回合内已过期的 Word 读取结果：同一读取工具后来又被调用过，并且两次之间文档被修改过
        /// （或两次参数完全相同）。段号会随写入漂移，旧结果只会误导模型重做。返回本次折叠的条数。
        /// </summary>
        public static int FoldStaleWordReads(IList<ModelClientMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return 0;
            }

            var argumentsById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var message in messages)
            {
                if (message?.ToolCalls == null)
                {
                    continue;
                }

                foreach (var call in message.ToolCalls)
                {
                    if (!string.IsNullOrEmpty(call?.Id))
                    {
                        argumentsById[call.Id] = call.ArgumentsJson ?? string.Empty;
                    }
                }
            }

            var tools = new List<int>();
            for (var index = 0; index < messages.Count; index++)
            {
                if (messages[index] != null && string.Equals(messages[index].Role, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    tools.Add(index);
                }
            }

            var folded = 0;
            for (var i = 0; i < tools.Count; i++)
            {
                var read = messages[tools[i]];
                if (!FoldableReadTools.Contains(read.Name ?? string.Empty)
                    || (read.Content ?? string.Empty).StartsWith(StaleReadMarker, StringComparison.Ordinal))
                {
                    continue;
                }

                var readArguments = ArgumentsOf(argumentsById, read);
                var writeSeen = false;
                for (var j = i + 1; j < tools.Count; j++)
                {
                    var later = messages[tools[j]];
                    if (PriorTurnWordProgressCarryOver.IsWriteTool(later.Name))
                    {
                        writeSeen = true;
                        continue;
                    }

                    if (string.Equals(later.Name, read.Name, StringComparison.OrdinalIgnoreCase)
                        && (writeSeen || string.Equals(ArgumentsOf(argumentsById, later), readArguments, StringComparison.Ordinal)))
                    {
                        read.Content = StaleReadMarker + " 这次 " + read.Name + " 的结果已被后面的读取取代"
                            + (writeSeen ? "（期间文档已修改，段号可能已变化）" : string.Empty)
                            + "。以最新一次读取为准，不要依据本条结果重做排版。";
                        folded++;
                        break;
                    }
                }
            }

            return folded;
        }

        private static string ArgumentsOf(Dictionary<string, string> argumentsById, ModelClientMessage toolMessage)
        {
            string arguments;
            return !string.IsNullOrEmpty(toolMessage.ToolCallId) && argumentsById.TryGetValue(toolMessage.ToolCallId, out arguments)
                ? arguments
                : string.Empty;
        }

        private static int LastUserIndex(IList<ModelClientMessage> messages)
        {
            for (var index = messages.Count - 1; index >= 0; index--)
            {
                if (messages[index] != null && string.Equals(messages[index].Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
