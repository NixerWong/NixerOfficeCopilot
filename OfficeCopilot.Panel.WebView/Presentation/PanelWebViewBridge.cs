using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Persistence.Jsonl;
using OfficeCopilot.Panel.WebAssets;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OfficeCopilot.Panel.WebView.Presentation
{
    /// <summary>
    /// F1：从 <c>CopilotPanelControl</c> 抽出的 WebView2 承载层，
    /// 独占「控件初始化 → 虚拟主机映射 → 页面导航 → 消息收发」这条链路。
    /// 面板只保留业务语义，不再直接触碰 <see cref="CoreWebView2"/>。
    /// </summary>
    internal sealed class PanelWebViewBridge
    {
        public const string WebAssetsHostName = "appassets.local";
        public const string DecksHostName = "nixer-decks.local";

        private readonly Control _owner;
        private readonly WebView2 _webView;
        private readonly Action<string, string> _showPlaceholder;
        private readonly Action _hidePlaceholder;
        private readonly Func<Exception, string> _buildInitializationErrorMessage;
        private readonly Func<string> _hostApplicationNameAccessor;

        private bool _initializing;
        private bool _navigationHandlerAttached;
        private bool _processFailedHandlerAttached;

        public PanelWebViewBridge(
            Control owner,
            WebView2 webView,
            Action<string, string> showPlaceholder,
            Action hidePlaceholder,
            Func<Exception, string> buildInitializationErrorMessage,
            Func<string> hostApplicationNameAccessor)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (webView == null) throw new ArgumentNullException(nameof(webView));
            if (showPlaceholder == null) throw new ArgumentNullException(nameof(showPlaceholder));
            if (hidePlaceholder == null) throw new ArgumentNullException(nameof(hidePlaceholder));
            if (buildInitializationErrorMessage == null) throw new ArgumentNullException(nameof(buildInitializationErrorMessage));
            if (hostApplicationNameAccessor == null) throw new ArgumentNullException(nameof(hostApplicationNameAccessor));

            _owner = owner;
            _webView = webView;
            _showPlaceholder = showPlaceholder;
            _hidePlaceholder = hidePlaceholder;
            _buildInitializationErrorMessage = buildInitializationErrorMessage;
            _hostApplicationNameAccessor = hostApplicationNameAccessor;
        }

        /// <summary>WebView2 控件本身是否已完成 CoreWebView2 创建。</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>前端页面是否已回报 ready（由面板在收到 webClientReady 消息时置位）。</summary>
        public bool IsClientReady { get; set; }

        public bool CanPost
        {
            get { return IsInitialized && IsClientReady; }
        }

        public event EventHandler<CoreWebView2WebMessageReceivedEventArgs> WebMessageReceived;

        public async Task EnsureReadyAsync()
        {
            if (IsInitialized || _initializing)
            {
                return;
            }

            _initializing = true;
            _showPlaceholder("正在加载界面", "WebView2 正在初始化，请稍候...");

            try
            {
                var userDataFolder = GetUserDataFolder(_hostApplicationNameAccessor());
                Directory.CreateDirectory(userDataFolder);

                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webView.EnsureCoreWebView2Async(environment);

                IsInitialized = true;

                Configure();
                LoadWebAssetsPage();
            }
            catch (Exception ex)
            {
                _showPlaceholder("界面加载失败", _buildInitializationErrorMessage(ex));
            }
            finally
            {
                _initializing = false;
            }
        }

        /// <summary>
        /// 向前端投递一条消息。未就绪时静默丢弃——前端 ready 后面板会重推整段会话。
        /// </summary>
        public void Post(object payload, Action<Action> beginInvoke)
        {
            if (!CanPost)
            {
                return;
            }

            var json = JsonConvert.SerializeObject(payload);

            beginInvoke(() =>
            {
                try
                {
                    if (_owner.IsDisposed || _webView.IsDisposed || !_webView.IsHandleCreated)
                    {
                        return;
                    }

                    var coreWebView2 = _webView.CoreWebView2;
                    if (coreWebView2 == null)
                    {
                        return;
                    }

                    coreWebView2.PostWebMessageAsJson(json);
                }
                catch (InvalidOperationException)
                {
                    // WebView2 在切换/销毁瞬间可能抛异常，不影响宿主控件状态恢复
                }
            });
        }

        /// <summary>
        /// WebView2 用户数据目录，按宿主隔离：<c>%LOCALAPPDATA%\NixerOfficeCopilot\&lt;Host&gt;\WebView2</c>。
        /// </summary>
        public static string GetUserDataFolder(string hostApplicationName)
        {
            var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var hostScope = ConversationScope.ResolveHostScope(hostApplicationName);
            return Path.Combine(baseFolder, "NixerOfficeCopilot", hostScope, "WebView2");
        }

        private void Configure()
        {
            if (_webView.CoreWebView2 == null)
            {
                return;
            }

            if (!_navigationHandlerAttached)
            {
                _webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                _navigationHandlerAttached = true;
            }

            if (!_processFailedHandlerAttached)
            {
                _webView.CoreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;
                _processFailedHandlerAttached = true;
            }

            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                WebAssetsHostName,
                GetWebAssetsRoot(),
                CoreWebView2HostResourceAccessKind.DenyCors);

            // 幻灯片工作区（候选预览图等）：只供 <img> 显示，DenyCors 禁止脚本读取。
            // 路径须与 OfficeCopilot.Slides.Deck.DeckWorkspaceStore.GetDefaultRoot 一致。
            var decksRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "Decks");
            Directory.CreateDirectory(decksRoot);
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                DecksHostName,
                decksRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);

            _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        }

        private void LoadWebAssetsPage()
        {
            var indexPath = Path.Combine(GetWebAssetsRoot(), "index.html");
            if (!File.Exists(indexPath))
            {
                _showPlaceholder("界面加载失败", "未找到前端页面资源：\r\n\r\n" + indexPath);
                return;
            }

            IsClientReady = false;
            _showPlaceholder("正在加载界面", "正在加载本地页面资源，请稍候...");
            _webView.CoreWebView2.Navigate("https://" + WebAssetsHostName + "/index.html");
        }

        private static string GetWebAssetsRoot()
        {
            var assetsRoot = WebAssetsLocator.EnsureAssetsRoot();
            if (string.IsNullOrWhiteSpace(assetsRoot) || !Directory.Exists(assetsRoot))
            {
                throw new DirectoryNotFoundException("未找到 Web 资源目录：" + assetsRoot);
            }

            return assetsRoot;
        }

        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                _hidePlaceholder();
                return;
            }

            _showPlaceholder(
                "界面加载失败",
                "页面资源加载失败。"
                + "\r\n\r\n状态："
                + e.WebErrorStatus
                + "\r\n\r\n页面：https://"
                + WebAssetsHostName
                + "/index.html");
        }

        /// <summary>
        /// WebView2 渲染进程崩溃时，默认会停留在一个灰色错误页（带「重新加载」按钮）。
        /// 这里记录崩溃详情并自动重载，让面板走「ready → 重推整段会话」的既有恢复路径，
        /// 避免用户看到半途「内容被清空 + 灰屏」再手动刷新。
        /// </summary>
        private void CoreWebView2_ProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            try
            {
                PanelTrace.Write(
                    "[PanelWebViewBridge] WebView2 ProcessFailed: Kind=" + e.ProcessFailedKind
                    + ", Reason=" + e.Reason
                    + ", ExitCode=" + e.ExitCode
                    + ", Process=" + (e.ProcessDescription ?? string.Empty));
            }
            catch (Exception logEx)
            {
                PanelTrace.Write("[PanelWebViewBridge] WebView2 ProcessFailed (logging failed): " + logEx.Message);
            }

            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited
                || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
            {
                try
                {
                    IsClientReady = false;
                    _webView.CoreWebView2.Reload();
                }
                catch (Exception)
                {
                    // WebView2 可能正处于销毁/重初始化状态，无法 Reload 时忽略，由内置错误页兕底。
                }
            }
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var handler = WebMessageReceived;
            if (handler != null)
            {
                handler(this, e);
            }
        }
    }
}
