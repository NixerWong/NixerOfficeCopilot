using OfficeCopilot.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 记忆写入工具：用户说"记住xx"时，模型调用此工具将内容写入持久化记忆。
    /// 对应 cc-haha <c>updateMemory.ts</c> 中的 updateMemory 函数。
    /// </summary>
    internal sealed class MemoryWriteTool
    {
        public const string ToolName = "runtime.record_memory";

        private readonly IMemoryStore _memoryStore;

        public MemoryWriteTool(IMemoryStore memoryStore)
        {
            _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
        }

        public async Task<ToolExecutionOutcome> ExecuteAsync(
            string toolCallId, string toolName, string argumentsJson,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content = string.Empty;
            List<string> tags = null;
            string scope = "user";
            string type = "user";

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson ?? "{}");
                if (doc.RootElement.TryGetProperty("content", out var contentProp))
                    content = contentProp.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                {
                    tags = new List<string>();
                    foreach (var item in tagsProp.EnumerateArray())
                    {
                        var t = item.GetString();
                        if (!string.IsNullOrWhiteSpace(t))
                            tags.Add(t.Trim());
                    }
                }
                if (doc.RootElement.TryGetProperty("scope", out var scopeProp))
                    scope = scopeProp.GetString() ?? "user";
                if (doc.RootElement.TryGetProperty("type", out var typeProp))
                    type = typeProp.GetString() ?? "user";
            }
            catch
            {
                return Fail(toolCallId, toolName, "record_memory.parse_error",
                    "Failed to parse memory record arguments.");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return Fail(toolCallId, toolName, "record_memory.empty_content",
                    "Memory content cannot be empty.");
            }

            // Normalize scope
            var memoryScope = MemoryScope.User;
            if (string.Equals(scope, "workspace", StringComparison.OrdinalIgnoreCase))
                memoryScope = MemoryScope.Workspace;
            else if (string.Equals(scope, "session", StringComparison.OrdinalIgnoreCase))
                memoryScope = MemoryScope.Session;

            // Generate stable fragment ID from content + tags
            var fragmentId = GenerateFragmentId(content, tags);

            try
            {
                var fragment = new MemoryFragment
                {
                    Id = fragmentId,
                    Tags = tags ?? new List<string>(),
                    Scope = memoryScope,
                    Type = string.IsNullOrWhiteSpace(type) ? "user" : type,
                    Content = content.Trim(),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                await _memoryStore.SaveFragmentAsync(fragment, cancellationToken).ConfigureAwait(false);

                // Also append index line to MEMORY.md entrypoint
                var tagStr = fragment.Tags.Count > 0
                    ? string.Join(", ", fragment.Tags)
                    : "user";
                var summary = content.Trim();
                if (summary.Length > 80)
                    summary = summary.Substring(0, 80) + "...";
                var indexLine = $"- [{tagStr}] {summary} -> {fragmentId}.md";
                await _memoryStore.AppendEntrypointIndexAsync(indexLine, cancellationToken).ConfigureAwait(false);

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = true,
                    Content = $"记忆已记录：{summary}\n片段 ID: {fragmentId}\n标签: {tagStr}\n作用域: {scope}"
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Fail(toolCallId, toolName, "record_memory.write_failed",
                    $"Failed to write memory: {ex.Message}");
            }
        }

        private static string GenerateFragmentId(string content, List<string> tags)
        {
            // Simple hash-based ID from content for dedup
            var input = (content ?? string.Empty).Trim();
            if (tags != null && tags.Count > 0)
                input += "|" + string.Join(",", tags.OrderBy(t => t));

            var hash = Math.Abs(input.GetHashCode()).ToString("x8");
            return $"mem-{hash}";
        }

        private static ToolExecutionOutcome Fail(string toolCallId, string toolName,
            string errorCode, string message)
        {
            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = false,
                ErrorCode = errorCode,
                ErrorMessage = message
            };
        }
    }
}
