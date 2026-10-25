using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OfficeCopilot.Agent.Persistence;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 跨轮次回放只保留 user/assistant 文本：Word 任务被轮次上限或中止打断后，用户回复“继续”，
    /// 模型看不到上一轮做过的写操作和用户在 ask_user 中的选择，于是从头重排。
    /// 这里从已持久化的工具消息中提取进度，作为 system 提示交给当前轮次。
    /// </summary>
    internal static class PriorTurnWordProgressCarryOver
    {
        private const string AskUserToolName = "runtime.ask_user_question";
        private const int MaxLookbackTurns = 3;
        private const int MaxRecentWrites = 12;
        private const int MaxAskAnswers = 4;
        private const int MaxArgumentPreviewLength = 140;
        private const int MaxResultPreviewLength = 240;
        private const int MaxAnswerLength = 600;

        private static readonly string[] ReadOnlyPrefixes = { "word_get_", "word_list_", "word_move_", "word_read_", "word_select_" };

        public static string BuildNote(IEnumerable<BrokerConversationMessage> messages, string currentMessageId)
        {
            var history = (messages ?? Enumerable.Empty<BrokerConversationMessage>()).ToList();
            var currentIndex = history.FindIndex(message => message != null
                && string.Equals(message.MessageId, currentMessageId, StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0)
            {
                currentIndex = history.Count;
            }

            var turns = CollectPriorTurns(history, currentIndex);
            if (!turns.Any(t => t.Calls.Any(c => c.Name.StartsWith("word_", StringComparison.OrdinalIgnoreCase))))
            {
                return string.Empty;
            }

            var withWrites = turns.FirstOrDefault(t => t.Calls.Any(c => IsWriteTool(c.Name)));
            var answers = turns
                .SelectMany(t => t.Calls)
                .Where(c => c.Success && string.Equals(c.Name, AskUserToolName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Result))
                .Take(MaxAskAnswers)
                .Reverse()
                .ToList();
            if (withWrites == null && answers.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Word progress from earlier turns in this session (original tool results are not replayed; the following was extracted by the runtime):");
            if (answers.Count > 0)
            {
                builder.AppendLine("The user confirmed the following in a prior question (still valid; do not ask again):");
                foreach (var answer in answers)
                {
                    builder.Append("- ").AppendLine(Compact(StripAnswerHeader(answer.Result), MaxAnswerLength));
                }
            }

            if (withWrites != null)
            {
                AppendWrites(builder, withWrites);
            }

            builder.Append("If the user asks to continue, first inspect the current document once with word_read_paragraphs / word_read_outline, then complete only the remaining work. ")
                .Append("Do not repeat successful edits or restart formatting from paragraph 1.");
            return builder.ToString();
        }

        internal static bool IsWriteTool(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName) || !toolName.StartsWith("word_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !ReadOnlyPrefixes.Any(prefix => toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static void AppendWrites(StringBuilder builder, PriorTurn turn)
        {
            var writes = turn.Calls.Where(c => IsWriteTool(c.Name)).ToList();
            var succeeded = writes.Count(c => c.Success);
            builder.Append("Most recent turn with writes: ").Append(turn.Calls.Count).Append(" tool calls, ")
                .Append(writes.Count).Append(" write operations (").Append(succeeded).Append(" succeeded). ");
            var distribution = writes
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Key + " ×" + g.Count())
                .ToList();
            builder.Append("Write operations by tool: ").Append(string.Join(", ", distribution)).AppendLine(".");

            var batch = writes.LastOrDefault(c => c.Success && string.Equals(c.Name, "word_format_paragraphs", StringComparison.OrdinalIgnoreCase));
            if (batch != null)
            {
                builder.Append("Most recent batch-formatting result: ").AppendLine(Compact(batch.Result, MaxResultPreviewLength));
            }

            builder.AppendLine("Recent write operations (chronological):");
            foreach (var call in writes.Skip(Math.Max(0, writes.Count - MaxRecentWrites)))
            {
                builder.Append("- ").Append(call.Success ? "✓ " : "✗ ").Append(call.Name);
                if (call.SelectedParagraph.HasValue)
                {
                    builder.Append(" (paragraph ").Append(call.SelectedParagraph.Value).Append(")");
                }

                builder.Append(' ').Append(Compact(call.Arguments, MaxArgumentPreviewLength));
                if (!call.Success && !string.IsNullOrWhiteSpace(call.Result))
                {
                    builder.Append(" → ").Append(Compact(call.Result, 100));
                }

                builder.AppendLine();
            }
        }

        /// <summary>从当前用户消息往前，按“用户消息”切分出最多 N 个已结束的轮次（最近的在前）。</summary>
        private static List<PriorTurn> CollectPriorTurns(List<BrokerConversationMessage> history, int currentIndex)
        {
            var turns = new List<PriorTurn>();
            var end = currentIndex;
            for (var index = currentIndex - 1; index >= 0 && turns.Count < MaxLookbackTurns; index--)
            {
                var message = history[index];
                if (message == null || !string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                turns.Add(ParseTurn(history, index + 1, end));
                end = index;
            }

            return turns;
        }

        private static PriorTurn ParseTurn(List<BrokerConversationMessage> history, int start, int end)
        {
            var turn = new PriorTurn();
            var pending = new Dictionary<string, ToolCallRecord>(StringComparer.Ordinal);
            int? selectedParagraph = null;
            for (var index = start; index < end; index++)
            {
                var message = history[index];
                if (message == null)
                {
                    continue;
                }

                if (string.Equals(message.MessageType, "tool_call", StringComparison.OrdinalIgnoreCase))
                {
                    var name = string.IsNullOrWhiteSpace(message.Name) ? message.Content : message.Name;
                    var record = new ToolCallRecord
                    {
                        Name = name ?? string.Empty,
                        Arguments = message.StructuredContentJson ?? string.Empty,
                        SelectedParagraph = IsWriteTool(name) ? selectedParagraph : null
                    };
                    turn.Calls.Add(record);
                    if (!string.IsNullOrEmpty(message.ToolCallId))
                    {
                        pending[message.ToolCallId] = record;
                    }

                    if (string.Equals(name, "word_select_paragraph", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedParagraph = ReadParagraphIndex(message.StructuredContentJson);
                    }
                    else if (string.Equals(name, "word_select_document", StringComparison.OrdinalIgnoreCase))
                    {
                        selectedParagraph = null;
                    }

                    continue;
                }

                if (string.Equals(message.MessageType, "tool_result", StringComparison.OrdinalIgnoreCase))
                {
                    ToolCallRecord record;
                    if (!string.IsNullOrEmpty(message.ToolCallId) && pending.TryGetValue(message.ToolCallId, out record))
                    {
                        record.Success = message.Success;
                        record.Result = message.Content ?? string.Empty;
                    }
                }
            }

            return turn;
        }

        private static int? ReadParagraphIndex(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return null;
            }

            var match = System.Text.RegularExpressions.Regex.Match(argumentsJson, "\"paragraphIndex\"\\s*:\\s*(\\d+)");
            int value;
            return match.Success && int.TryParse(match.Groups[1].Value, out value) ? value : (int?)null;
        }

        private static string StripAnswerHeader(string result)
        {
            var text = result ?? string.Empty;
            const string header = "User has answered your questions.";
            return text.StartsWith(header, StringComparison.OrdinalIgnoreCase) ? text.Substring(header.Length) : text;
        }

        private static string Compact(string text, int maximumLength)
        {
            var compact = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            while (compact.Contains("  "))
            {
                compact = compact.Replace("  ", " ");
            }

            return compact.Length <= maximumLength ? compact : compact.Substring(0, maximumLength) + "…";
        }

        private sealed class PriorTurn
        {
            public List<ToolCallRecord> Calls { get; } = new List<ToolCallRecord>();
        }

        private sealed class ToolCallRecord
        {
            public string Name { get; set; }
            public string Arguments { get; set; }
            public bool Success { get; set; }
            public string Result { get; set; }
            public int? SelectedParagraph { get; set; }
        }
    }
}
