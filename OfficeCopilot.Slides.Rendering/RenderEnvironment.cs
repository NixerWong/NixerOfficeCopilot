using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Slides.Rendering
{
    /// <summary>
    /// 渲染用 WebView2 环境：与面板分开的 userDataFolder；关闭遮挡检测与后台节流，
    /// 否则屏幕外窗口不出帧、截图会挂起。
    /// </summary>
    internal static class RenderEnvironment
    {
        private const string BrowserArguments =
            "--disable-features=CalculateNativeWinOcclusion "
            + "--disable-backgrounding-occluded-windows "
            + "--disable-renderer-backgrounding "
            + "--disable-background-timer-throttling "
            + "--force-color-profile=srgb";

        private static readonly object SyncRoot = new object();
        private static Task<CoreWebView2Environment> _environment;

        public static string UserDataFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NixerOfficeCopilot",
                    "SlideRender",
                    "WebView2");
            }
        }

        /// <summary>须在 UI 线程调用。</summary>
        public static Task<CoreWebView2Environment> GetAsync()
        {
            lock (SyncRoot)
            {
                if (_environment == null || _environment.IsFaulted || _environment.IsCanceled)
                {
                    Directory.CreateDirectory(UserDataFolder);
                    _environment = CoreWebView2Environment.CreateAsync(
                        null,
                        UserDataFolder,
                        new CoreWebView2EnvironmentOptions(BrowserArguments));
                }

                return _environment;
            }
        }
    }

    /// <summary>
    /// 网络隔离与能力收紧（蓝图 §5.9）：只放行 <c>https://nixer-deck.local/</c>（经 <see cref="DeckUrlResolver"/> 映射到本地文件），
    /// 其余请求一律 403 并记录；不注入宿主对象、不开 WebMessage / DevTools / 下载 / 新窗口 / 权限。
    /// </summary>
    internal sealed class RenderIsolation
    {
        private readonly DeckUrlResolver _resolver;
        private readonly List<string> _blocked = new List<string>();

        public RenderIsolation(DeckUrlResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public IList<string> TakeBlocked()
        {
            var copy = new List<string>(_blocked);
            _blocked.Clear();
            return copy;
        }

        public void Attach(CoreWebView2 core)
        {
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsWebMessageEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsBuiltInErrorPageEnabled = false;

            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
            core.NewWindowRequested += (s, e) => e.Handled = true;
            core.DownloadStarting += (s, e) => e.Cancel = true;
            core.PermissionRequested += (s, e) => e.State = CoreWebView2PermissionState.Deny;
            core.NavigationStarting += (s, e) =>
            {
                if (!IsOwnUrl(e.Uri))
                {
                    e.Cancel = true;
                    Record(e.Uri);
                }
            };
            core.FrameNavigationStarting += (s, e) =>
            {
                if (!IsOwnUrl(e.Uri))
                {
                    e.Cancel = true;
                    Record(e.Uri);
                }
            };
        }

        /// <summary>以原始字节提供随包样式表；系统同名字体仅记录诊断信息。</summary>
        private static byte[] ReadFontCss(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var css = Encoding.UTF8.GetString(bytes);
            var shadows = FontShadowDiagnostics.FindSystemShadows(css, InstalledFontState.FamilyNames());
            if (shadows.Count > 0)
            {
                Trace.TraceInformation(
                    "Bundled font faces are loaded unchanged; system fonts with matching names: {0}",
                    string.Join(", ", shadows));
            }

            return bytes;
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            var core = (CoreWebView2)sender;
            var uri = e.Request.Uri;
            string path;
            if (_resolver.TryResolve(uri, out path) && File.Exists(path))
            {
                byte[] bytes;
                try
                {
                    bytes = uri.EndsWith("/fonts/fonts.css", StringComparison.OrdinalIgnoreCase)
                        ? ReadFontCss(path)
                        : File.ReadAllBytes(path);
                }
                catch (IOException)
                {
                    e.Response = core.Environment.CreateWebResourceResponse(null, 500, "Read Error", string.Empty);
                    return;
                }

                e.Response = core.Environment.CreateWebResourceResponse(
                    new MemoryStream(bytes, false),
                    200,
                    "OK",
                    "Content-Type: " + DeckUrlResolver.GetContentType(path) + "\r\nCache-Control: no-store");
                return;
            }

            if (IsOwnUrl(uri))
            {
                e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
                return;
            }

            Record(uri);
            e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);
        }

        private static bool IsOwnUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Host, DeckUrls.RenderHost, StringComparison.OrdinalIgnoreCase)
                && uri.IsDefaultPort;
        }

        private void Record(string url)
        {
            if (string.IsNullOrEmpty(url) || _blocked.Count >= 50) return;
            var clipped = url.Length <= 160 ? url : url.Substring(0, 160) + "…";
            if (!_blocked.Contains(clipped)) _blocked.Add(clipped);
        }
    }
}
