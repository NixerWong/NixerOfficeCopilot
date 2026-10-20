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
            sb.AppendLine("你是一个记忆抽取助手。请审阅以下对话，判断是否有值得保存为持久记忆的有用信息。");
            sb.AppendLine();
            sb.AppendLine("仅在对话包含以下内容时才抽取：");
            sb.AppendLine("- 用户偏好（如「我喜欢用 Python 而非 C#」）");
            sb.AppendLine("- 用户经常使用的模式或工作流");
            sb.AppendLine("- 犯过的错误和经验教训");
            sb.AppendLine("- 重要的决策或约束条件");
            sb.AppendLine("- 用户环境或工具有用的事实信息");
            sb.AppendLine();
            sb.AppendLine("如果对话中没有值得保存的内容，请回复：{\"extract\": false}");
            sb.AppendLine("如果有值得保存的内容，请用中文回复一个 JSON 对象：");
            sb.AppendLine("{");
            sb.AppendLine("  \"extract\": true,");
            sb.AppendLine("  \"tags\": [\"标签1\", \"标签2\"],");
            sb.AppendLine("  \"content\": \"用中文总结的记忆内容，1-3 句话。\"");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("--- 对话内容 ---");

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
                return "(空)";

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
