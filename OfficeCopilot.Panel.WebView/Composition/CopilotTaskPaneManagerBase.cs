using Microsoft.Extensions.Logging;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Diagnostics;
using OfficeCopilot.Diagnostics.Logging;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.HostBridge.Server;
using OfficeCopilot.Panel.WebView.Services;
using OfficeCopilot.Panel.WebView.Services.References;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OfficeCopilot.Panel.WebView.Composition
{
    /// <summary>
    /// 三个 VSTO 加载项共用的任务窗格生命周期管理。此前 Excel / Word / PowerPoint 各持一份
    /// 近乎逐字相同的 ~530 行实现，差异仅在窗口类型、工具注册与宿主服务登记三处。
    /// </summary>
    /// <typeparam name="TWindow">宿主的窗口类型（Excel.Window / Word.Window / PowerPoint.DocumentWindow）。</typeparam>
    public abstract class CopilotTaskPaneManagerBase<TWindow> : IDisposable
        where TWindow : class
    {
        private const int TaskPaneWidth = 550;

        private readonly Dictionary<string, WindowPaneContext> _windowContexts =
            new Dictionary<string, WindowPaneContext>(StringComparer.OrdinalIgnoreCase);

        /// <summary>日志类别名，例如 <c>NixerExcelCopilot.TaskPane</c>。</summary>
        protected abstract string LogCategory { get; }

        /// <summary>取不到活动窗口时抛出的异常文案。</summary>
        protected abstract string NoActiveWindowMessage { get; }

        protected abstract bool TryGetActiveWindow(out TWindow activeWindow, out string windowKey);

        protected abstract ICopilotHostAdapter CreateHostAdapter();

        /// <summary>登记宿主专属服务契约（<c>IExcelToolHost</c> / <c>IWordToolHost</c> 等）。</summary>
        protected abstract void RegisterHostServices(AgentServiceProvider services, ICopilotHostAdapter hostAdapter);

        protected abstract void RegisterTools(InMemoryToolRegistry registry, OfficeCopilotSettings settings);

        protected virtual void RegisterSettingsServices(AgentServiceProvider services, OfficeCopilotSettings settings)
        {
        }

        /// <summary>创建并停靠任务窗格。停靠属性依赖 Office PIA，故留在各加载项内。</summary>
        protected abstract Microsoft.Office.Tools.CustomTaskPane CreateTaskPane(
            UserControl panelControl,
            TWindow activeWindow,
            int width);

        protected abstract void RemoveTaskPane(Microsoft.Office.Tools.CustomTaskPane taskPane);

        public bool IsPaneCreated
        {
            get
            {
                WindowPaneContext context;
                return TryGetCurrentContext(out context);
            }
        }

        public void TogglePane()
        {
            if (TryHideLivePaneWithoutActiveWindow())
            {
                return;
            }

            var context = EnsurePane();
            StartupTimingLog.Mark("pane-show-begin");
            context.TaskPane.Visible = !context.TaskPane.Visible;
            if (context.TaskPane.Visible)
            {
                StartupTimingLog.Mark("pane-visible");
                context.PanelControl.RefreshRuntimeSettings();
            }
        }

        public void ShowPane()
        {
            var context = EnsurePane();
            StartupTimingLog.Mark("pane-show-begin");
            context.TaskPane.Visible = true;
            StartupTimingLog.Mark("pane-visible");
            context.PanelControl.RefreshRuntimeSettings();
        }

        public void RefreshContext()
        {
            foreach (var context in new List<WindowPaneContext>(_windowContexts.Values))
            {
                if (context != null && context.PanelControl != null)
                {
                    context.PanelControl.RefreshRuntimeSettings();
                }
            }
        }

        public bool TryGetToolRuntime(
            out IToolRegistry toolRegistry,
            out IServiceProvider toolServices,
            out string hostApplicationName)
        {
            toolRegistry = null;
            toolServices = null;
            hostApplicationName = string.Empty;

            try
            {
                var context = EnsurePane();
                if (context == null || context.PanelControl == null)
                {
                    return false;
                }

                toolRegistry = context.PanelControl.ToolRegistry;
                toolServices = context.PanelControl.AgentServices;
                hostApplicationName = context.PanelControl.HostAdapter == null
                    ? string.Empty
                    : context.PanelControl.HostAdapter.HostApplicationName;

                return toolRegistry != null && toolServices != null;
            }
            catch (Exception ex)
            {
                OfficeCopilotLog.Error(LogCategory, "TryGetToolRuntime failed; tool test page will report no runtime.", ex);
                return false;
            }
        }

        public void Dispose()
        {
            foreach (var entry in new List<KeyValuePair<string, WindowPaneContext>>(_windowContexts))
            {
                DisposeWindowContext(entry.Key, entry.Value);
            }

            _windowContexts.Clear();
        }

        /// <summary>
        /// 把设置里的工具定义覆写映射为运行时模型。三个宿主的 <c>*ToolCatalogOptions</c> 类型不同，
        /// 但覆写条目本身完全一致，故只共享这一段。
        /// </summary>
        protected static IList<ToolDefinitionOverride> CreateToolDefinitionOverrides(OfficeCopilotSettings settings)
        {
            var result = new List<ToolDefinitionOverride>();
            if (settings == null)
            {
                return result;
            }

            var overrides = settings.GetToolDefinitionOverrides();
            for (var i = 0; i < overrides.Count; i++)
            {
                var definitionOverride = overrides[i];
                if (definitionOverride == null)
                {
                    continue;
                }

                result.Add(new ToolDefinitionOverride
                {
                    ToolName = definitionOverride.ToolName,
                    DisplayName = definitionOverride.DisplayName,
                    Description = definitionOverride.Description,
                    RequiresConfirmation = definitionOverride.RequiresConfirmation,
                    RequiresBackup = definitionOverride.RequiresBackup,
                    InputSchemaJson = definitionOverride.InputSchemaJson,
                    OutputSchemaJson = definitionOverride.OutputSchemaJson,
                    UpdatedAtUtc = definitionOverride.UpdatedAtUtc
                });
            }

            return result;
        }

        /// <summary>是否需要为宿主构造 <c>*ToolCatalogOptions</c>；两类配置都为空时走无参注册。</summary>
        protected static bool HasToolCatalogConfiguration(OfficeCopilotSettings settings)
        {
            return settings != null
                && (settings.HasToolRegistrationConfiguration() || settings.HasToolDefinitionOverrideConfiguration());
        }

        /// <summary>
        /// 没有活动窗口（例如 Excel 中工作簿全部关闭）时，若残留的面板仍可见，直接隐藏它；
        /// 否则切换会去重新挂载面板并抛出 "No Excel window" 异常。
        /// </summary>
        private bool TryHideLivePaneWithoutActiveWindow()
        {
            TWindow activeWindow;
            string windowKey;
            if (TryGetActiveWindow(out activeWindow, out windowKey))
            {
                return false;
            }

            foreach (var entry in new List<KeyValuePair<string, WindowPaneContext>>(_windowContexts))
            {
                if (!IsTaskPaneAlive(entry.Value) || !entry.Value.TaskPane.Visible)
                {
                    continue;
                }

                entry.Value.TaskPane.Visible = false;
                return true;
            }

            return false;
        }

        private WindowPaneContext EnsurePane()
        {
            TWindow activeWindow;
            string windowKey;
            if (!TryGetActiveWindow(out activeWindow, out windowKey))
            {
                throw new InvalidOperationException(NoActiveWindowMessage);
            }

            WindowPaneContext existing;
            if (_windowContexts.TryGetValue(windowKey, out existing))
            {
                if (IsTaskPaneAlive(existing))
                {
                    return existing;
                }

                DisposeWindowContext(windowKey, existing);
            }

            var settingsStore = new JsonSettingsStore();
            var settings = OfficeCopilotSettingsFactory.Normalize(
                settingsStore.Load() ?? OfficeCopilotSettingsFactory.CreateDefault());

            StartupTimingLog.Mark("ensurepane-settings-loaded");

            ToolCallDiagnosticsLogger.Configure(
                settings.Logging != null
                && settings.Logging.ToolCalls != null
                && settings.Logging.ToolCalls.IsEnabled);

            var loggerFactory = OfficeCopilotDiagnostics.CreateLoggerFactory(settings.Logging);
            var taskPaneLogger = loggerFactory.CreateLogger(LogCategory);
            OfficeCopilotDiagnostics.CreateAndWireToolCallLogSink(taskPaneLogger, settings.Logging);
            var panelLogger = loggerFactory.CreateLogger(typeof(CopilotPanelControl).FullName);
            StartupTimingLog.Mark("ensurepane-logger-ready");

            Microsoft.Office.Tools.CustomTaskPane taskPane = null;
            CopilotPanelControl panelControl = null;
            BrokerRuntimeHost brokerRuntimeHost = null;
            IHostBridgeServer hostBridgeServer = null;
            CancellationTokenSource hostBridgeServerCts = null;

            try
            {
                taskPaneLogger.LogInformation("EnsurePane begin, window={WindowKey}", windowKey);
                StartupTimingLog.Mark("ensurepane-begin");

                var hostAdapter = CreateHostAdapter();
                StartupTimingLog.Mark("ensurepane-host-adapter-created");

                var services = new AgentServiceProvider();
                services.AddService<ICopilotHostAdapter>(hostAdapter);
                RegisterHostServices(services, hostAdapter);
                RegisterSettingsServices(services, settings);

                var registry = new InMemoryToolRegistry();
                RegisterTools(registry, settings);
                StartupTimingLog.Mark("ensurepane-tools-registered");

                panelControl = new CopilotPanelControl(settingsStore, panelLogger);
                StartupTimingLog.Mark("ensurepane-panel-created");
                services.AddService<OfficeCopilot.Panel.Abstractions.IReferenceArtifactStore>(panelControl.ReferenceArtifactStore);
                registry.Register(new GetReferenceContentTool());
                panelControl.HostAdapter = hostAdapter;
                panelControl.AgentServices = services;
                panelControl.ToolRegistry = registry;

                services.AddService<IToolConfirmationHandler>(new ChatToolConfirmationHandler(panelControl));

                var hostBridgePipeName = HostBridgePipeNameFactory.Create(windowKey);
                hostBridgeServerCts = new CancellationTokenSource();
                hostBridgeServer = new NamedPipeHostBridgeServer(
                    new HostBridgeServerOptions
                    {
                        Endpoint = hostBridgePipeName,
                        SessionId = string.Empty
                    },
                    new DefaultHostBridgeRequestHandler(hostAdapter, registry, services));
                _ = hostBridgeServer.StartAsync(hostBridgeServerCts.Token);

                brokerRuntimeHost = new BrokerRuntimeHost(
                    taskPaneLogger,
                    hostBridgePipeName,
                    settings.Runtime == null ? 0 : settings.Runtime.BrokerReadyTimeoutSeconds,
                    hostAdapter.HostApplicationName);
                StartupTimingLog.Mark("ensurepane-broker-host-created");

                // 不在 UI 线程上同步等待 Broker 进程启动：CreateClient() 会轮询 Ping 数秒，
                // 那是面板打开假死的根因。先出界面，再后台预热。
                panelControl.BrokerClient = brokerRuntimeHost.CreateClientNoStart();

                taskPane = CreateTaskPane(panelControl, activeWindow, TaskPaneWidth);
                taskPane.Visible = false;
                StartupTimingLog.Mark("ensurepane-taskpane-created");

                // BrokerClient 注入完成后才触发历史会话加载（构造时还是 Null 占位）。
                panelControl.BeginLoadSessionsFromBroker();
                StartupTimingLog.Mark("ensurepane-history-requested");

                var capturedHost = brokerRuntimeHost;
                _ = Task.Run(() =>
                {
                    try
                    {
                        capturedHost.EnsureStarted();
                    }
                    catch (Exception warmupEx)
                    {
                        taskPaneLogger.LogWarning(
                            warmupEx,
                            "Background broker warmup failed. PipeName={PipeName}",
                            capturedHost.PipeName ?? "unknown");
                    }
                });

                var context = new WindowPaneContext
                {
                    TaskPane = taskPane,
                    PanelControl = panelControl,
                    BrokerRuntimeHost = brokerRuntimeHost,
                    HostBridgeServer = hostBridgeServer,
                    HostBridgeServerCts = hostBridgeServerCts,
                    LoggerFactoryDisposable = loggerFactory
                };

                _windowContexts[windowKey] = context;

                taskPaneLogger.LogInformation("EnsurePane end, window={WindowKey}", windowKey);
                StartupTimingLog.Mark("ensurepane-end");
                return context;
            }
            catch (Exception ex)
            {
                taskPaneLogger.LogError(ex, "EnsurePane failed, window={WindowKey}", windowKey);
                ReleaseResources(taskPane, panelControl, brokerRuntimeHost, hostBridgeServer, hostBridgeServerCts, loggerFactory);
                throw;
            }
        }

        private bool TryGetCurrentContext(out WindowPaneContext context)
        {
            context = null;

            TWindow activeWindow;
            string windowKey;
            if (!TryGetActiveWindow(out activeWindow, out windowKey))
            {
                return false;
            }

            return _windowContexts.TryGetValue(windowKey, out context) && IsTaskPaneAlive(context);
        }

        private bool IsTaskPaneAlive(WindowPaneContext context)
        {
            if (context == null || context.TaskPane == null)
            {
                return false;
            }

            try
            {
                // 窗口已关闭时读取 Visible 会抛 COM 异常，这是唯一可靠的存活探测方式。
                var unused = context.TaskPane.Visible;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void DisposeWindowContext(string windowKey, WindowPaneContext context)
        {
            if (!string.IsNullOrEmpty(windowKey))
            {
                _windowContexts.Remove(windowKey);
            }

            if (context == null)
            {
                return;
            }

            ReleaseResources(
                context.TaskPane,
                context.PanelControl,
                context.BrokerRuntimeHost,
                context.HostBridgeServer,
                context.HostBridgeServerCts,
                context.LoggerFactoryDisposable);
        }

        private void ReleaseResources(
            Microsoft.Office.Tools.CustomTaskPane taskPane,
            CopilotPanelControl panelControl,
            BrokerRuntimeHost brokerRuntimeHost,
            IHostBridgeServer hostBridgeServer,
            CancellationTokenSource hostBridgeServerCts,
            IDisposable loggerFactoryDisposable)
        {
            if (taskPane != null)
            {
                SafeRelease("RemoveTaskPane", () => RemoveTaskPane(taskPane));
            }

            if (hostBridgeServerCts != null)
            {
                SafeRelease("CancelHostBridgeServer", () => hostBridgeServerCts.Cancel());
                SafeRelease("DisposeHostBridgeServerCts", () => hostBridgeServerCts.Dispose());
            }

            if (hostBridgeServer != null)
            {
                SafeRelease("DisposeHostBridgeServer", () => hostBridgeServer.Dispose());
            }

            if (panelControl != null)
            {
                SafeRelease("DisposePanelControl", () => panelControl.Dispose());
            }

            if (brokerRuntimeHost != null)
            {
                SafeRelease("DisposeBrokerRuntimeHost", () => brokerRuntimeHost.Dispose());
            }

            if (loggerFactoryDisposable != null)
            {
                SafeRelease("DisposeLoggerFactory", () => loggerFactoryDisposable.Dispose());
            }
        }

        private void SafeRelease(string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // 释放阶段失败不能阻断后续资源回收，但必须留痕（原实现是三份空 catch）。
                OfficeCopilotLog.Error(LogCategory, "Pane teardown step '" + step + "' failed.", ex);
            }
        }
    }
}
