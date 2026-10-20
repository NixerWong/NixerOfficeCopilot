using OfficeCopilot.Agent.Client;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal sealed class NamedPipeBrokerEventSink : IAgentBrokerEventSink
    {
        private readonly string _requestId;
        private readonly TextWriter _writer;
        private readonly SemaphoreSlim _writeLock;
        private readonly CancellationToken _shutdownToken;
        private int _disposed;

        public NamedPipeBrokerEventSink(string requestId, TextWriter writer)
            : this(requestId, writer, CancellationToken.None)
        {
        }

        public NamedPipeBrokerEventSink(string requestId, TextWriter writer, CancellationToken shutdownToken)
        {
            _requestId = requestId ?? string.Empty;
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _writeLock = new SemaphoreSlim(1, 1);
            _shutdownToken = shutdownToken;
        }

        public async Task OnEventAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken)
        {
            // 如果 shutdown token 已取消，不再尝试写入（进程正在关闭）
            if (_shutdownToken.IsCancellationRequested)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (brokerEvent == null)
            {
                return;
            }

            // === 防崩溃: 写入失败静默保护 ===
            // 当客户端断开连接（如取消操作后 panel 关闭了原来 turn 的 pipe）时，
            // 写入操作可能抛出 IOException。此时静默跳过，不崩进程。

            if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
            {
                return; // 已标记为失效
            }

            var envelope = new AgentBrokerEventEnvelope
            {
                RequestId = _requestId,
                MessageType = AgentBrokerMessageTypes.TurnEvent,
                PayloadJson = JsonSerializer.Serialize(brokerEvent)
            };

            var payload = JsonSerializer.Serialize(envelope);

            try
            {
                await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await _writer.WriteLineAsync(payload).ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                // 管道断开（客户端断开连接），将此 sink 标记为失效，
                // 后续写入直接跳过
                BrokerRuntimeLogger.Warning("NamedPipeBrokerEventSink", "Write failed (pipe broken). RequestId=" + _requestId + ". " + ex.Message);
                Interlocked.Exchange(ref _disposed, 1);
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Exchange(ref _disposed, 1);
            }
            catch (InvalidOperationException)
            {
                // StreamWriter 已关闭
                Interlocked.Exchange(ref _disposed, 1);
            }
            finally
            {
                try { _writeLock.Release(); } catch { }
            }
        }
    }
}
