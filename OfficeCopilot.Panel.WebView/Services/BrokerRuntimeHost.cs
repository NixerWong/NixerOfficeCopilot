using Microsoft.Extensions.Logging;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Diagnostics;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Panel.WebView.Services
{
    public sealed class BrokerRuntimeHost : IDisposable
    {
        private const int DefaultBrokerReadyTimeoutSeconds = 5;
        private static readonly TimeSpan BrokerShutdownTimeout = TimeSpan.FromSeconds(3);

        private readonly TimeSpan _brokerReadyTimeout;
        private readonly ILogger _logger;
        private readonly string _hostBridgePipeName;
        private readonly string _hostApplicationName;
        private readonly object _syncRoot;
        private readonly string _pipeName;
        private readonly ManagedAgentBrokerClient _managedClient;
        private readonly AgentBrokerEndpointRegistration _endpointRegistration;
        private Process _brokerProcess;
        private BrokerJobObject _jobObject;
        private bool _disposed;
        // 标记本次进程退出是否由我们主动回收触发，用于区分正常回收与异常退出。
        private volatile bool _brokerStopRequested;
        private BrokerOutputCapture _outputCapture;

        public BrokerRuntimeHost(ILogger logger)
            : this(logger, string.Empty)
        {
        }

        public BrokerRuntimeHost(ILogger logger, string hostBridgePipeName)
            : this(logger, hostBridgePipeName, DefaultBrokerReadyTimeoutSeconds)
        {
        }

        /// <param name="readyTimeoutSeconds">
        /// 启动后等待 Broker Ping 就绪的超时（秒），对应
        /// <c>OfficeCopilotSettings.Runtime.BrokerReadyTimeoutSeconds</c>。非正数时回退到默认 5 秒。
        /// </param>
        public BrokerRuntimeHost(ILogger logger, string hostBridgePipeName, int readyTimeoutSeconds)
            : this(logger, hostBridgePipeName, readyTimeoutSeconds, string.Empty)
        {
        }

        /// <param name="hostApplicationName">
        /// 宿主应用名（Excel / Word / PowerPoint），透传给 Broker 以选择该宿主专属的用户 Skill 目录。
        /// </param>
        public BrokerRuntimeHost(ILogger logger, string hostBridgePipeName, int readyTimeoutSeconds, string hostApplicationName)
        {
            _logger = logger;
            _hostBridgePipeName = hostBridgePipeName ?? string.Empty;
            _hostApplicationName = hostApplicationName ?? string.Empty;
            _brokerReadyTimeout = TimeSpan.FromSeconds(
                readyTimeoutSeconds > 0 ? readyTimeoutSeconds : DefaultBrokerReadyTimeoutSeconds);
            _syncRoot = new object();
            _pipeName = BuildBrokerPipeName(_hostBridgePipeName);
            _managedClient = new ManagedAgentBrokerClient(this);
            try
            {
                _endpointRegistration = AgentBrokerEndpointRegistry.Register(_pipeName, ".");
            }
            catch (Exception ex) when (
                ex is IOException
                || ex is UnauthorizedAccessException
                || ex is System.Security.SecurityException)
            {
                _logger?.LogWarning(
                    ex,
                    "Broker endpoint discovery registration failed. PipeName={PipeName}",
                    _pipeName);
            }
        }

        /// <summary>
        /// 计算 Broker 命名管道名。HostBridge 管道名已包含 windowKey
        /// （形如 officecopilot-host-bridge-{pid}-{windowKey}），据此为每个
        /// Office 窗口派生独立的 Broker 管道名。否则同一进程内多个窗口会共用
        /// 同一个 Broker（管道名冲突），导致工具调用 / 权限确认卡片被路由到
        /// 错误的窗口。
        /// </summary>
        private static string BuildBrokerPipeName(string hostBridgePipeName)
        {
            var processId = Process.GetCurrentProcess().Id.ToString();

            if (string.IsNullOrWhiteSpace(hostBridgePipeName))
            {
                return "officecopilot-agent-broker-" + processId;
            }

            var suffix = hostBridgePipeName.Trim();
            const string hostBridgePrefix = "officecopilot-host-bridge-";
            if (suffix.StartsWith(hostBridgePrefix, StringComparison.OrdinalIgnoreCase))
            {
                suffix = suffix.Substring(hostBridgePrefix.Length);
            }

            return "officecopilot-agent-broker-" + suffix;
        }

        public string PipeName => _pipeName;

        public IAgentBrokerClient CreateClient()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
            }

            if (!EnsureStarted())
            {
                _logger?.LogWarning("Broker 预热失败，将在首次调用时懒启动。PipeName={PipeName}", _pipeName);
            }

            return _managedClient;
        }

        /// <summary>
        /// === 性能优化: 创建客户端但不立即启动 Broker 进程 ===
        /// 用于 Panel 首次打开时避免 UI 线程阻塞。Broker 进程由后续的
        /// <see cref="EnsureStarted"/> 或后台预热任务延迟启动。
        /// </summary>
        public IAgentBrokerClient CreateClientNoStart()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
            }
            // 不调用 EnsureStarted() — broker 进程会在首次 SendTurnAsync /
            // 后台预热任务中延迟启动。
            return _managedClient;
        }

        /// <summary>
        /// === 性能优化: 暴露为 public，供 TaskPaneManager 后台预热调用 ===
        /// </summary>
        public bool EnsureStarted()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (IsBrokerProcessAlive(_brokerProcess) && IsBrokerResponsiveNoLock(TimeSpan.FromMilliseconds(500)))
                {
                    return true;
                }

                return StartBrokerProcessNoLock("ensure_started");
            }
        }

        private bool TryRestart(string reason)
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return false;
                }

                _logger?.LogWarning("Broker is unavailable; preparing to restart. PipeName={PipeName} Reason={Reason}", _pipeName, reason ?? string.Empty);
                StopBrokerProcessNoLock(reason ?? string.Empty);
                return StartBrokerProcessNoLock(reason ?? string.Empty);
            }
        }

        private bool StartBrokerProcessNoLock(string reason)
        {
            StopBrokerProcessNoLock(reason ?? string.Empty);

            var launchInfo = ResolveBrokerLaunchInfo();
            if (launchInfo == null)
            {
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                _logger?.LogWarning(
                    "Broker executable was not found. PipeName={PipeName} BaseDirectory={BaseDirectory} Searched={Searched}",
                    _pipeName,
                    baseDirectory,
                    "[BaseDirectory]\\OfficeCopilot.Agent.Broker.exe, [BaseDirectory]\\Broker\\OfficeCopilot.Agent.Broker.exe, parent OfficeCopilot.Agent.Broker\\bin\\{Debug|Release}\\net472");
                return false;
            }

            try
            {
                EnsureJobObjectNoLock();

                var startInfo = new ProcessStartInfo
                {
                    FileName = launchInfo.FileName,
                    Arguments = launchInfo.Arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = launchInfo.WorkingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                _logger?.LogInformation(
                    "Starting Broker. PipeName={PipeName} FileName={FileName} WorkingDirectory={WorkingDirectory} Reason={Reason}",
                    _pipeName,
                    startInfo.FileName,
                    startInfo.WorkingDirectory,
                    reason ?? string.Empty);

                _brokerStopRequested = false;
                _brokerProcess = Process.Start(startInfo);
                if (_brokerProcess == null)
                {
                    _logger?.LogWarning("Broker process failed to start: Process.Start returned null. PipeName={PipeName}", _pipeName);
                    return false;
                }

                _brokerProcess.EnableRaisingEvents = true;
                _brokerProcess.Exited += BrokerProcess_Exited;
                _outputCapture = new BrokerOutputCapture(OfficeCopilotDiagnostics.GetDefaultLogsFolder(), _brokerProcess.Id, _logger);
                _outputCapture.Attach(_brokerProcess);

                if (_jobObject != null)
                {
                    try
                    {
                        _jobObject.AddProcess(_brokerProcess);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to add the Broker process to the JobObject; it may not be terminated automatically when the host exits. PipeName={PipeName} ProcessId={ProcessId}", _pipeName, _brokerProcess.Id);
                    }
                }

                var readyStopwatch = Stopwatch.StartNew();
                var ready = WaitForBrokerReadyNoLock(_brokerReadyTimeout);
                if (!ready)
                {
                    var exitCode = TryGetExitCode(_brokerProcess);
                    _logger?.LogWarning(
                        "Broker did not pass the Ping probe within the startup timeout. PipeName={PipeName} ProcessId={ProcessId} ElapsedMs={ElapsedMs} TimeoutSeconds={TimeoutSeconds} ProcessExited={ProcessExited} ExitCode={ExitCode} OutputLog={OutputLog}",
                        _pipeName,
                        _brokerProcess.Id,
                        readyStopwatch.ElapsedMilliseconds,
                        _brokerReadyTimeout.TotalSeconds,
                        exitCode.HasValue,
                        exitCode,
                        _outputCapture?.FilePath);
                    StopBrokerProcessNoLock("pipe_not_ready");
                    return false;
                }

                _logger?.LogInformation("Broker is ready. PipeName={PipeName} ProcessId={ProcessId} ElapsedMs={ElapsedMs}", _pipeName, _brokerProcess.Id, readyStopwatch.ElapsedMilliseconds);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to start Broker. PipeName={PipeName} FileName={FileName}", _pipeName, launchInfo.FileName);
                StopBrokerProcessNoLock("start_failed");
                return false;
            }
        }

        private void BrokerProcess_Exited(object sender, EventArgs e)
        {
            var process = sender as Process;
            if (process == null)
            {
                return;
            }

            try
            {
                if (_brokerStopRequested)
                {
                    _logger?.LogInformation("Broker process has exited. PipeName={PipeName} ProcessId={ProcessId} ExitCode={ExitCode}", _pipeName, process.Id, process.ExitCode);
                }
                else
                {
                    _logger?.LogWarning("Broker process exited unexpectedly. PipeName={PipeName} ProcessId={ProcessId} ExitCode={ExitCode} OutputLog={OutputLog}", _pipeName, process.Id, process.ExitCode, _outputCapture?.FilePath);
                }
            }
            catch
            {
                _logger?.LogInformation("Broker process has exited. PipeName={PipeName}", _pipeName);
            }
        }

        internal async Task<AgentBrokerTurnResult> SendTurnAsync(
            AgentBrokerTurnRequest request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureStarted())
            {
                return new AgentBrokerTurnResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.start_failed",
                    ErrorMessage = "Broker 启动失败，请检查 Broker 可执行文件或稍后重试。"
                };
            }

            // === 防崩溃: 发送前快速健康检查 ===
            // 如果 broker 进程存在但 ping 不通（可能假死），触发重启。
            if (!IsBrokerHealthyFast())
            {
                _logger?.LogWarning("Broker pre-flight health check failed. Attempting restart. PipeName={PipeName}", _pipeName);
                if (!TryRestart("pre_flight_health_failed"))
                {
                    return new AgentBrokerTurnResult
                    {
                        ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                        MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                        Success = false,
                        ErrorCode = "broker.restart_failed",
                        ErrorMessage = "Broker 健康检查失败且自动重启失败。"
                    };
                }
            }

            try
            {
                _logger?.LogDebug(
                    "开始向 Broker 发送回合。PipeName={PipeName} ConversationId={ConversationId} MessageId={MessageId}",
                    _pipeName,
                    request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    request == null ? string.Empty : request.MessageId ?? string.Empty);

                return await CreateTransportClient().SendTurnAsync(request, eventSink, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRestartAfterFailure(ex))
            {
                _logger?.LogWarning(ex, "Broker 调用失败且检测到 Broker 已下线，准备重启后重试一次。PipeName={PipeName}", _pipeName);

                if (!TryRestart("send_turn_retry"))
                {
                    return new AgentBrokerTurnResult
                    {
                        ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                        MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                        Success = false,
                        ErrorCode = "broker.restart_failed",
                        ErrorMessage = "Broker 下线后自动重启失败。"
                    };
                }

                try
                {
                    _logger?.LogInformation("Broker 重启成功，开始重试当前回合。PipeName={PipeName}", _pipeName);
                    return await CreateTransportClient().SendTurnAsync(request, eventSink, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception retryEx)
                {
                    _logger?.LogError(retryEx, "Broker 重试失败。PipeName={PipeName}", _pipeName);
                    return new AgentBrokerTurnResult
                    {
                        ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                        MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                        Success = false,
                        ErrorCode = "broker.retry_failed",
                        ErrorMessage = retryEx.Message
                    };
                }
            }
        }

        /// <summary>
        /// === 防崩溃: 快速健康检查（200ms 超时） ===
        /// 在每次操作前快速验证 broker 是否响应，避免向假死的 broker 发送请求。
        /// </summary>
        private bool IsBrokerHealthyFast()
        {
            return IsBrokerResponsiveNoLock(TimeSpan.FromMilliseconds(200));
        }

        internal async Task<AgentBrokerCancelTurnResult> CancelTurnAsync(
            AgentBrokerCancelTurnRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureStarted())
            {
                return new AgentBrokerCancelTurnResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                    Success = false,
                    WasCancelled = false,
                    ErrorCode = "broker.start_failed",
                    ErrorMessage = "Broker 未启动，无法取消当前回合。"
                };
            }

            // === 防崩溃: 取消前快速健康检查 ===
            if (!IsBrokerHealthyFast())
            {
                _logger?.LogWarning("Broker pre-flight health check failed before cancel. PipeName={PipeName}", _pipeName);
                // 即使 broker 假死，也要返回结果——UI 层会 fallback 到本地取消
                return new AgentBrokerCancelTurnResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                    Success = false,
                    WasCancelled = false,
                    ErrorCode = "broker.not_healthy",
                    ErrorMessage = "Broker 未响应，取消请求无法送达。将通过本地方式取消。"
                };
            }

            try
            {
                return await CreateTransportClient().CancelTurnAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRestartAfterFailure(ex))
            {
                _logger?.LogWarning(ex, "取消 Broker 回合时检测到 Broker 已下线。PipeName={PipeName}", _pipeName);
                return new AgentBrokerCancelTurnResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                    Success = false,
                    WasCancelled = false,
                    ErrorCode = "broker.not_available",
                    ErrorMessage = "Broker 已下线，当前回合取消失败。"
                };
            }
        }

        internal async Task<AgentBrokerSeedConversationResult> SeedConversationAsync(
            AgentBrokerSeedConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureStarted())
            {
                return new AgentBrokerSeedConversationResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.start_failed",
                    ErrorMessage = "Broker 未启动，无法同步会话历史。"
                };
            }

            try
            {
                return await CreateTransportClient().SeedConversationAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRestartAfterFailure(ex))
            {
                _logger?.LogWarning(ex, "同步会话历史到 Broker 时检测到 Broker 已下线。PipeName={PipeName}", _pipeName);
                return new AgentBrokerSeedConversationResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.not_available",
                    ErrorMessage = "Broker 已下线，会话历史同步失败。"
                };
            }
        }

        internal async Task<AgentBrokerClearExecutionModePreferenceResult> ClearExecutionModePreferenceAsync(
            AgentBrokerClearExecutionModePreferenceRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureStarted())
            {
                return new AgentBrokerClearExecutionModePreferenceResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.start_failed",
                    ErrorMessage = "Broker 未启动，无法清除执行方式默认值。"
                };
            }

            try
            {
                return await CreateTransportClient()
                    .ClearExecutionModePreferenceAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRestartAfterFailure(ex))
            {
                _logger?.LogWarning(ex, "清除执行方式默认值时检测到 Broker 已下线。PipeName={PipeName}", _pipeName);
                return new AgentBrokerClearExecutionModePreferenceResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.not_available",
                    ErrorMessage = "Broker 已下线，执行方式默认值清除失败。"
                };
            }
        }

        internal async Task<AgentBrokerCompletionResult> CompleteAsync(
            AgentBrokerCompletionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureStarted())
            {
                return new AgentBrokerCompletionResult
                {
                    Success = false,
                    ErrorCode = "broker.start_failed",
                    ErrorMessage = "Broker 未启动，无法完成模型请求。"
                };
            }

            try
            {
                return await CreateTransportClient().CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRestartAfterFailure(ex))
            {
                _logger?.LogWarning(ex, "向 Broker 请求单次模型完成时检测到 Broker 已下线。PipeName={PipeName}", _pipeName);
                return new AgentBrokerCompletionResult
                {
                    Success = false,
                    ErrorCode = "broker.not_available",
                    ErrorMessage = "Broker 已下线，无法完成模型请求。"
                };
            }
        }

        internal async Task<AgentBrokerListConversationsResult> ListConversationsAsync(
            AgentBrokerListConversationsRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureStarted())
            {
                return new AgentBrokerListConversationsResult
                {
                    Success = false,
                    ErrorCode = "broker.start_failed",
                    ErrorMessage = "Broker 未启动，无法列出会话。"
                };
            }

            try
            {
                return await CreateTransportClient().ListConversationsAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRestartAfterFailure(ex))
            {
                _logger?.LogWarning(ex, "向 Broker 请求会话列表时检测到 Broker 已下线。PipeName={PipeName}", _pipeName);
                return new AgentBrokerListConversationsResult
                {
                    Success = false,
                    ErrorCode = "broker.not_available",
                    ErrorMessage = "Broker 已下线，无法列出会话。"
                };
            }
        }

        internal async Task<AgentBrokerLoadConversationResult> LoadConversationAsync(
            AgentBrokerLoadConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureStarted())
            {
                return new AgentBrokerLoadConversationResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.start_failed",
                    ErrorMessage = "Broker 未启动，无法加载会话。"
                };
            }

            try
            {
                return await CreateTransportClient().LoadConversationAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRestartAfterFailure(ex))
            {
                _logger?.LogWarning(ex, "向 Broker 请求加载会话时检测到 Broker 已下线。ConversationId={ConversationId} PipeName={PipeName}",
                    request == null ? string.Empty : request.ConversationId ?? string.Empty, _pipeName);
                return new AgentBrokerLoadConversationResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.not_available",
                    ErrorMessage = "Broker 已下线，无法加载会话。"
                };
            }
        }

        internal async Task<AgentBrokerDeleteConversationResult> DeleteConversationAsync(
            AgentBrokerDeleteConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureStarted())
            {
                return new AgentBrokerDeleteConversationResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.start_failed",
                    ErrorMessage = "Broker 未启动，无法删除会话。"
                };
            }

            try
            {
                return await CreateTransportClient().DeleteConversationAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRestartAfterFailure(ex))
            {
                _logger?.LogWarning(ex, "向 Broker 请求删除会话时检测到 Broker 已下线。ConversationId={ConversationId} PipeName={PipeName}",
                    request == null ? string.Empty : request.ConversationId ?? string.Empty, _pipeName);
                return new AgentBrokerDeleteConversationResult
                {
                    ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                    Success = false,
                    ErrorCode = "broker.not_available",
                    ErrorMessage = "Broker 已下线，无法删除会话。"
                };
            }
        }

        /// <summary>
        /// Phase 1: Panel 向 Broker 发送用户交互响应。
        /// 通过 TransportClient 发送 <c>broker.tool.user_interaction.response</c> 消息。
        /// </summary>
        internal async Task<AgentBrokerToolUserInteractionResponse> SendUserInteractionResponseAsync(
            AgentBrokerToolUserInteractionResponse response,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!EnsureStarted())
            {
                return new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = response?.ToolCallId ?? string.Empty,
                    Cancelled = true,
                    ErrorCode = "broker.start_failed",
                    ErrorMessage = "Broker 未启动，无法发送用户交互响应。"
                };
            }

            try
            {
                return await CreateTransportClient().SendUserInteractionResponseAsync(response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ShouldRestartAfterFailure(ex))
            {
                _logger?.LogWarning(ex, "向 Broker 发送用户交互响应时检测到 Broker 已下线。ToolCallId={ToolCallId} PipeName={PipeName}",
                    response?.ToolCallId ?? string.Empty, _pipeName);
                return new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = response?.ToolCallId ?? string.Empty,
                    Cancelled = true,
                    ErrorCode = "broker.not_available",
                    ErrorMessage = "Broker 已下线，无法发送用户交互响应。"
                };
            }
        }

        internal async Task ShutdownAsync()
        {
            Process process;
            lock (_syncRoot)
            {
                process = _brokerProcess;
            }

            if (!IsBrokerProcessAlive(process))
            {
                lock (_syncRoot)
                {
                    CleanupProcessNoLock();
                }
                return;
            }

            _logger?.LogInformation("Shutting down Broker. PipeName={PipeName} ProcessId={ProcessId}", _pipeName, process.Id);

            try
            {
                using (var cts = new CancellationTokenSource(BrokerShutdownTimeout))
                {
                    var request = new AgentBrokerRequestEnvelope
                    {
                        MessageType = AgentBrokerMessageTypes.ShutdownRequest,
                        PayloadJson = "{}"
                    };

                    var response = await CreateTransport().SendAsync(request, null, cts.Token).ConfigureAwait(false);
                    if (response == null || !response.Success)
                    {
                        _logger?.LogWarning(
                            "The Broker shutdown request did not return a success response. PipeName={PipeName} ErrorCode={ErrorCode} ErrorMessage={ErrorMessage}",
                            _pipeName,
                            response == null ? string.Empty : response.ErrorCode ?? string.Empty,
                            response == null ? string.Empty : response.ErrorMessage ?? string.Empty);
                    }
                    else
                    {
                        _logger?.LogInformation("Broker accepted the shutdown request. PipeName={PipeName}", _pipeName);
                    }
                }

            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send the shutdown request to Broker; falling back to force-terminating the process. PipeName={PipeName}", _pipeName);
            }

            ForceStopProcess(process, "shutdown_timeout");
        }

        private void ForceStopProcess(Process process, string reason)
        {
            if (!IsBrokerProcessAlive(process))
            {
                lock (_syncRoot)
                {
                    CleanupProcessNoLock();
                }
                return;
            }

            try
            {
                if (process.WaitForExit((int)BrokerShutdownTimeout.TotalMilliseconds))
                {
                    _logger?.LogInformation("Broker process has exited. PipeName={PipeName} ProcessId={ProcessId}", _pipeName, process.Id);
                }
                else
                {
                    _logger?.LogWarning(
                        "Broker did not exit within the timeout; forcing termination. PipeName={PipeName} ProcessId={ProcessId} Reason={Reason}",
                        _pipeName,
                        process.Id,
                        reason ?? string.Empty);
                    process.Kill();
                    process.WaitForExit(2000);
                    _logger?.LogInformation("Broker was force-terminated. PipeName={PipeName} ProcessId={ProcessId}", _pipeName, process.Id);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "An exception occurred while stopping the Broker process. PipeName={PipeName}", _pipeName);
            }
            finally
            {
                lock (_syncRoot)
                {
                    CleanupProcessNoLock();
                }
            }
        }

        private TransportAgentBrokerClient CreateTransportClient()
        {
            return new TransportAgentBrokerClient(
                new AgentBrokerConnectionOptions
                {
                    ClientId = "officecopilot-panel",
                    SessionId = string.Empty
                },
                CreateTransport());
        }

        private NamedPipeAgentBrokerTransport CreateTransport()
        {
            return CreateTransport(5000);
        }

        private NamedPipeAgentBrokerTransport CreateTransport(int connectTimeoutMilliseconds)
        {
            return new NamedPipeAgentBrokerTransport(
                new NamedPipeAgentBrokerTransportOptions
                {
                    PipeName = _pipeName,
                    ConnectTimeoutMilliseconds = Math.Max(1, connectTimeoutMilliseconds),
                    ReadTimeoutMilliseconds = 30000,
                    WriteTimeoutMilliseconds = 30000
                });
        }

        private bool ShouldRestartAfterFailure(Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            // === 防崩溃: 更全面的故障检测 ===
            // 以下异常类型均表明 broker 通信通道损坏或 broker 已不可用。

            // Named pipe 超时
            if (exception is TimeoutException)
            {
                return true;
            }

            // Named pipe 断连 / 网络级错误
            if (exception is IOException)
            {
                return true;
            }

            // 操作取消（通常是 broker 端发起的取消，不是用户取消）
            if (exception is OperationCanceledException)
            {
                lock (_syncRoot)
                {
                    return !IsBrokerProcessAlive(_brokerProcess);
                }
            }

            // AggregateException 可能包含上述类型
            var agg = exception as AggregateException;
            if (agg != null)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    if (inner is TimeoutException || inner is IOException)
                        return true;
                    if (inner is OperationCanceledException)
                    {
                        lock (_syncRoot)
                        {
                            if (!IsBrokerProcessAlive(_brokerProcess))
                                return true;
                        }
                    }
                }
            }

            // ObjectDisposed / InvalidOperation 仅在进程确实死亡时才重启
            if (exception is InvalidOperationException || exception is ObjectDisposedException)
            {
                lock (_syncRoot)
                {
                    return !IsBrokerProcessAlive(_brokerProcess) || !IsBrokerResponsiveNoLock(TimeSpan.FromMilliseconds(300));
                }
            }

            return false;
        }

        private static bool IsBrokerProcessAlive(Process process)
        {
            return process != null && !process.HasExited;
        }

        private void EnsureJobObjectNoLock()
        {
            if (_jobObject != null)
            {
                return;
            }

            try
            {
                _jobObject = new BrokerJobObject("OfficeCopilot.BrokerJob." + Process.GetCurrentProcess().Id.ToString());
                _logger?.LogInformation("Broker JobObject created. PipeName={PipeName}", _pipeName);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to create the Broker JobObject; the child process may not be terminated automatically when the host exits. PipeName={PipeName}", _pipeName);
                _jobObject = null;
            }
        }

        private void CleanupProcessNoLock()
        {
            if (_brokerProcess == null)
            {
                return;
            }

            try
            {
                _brokerProcess.Exited -= BrokerProcess_Exited;
                // 已退出的进程需等待异步输出读取完成，否则最后几行（如崩溃信息）可能丢失。
                if (_brokerProcess.HasExited)
                {
                    _brokerProcess.WaitForExit(2000);
                }
            }
            catch
            {
            }

            try
            {
                _brokerProcess.Dispose();
            }
            catch
            {
            }
            finally
            {
                _brokerProcess = null;
                if (_outputCapture != null)
                {
                    _outputCapture.Dispose();
                    _outputCapture = null;
                }
            }
        }

        private void StopBrokerProcessNoLock(string reason)
        {
            _brokerStopRequested = true;
            if (_brokerProcess == null)
            {
                return;
            }

            try
            {
                if (!_brokerProcess.HasExited)
                {
                    _logger?.LogInformation("Reclaiming the previous Broker process. PipeName={PipeName} ProcessId={ProcessId} Reason={Reason}", _pipeName, _brokerProcess.Id, reason ?? string.Empty);
                    _brokerProcess.Kill();
                    _brokerProcess.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to reclaim the previous Broker process. PipeName={PipeName}", _pipeName);
            }
            finally
            {
                CleanupProcessNoLock();
            }
        }

        private bool WaitForBrokerReadyNoLock(TimeSpan timeout)
        {
            var startedAt = DateTime.UtcNow;
            while (DateTime.UtcNow - startedAt < timeout)
            {
                if (_brokerProcess != null && _brokerProcess.HasExited)
                {
                    return false;
                }

                if (IsBrokerResponsiveNoLock(TimeSpan.FromMilliseconds(600)))
                {
                    return true;
                }

                Thread.Sleep(100);
            }

            return false;
        }

        private bool IsBrokerResponsiveNoLock(TimeSpan timeout)
        {
            try
            {
                using (var cts = new CancellationTokenSource(timeout))
                {
                    var response = CreateTransport(Math.Max(200, (int)timeout.TotalMilliseconds)).SendAsync(
                        new AgentBrokerRequestEnvelope
                        {
                            MessageType = AgentBrokerMessageTypes.PingRequest,
                            PayloadJson = "{}"
                        },
                        null,
                        cts.Token).GetAwaiter().GetResult();

                    return response != null
                        && response.Success
                        && string.Equals(response.MessageType, AgentBrokerMessageTypes.PingResponse, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Broker Ping probe failed. PipeName={PipeName}", _pipeName);
                return false;
            }
        }

        private BrokerLaunchInfo ResolveBrokerLaunchInfo()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var configuration = InferConfiguration(baseDirectory);
            var candidate = FindBrokerEntryPath(baseDirectory, configuration);
            if (candidate == null)
            {
                candidate = FindBrokerEntryPath(baseDirectory, configuration == "Release" ? "Debug" : "Release");
            }

            if (candidate == null)
            {
                return null;
            }

            return new BrokerLaunchInfo
            {
                FileName = candidate.UseDotNetHost ? "dotnet" : candidate.EntryPath,
                Arguments = BuildBrokerArguments(candidate.EntryPath, candidate.UseDotNetHost),
                WorkingDirectory = Path.GetDirectoryName(candidate.EntryPath) ?? AppDomain.CurrentDomain.BaseDirectory
            };
        }

        private string BuildBrokerArguments(string brokerEntryPath, bool useDotNetHost)
        {
            var arguments = string.Empty;

            if (useDotNetHost)
            {
                arguments = QuoteArgument(brokerEntryPath);
            }

            arguments = AppendArgument(arguments, "--pipe", _pipeName);
            arguments = AppendArgument(arguments, "--parentPid", Process.GetCurrentProcess().Id.ToString());

            if (!string.IsNullOrWhiteSpace(_hostBridgePipeName))
            {
                arguments = AppendArgument(arguments, "--hostBridgePipe", _hostBridgePipeName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(_hostApplicationName))
            {
                arguments = AppendArgument(arguments, "--hostApplicationName", _hostApplicationName.Trim());
            }

            return arguments;
        }

        private static string AppendArgument(string existing, string name, string value)
        {
            var prefix = string.IsNullOrWhiteSpace(existing) ? string.Empty : existing + " ";
            return prefix + name + " " + QuoteArgument(value ?? string.Empty);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string InferConfiguration(string baseDirectory)
        {
            var normalized = (baseDirectory ?? string.Empty).Replace('/', '\\');
            if (normalized.IndexOf("\\Release\\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Release";
            }

            return "Debug";
        }

        private static BrokerEntryPath FindBrokerEntryPath(string startDirectory, string configuration)
        {
            var baseDir = startDirectory ?? AppDomain.CurrentDomain.BaseDirectory;

            // Phase 1: Production / installed layout — look in the same directory and a "Broker" subdirectory.
            var prodCandidates = new[]
            {
                baseDir,
                Path.Combine(baseDir, "Broker")
            };

            foreach (var candidateDir in prodCandidates)
            {
                var exeCandidate = Path.Combine(candidateDir, "OfficeCopilot.Agent.Broker.exe");
                if (File.Exists(exeCandidate))
                {
                    return new BrokerEntryPath { EntryPath = exeCandidate, UseDotNetHost = false };
                }

                var dllCandidate = Path.Combine(candidateDir, "OfficeCopilot.Agent.Broker.dll");
                if (File.Exists(dllCandidate))
                {
                    return new BrokerEntryPath { EntryPath = dllCandidate, UseDotNetHost = true };
                }
            }

            // Phase 2: Development layout — walk up looking for Broker project build output.
            var current = new DirectoryInfo(baseDir);
            for (var depth = 0; current != null && depth < 8; depth++, current = current.Parent)
            {
                var brokerDirectory = Path.Combine(
                    current.FullName,
                    "OfficeCopilot.Agent.Broker",
                    "bin",
                    configuration ?? "Debug",
                    "net472");

                var exeCandidate = Path.Combine(brokerDirectory, "OfficeCopilot.Agent.Broker.exe");
                if (File.Exists(exeCandidate))
                {
                    return new BrokerEntryPath { EntryPath = exeCandidate, UseDotNetHost = false };
                }

                var dllCandidate = Path.Combine(brokerDirectory, "OfficeCopilot.Agent.Broker.dll");
                if (File.Exists(dllCandidate))
                {
                    return new BrokerEntryPath { EntryPath = dllCandidate, UseDotNetHost = true };
                }
            }

            return null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            try
            {
                ShutdownAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to shut down Broker while disposing BrokerRuntimeHost. PipeName={PipeName}", _pipeName);
            }

            lock (_syncRoot)
            {
                CleanupProcessNoLock();

                if (_jobObject != null)
                {
                    _jobObject.Dispose();
                    _jobObject = null;
                }
            }

            try
            {
                _endpointRegistration?.Dispose();
            }
            catch (Exception ex) when (
                ex is IOException
                || ex is UnauthorizedAccessException
                || ex is System.Security.SecurityException)
            {
                _logger?.LogWarning(
                    ex,
                    "Failed to remove Broker endpoint discovery registration. PipeName={PipeName}",
                    _pipeName);
            }
        }

        private sealed class ManagedAgentBrokerClient : IAgentBrokerClient
        {
            private readonly BrokerRuntimeHost _owner;

            public ManagedAgentBrokerClient(BrokerRuntimeHost owner)
            {
                _owner = owner;
                Options = new AgentBrokerConnectionOptions
                {
                    ClientId = "officecopilot-panel",
                    SessionId = string.Empty
                };
            }

            public AgentBrokerConnectionOptions Options { get; private set; }

            public Task<AgentBrokerTurnResult> SendTurnAsync(
                AgentBrokerTurnRequest request,
                IAgentBrokerEventSink eventSink,
                CancellationToken cancellationToken)
            {
                return _owner.SendTurnAsync(request, eventSink, cancellationToken);
            }

            public Task<AgentBrokerCancelTurnResult> CancelTurnAsync(
                AgentBrokerCancelTurnRequest request,
                CancellationToken cancellationToken)
            {
                return _owner.CancelTurnAsync(request, cancellationToken);
            }

            public Task<AgentBrokerSeedConversationResult> SeedConversationAsync(
                AgentBrokerSeedConversationRequest request,
                CancellationToken cancellationToken)
            {
                return _owner.SeedConversationAsync(request, cancellationToken);
            }

            public Task<AgentBrokerClearExecutionModePreferenceResult> ClearExecutionModePreferenceAsync(
                AgentBrokerClearExecutionModePreferenceRequest request,
                CancellationToken cancellationToken)
            {
                return _owner.ClearExecutionModePreferenceAsync(request, cancellationToken);
            }

            public Task<AgentBrokerCompletionResult> CompleteAsync(
                AgentBrokerCompletionRequest request,
                CancellationToken cancellationToken)
            {
                return _owner.CompleteAsync(request, cancellationToken);
            }

            public Task<AgentBrokerListConversationsResult> ListConversationsAsync(
                AgentBrokerListConversationsRequest request,
                CancellationToken cancellationToken)
            {
                return _owner.ListConversationsAsync(request, cancellationToken);
            }

            public Task<AgentBrokerLoadConversationResult> LoadConversationAsync(
                AgentBrokerLoadConversationRequest request,
                CancellationToken cancellationToken)
            {
                return _owner.LoadConversationAsync(request, cancellationToken);
            }

            public Task<AgentBrokerDeleteConversationResult> DeleteConversationAsync(
                AgentBrokerDeleteConversationRequest request,
                CancellationToken cancellationToken)
            {
                return _owner.DeleteConversationAsync(request, cancellationToken);
            }

            public Task<AgentBrokerToolUserInteractionResponse> SendUserInteractionResponseAsync(
                AgentBrokerToolUserInteractionResponse response,
                CancellationToken cancellationToken)
            {
                return _owner.SendUserInteractionResponseAsync(response, cancellationToken);
            }
        }

        /// <summary>
        /// 把 Broker 的 stdout/stderr 追加写入 logs/broker-output-{时间}-pid{进程号}.log。
        /// 文件在收到第一行输出时才创建，没有输出的 Broker 不产生文件。
        /// </summary>
        private sealed class BrokerOutputCapture : IDisposable
        {
            private readonly object _sync = new object();
            private readonly ILogger _logger;
            private StreamWriter _writer;
            private bool _disposed;

            public BrokerOutputCapture(string logDirectory, int processId, ILogger logger)
            {
                _logger = logger;
                FilePath = Path.Combine(
                    logDirectory,
                    "broker-output-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-pid" + processId.ToString(CultureInfo.InvariantCulture) + ".log");
            }

            public string FilePath { get; }

            public void Attach(Process process)
            {
                try
                {
                    process.OutputDataReceived += (sender, e) => Write("stdout", e.Data);
                    process.ErrorDataReceived += (sender, e) => Write("stderr", e.Data);
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to attach the Broker output capture. OutputLog={OutputLog}", FilePath);
                }
            }

            private void Write(string stream, string line)
            {
                if (line == null)
                {
                    return;
                }

                lock (_sync)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    try
                    {
                        if (_writer == null)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                            _writer = new StreamWriter(FilePath, true, new UTF8Encoding(false)) { AutoFlush = true };
                        }

                        _writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + stream + "] " + line);
                    }
                    catch
                    {
                        // 输出采集失败不能影响 Broker 运行
                    }
                }
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    _disposed = true;
                    _writer?.Dispose();
                    _writer = null;
                }
            }
        }

        private static int? TryGetExitCode(Process process)
        {
            try
            {
                return process != null && process.HasExited ? process.ExitCode : (int?)null;
            }
            catch
            {
                return null;
            }
        }

        private sealed class BrokerLaunchInfo
        {
            public string FileName { get; set; }

            public string Arguments { get; set; }

            public string WorkingDirectory { get; set; }
        }

        private sealed class BrokerEntryPath
        {
            public string EntryPath { get; set; }

            public bool UseDotNetHost { get; set; }
        }

        private sealed class BrokerJobObject : IDisposable
        {
            private const uint JobObjectLimitKillOnJobClose = 0x00002000;

            private IntPtr _handle;

            public BrokerJobObject(string name)
            {
                _handle = CreateJobObject(IntPtr.Zero, name);
                if (_handle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

                var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                var pointer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, pointer, false);
                    if (!SetInformationJobObject(_handle, 9, pointer, (uint)length))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }

            public void AddProcess(Process process)
            {
                if (process == null)
                {
                    throw new ArgumentNullException(nameof(process));
                }

                if (!AssignProcessToJobObject(_handle, process.Handle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            public void Dispose()
            {
                if (_handle == IntPtr.Zero)
                {
                    return;
                }

                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool SetInformationJobObject(IntPtr hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(IntPtr handle);

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public IntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct IO_COUNTERS
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }
        }
    }
}
