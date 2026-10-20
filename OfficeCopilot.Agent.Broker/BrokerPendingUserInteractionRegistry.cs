using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime.Turn;
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    /// <summary>
    /// Phase 0: 实现 <see cref="IUserInteractionHandler"/>，基于 <see cref="TaskCompletionSource{T}"/> 桥接
    /// Runtime 的同步等待与 NamedPipe 的异步响应。
    ///
    /// 工作流程:
    /// 1. Runtime 调用 <see cref="RequestUserInteractionAsync"/> → 创建 TCS，注册到字典，返回 Task
    /// 2. Broker <see cref="BrokerEnvelopeDispatcher"/> 收到 <c>broker.tool.user_interaction.response</c> →
    ///    调用 <see cref="CompleteResponse"/> 完成 TCS
    /// 3. Runtime 的 await 恢复，拿到响应继续执行
    ///
    /// 超时处理: 5 分钟后自动取消。
    /// </summary>
    internal sealed class BrokerPendingUserInteractionRegistry : IUserInteractionHandler, IDisposable
    {
        /// <summary>默认超时时间。</summary>
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

        private readonly ConcurrentDictionary<string, PendingEntry> _pending =
            new ConcurrentDictionary<string, PendingEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 当前待处理的交互请求数量（用于诊断）。
        /// </summary>
        public int PendingCount => _pending.Count;

        /// <inheritdoc/>
        public Task<AgentBrokerToolUserInteractionResponse> RequestUserInteractionAsync(
            AgentBrokerToolUserInteractionRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ToolCallId))
            {
                return Task.FromResult(new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = request?.ToolCallId ?? string.Empty,
                    Cancelled = true,
                    ErrorCode = "user_interaction.invalid_request",
                    ErrorMessage = "ToolUserInteractionRequest must have a non-empty ToolCallId."
                });
            }

            var tcs = new TaskCompletionSource<AgentBrokerToolUserInteractionResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var timeoutSeconds = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : (int)DefaultTimeout.TotalSeconds;

            // Phase 7C: 如果外部 token 已取消，不要注册（回调会同步触发，立即取消 TCS）
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = request.ToolCallId,
                    Cancelled = true,
                    ErrorCode = "user_interaction.external_cancel",
                    ErrorMessage = "External cancellation token was already cancelled."
                });
            }

            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            // 链接外部取消令牌
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            var entry = new PendingEntry
            {
                Tcs = tcs,
                TimeoutCts = timeoutCts,
                LinkedCts = linkedCts
            };

            if (!_pending.TryAdd(request.ToolCallId, entry))
            {
                linkedCts.Dispose();
                timeoutCts.Dispose();
                return Task.FromResult(new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = request.ToolCallId,
                    Cancelled = true,
                    ErrorCode = "user_interaction.duplicate",
                    ErrorMessage = $"A pending interaction already exists for toolCallId '{request.ToolCallId}'."
                });
            }

            // 超时/外部取消自动清理
            linkedCts.Token.Register(() =>
            {
                if (_pending.TryRemove(request.ToolCallId, out var removed))
                {
                    var errorCode = timeoutCts.IsCancellationRequested
                        ? "user_interaction.timeout"
                        : "user_interaction.external_cancel";
                    removed.Tcs.TrySetResult(new AgentBrokerToolUserInteractionResponse
                    {
                        ToolCallId = request.ToolCallId,
                        Cancelled = true,
                        ErrorCode = errorCode,
                        ErrorMessage = errorCode == "user_interaction.timeout"
                            ? $"User interaction timed out after {timeoutSeconds} seconds."
                            : "Turn was cancelled externally."
                    });
                    removed.Dispose();
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// 完成一个待处理的交互请求。由 <see cref="BrokerEnvelopeDispatcher"/> 调用。
        /// </summary>
        /// <param name="response">Panel 返回的响应。</param>
        /// <returns>true 如果找到并完成了对应的请求；false 如果没有找到（可能已超时）。</returns>
        public bool CompleteResponse(AgentBrokerToolUserInteractionResponse response)
        {
            if (response == null || string.IsNullOrWhiteSpace(response.ToolCallId))
            {
                return false;
            }

            if (_pending.TryRemove(response.ToolCallId, out var entry))
            {
                entry.Tcs.TrySetResult(response);
                entry.Dispose();
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            foreach (var kvp in _pending)
            {
                if (_pending.TryRemove(kvp.Key, out var entry))
                {
                    entry.Tcs.TrySetResult(new AgentBrokerToolUserInteractionResponse
                    {
                        ToolCallId = kvp.Key,
                        Cancelled = true,
                        ErrorCode = "user_interaction.disposed",
                        ErrorMessage = "Broker is shutting down."
                    });
                    entry.Dispose();
                }
            }
        }

        private sealed class PendingEntry : IDisposable
        {
            public TaskCompletionSource<AgentBrokerToolUserInteractionResponse> Tcs { get; set; }
            public CancellationTokenSource TimeoutCts { get; set; }
            public CancellationTokenSource LinkedCts { get; set; }

            public void Dispose()
            {
                LinkedCts?.Dispose();
                TimeoutCts?.Dispose();
            }
        }
    }
}
