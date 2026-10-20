using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Persistence;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 上下文窗口策略：判断当前会话是否需要 compact、micro-compact 或 snip，
    /// 并执行相应的消息裁剪 / 摘要替换。阶段 0 仅声明，阶段 1 实装。
    /// 设计参考 <c>Learn/cc-haha-main/src/services/compact/</c>。
    /// </summary>
    internal interface IContextWindowPolicy
    {
        Task<ContextWindowEvaluation> EvaluateAsync(
            BrokerConversationState conversation,
            CancellationToken cancellationToken);

        Task<BrokerConversationState> ApplyAsync(
            BrokerConversationState conversation,
            ContextWindowAction action,
            CancellationToken cancellationToken);
    }

    internal enum ContextWindowAction
    {
        None = 0,
        AutoCompact = 1,
        MicroCompact = 2,
        Snip = 3
    }

    internal sealed class ContextWindowEvaluation
    {
        public static readonly ContextWindowEvaluation NoAction =
            new ContextWindowEvaluation(ContextWindowAction.None, 0, 0, string.Empty);

        public ContextWindowEvaluation(
            ContextWindowAction recommendedAction,
            int estimatedPromptTokens,
            int maxPromptTokens,
            string reason)
        {
            RecommendedAction = recommendedAction;
            EstimatedPromptTokens = estimatedPromptTokens;
            MaxPromptTokens = maxPromptTokens;
            Reason = reason ?? string.Empty;
        }

        public ContextWindowAction RecommendedAction { get; private set; }

        public int EstimatedPromptTokens { get; private set; }

        public int MaxPromptTokens { get; private set; }

        public string Reason { get; private set; }
    }
}
