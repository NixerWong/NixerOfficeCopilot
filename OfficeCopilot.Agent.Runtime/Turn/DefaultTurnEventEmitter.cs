using System;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Client;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 默认 <see cref="ITurnEventEmitter"/> 实现：把语义化事件转译成 <see cref="AgentBrokerEvent"/>，
    /// 委托给现有 <see cref="BrokerEventDispatcher"/> + <see cref="IAgentBrokerEventSink"/> 链路。
    /// 阶段 0 引入；阶段 0.3 把 <see cref="BrokerTurnExecutor"/> 内的事件下沉点全部切换为调用本类。
    /// 构造时绑定 sink + conversationId + messageId，调用方只关心动作语义。
    /// </summary>
    internal sealed class DefaultTurnEventEmitter : ITurnEventEmitter, ITurnRoundReporter
    {
        private readonly BrokerEventDispatcher _dispatcher;
        private readonly IAgentBrokerEventSink _sink;
        private readonly string _conversationId;
        private readonly string _messageId;
        private int _round;

        public DefaultTurnEventEmitter(
            BrokerEventDispatcher dispatcher,
            IAgentBrokerEventSink sink,
            string conversationId,
            string messageId)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _sink = sink;
            _conversationId = conversationId ?? string.Empty;
            _messageId = messageId ?? string.Empty;
        }

        public Task EmitAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken)
        {
            if (brokerEvent == null)
            {
                return Task.CompletedTask;
            }

            if (string.IsNullOrEmpty(brokerEvent.ConversationId))
            {
                brokerEvent.ConversationId = _conversationId;
            }

            if (string.IsNullOrEmpty(brokerEvent.MessageId))
            {
                brokerEvent.MessageId = _messageId;
            }

            if (brokerEvent.Round <= 0)
            {
                brokerEvent.Round = _round;
            }

            return _dispatcher.DispatchAsync(_sink, brokerEvent, cancellationToken);
        }

        public void SetRound(int round)
        {
            _round = Math.Max(0, round);
        }

        public Task EmitStatusAsync(string text, CancellationToken cancellationToken)
        {
            return EmitAsync(
                new AgentBrokerEvent
                {
                    EventType = AgentBrokerEventTypes.Status,
                    ConversationId = _conversationId,
                    MessageId = _messageId,
                    Text = text ?? string.Empty
                },
                cancellationToken);
        }

        public Task EmitContentDeltaAsync(string text, CancellationToken cancellationToken)
        {
            return EmitAsync(
                new AgentBrokerEvent
                {
                    EventType = AgentBrokerEventTypes.ContentDelta,
                    ConversationId = _conversationId,
                    MessageId = _messageId,
                    Text = text ?? string.Empty
                },
                cancellationToken);
        }

        public Task EmitThinkingDeltaAsync(string text, CancellationToken cancellationToken)
        {
            return EmitAsync(
                new AgentBrokerEvent
                {
                    EventType = AgentBrokerEventTypes.ThinkingDelta,
                    ConversationId = _conversationId,
                    MessageId = _messageId,
                    Text = text ?? string.Empty
                },
                cancellationToken);
        }

        public Task EmitTurnStartedAsync(CancellationToken cancellationToken)
        {
            return EmitAsync(
                new AgentBrokerEvent
                {
                    EventType = AgentBrokerEventTypes.TurnStarted,
                    ConversationId = _conversationId,
                    MessageId = _messageId
                },
                cancellationToken);
        }

        public Task EmitTurnCompletedAsync(
            string finalContent,
            bool success,
            AgentBrokerTurnQualitySummary qualitySummary,
            CancellationToken cancellationToken)
        {
            return EmitAsync(
                new AgentBrokerEvent
                {
                    EventType = AgentBrokerEventTypes.TurnCompleted,
                    ConversationId = _conversationId,
                    MessageId = _messageId,
                    Text = finalContent ?? string.Empty,
                    Success = success,
                    QualitySummary = qualitySummary
                },
                cancellationToken);
        }

        public Task EmitToolCallStartedAsync(string toolCallId, string toolName, string toolDisplayName, string argumentsJson, bool requiresBackup, string backupPath, CancellationToken cancellationToken)
        {
            return EmitAsync(
                new AgentBrokerEvent
                {
                    EventType = AgentBrokerEventTypes.ToolCallStarted,
                    ConversationId = _conversationId,
                    MessageId = _messageId,
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    ToolDisplayName = toolDisplayName ?? string.Empty,
                    Text = argumentsJson ?? string.Empty,
                    RequiresBackup = requiresBackup,
                    BackupPath = backupPath ?? string.Empty
                },
                cancellationToken);
        }

        public Task EmitToolCallStartedAsync(string toolCallId, string toolName, string argumentsJson, bool requiresBackup, string backupPath, CancellationToken cancellationToken)
        {
            return EmitToolCallStartedAsync(
                toolCallId,
                toolName,
                toolName,
                argumentsJson,
                requiresBackup,
                backupPath,
                cancellationToken);
        }

        public Task EmitToolCallCompletedAsync(string toolCallId, string toolName, string toolDisplayName, string text, bool success, bool requiresBackup, string backupPath, string errorCode, string errorMessage, CancellationToken cancellationToken)
        {
            return EmitAsync(
                new AgentBrokerEvent
                {
                    EventType = AgentBrokerEventTypes.ToolCallCompleted,
                    ConversationId = _conversationId,
                    MessageId = _messageId,
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    ToolDisplayName = toolDisplayName ?? string.Empty,
                    Text = text ?? string.Empty,
                    Success = success,
                    RequiresBackup = requiresBackup,
                    BackupPath = backupPath ?? string.Empty,
                    ErrorCode = errorCode ?? string.Empty,
                    ErrorMessage = errorMessage ?? string.Empty,
                    ErrorParameters = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "toolDisplayName", toolDisplayName ?? toolName ?? string.Empty }
                    }
                },
                cancellationToken);
        }

        public Task EmitToolCallCompletedAsync(string toolCallId, string toolName, string text, bool success, bool requiresBackup, string backupPath, string errorCode, string errorMessage, CancellationToken cancellationToken)
        {
            return EmitToolCallCompletedAsync(
                toolCallId,
                toolName,
                toolName,
                text,
                success,
                requiresBackup,
                backupPath,
                errorCode,
                errorMessage,
                cancellationToken);
        }
    }
}
