using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Memory
{
    /// <summary>
    /// 记忆存储抽象：加载入口文件、管理片段、关键词检索。
    /// 对应 cc-haha <c>memdir.ts</c> + <c>findRelevantMemories.ts</c>。
    /// </summary>
    public interface IMemoryStore
    {
        /// <summary>加载并截断 MEMORY.md 入口文件。</summary>
        Task<EntrypointLoadResult> LoadEntrypointAsync(CancellationToken cancellationToken);

        /// <summary>保存（覆写）MEMORY.md 入口文件。</summary>
        Task SaveEntrypointAsync(string content, CancellationToken cancellationToken);

        /// <summary>向 MEMORY.md 末尾追加一行索引（自动去重）。</summary>
        /// <param name="indexLine">格式如 "- [tag1, tag2] 摘要 -> fragmentId.md"</param>
        Task AppendEntrypointIndexAsync(string indexLine, CancellationToken cancellationToken);

        /// <summary>按查询关键词检索相关记忆片段（最多 topK 条）。</summary>
        Task<IReadOnlyList<MemoryFragment>> FindRelevantAsync(string query, int topK, CancellationToken cancellationToken);

        /// <summary>列出所有记忆片段。</summary>
        Task<IReadOnlyList<MemoryFragment>> ListFragmentsAsync(CancellationToken cancellationToken);

        /// <summary>追加或更新一条记忆片段。</summary>
        Task SaveFragmentAsync(MemoryFragment fragment, CancellationToken cancellationToken);

        /// <summary>删除一条记忆片段。</summary>
        Task DeleteFragmentAsync(string fragmentId, CancellationToken cancellationToken);

        /// <summary>统计与指定 conversationId 关联的片段数量（用于限速）。</summary>
        Task<int> CountFragmentsForConversationAsync(string conversationId, CancellationToken cancellationToken);
    }
}
