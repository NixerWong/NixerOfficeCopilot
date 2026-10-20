using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Persistence;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 占位 <see cref="IContextWindowPolicy"/> 实现：永远返回 <see cref="ContextWindowAction.None"/>，
    /// 行为等价于"无 compact 策略"。
    /// 阶段 0 占位，阶段 1.2 由 <c>AutoCompactPolicy</c> / <c>MicroCompactPolicy</c> / <c>SnipPolicy</c> 替换。
    /// </summary>
    internal sealed class NullContextWindowPolicy : IContextWindowPolicy
    {
        public static readonly NullContextWindowPolicy Instance = new NullContextWindowPolicy();

        public Task<ContextWindowEvaluation> EvaluateAsync(
            BrokerConversationState conversation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ContextWindowEvaluation.NoAction);
        }

        public Task<BrokerConversationState> ApplyAsync(
            BrokerConversationState conversation,
            ContextWindowAction action,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(conversation);
        }
    }
}
