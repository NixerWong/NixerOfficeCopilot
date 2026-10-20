using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Persistence
{
    /// <summary>
    /// 会话持久化契约。<br/>
    /// Phase 1.1 引入，对应蓝图 §3.1。两个实现：
    /// <list type="bullet">
    ///   <item><see cref="InMemoryConversationStore"/> — 进程内字典，重启即丢，主要给单测和"无持久化"配置用。</item>
    ///   <item><see cref="Jsonl.JsonlConversationStore"/> — 落盘到 <c>%APPDATA%/NixerOfficeCopilot/sessions/&lt;yyyyMMdd&gt;/&lt;convId&gt;.jsonl</c>，每条消息一行 JSON。</item>
    /// </list>
    /// 现有 <c>IBrokerSessionStore</c>（Agent.Runtime 程序集）仍是 production 实际入口；
    /// 切换到本接口的 broker 接线在 Phase 1.3 完成。
    /// </summary>
    public interface IConversationStore
    {
        /// <summary>
        /// 取回会话；若不存在则按 <paramref name="conversationId"/> 创建空会话。
        /// 实现方负责在内部去重，本方法对同一 id 多次调用应幂等。
        /// </summary>
        Task<BrokerConversationState> GetOrCreateAsync(
            string conversationId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 追加单条消息并立即落盘（如果是落盘实现）。
        /// 等价于 <c>state.Messages.Add(message); await SaveAsync(state, ct);</c>，但落盘实现可用 append-only IO 跳过整文件重写。
        /// </summary>
        Task AppendMessageAsync(
            string conversationId,
            BrokerConversationMessage message,
            CancellationToken cancellationToken);

        /// <summary>
        /// 把整个会话状态写回。用于 metadata 变更或批量更新。
        /// </summary>
        Task SaveAsync(
            BrokerConversationState conversation,
            CancellationToken cancellationToken);

        /// <summary>
        /// 列出最近的 <paramref name="take"/> 个会话摘要，按 <see cref="ConversationSummary.UpdatedAtUtc"/> 倒序。
        /// 不返回完整消息，仅用于面板"历史会话"侧边栏。
        /// </summary>
        Task<IReadOnlyList<ConversationSummary>> ListAsync(
            int take,
            CancellationToken cancellationToken);

        /// <summary>
        /// Phase 1.4：删除会话。返回 true 表示真有该会话并已删；false 表示该会话不存在（不视为错误）。
        /// </summary>
        Task<bool> DeleteAsync(
            string conversationId,
            CancellationToken cancellationToken);
    }
}
