using Microsoft.Extensions.Logging;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Panel.WebView.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Panel.WebView.Services
{
    /// <summary>
    /// F1：从 <c>CopilotPanelControl</c> 抽出的会话持久化访问层。
    /// 面板原先把「拉列表 / 补消息 / 删会话 / 回灌标题」四条 Broker IPC 与各自的
    /// try-catch 直接写在控件里；这里统一收口，控件只保留 UI 编排。读取历史时兼容旧版本地会话文件。
    /// 每个方法都自吞异常（会话历史属于尽力而为的元数据，失败不应打断当前轮次）。
    /// </summary>
    internal sealed class BrokerSessionRepository
    {
        private readonly Func<IAgentBrokerClient> _clientAccessor;
        private readonly ILogger _logger;

        public BrokerSessionRepository(Func<IAgentBrokerClient> clientAccessor, ILogger logger)
        {
            if (clientAccessor == null) throw new ArgumentNullException(nameof(clientAccessor));

            _clientAccessor = clientAccessor;
            _logger = logger;
        }

        /// <summary>
        /// 拉取指定宿主下最近的会话列表。
        /// 行为变更：面板默认展示空白新会话，不再自动恢复上一次会话，因此这里只返回摘要
        /// （不拉取最近会话的完整消息），避免打开面板时额外的 IPC 与渲染开销；
        /// 消息在用户主动切换到某会话时由 <see cref="EnsureMessagesLoadedAsync"/> 按需加载。
        /// 旧版共享列表 <c>ChatSessions\sessions.json</c> 不再合并（无宿主字段，无法隔离），见 docs/CHAT_HISTORY_ISOLATION.md。
        /// Broker 不可用或无历史时返回 <c>null</c>。
        /// </summary>
        public async Task<LoadedSessions> LoadRecentAsync(string hostApplicationName, int take, CancellationToken cancellationToken)
        {
            var client = _clientAccessor();
            if (client == null)
            {
                return null;
            }

            try
            {
                var listResult = await client.ListConversationsAsync(
                    new AgentBrokerListConversationsRequest
                    {
                        HostApplicationName = hostApplicationName ?? string.Empty,
                        Take = take
                    },
                    cancellationToken).ConfigureAwait(false);

                PanelTrace.Write($"[panel] ListConversations result: Success={listResult?.Success}, Count={listResult?.Summaries?.Count ?? 0}");

                if (listResult == null || !listResult.Success || listResult.Summaries == null)
                {
                    return null;
                }

                var summaries = listResult.Summaries
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.ConversationId))
                    .OrderByDescending(s => s.UpdatedAtUtc)
                    .ToList();

                // 行为变更：不加载最近一次历史会话的完整消息。面板打开时展示空白新会话，
                // 历史会话仅以摘要形式进入历史菜单，用户切换时再按需拉取消息。
                var sessions = summaries
                    .Select(BrokerToPanelConverter.ConvertSummary)
                    .Where(s => s != null)
                    .ToList();

                return CreateLoadedSessions(sessions);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "从 Broker 加载历史会话失败。");
                return null;
            }
        }

        private static LoadedSessions CreateLoadedSessions(IList<ChatSessionState> sessions)
        {
            if (sessions == null || sessions.Count == 0)
            {
                return null;
            }

            var ordered = sessions
                .OrderByDescending(session => session.UpdatedAt)
                .ToList();
            return new LoadedSessions(ordered[0], ordered.Skip(1).ToList());
        }

        /// <summary>
        /// 为仅有摘要的会话补齐消息列表；已有消息时直接返回。
        /// </summary>
        public async Task EnsureMessagesLoadedAsync(ChatSessionState session, CancellationToken cancellationToken)
        {
            if (session == null || (session.Messages != null && session.Messages.Count > 0))
            {
                return;
            }

            var converted = await LoadConversationAsync(session.Id, cancellationToken).ConfigureAwait(false);
            if (converted == null || converted.Messages == null)
            {
                return;
            }

            session.Messages = converted.Messages;

            if (string.IsNullOrWhiteSpace(session.Title) && !string.IsNullOrWhiteSpace(converted.Title))
            {
                session.Title = converted.Title;
            }

            if (!session.ModelProviderId.HasValue && converted.ModelProviderId.HasValue)
            {
                session.ModelProviderId = converted.ModelProviderId;
            }
        }

        /// <summary>
        /// 强制重拉消息，用于 broker turn 结束后刷新 <c>ContentParts</c>
        /// （文本与工具调用的交错顺序），避免切回会话时工具调用被堆到底部。
        /// </summary>
        public async Task RefreshMessagesAsync(ChatSessionState session, CancellationToken cancellationToken)
        {
            if (session == null)
            {
                return;
            }

            var converted = await LoadConversationAsync(session.Id, cancellationToken).ConfigureAwait(false);
            if (converted != null && converted.Messages != null)
            {
                session.Messages = converted.Messages;
            }
        }

        public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            var client = _clientAccessor();
            if (client != null)
            {
                try
                {
                    await client.DeleteConversationAsync(
                        new AgentBrokerDeleteConversationRequest { ConversationId = sessionId.ToString("D") },
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "从 Broker 删除会话失败。ConversationId={ConversationId}", sessionId);
                }
            }
        }

        /// <summary>
        /// 把面板侧生成的会话标题回灌到 Broker（不覆盖已有会话内容）。
        /// </summary>
        public async Task PersistTitleAsync(ChatSessionState session, string hostApplicationName, CancellationToken cancellationToken)
        {
            var client = _clientAccessor();
            if (session == null || client == null)
            {
                return;
            }

            var title = (session.Title ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(title) || string.Equals(title, ChatSessionFactory.DefaultNewSessionTitle, StringComparison.Ordinal))
            {
                return;
            }

            var request = new AgentBrokerSeedConversationRequest
            {
                ConversationId = session.Id.ToString("D"),
                HostApplicationName = hostApplicationName ?? string.Empty,
                Title = title,
                OverwriteExisting = false,
                Messages = new List<AgentBrokerSeedMessage>()
            };

            try
            {
                var result = await client.SeedConversationAsync(request, cancellationToken).ConfigureAwait(false);
                if (result != null && !result.Success)
                {
                    _logger?.LogWarning(
                        "Broker 标题持久化失败。ConversationId={ConversationId} ErrorCode={ErrorCode} ErrorMessage={ErrorMessage}",
                        request.ConversationId,
                        result.ErrorCode,
                        result.ErrorMessage ?? string.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                // 不抛：仅是元数据回灌，被取消可接受。
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Broker 标题持久化异常。ConversationId={ConversationId}", request.ConversationId);
            }
        }

        private async Task<ChatSessionState> LoadConversationAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            var client = _clientAccessor();
            if (client == null)
            {
                return null;
            }

            try
            {
                var loadResult = await client.LoadConversationAsync(
                    new AgentBrokerLoadConversationRequest { ConversationId = sessionId.ToString("D") },
                    cancellationToken).ConfigureAwait(false);

                if (loadResult == null || !loadResult.Success || !loadResult.Found)
                {
                    return null;
                }

                return BrokerToPanelConverter.ConvertConversation(loadResult);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "从 Broker 加载会话消息失败。ConversationId={ConversationId}", sessionId);
                return null;
            }
        }

        internal sealed class LoadedSessions
        {
            public LoadedSessions(ChatSessionState mostRecent, List<ChatSessionState> others)
            {
                MostRecent = mostRecent;
                Others = others ?? new List<ChatSessionState>();
            }

            public ChatSessionState MostRecent { get; }

            public List<ChatSessionState> Others { get; }
        }
    }
}
