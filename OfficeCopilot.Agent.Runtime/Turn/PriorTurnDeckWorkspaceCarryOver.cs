using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OfficeCopilot.Agent.Persistence;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 跨轮次回放只保留 user/assistant 文本，上一轮 tool_result 中的演示工作区 ID 会丢失，
    /// 模型在“继续/构建”轮次只能按格式自行编造 deckId。这里从已持久化的工具结果中
    /// 找出最近一次成功返回的工作区，作为 system 提示交给当前轮次。
    /// </summary>
    internal static class PriorTurnDeckWorkspaceCarryOver
    {
        private static readonly Regex DeckIdPattern = new Regex(
            @"^\d{8}-\d{6}-[a-z0-9]{6}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string BuildNote(
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

            string deckId = null;
            string title = null;
            int? slideCount = null;
            for (var index = currentIndex - 1; index >= 0; index--)
            {
                var message = history[index];
                if (message == null
                    || !message.Success
                    || !string.Equals(message.MessageType, "tool_result", StringComparison.OrdinalIgnoreCase)
                    || !IsDeckToolName(message.Name))
                {
                    continue;
                }

                string resultDeckId;
                string resultTitle;
                int? resultSlideCount;
                if (!TryReadWorkspace(message.StructuredContentJson, out resultDeckId, out resultTitle, out resultSlideCount))
                {
                    continue;
                }

                if (deckId == null)
                {
                    deckId = resultDeckId;
                }
                else if (!string.Equals(deckId, resultDeckId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (title == null && !string.IsNullOrWhiteSpace(resultTitle))
                {
                    title = resultTitle;
                }

                if (!slideCount.HasValue && resultSlideCount.HasValue)
                {
                    slideCount = resultSlideCount;
                }

                if (title != null && slideCount.HasValue)
                {
                    break;
                }
            }

            if (deckId == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine("A presentation workspace exists in tool results from an earlier turn in this session (the original results are not replayed; key fields follow):");
            builder.Append("- deckId：").AppendLine(deckId);
            if (title != null)
            {
                builder.Append("- Title: ").AppendLine(title);
            }

            if (slideCount.HasValue)
            {
                builder.Append("- Slide count: ").AppendLine(slideCount.Value.ToString());
            }

            builder.Append("When continuing to edit or preview this workspace, use the deckId above exactly as-is. ")
                .Append("ppt_deck_build may omit deckId, workspaceTitle, and slideCount (the most recently previewed workspace in this session is used automatically); if provided, they must match the values above. ")
                .Append("Never construct a new deckId based on its format.");
            return builder.ToString();
        }

        private static bool IsDeckToolName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && (name.StartsWith("ppt_deck_", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "ppt_style_preview", StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryReadWorkspace(
            string json,
            out string deckId,
            out string title,
            out int? slideCount)
        {
            deckId = null;
            title = null;
            slideCount = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    foreach (var property in root.EnumerateObject())
                    {
                        if (string.Equals(property.Name, "deckId", StringComparison.OrdinalIgnoreCase)
                            && property.Value.ValueKind == JsonValueKind.String)
                        {
                            deckId = property.Value.GetString();
                        }
                        else if (string.Equals(property.Name, "title", StringComparison.OrdinalIgnoreCase)
                            && property.Value.ValueKind == JsonValueKind.String)
                        {
                            title = property.Value.GetString();
                        }
                        else if (string.Equals(property.Name, "slideCount", StringComparison.OrdinalIgnoreCase)
                            && property.Value.ValueKind == JsonValueKind.Number)
                        {
                            int count;
                            if (property.Value.TryGetInt32(out count))
                            {
                                slideCount = count;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return deckId != null && DeckIdPattern.IsMatch(deckId);
        }
    }
}
