using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;
using OfficeCopilot.Slides.Rendering;

namespace OfficeCopilot.Host.PowerPoint
{
    public sealed class DeckRenderCapture
    {
        public byte[] Png { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public IList<string> BlockedRequests { get; set; } = new List<string>();
    }

    /// <summary>幻灯片渲染服务：工具在后台线程调用，实现负责切到 UI 线程驱动 WebView2。</summary>
    public interface IDeckRenderService
    {
        /// <summary>截取 <paramref name="url"/>（须为 https://nixer-deck.local/）的 1920×1080 画面，按 scale 缩放输出 PNG。</summary>
        Task<DeckRenderCapture> CaptureAsync(string url, double scale, CancellationToken cancellationToken);

        /// <summary>采集单页 DOM 几何、整页 PNG 与隐藏原生元素后的背景 PNG。</summary>
        Task<DeckSlideCapture> CaptureSlideAsync(string url, int slide, string mode, CancellationToken cancellationToken);

        /// <summary>执行 L1 DOM 与 L2 PDF 分页检测，并为问题页返回截图。</summary>
        Task<DeckValidationCapture> ValidateAsync(string url, string validationUrl, string density, CancellationToken cancellationToken);

        /// <summary>打开（或刷新）非模态预览窗口。</summary>
        Task ShowPreviewAsync(string key, string title, string url);
    }

    /// <summary>
    /// 必须在 UI 线程构造：捕获当前 <see cref="SynchronizationContext"/>，之后通过 Post 把渲染工作切回 UI 线程。
    /// 截图串行执行（共用一个隐藏 WebView2）。
    /// </summary>
    public sealed class PowerPointDeckRenderService : IDeckRenderService, IDisposable
    {
        private const int CaptureTimeoutMilliseconds = 30000;

        private readonly SynchronizationContext _ui;
        private readonly DeckUrlResolver _resolver;
        private readonly FontPolicy _fontPolicy;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private DeckRenderHost _host;

        public PowerPointDeckRenderService()
            : this(SynchronizationContext.Current, SlideAssetPaths.CreateDefault(), DeckWorkspaceStore.GetDefaultRoot())
        {
        }

        public PowerPointDeckRenderService(SynchronizationContext ui, SlideAssetPaths paths, string decksRoot)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            _ui = ui ?? throw new InvalidOperationException("渲染服务必须在 UI 线程上创建。");
            _resolver = new DeckUrlResolver(paths.BuiltInRoot, paths.UserRoot, decksRoot);
            _fontPolicy = FontPolicy.FromCatalog(FontCatalog.Load(paths));
        }

        public async Task<DeckRenderCapture> CaptureAsync(string url, double scale, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RunOnUiAsync(async () =>
                {
                    if (_host == null) _host = new DeckRenderHost(_resolver, _fontPolicy);
                    var capture = await _host.CaptureAsync(url, scale, CaptureTimeoutMilliseconds).ConfigureAwait(true);
                    return new DeckRenderCapture
                    {
                        Png = capture.Png,
                        Width = capture.Width,
                        Height = capture.Height,
                        BlockedRequests = capture.BlockedRequests
                    };
                }).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<DeckSlideCapture> CaptureSlideAsync(string url, int slide, string mode, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RunOnUiAsync(async () =>
                {
                    if (_host == null) _host = new DeckRenderHost(_resolver, _fontPolicy);
                    return await _host.CaptureSlideAsync(url, slide, mode, CaptureTimeoutMilliseconds).ConfigureAwait(true);
                }).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task ShowPreviewAsync(string key, string title, string url)
        {
            return RunOnUiAsync(async () =>
            {
                await DeckPreviewForm.ShowAsync(key, title, url, _resolver).ConfigureAwait(true);
                return true;
            });
        }

        public async Task<DeckValidationCapture> ValidateAsync(string url, string validationUrl, string density, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RunOnUiAsync(async () =>
                {
                    if (_host == null) _host = new DeckRenderHost(_resolver, _fontPolicy);
                    return await _host.ValidateAsync(url, validationUrl, density, CaptureTimeoutMilliseconds).ConfigureAwait(true);
                }).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            var host = _host;
            _host = null;
            if (host == null) return;
            _ui.Post(_ => host.Dispose(), null);
        }

        private Task<T> RunOnUiAsync<T>(Func<Task<T>> work)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ui.Post(async _ =>
            {
                try
                {
                    completion.TrySetResult(await work().ConfigureAwait(true));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }, null);
            return completion.Task;
        }
    }
}
