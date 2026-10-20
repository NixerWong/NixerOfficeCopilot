using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Persistence
{
    /// <summary>
    /// 进程内字典实现。语义与现有 <see cref="InMemoryBrokerSessionStore"/> 一致，
    /// 但额外提供 <see cref="AppendMessageAsync"/> / <see cref="ListAsync"/>，承载 Phase 1.1 新契约。
    ///
    /// 用途：
    /// <list type="bullet">
    ///   <item>单测桩 —— 比落盘实现快、无 IO 依赖。</item>
    ///   <item>"InMemory" 配置模式 —— 用户显式关闭持久化时的兜底实现。</item>
    /// </list>
    /// 线程安全：<see cref="ConcurrentDictionary{TKey,TValue}"/> 保证 GetOrCreate 原子；
    /// 但单个 <see cref="BrokerConversationState.Messages"/> 列表本身不是线程安全的，
    /// 同一会话并发 Append 由调用方串行（与原 broker 行为一致）。
    /// </summary>
    public sealed class InMemoryConversationStore : IConversationStore
    {
        private readonly ConcurrentDictionary<string, BrokerConversationState> _conversations;

        public InMemoryConversationStore()
        {
            _conversations = new ConcurrentDictionary<string, BrokerConversationState>(StringComparer.OrdinalIgnoreCase);
        }

        public Task<BrokerConversationState> GetOrCreateAsync(string conversationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = string.IsNullOrWhiteSpace(conversationId)
                ? Guid.NewGuid().ToString("N")
                : conversationId.Trim();

            var conversation = _conversations.GetOrAdd(
                key,
                static id => new BrokerConversationState { ConversationId = id });

            return Task.FromResult(conversation);
        }

        public async Task AppendMessageAsync(
            string conversationId,
            BrokerConversationMessage message,
            CancellationToken cancellationToken)
        {
            if (message == null)
            {
                return;
            }

            var conversation = await GetOrCreateAsync(conversationId, cancellationToken).ConfigureAwait(false);
            if (conversation.Messages == null)
            {
                conversation.Messages = new List<BrokerConversationMessage>();
            }

            conversation.Messages.Add(message);
            conversation.UpdatedAtUtc = DateTime.UtcNow;
        }

        public Task SaveAsync(BrokerConversationState conversation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (conversation == null || string.IsNullOrWhiteSpace(conversation.ConversationId))
            {
                return Task.CompletedTask;
            }

            conversation.UpdatedAtUtc = DateTime.UtcNow;
            _conversations[conversation.ConversationId] = conversation;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ConversationSummary>> ListAsync(int take, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var effectiveTake = take <= 0 ? int.MaxValue : take;

            IReadOnlyList<ConversationSummary> summaries = _conversations.Values
                .OrderByDescending(c => c.UpdatedAtUtc)
                .Take(effectiveTake)
                .Select(BuildSummary)
                .ToList();

            return Task.FromResult(summaries);
        }

        public Task<bool> DeleteAsync(string conversationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return Task.FromResult(false);
            }

            BrokerConversationState _;
            var removed = _conversations.TryRemove(conversationId, out _);
            return Task.FromResult(removed);
        }

        private static ConversationSummary BuildSummary(BrokerConversationState conversation)
        {
            var summary = new ConversationSummary
            {
                ConversationId = conversation.ConversationId ?? string.Empty,
                MessageCount = conversation.Messages == null ? 0 : conversation.Messages.Count,
                CreatedAtUtc = conversation.CreatedAtUtc,
                UpdatedAtUtc = conversation.UpdatedAtUtc
            };

            if (conversation.Metadata != null)
            {
                string hostApp;
                if (conversation.Metadata.TryGetValue("hostApplicationName", out hostApp))
                {
                    summary.HostApplicationName = hostApp ?? string.Empty;
                }

                string explicitTitle;
                if (conversation.Metadata.TryGetValue("title", out explicitTitle)
                    && !string.IsNullOrWhiteSpace(explicitTitle))
                {
                    summary.Title = explicitTitle.Trim();
                }

                foreach (var kv in conversation.Metadata)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                    {
                        summary.Metadata[kv.Key] = kv.Value ?? string.Empty;
                    }
                }
            }

            // Title 兜底：首条 user 消息的前 80 字符（与 cc-haha sessionTitle.ts 等价）。
            if (string.IsNullOrWhiteSpace(summary.Title) && conversation.Messages != null)
            {
                for (var i = 0; i < conversation.Messages.Count; i++)
                {
                    var m = conversation.Messages[i];
                    if (m != null
                        && string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(m.Content))
                    {
                        var content = m.Content.Trim();
                        summary.Title = content.Length > 80 ? content.Substring(0, 80) : content;
                        break;
                    }
                }
            }

            return summary;
        }
    }
}
