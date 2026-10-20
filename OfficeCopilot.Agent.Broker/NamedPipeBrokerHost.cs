using OfficeCopilot.Agent.Client;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal sealed class NamedPipeBrokerHost : INamedPipeBrokerHost
    {
        private readonly IBrokerEnvelopeDispatcher _dispatcher;
        private readonly ConcurrentDictionary<int, Task> _activeConnectionTasks;
        private readonly SemaphoreSlim _connectionSemaphore;
        private bool _started;

        public NamedPipeBrokerHost(
            NamedPipeBrokerHostOptions options,
            IBrokerEnvelopeDispatcher dispatcher)
        {
            Options = options ?? new NamedPipeBrokerHostOptions();
            _dispatcher = dispatcher;
            _activeConnectionTasks = new ConcurrentDictionary<int, Task>();
            _connectionSemaphore = new SemaphoreSlim(
                Math.Max(1, Options.MaxServerInstances),
                Math.Max(1, Options.MaxServerInstances));
        }

        public NamedPipeBrokerHostOptions Options { get; private set; }

        /// <summary>
        /// 当前正在处理的活跃连接数量（用于诊断）。
        /// </summary>
        public int ActiveConnectionCount => _activeConnectionTasks.Count;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _started = true;

            // === 防崩溃: 连续创建失败计数 ===
            // 如果连续多次创建 pipe server 都失败（如 pipe 名被占用），暂停更长时间。
            var consecutiveCreateFailures = 0;
            const int maxConsecutiveFailures = 5;

            while (!cancellationToken.IsCancellationRequested && _started)
            {
                // === 防崩溃: 连接数限流 ===
                // 等待信号量，防止无限并发连接耗尽资源。
                // 超时 5 秒后继续尝试——宁可拒绝新连接也不死锁。
                try
                {
                    if (!await _connectionSemaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false))
                    {
                        BrokerRuntimeLogger.Warning("NamedPipeBrokerHost",
                            $"Connection semaphore wait timed out. Active={_activeConnectionTasks.Count}/{Options.MaxServerInstances}");
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                NamedPipeServerStream server;
                try
                {
                    server = CreateServer();
                    consecutiveCreateFailures = 0; // 重置失败计数
                }
                catch (Exception ex)
                {
                    consecutiveCreateFailures++;
                    _connectionSemaphore.Release();

                    var backoffMs = Math.Min(500 * consecutiveCreateFailures, 5000);
                    BrokerRuntimeLogger.Warning("NamedPipeBrokerHost",
                        $"CreateServer failed (attempt {consecutiveCreateFailures}/{maxConsecutiveFailures}): {ex.Message}. Backoff={backoffMs}ms");

                    if (consecutiveCreateFailures >= maxConsecutiveFailures)
                    {
                        BrokerRuntimeLogger.Error("NamedPipeBrokerHost",
                            $"Too many consecutive pipe creation failures ({maxConsecutiveFailures}). Restarting acceptance loop.");
                        consecutiveCreateFailures = 0;
                    }

                    try { await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    server.Dispose();
                    _connectionSemaphore.Release();
                    break;
                }
                catch (Exception ex)
                {
                    BrokerRuntimeLogger.Warning("NamedPipeBrokerHost", "WaitForConnection failed: " + ex.Message);
                    server.Dispose();
                    _connectionSemaphore.Release();

                    try { await Task.Delay(200, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                // 每个连接在后台独立处理，使取消等消息不被 turn 阻塞。
                var capturedServer = server;
                var connectionId = Environment.CurrentManagedThreadId;
                var connectionTask = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessConnectionAsync(capturedServer, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // 进程关闭导致的正常取消，无需记录
                    }
                    catch (IOException ex)
                    {
                        // 客户端关闭管道时，writer.Dispose 仍可能在 Flush 阶段抛出 IOException。
                        BrokerRuntimeLogger.Info("NamedPipeBrokerHost",
                            "Connection closed by client: " + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        BrokerRuntimeLogger.Error("NamedPipeBrokerHost", "Connection task faulted", ex);
                    }
                    finally
                    {
                        try { capturedServer.Dispose(); } catch { }
                        _activeConnectionTasks.TryRemove(connectionId, out _);
                        try { _connectionSemaphore.Release(); } catch { }
                    }
                });

                _activeConnectionTasks[connectionId] = connectionTask;

                // 清理已完成的连接任务（惰性清理，防止字典无限增长）
                if (_activeConnectionTasks.Count > Options.MaxServerInstances * 2)
                {
                    CleanupCompletedTasks();
                }
            } // end while
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _started = false;

            // 等待所有活跃连接完成（最多 5 秒）
            var tasks = _activeConnectionTasks.Values.ToArray();
            if (tasks.Length > 0)
            {
                BrokerRuntimeLogger.Info("NamedPipeBrokerHost",
                    $"StopAsync: waiting for {tasks.Length} active connection(s) to complete...");

                var drainTask = Task.WhenAll(tasks);
                var timeoutTask = Task.Delay(5000, cancellationToken);
                var completed = await Task.WhenAny(drainTask, timeoutTask).ConfigureAwait(false);
                if (completed == timeoutTask)
                {
                    BrokerRuntimeLogger.Warning("NamedPipeBrokerHost",
                        $"StopAsync: {tasks.Length} connection(s) did not complete within 5s timeout.");
                }
            }

            // 释放信号量（销毁所有等待者）
            try { _connectionSemaphore.Dispose(); } catch { }
        }

        public void Dispose()
        {
            _started = false;
            try { _connectionSemaphore.Dispose(); } catch { }
        }

        private void CleanupCompletedTasks()
        {
            var completedKeys = _activeConnectionTasks
                .Where(kvp => kvp.Value.IsCompleted || kvp.Value.IsFaulted || kvp.Value.IsCanceled)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in completedKeys)
            {
                _activeConnectionTasks.TryRemove(key, out _);
            }
        }

        private NamedPipeServerStream CreateServer()
        {
            return new NamedPipeServerStream(
                Options.PipeName ?? "officecopilot-agent-broker",
                PipeDirection.InOut,
                Math.Max(1, Options.MaxServerInstances),
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        private async Task ProcessConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
        {
            AgentBrokerResponseEnvelope response = null;
            var requestId = string.Empty;

            using (var reader = new StreamReader(server, Encoding.UTF8, false, 8192, true))
            using (var writer = new StreamWriter(server, new UTF8Encoding(false), 8192, true))
            {
                writer.AutoFlush = true;

                // 读取请求行
                string line;
                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    BrokerRuntimeLogger.Warning("NamedPipeBrokerHost", "ReadLineAsync failed (client disconnected): " + ex.Message);
                    return; // 客户端断开，无法读取请求，直接退出
                }
                catch (OperationCanceledException)
                {
                    return; // 进程关闭
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    response = new AgentBrokerResponseEnvelope
                    {
                        MessageType = AgentBrokerMessageTypes.Error,
                        Success = false,
                        ErrorCode = "broker.pipe.empty_request",
                        ErrorMessage = "The client sent an empty named-pipe request."
                    };
                }
                else
                {
                    try
                    {
                        var request = JsonSerializer.Deserialize<AgentBrokerRequestEnvelope>(line)
                            ?? new AgentBrokerRequestEnvelope();
                        requestId = request.RequestId ?? string.Empty;

                        var eventSink = new NamedPipeBrokerEventSink(
                            requestId,
                            writer,
                            cancellationToken);

                        response = await _dispatcher.DispatchAsync(
                            request,
                            eventSink,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (JsonException ex)
                    {
                        response = new AgentBrokerResponseEnvelope
                        {
                            MessageType = AgentBrokerMessageTypes.Error,
                            Success = false,
                            ErrorCode = "broker.pipe.invalid_request",
                            ErrorMessage = ex.Message
                        };
                    }
                }

                // 写入响应（如果 Dispatcher 返回了响应）
                if (response != null)
                {
                    try
                    {
                        var responsePayload = JsonSerializer.Serialize(response);
                        await writer.WriteLineAsync(responsePayload).ConfigureAwait(false);
                        await writer.FlushAsync().ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        // 客户端已断开，响应无法发送，静默忽略
                    }
                    catch (ObjectDisposedException)
                    {
                        // 管道已关闭
                    }
                }
            } // using reader/writer
        }
    }
}
