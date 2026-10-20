using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using OfficeCopilot.Slides.Deck;

namespace OfficeCopilot.Slides.Rendering
{
    /// <summary>
    /// 非模态整稿预览窗口：黑底居中 16:9，按 ZoomFactor 把页面视口固定为 1920 设计宽度。
    /// 每个 deck 一个窗口，重复打开时刷新并前置。必须在 UI 线程调用。
    /// </summary>
    public sealed class DeckPreviewForm : Form
    {
        private static readonly Dictionary<string, DeckPreviewForm> OpenForms =
            new Dictionary<string, DeckPreviewForm>(StringComparer.Ordinal);

        /// <summary>由宿主加载项在启动时注入，新建的预览窗口使用该图标。</summary>
        public static Icon WindowIcon { get; set; }

        private readonly string _key;
        private readonly RenderIsolation _isolation;
        private readonly Panel _stage;
        private readonly WebView2 _view;
        private readonly Timer _fitTimer;
        private Task _initialization;

        private DeckPreviewForm(string key, DeckUrlResolver resolver)
        {
            _key = key;
            _isolation = new RenderIsolation(resolver);
            Text = "Nixer 预览";
            if (WindowIcon != null) Icon = WindowIcon;
            BackColor = Color.Black;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1280, 720);
            MinimumSize = new Size(480, 300);
            ShowInTaskbar = true;
            // 预览需持续可见，避免被后续激活的 agent 输出窗口盖到后台。
            TopMost = true;
            KeyPreview = true;

            _stage = new Panel { BackColor = Color.Black };
            _view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
            _stage.Controls.Add(_view);
            Controls.Add(_stage);

            _fitTimer = new Timer { Interval = 150 };
            _fitTimer.Tick += async (s, e) =>
            {
                _fitTimer.Stop();
                await FitZoomAsync().ConfigureAwait(true);
            };

            Resize += (s, e) =>
            {
                LayoutStage();
                _fitTimer.Stop();
                _fitTimer.Start();
            };
            LayoutStage();
        }

        public static async Task ShowAsync(string key, string title, string url, DeckUrlResolver resolver)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("key 不能为空。", nameof(key));
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));

            DeckPreviewForm form;
            if (!OpenForms.TryGetValue(key, out form) || form.IsDisposed)
            {
                form = new DeckPreviewForm(key, resolver);
                OpenForms[key] = form;
                form.Show();
            }
            else
            {
                if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
                form.Activate();
            }

            form.Text = "Nixer 预览 — " + (string.IsNullOrWhiteSpace(title) ? key : title.Trim()) + "（← → 翻页）";
            await form.NavigateAsync(url).ConfigureAwait(true);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            DeckPreviewForm current;
            if (OpenForms.TryGetValue(_key, out current) && ReferenceEquals(current, this))
            {
                OpenForms.Remove(_key);
            }

            _fitTimer.Dispose();
            base.OnFormClosed(e);
        }

        private async Task NavigateAsync(string url)
        {
            if (_initialization == null || _initialization.IsFaulted)
            {
                _initialization = InitializeAsync();
            }

            await _initialization.ConfigureAwait(true);
            _view.CoreWebView2.Navigate(url);
        }

        private async Task InitializeAsync()
        {
            var environment = await RenderEnvironment.GetAsync().ConfigureAwait(true);
            await _view.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            _isolation.Attach(_view.CoreWebView2);
            _view.CoreWebView2.NavigationCompleted += async (s, e) => await PrepareViewAsync().ConfigureAwait(true);
        }

        private async Task PrepareViewAsync()
        {
            if (IsDisposed || _view.CoreWebView2 == null) return;
            try
            {
                await DeckRenderHost.HideScrollbarsAsync(_view.CoreWebView2).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // 页面切换或窗口关闭期间忽略，缩放适配仍继续。
            }

            await FitZoomAsync().ConfigureAwait(true);
        }

        private void LayoutStage()
        {
            var client = ClientSize;
            if (client.Width <= 0 || client.Height <= 0) return;
            var width = client.Width;
            var height = (int)Math.Round(width * 9.0 / 16.0);
            if (height > client.Height)
            {
                height = client.Height;
                width = (int)Math.Round(height * 16.0 / 9.0);
            }

            _stage.Bounds = new Rectangle((client.Width - width) / 2, (client.Height - height) / 2, width, height);
        }

        private async Task FitZoomAsync()
        {
            if (IsDisposed || _view.CoreWebView2 == null) return;
            try
            {
                // 缩放会改变 innerWidth，迭代两次即可收敛到 1920。
                for (var i = 0; i < 2; i++)
                {
                    var raw = await _view.CoreWebView2.ExecuteScriptAsync("window.innerWidth").ConfigureAwait(true);
                    double innerWidth;
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out innerWidth) || innerWidth <= 0)
                    {
                        return;
                    }

                    var zoom = _view.ZoomFactor * innerWidth / DeckAssembler.DesignWidth;
                    zoom = Math.Max(0.1, Math.Min(4.0, zoom));
                    if (Math.Abs(zoom - _view.ZoomFactor) < 0.002) return;
                    _view.ZoomFactor = zoom;
                }
            }
            catch (Exception)
            {
                // 窗口关闭或页面切换中，忽略本次适配。
            }
        }
    }
}
