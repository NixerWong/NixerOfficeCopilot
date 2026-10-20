using OfficeCopilot.Memory;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 记忆上下文注入器：将 MEMORY.md 和关联片段拼入 system prompt 与用户消息。
    /// 对应 cc-haha 的 <c>loadMemoryPrompt</c> + <c>findRelevantMemories</c>。
    /// </summary>
    internal sealed class MemoryContextInjector
    {
        private readonly IMemoryStore _memoryStore;

        public MemoryContextInjector(IMemoryStore memoryStore)
        {
            _memoryStore = memoryStore;
        }

        /// <summary>
        /// 将 MEMORY.md 内容（截断后）拼入 system prompt 末尾。
        /// </summary>
        public async Task<string> InjectMemoryIntoSystemPromptAsync(
            string systemPrompt,
            CancellationToken cancellationToken)
        {
            if (_memoryStore == null)
                return systemPrompt ?? string.Empty;

            try
            {
                var entrypoint = await _memoryStore.LoadEntrypointAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(entrypoint.Content))
                    return systemPrompt ?? string.Empty;

                var sb = new StringBuilder(systemPrompt ?? string.Empty);

                sb.AppendLine();
                sb.AppendLine();
                sb.AppendLine("## User Memory (MEMORY.md)");
                sb.AppendLine("The following is the user's persistent memory. "
                    + "Use this context to personalize your responses and avoid repeating past mistakes.");
                sb.AppendLine();
                sb.AppendLine(entrypoint.Content);

                return sb.ToString();
            }
            catch (Exception)
            {
                return systemPrompt ?? string.Empty;
            }
        }

        /// <summary>
        /// 为当前用户输入查找相关记忆，拼接到用户消息前。
        /// </summary>
        public async Task<string> InjectRelevantMemoriesAsync(
            string userInput,
            CancellationToken cancellationToken)
        {
            if (_memoryStore == null || string.IsNullOrWhiteSpace(userInput))
                return userInput ?? string.Empty;

            try
            {
                var fragments = await _memoryStore.FindRelevantAsync(userInput, 5, cancellationToken)
                    .ConfigureAwait(false);

                if (fragments == null || fragments.Count == 0)
                    return userInput ?? string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine("[Relevant Memories]");
                foreach (var fragment in fragments)
                {
                    sb.Append("- ").Append(fragment.Content).AppendLine();
                }
                sb.AppendLine();
                sb.Append(userInput);

                return sb.ToString();
            }
            catch (Exception)
            {
                return userInput ?? string.Empty;
            }
        }
    }
}
