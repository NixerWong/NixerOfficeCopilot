using Microsoft.Extensions.DependencyInjection;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.Agent.Runtime.Composition;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.HostBridge.Client;
using OfficeCopilot.Mcp;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal sealed class BrokerApplication
    {
        private readonly IBrokerTurnService _turnService;
        private readonly BrokerPendingUserInteractionRegistry _userInteractionRegistry;

        public BrokerApplication()
            : this(Array.Empty<string>())
        {
        }

        public BrokerApplication(string[] args)
            : this(CreateTurnService(args, out var registry), registry)
        {
        }

        public BrokerApplication(IBrokerTurnService turnService)
            : this(turnService, new BrokerPendingUserInteractionRegistry())
        {
        }

        internal BrokerApplication(IBrokerTurnService turnService, BrokerPendingUserInteractionRegistry userInteractionRegistry)
        {
            _turnService = turnService ?? throw new ArgumentNullException(nameof(turnService));
            _userInteractionRegistry = userInteractionRegistry ?? throw new ArgumentNullException(nameof(userInteractionRegistry));
        }

        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pipeName = GetPipeName(args);
            if (!string.IsNullOrWhiteSpace(pipeName))
            {
                BrokerRuntimeLogger.Info("BrokerApplication", "Starting named pipe host. PipeName=" + pipeName);
                var lifetime = new BrokerApplicationLifetime();
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // === 防崩溃: 使用 try-finally 确保释放顺序 ===
                // linkedCts 必须在 _userInteractionRegistry 之前释放，
                // 因为 _userInteractionRegistry.Dispose() 可能触发回调中使用 linkedCts。
                // 修正前的 using 嵌套顺序是错误的。
                var parentProcessMonitorTask = StartParentProcessMonitorAsync(args, linkedCts);

                try
                {
                    using (_userInteractionRegistry)
                    using (linkedCts)
                    {
                        var envelopeDispatcher = new BrokerEnvelopeDispatcher(
                            _turnService, new BrokerTurnCancellationRegistry(), lifetime, linkedCts, _userInteractionRegistry);

                        using (var host = new NamedPipeBrokerHost(
                            new NamedPipeBrokerHostOptions { PipeName = pipeName },
                            envelopeDispatcher))
                        {
                            ConsoleCancelEventHandler cancelHandler = delegate (object sender, ConsoleCancelEventArgs e)
                            {
                                e.Cancel = true;
                                BrokerRuntimeLogger.Info("BrokerApplication", "Ctrl+C received, requesting shutdown.");
                                linkedCts.Cancel();
                            };

                            Console.CancelKeyPress += cancelHandler;

                            try
                            {
                                var sinceProcessStartMs = (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                                BrokerRuntimeLogger.Info("BrokerApplication", $"Broker started. Waiting for connections... SinceProcessStartMs={sinceProcessStartMs}");
                                await host.StartAsync(linkedCts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                BrokerRuntimeLogger.Info("BrokerApplication", "Broker shutdown requested (cancellation).");
                            }
                            catch (Exception ex)
                            {
                                BrokerRuntimeLogger.Error("BrokerApplication", "StartAsync faulted", ex);
                            }
                            finally
                            {
                                Console.CancelKeyPress -= cancelHandler;
                                var stopReason = lifetime.StopReason ?? "normal";
                                BrokerRuntimeLogger.Info("BrokerApplication",
                                    $"Broker shutting down. StopReason={stopReason}, ActiveConnections={host.ActiveConnectionCount}");

                                try
                                {
                                    await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                                }
                                catch (Exception stopEx)
                                {
                                    BrokerRuntimeLogger.Warning("BrokerApplication", "StopAsync failed: " + stopEx.Message);
                                }
                            }
                        } // using host
                    } // using linkedCts, using _userInteractionRegistry

                    // 等待 parent process monitor 结束
                    try
                    {
                        await parentProcessMonitorTask.ConfigureAwait(false);
                    }
                    catch (Exception monitorEx)
                    {
                        BrokerRuntimeLogger.Warning("BrokerApplication", "ParentProcessMonitor failed: " + monitorEx.Message);
                    }
                }
                catch (Exception ex)
                {
                    BrokerRuntimeLogger.Error("BrokerApplication", "RunAsync fatal error", ex);
                    return 1;
                }

                return 0;
            }

            var prompt = GetPrompt(args);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return 0;
            }

            var result = await _turnService.ExecuteAsync(
                new AgentBrokerTurnRequest
                {
                    ConversationId = Guid.NewGuid().ToString("N"),
                    MessageId = Guid.NewGuid().ToString("N"),
                    UserInput = prompt,
                    InteractiveMode = "agent"
                },
                new ConsoleBrokerEventSink(),
                cancellationToken).ConfigureAwait(false);

            return result != null && result.Success ? 0 : 1;
        }

        private static string GetPipeName(string[] args)
        {
            return GetArgumentValue(args, "--pipe");
        }

        private static string GetPrompt(string[] args)
        {
            return GetArgumentValue(args, "--prompt");
        }

        private static string GetHostBridgePipeName(string[] args)
        {
            return GetArgumentValue(args, "--hostBridgePipe");
        }

        private static string GetHostApplicationName(string[] args)
        {
            return GetArgumentValue(args, "--hostApplicationName");
        }

        private static int? GetParentProcessId(string[] args)
        {
            int parentProcessId;
            return int.TryParse(GetArgumentValue(args, "--parentPid"), out parentProcessId)
                ? (int?)parentProcessId
                : null;
        }

        private static string GetArgumentValue(string[] args, string argumentName)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] ?? string.Empty;
                if (!string.Equals(arg, argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 < args.Length)
                {
                    return args[i + 1] ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static IBrokerTurnService CreateTurnService(string[] args, out BrokerPendingUserInteractionRegistry userInteractionRegistry)
        {
            var hostBridgePipeName = GetHostBridgePipeName(args);
            IHostBridgeClient hostBridgeClient = string.IsNullOrWhiteSpace(hostBridgePipeName)
                ? (IHostBridgeClient)new NullHostBridgeClient()
                : new NamedPipeHostBridgeClient(
                    new HostBridgeClientOptions
                    {
                        Endpoint = hostBridgePipeName,
                        SessionId = string.Empty
                    });

            // Phase 1.3c：从 appsettings.json 读取 StorageMode / JsonlRootDirectory
            var stepStopwatch = Stopwatch.StartNew();
            var runtimeOptions = BrokerConfig.LoadRuntimeOptions(args);
            runtimeOptions.HostApplicationName = GetHostApplicationName(args);
            BrokerRuntimeLogger.Info("BrokerApplication", $"Startup step runtime options loaded in {stepStopwatch.ElapsedMilliseconds}ms");

            // Phase 0: 创建 UserInteractionRegistry，由 BrokerApplication 统一管理生命周期
            userInteractionRegistry = new BrokerPendingUserInteractionRegistry();

            // 阶段 2：从 settings.json 加载 MCP 配置
            // 连接 MCP 服务可能需要数秒（npx 冷启动、远程 HTTP），必须后台加载，
            // 否则命名管道迟迟不能建立，宿主 Ping 探测超时后会杀掉 Broker。
            IMcpRegistry mcpRegistry = null;
            Task mcpLoadTask = Task.CompletedTask;
            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NixerOfficeCopilot",
                    "settings.json");

                var mcpConfig = McpJsonConfig.FromSettingsJson(settingsPath);
                var registry = new McpRegistry();
                mcpRegistry = registry;
                mcpLoadTask = Task.Run(() => LoadMcpRegistryAsync(registry, mcpConfig));
            }
            catch (Exception ex)
            {
                BrokerRuntimeLogger.Error("BrokerApplication", "MCP registry startup load failed.", ex);
            }

            var services = new ServiceCollection();
            services.AddSingleton(hostBridgeClient);
            services.AddSingleton<IUserInteractionHandler>(userInteractionRegistry);
            if (mcpRegistry != null)
            {
                services.AddSingleton(mcpRegistry);
            }

            stepStopwatch.Restart();
            services.AddOfficeCopilotAgentRuntime(runtimeOptions);
            var agentRuntime = services.BuildServiceProvider().GetRequiredService<IAgentRuntime>();
            BrokerRuntimeLogger.Info("BrokerApplication", $"Startup step agent runtime built in {stepStopwatch.ElapsedMilliseconds}ms");

            return new BrokerTurnService(agentRuntime, mcpLoadTask);
        }

        private static async Task LoadMcpRegistryAsync(McpRegistry registry, McpJsonConfig config)
        {
            try
            {
                await registry.LoadAsync(config, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                BrokerRuntimeLogger.Error("BrokerApplication", "MCP registry startup load failed.", ex);
            }
        }

        private static Task StartParentProcessMonitorAsync(string[] args, CancellationTokenSource linkedCts)
        {
            var parentProcessId = GetParentProcessId(args);
            if (!parentProcessId.HasValue || parentProcessId.Value <= 0)
            {
                return Task.CompletedTask;
            }

            return Task.Run(async delegate
            {
                BrokerRuntimeLogger.Info("ParentProcessMonitor", $"Monitoring parent process {parentProcessId.Value}.");
                while (!linkedCts.IsCancellationRequested)
                {
                    if (!IsProcessAlive(parentProcessId.Value))
                    {
                        BrokerRuntimeLogger.Warning("ParentProcessMonitor", "Parent process exited. Requesting broker shutdown.");
                        try
                        {
                            linkedCts.Cancel();
                        }
                        catch
                        {
                        }

                        break;
                    }

                    try
                    {
                        await Task.Delay(1000, linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                BrokerRuntimeLogger.Info("ParentProcessMonitor", "Monitor stopped.");
            });
        }

        private static bool IsProcessAlive(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    return !process.HasExited;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
