using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Client
{
    public sealed class NamedPipeAgentBrokerTransport : IAgentBrokerTransport
    {
        public NamedPipeAgentBrokerTransport()
            : this(new NamedPipeAgentBrokerTransportOptions())
        {
        }

        public NamedPipeAgentBrokerTransport(NamedPipeAgentBrokerTransportOptions options)
        {
            Options = options ?? new NamedPipeAgentBrokerTransportOptions();
        }

        public NamedPipeAgentBrokerTransportOptions Options { get; private set; }

        public async Task<AgentBrokerResponseEnvelope> SendAsync(
            AgentBrokerRequestEnvelope request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerRequestEnvelope();

            // === 防崩溃: 连接重试（指数退避） ===
            // 在 broker 进程刚重启时，pipe 名可能尚未释放，需要进行重试。
            const int maxConnectRetries = 3;
            const int baseBackoffMs = 200;

            for (var retry = 0; retry <= maxConnectRetries; retry++)
            {
                try
                {
                    return await SendOnceAsync(request, eventSink, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException) when (retry < maxConnectRetries)
                {
                    var backoffMs = baseBackoffMs * (1 << retry); // 200, 400, 800 ms
                    try { await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
                catch (IOException ex) when (retry < maxConnectRetries && IsPipeUnavailable(ex))
                {
                    var backoffMs = baseBackoffMs * (1 << retry);
                    try { await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
            }

            // 最后一次尝试（retry == maxConnectRetries）
            return await SendOnceAsync(request, eventSink, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AgentBrokerResponseEnvelope> SendOnceAsync(
            AgentBrokerRequestEnvelope request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (var pipe = new NamedPipeClientStream(
                Options.ServerName ?? ".",
                Options.PipeName ?? "officecopilot-agent-broker",
                PipeDirection.InOut,
                PipeOptions.Asynchronous))
            {
                await Task.Run(
                    () => pipe.Connect(Math.Max(1, Options.ConnectTimeoutMilliseconds)),
                    cancellationToken).ConfigureAwait(false);

                // === 性能优化: 增大缓冲区到 8KB，减少大 JSON 负载的碎片化读写 ===
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 8192, true))
                using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 8192, true))
                {
                    writer.AutoFlush = true;
                    pipe.ReadMode = PipeTransmissionMode.Byte;

                    var payload = JsonConvert.SerializeObject(request);
                    await writer.WriteLineAsync(payload).ConfigureAwait(false);

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // === 防崩溃: 可取消的 ReadLine ===
                        // .NET Framework 4.x 的 StreamReader.ReadLineAsync 不接受 CancellationToken，
                        // 当 broker 端挂住时此调用会无限阻塞。使用 Task.WhenAny + 超时轮询来解除阻塞。
                        var responseLine = await ReadLineWithCancellationAsync(reader, pipe, cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(responseLine))
                        {
                            return new AgentBrokerResponseEnvelope
                            {
                                RequestId = request.RequestId ?? string.Empty,
                                MessageType = AgentBrokerMessageTypes.Error,
                                Success = false,
                                ErrorCode = "broker.transport.named_pipe.empty_response",
                                ErrorMessage = "The named pipe broker returned an empty response."
                            };
                        }

                        try
                        {
                            var envelope = JObject.Parse(responseLine);
                            var messageType = (string)envelope["MessageType"] ?? string.Empty;

                            if (string.Equals(messageType, AgentBrokerMessageTypes.TurnEvent, StringComparison.OrdinalIgnoreCase))
                            {
                                var eventEnvelope = envelope.ToObject<AgentBrokerEventEnvelope>()
                                    ?? new AgentBrokerEventEnvelope();

                                if (eventSink != null)
                                {
                                    var brokerEvent = JsonConvert.DeserializeObject<AgentBrokerEvent>(eventEnvelope.PayloadJson ?? "{}")
                                        ?? new AgentBrokerEvent();
                                    await eventSink.OnEventAsync(brokerEvent, cancellationToken).ConfigureAwait(false);
                                }

                                continue;
                            }

                            return envelope.ToObject<AgentBrokerResponseEnvelope>()
                                ?? new AgentBrokerResponseEnvelope
                                {
                                    RequestId = request.RequestId ?? string.Empty,
                                    MessageType = AgentBrokerMessageTypes.Error,
                                    Success = false,
                                    ErrorCode = "broker.transport.named_pipe.null_response",
                                    ErrorMessage = "The named pipe broker returned a null response envelope."
                                };
                        }
                        catch (JsonException ex)
                        {
                            return new AgentBrokerResponseEnvelope
                            {
                                RequestId = request.RequestId ?? string.Empty,
                                MessageType = AgentBrokerMessageTypes.Error,
                                Success = false,
                                ErrorCode = "broker.transport.named_pipe.invalid_response",
                                ErrorMessage = ex.Message
                            };
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 判断 IOException 是否因 pipe 不可用（可重试）。
        /// </summary>
        private static bool IsPipeUnavailable(Exception ex)
        {
            if (ex == null) return false;
            var msg = (ex.Message ?? string.Empty).ToLowerInvariant();
            return msg.Contains("pipe") && (msg.Contains("not available") || msg.Contains("not found") || msg.Contains("broken"));
        }

        /// <summary>
        /// === 防崩溃: 可取消的 ReadLine ===
        /// .NET Framework 4.x 的 StreamReader.ReadLineAsync 不接受 CancellationToken。
        /// 使用轮询 + 超时机制检测取消信号。
        /// === 性能优化: 轮询间隔从 2000ms 降低到 250ms，取消延迟从 2s 降至 0.25s ===
        /// </summary>
        private static async Task<string> ReadLineWithCancellationAsync(
            StreamReader reader,
            NamedPipeClientStream pipe,
            CancellationToken cancellationToken)
        {
            var readTask = reader.ReadLineAsync();

            // 每 250ms 检查一次取消信号（原来 2000ms，取消延迟降低 8x）
            const int pollIntervalMs = 250;

            while (!readTask.IsCompleted)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // 关闭 pipe 强制 ReadLineAsync 抛出异常
                    try { pipe.Close(); } catch { }
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var delayTask = Task.Delay(pollIntervalMs, cancellationToken);
                var completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);

                if (completed == readTask)
                {
                    return await readTask.ConfigureAwait(false);
                }

                // delayTask 完成（250ms 到了），循环回去再检查一次
            }

            return await readTask.ConfigureAwait(false);
        }
    }
}
