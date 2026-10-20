using OfficeCopilot.Agent.Client;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 用户交互处理器：抽象"暂停 Agent 执行 → 向 Panel 请求交互 → 等待响应"流程。
    ///
    /// Broker 端通过 <see cref="BrokerPendingUserInteractionRegistry"/> 实现（基于 TaskCompletionSource）。
    /// 测试端可用 Mock 实现。
    /// </summary>
    public interface IUserInteractionHandler
    {
        /// <summary>
        /// 向 Panel 发起用户交互请求，并等待用户响应。
        /// </summary>
        /// <param name="request">交互请求数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>用户响应（含答案、取消状态等）。</returns>
        Task<AgentBrokerToolUserInteractionResponse> RequestUserInteractionAsync(
            AgentBrokerToolUserInteractionRequest request,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// 空实现：当没有 Panel 连接时，直接返回"超时/不可用"。
    /// </summary>
    public sealed class NullUserInteractionHandler : IUserInteractionHandler
    {
        public Task<AgentBrokerToolUserInteractionResponse> RequestUserInteractionAsync(
            AgentBrokerToolUserInteractionRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentBrokerToolUserInteractionResponse
            {
                ToolCallId = request?.ToolCallId ?? string.Empty,
                Cancelled = true,
                ErrorCode = "user_interaction.unavailable",
                ErrorMessage = "User interaction is not available (no Panel connected)."
            });
        }
    }
}
