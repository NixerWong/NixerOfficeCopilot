using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Memory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.Memory
{
    /// <summary>
    /// 记忆自动抽取器：每 N 轮 turn 后用便宜模型判断是否从对话中抽取持久记忆。
    /// 对应 cc-haha <c>extractMemories.ts</c>。
    /// </summary>
    internal sealed class MemoryExtractor
    {
        private readonly IMemoryStore _store;
        private readonly IModelClient _cheapModel;
        private readonly int _extractInterval;  // 每 N 轮跑一次
        private readonly int _maxFragmentsPerConversation;

        public MemoryExtractor(IMemoryStore store, IModelClient cheapModel, int extractInterval = 3, int maxFragmentsPerConversation = 10)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _cheapModel = cheapModel ?? throw new ArgumentNullException(nameof(cheapModel));
            _extractInterval = Math.Max(1, extractInterval);
            _maxFragmentsPerConversation = Math.Max(1, maxFragmentsPerConversation);
        }

        /// <summary>
        /// 如果满足条件（每 N 轮 + 未超限），则尝试从对话中抽取记忆。
        /// </summary>
        public async Task MaybeExtractAsync(
            IList<OfficeCopilot.Agent.Persistence.BrokerConversationMessage> messages,
            string conversationId,
            AgentModelOptions modelOptions,
            CancellationToken ct)
        {
            if (messages == null || messages.Count == 0) return;
            if (string.IsNullOrWhiteSpace(conversationId)) return;
            if (modelOptions == null || string.IsNullOrWhiteSpace(modelOptions.Endpoint)) return;

            // 每 N 轮才跑一次
            var turnCount = messages.Count(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
            if (turnCount % _extractInterval != 0) return;

            // 限速：单会话最多 N 条
            var existingCount = await _store.CountFragmentsForConversationAsync(conversationId, ct).ConfigureAwait(false);
            if (existingCount >= _maxFragmentsPerConversation) return;

            try
            {
                var prompt = BuildExtractionPrompt(messages);
                var completion = await _cheapModel.CompleteAsync(
                    modelOptions,
                    new List<ModelClientMessage> { new ModelClientMessage { Role = "user", Content = prompt } },
                    new List<ModelClientTool>(),
                    null,
                    ct).ConfigureAwait(false);

                var fragment = ParseExtractionResult(completion?.Content ?? string.Empty, conversationId);
                if (fragment != null)
                {
                    await _store.SaveFragmentAsync(fragment, ct).ConfigureAwait(false);

                    // 自动维护 MEMORY.md 索引：追加一行摘要 -> fragmentId
                    var tagStr = fragment.Tags != null && fragment.Tags.Count > 0
                        ? "[" + string.Join(", ", fragment.Tags) + "] "
                        : "";
                    var summary = TruncateSummary(fragment.Content);
                    var indexLine = $"- {tagStr}{summary} -> {fragment.Id}.md";
                    await _store.AppendEntrypointIndexAsync(indexLine, ct).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
        }

        private static string BuildExtractionPrompt(IList<OfficeCopilot.Agent.Persistence.BrokerConversationMessage> messages)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are a memory-extraction assistant. Review the conversation and determine whether it contains useful information worth saving as persistent memory.");
            sb.AppendLine();
            sb.AppendLine("Extract a memory only when the conversation contains:");
            sb.AppendLine("- User preferences (for example, \"I prefer Python over C#\")");
            sb.AppendLine("- Patterns or workflows the user often follows");
            sb.AppendLine("- Mistakes and lessons learned");
            sb.AppendLine("- Important decisions or constraints");
            sb.AppendLine("- Useful facts about the user's environment or tools");
            sb.AppendLine();
            sb.AppendLine("If there is nothing worth saving, respond with: {\"extract\": false}");
            sb.AppendLine("If there is something worth saving, respond with one JSON object:");
            sb.AppendLine("{");
            sb.AppendLine("  \"extract\": true,");
            sb.AppendLine("  \"tags\": [\"tag1\", \"tag2\"],");
            sb.AppendLine("  \"content\": \"Summarize the memory in the language used by the user in the conversation, in 1-3 sentences.\"");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("--- Conversation ---");

            var recentMessages = messages.Skip(Math.Max(0, messages.Count - 10)).ToList();
            foreach (var msg in recentMessages)
            {
                var role = msg.Role ?? "unknown";
                var content = msg.Content ?? string.Empty;
                if (content.Length > 500) content = content.Substring(0, 500) + "...";
                sb.AppendLine($"[{role}]: {content}");
            }

            return sb.ToString().Trim();
        }

        private static MemoryFragment ParseExtractionResult(string modelResponse, string conversationId)
        {
            if (string.IsNullOrWhiteSpace(modelResponse)) return null;

            try
            {
                // Try to extract JSON from the response
                var json = modelResponse.Trim();
                var start = json.IndexOf('{');
                var end = json.LastIndexOf('}');
                if (start < 0 || end <= start) return null;
                json = json.Substring(start, end - start + 1);

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("extract", out var extractProp) || !extractProp.GetBoolean())
                    return null;

                var tags = new List<string>();
                if (root.TryGetProperty("tags", out var tagsProp))
                {
                    foreach (var tag in tagsProp.EnumerateArray())
                        if (tag.GetString() is { } t && !string.IsNullOrWhiteSpace(t))
                            tags.Add(t.Trim());
                }

                var content = root.TryGetProperty("content", out var contentProp)
                    ? contentProp.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(content)) return null;

                return new MemoryFragment
                {
                    Id = $"auto-{conversationId}-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    Tags = tags,
                    Scope = MemoryScope.User,
                    Content = content,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 截取 fragment 内容的前 80 个字符作为索引摘要（在第一个句号或换行处截断）。
        /// </summary>
        private static string TruncateSummary(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "(empty)";

            var maxLen = Math.Min(content.Length, 80);
            var summary = content.Substring(0, maxLen);

            // 尽量在句子边界截断
            var cutIdx = summary.IndexOfAny(new[] { '.', '。', '\n', '\r' });
            if (cutIdx > 10)
                summary = summary.Substring(0, cutIdx);

            return summary.Trim();
        }
    }
}
