using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Client;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 单 turn 的事件广播 facade，封装"事件下沉到 <see cref="IAgentBrokerEventSink"/>"的繁琐拼装。
    /// 阶段 0 仅声明；默认实现可直接委托给现有 <see cref="BrokerEventDispatcher"/>，
    /// 在构造时绑定 sink + conversationId + messageId，调用点只关心语义动作。
    /// </summary>
    internal interface ITurnEventEmitter
    {
        Task EmitAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken);

        Task EmitStatusAsync(string text, CancellationToken cancellationToken);

        Task EmitContentDeltaAsync(string text, CancellationToken cancellationToken);

        Task EmitThinkingDeltaAsync(string text, CancellationToken cancellationToken);

        Task EmitTurnStartedAsync(CancellationToken cancellationToken);

        Task EmitTurnCompletedAsync(
            string finalContent,
            bool success,
            AgentBrokerTurnQualitySummary qualitySummary,
            CancellationToken cancellationToken);

        /// <summary>发出 tool_call.started 事件，携带备份路径等信息供 Panel 显示备份图标。</summary>
        Task EmitToolCallStartedAsync(string toolCallId, string toolName, string argumentsJson, bool requiresBackup, string backupPath, CancellationToken cancellationToken);

        Task EmitToolCallStartedAsync(string toolCallId, string toolName, string toolDisplayName, string argumentsJson, bool requiresBackup, string backupPath, CancellationToken cancellationToken);

        /// <summary>发出 tool_call.completed 事件，携带备份路径等信息供 Panel 更新备份图标。</summary>
        Task EmitToolCallCompletedAsync(string toolCallId, string toolName, string text, bool success, bool requiresBackup, string backupPath, string errorCode, string errorMessage, CancellationToken cancellationToken);

        Task EmitToolCallCompletedAsync(string toolCallId, string toolName, string toolDisplayName, string text, bool success, bool requiresBackup, string backupPath, string errorCode, string errorMessage, CancellationToken cancellationToken);
    }

    internal interface ITurnRoundReporter
    {
        void SetRound(int round);
    }
}
