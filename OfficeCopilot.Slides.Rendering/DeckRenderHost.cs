using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Slides.Rendering
{
    public sealed class DeckValidationIssue
    {
        public string Code { get; set; }

        public string Severity { get; set; }

        public int Slide { get; set; }

        public string Message { get; set; }

        public string Selector { get; set; }

        public string Text { get; set; }
    }

    public sealed class DeckValidationScreenshot
    {
        public int Slide { get; set; }

        public byte[] Png { get; set; }
    }

    public sealed class DeckValidationFontTarget
    {
        public string Id { get; set; }

        public int Slide { get; set; }

        public string DeclaredFamily { get; set; }

        public string Selector { get; set; }

        public string Text { get; set; }
    }

    public sealed class DeckValidationCapture
    {
        public int SlideCount { get; set; }

        public int PdfPageCount { get; set; }

        public bool PdfPaginationChecked { get; set; }

        public IList<DeckValidationFontTarget> FontTargets { get; set; } = new List<DeckValidationFontTarget>();

        public bool FontTargetsTruncated { get; set; }

        public double GlyphCoverageMilliseconds { get; set; }

        public IList<DeckValidationIssue> Issues { get; set; } = new List<DeckValidationIssue>();

        public bool Truncated { get; set; }

        public IList<DeckValidationScreenshot> Screenshots { get; set; } = new List<DeckValidationScreenshot>();
    }

    public sealed class DeckCapture
    {
        public byte[] Png { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public IList<string> BlockedRequests { get; set; } = new List<string>();
    }

    public sealed class DeckSlideCapture
    {
        public SlideCapture Data { get; set; }

        public byte[] SlidePng { get; set; }

        public byte[] BackgroundPng { get; set; }

        public IList<string> BlockedRequests { get; set; } = new List<string>();
    }

    /// <summary>
    /// 屏幕外的隐藏 WebView2，用 CDP 把 1920×1080 设计稿截成 PNG。
    /// 所有成员必须在 UI（STA）线程上调用，且一次只截一张。
    /// </summary>
    public sealed class DeckRenderHost : IDisposable
    {
        private const string ReadyScript =
            "(async () => {"
            + "try { await document.fonts.ready; } catch (e) {}"
            + "const pending = Array.from(document.images).filter(i => !i.complete)"
            + ".map(i => new Promise(r => { i.addEventListener('load', r); i.addEventListener('error', r); }));"
            + "await Promise.race([Promise.all(pending), new Promise(r => setTimeout(r, 3000))]);"
            + "await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));"
            + "return true; })()";

        private readonly RenderIsolation _isolation;
        private readonly FontPolicy _fontPolicy;
        private OffscreenForm _form;
        private WebView2 _view;
        private Task _initialization;
        private int _sequence;
        private bool _disposed;

        public DeckRenderHost(DeckUrlResolver resolver, FontPolicy fontPolicy)
        {
            _isolation = new RenderIsolation(resolver);
            _fontPolicy = fontPolicy ?? throw new ArgumentNullException(nameof(fontPolicy));
        }

        public async Task<DeckCapture> CaptureAsync(string url, double scale, int timeoutMilliseconds)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DeckRenderHost));
            if (scale <= 0 || scale > 2) throw new ArgumentOutOfRangeException(nameof(scale));

            await EnsureInitializedAsync().ConfigureAwait(true);
            var core = _view.CoreWebView2;
            _isolation.TakeBlocked();

            await PreparePageAsync(core, url, timeoutMilliseconds).ConfigureAwait(true);
            return await CaptureCurrentPageAsync(scale, timeoutMilliseconds).ConfigureAwait(true);
        }

        public async Task<DeckSlideCapture> CaptureSlideAsync(string url, int slide, string mode, int timeoutMilliseconds)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DeckRenderHost));
            if (slide < 1) throw new ArgumentOutOfRangeException(nameof(slide));
            if (!SlideBuildModes.IsValid(mode))
            {
                throw new ArgumentException("mode 必须是 fidelity、editable 或 image。", nameof(mode));
            }

            await EnsureInitializedAsync().ConfigureAwait(true);
            var core = _view.CoreWebView2;
            _isolation.TakeBlocked();
            await PreparePageAsync(core, url, timeoutMilliseconds).ConfigureAwait(true);

            var scriptUrl = DeckUrls.Lib("runtime/capture.js");
            var expression = "(async()=>{const r=await fetch(" + JsonConvert.SerializeObject(scriptUrl) + ");"
                + "if(!r.ok)throw new Error('无法加载本地 capture.js：'+r.status);"
                + "(0,eval)(await r.text());return await window.nixerCapture.read("
                + slide.ToString(CultureInfo.InvariantCulture) + "," + JsonConvert.SerializeObject(mode.ToLowerInvariant()) + ");})()";
            var value = await EvaluateCurrentAsync(core, expression, timeoutMilliseconds).ConfigureAwait(true);
            var capture = value == null ? null : value.ToObject<SlideCapture>();
            if (capture == null || capture.Width <= 0 || capture.Height <= 0 || capture.TextBlocks == null)
            {
                throw new InvalidOperationException("DOM 采集没有返回有效页面数据。");
            }
            if (capture.Slide != slide) throw new ArgumentOutOfRangeException(nameof(slide), "页面编号超出演示范围。");

            var blockedRequests = new List<string>(_isolation.TakeBlocked());
            var slideImage = await CaptureCurrentPageAsync(1, timeoutMilliseconds).ConfigureAwait(true);
            blockedRequests.AddRange(slideImage.BlockedRequests);
            await EvaluateCurrentAsync(core, "window.nixerCapture.setBackgroundMode(true)", timeoutMilliseconds).ConfigureAwait(true);
            DeckCapture background;
            try
            {
                background = await CaptureCurrentPageAsync(1, timeoutMilliseconds).ConfigureAwait(true);
                blockedRequests.AddRange(background.BlockedRequests);
            }
            finally
            {
                await EvaluateCurrentAsync(core, "window.nixerCapture.setBackgroundMode(false)", timeoutMilliseconds).ConfigureAwait(true);
            }

            return new DeckSlideCapture
            {
                Data = capture,
                SlidePng = slideImage.Png,
                BackgroundPng = background.Png,
                BlockedRequests = blockedRequests.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private async Task<DeckCapture> CaptureCurrentPageAsync(double scale, int timeoutMilliseconds)
        {
            if (scale <= 0 || scale > 2) throw new ArgumentOutOfRangeException(nameof(scale));
            const int width = DeckAssembler.DesignWidth;
            const int height = DeckAssembler.DesignHeight;
            var core = _view.CoreWebView2;

            var shot = await WithTimeout(
                CdpAsync(core, "Page.captureScreenshot", new JObject
                {
                    ["format"] = "png",
                    ["fromSurface"] = true,
                    ["clip"] = new JObject
                    {
                        ["x"] = 0,
                        ["y"] = 0,
                        ["width"] = width,
                        ["height"] = height,
                        ["scale"] = scale
                    }
                }),
                timeoutMilliseconds,
                "截图超时。").ConfigureAwait(true);

            var data = (string)shot["data"];
            if (string.IsNullOrEmpty(data)) throw new InvalidOperationException("截图结果为空。");

            return new DeckCapture
            {
                Png = Convert.FromBase64String(data),
                Width = (int)Math.Round(width * scale),
                Height = (int)Math.Round(height * scale),
                BlockedRequests = _isolation.TakeBlocked()
            };
        }

        public async Task<JToken> EvaluateAsync(string url, string expression, int timeoutMilliseconds)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DeckRenderHost));
            if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("expression 不能为空。", nameof(expression));

            await EnsureInitializedAsync().ConfigureAwait(true);
            var core = _view.CoreWebView2;
            _isolation.TakeBlocked();
            await PreparePageAsync(core, url, timeoutMilliseconds).ConfigureAwait(true);

            return await EvaluateCurrentAsync(core, expression, timeoutMilliseconds).ConfigureAwait(true);
        }

        private async Task<JToken> EvaluateCurrentAsync(CoreWebView2 core, string expression, int timeoutMilliseconds)
        {
            var response = await WithTimeout(
                CdpAsync(core, "Runtime.evaluate", new JObject
                {
                    ["expression"] = expression,
                    ["awaitPromise"] = true,
                    ["returnByValue"] = true
                }),
                timeoutMilliseconds,
                "页面脚本执行超时。").ConfigureAwait(true);

            var exception = response["exceptionDetails"];
            if (exception != null)
            {
                var detail = (string)exception["text"] ?? "验证脚本执行失败。";
                throw new InvalidOperationException(detail);
            }

            return response["result"] == null ? null : response["result"]["value"];
        }

        public async Task<DeckValidationCapture> ValidateAsync(string url, string validationUrl, string density, int timeoutMilliseconds)
        {
            var scriptUrl = DeckUrls.Lib("runtime/validate.js");
            var options = JsonConvert.SerializeObject(new
            {
                density = density,
                maxIssuesPerSlide = 30,
                safeArea = new { minX = 112, minY = 80, exceptionX = 80, exceptionY = 80 },
                fontPolicy = _fontPolicy
            });
            var expression = "(async()=>{const r=await fetch(" + JsonConvert.SerializeObject(scriptUrl) + ");"
                + "if(!r.ok)throw new Error('无法加载本地 validate.js：'+r.status);"
                + "(0,eval)(await r.text());return await window.nixerValidate(" + options + ");})()";
            var value = await EvaluateAsync(url, expression, timeoutMilliseconds).ConfigureAwait(true);
            var report = value == null ? null : value.ToObject<DeckValidationCapture>();
            if (report == null) throw new InvalidOperationException("DOM 验证没有返回报告。");

            await ValidateFontsAsync(report, timeoutMilliseconds).ConfigureAwait(true);
            await ValidatePaginationAsync(report, validationUrl, timeoutMilliseconds).ConfigureAwait(true);

            var issueSlides = report.Issues
                .Where(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Slide)
                .Distinct()
                .Where(i => i > 0 && i <= report.SlideCount)
                .Take(10)
                .ToList();
            foreach (var slide in issueSlides)
            {
                var slideUrl = WithQueryParameter(WithHash(url, slide), "mode", "capture");
                var screenshot = await CaptureAsync(slideUrl, 1.0 / 3.0, timeoutMilliseconds).ConfigureAwait(true);
                report.Screenshots.Add(new DeckValidationScreenshot { Slide = slide, Png = screenshot.Png });
            }

            return report;
        }

        private async Task ValidatePaginationAsync(DeckValidationCapture report, string validationUrl, int timeoutMilliseconds)
        {
            var core = _view.CoreWebView2;
            var pdf = await PrintPdfAsync(core, validationUrl, timeoutMilliseconds).ConfigureAwait(true);
            var pagination = PdfPaginationChecker.Inspect(pdf, report.SlideCount);
            report.PdfPageCount = pagination.PageCount;
            report.PdfPaginationChecked = true;
            if (pagination.PageCount == report.SlideCount) return;

            for (var slide = 1; slide <= report.SlideCount; slide++)
            {
                await SetOnlySlideAsync(core, slide, timeoutMilliseconds).ConfigureAwait(true);
                var isolatedPdf = await PrintCurrentPageAsync(core, timeoutMilliseconds).ConfigureAwait(true);
                var isolated = PdfPaginationChecker.Inspect(isolatedPdf, 1);
                if (isolated.PageCount <= 1) continue;

                var overflow = isolated.OverflowText.FirstOrDefault();
                report.Issues.Add(new DeckValidationIssue
                {
                    Code = "page.overflow",
                    Severity = "error",
                    Slide = slide,
                    Message = "页面打印为 " + isolated.PageCount + " 个 PDF 页面；溢出内容："
                        + (string.IsNullOrWhiteSpace(overflow) ? "（未能提取文字）" : overflow.Substring(0, Math.Min(180, overflow.Length)))
                });
            }

            if (!report.Issues.Any(i => i.Code == "page.overflow"))
            {
                report.Issues.Add(new DeckValidationIssue
                {
                    Code = "pagination.mismatch",
                    Severity = "error",
                    Slide = 0,
                    Message = "整份演示打印为 " + pagination.PageCount + " 页，预期 " + report.SlideCount + " 页；逐页检查未定位到单页溢出。"
                });
            }
        }

        private async Task ValidateFontsAsync(DeckValidationCapture report, int timeoutMilliseconds)
        {
            var targets = report.FontTargets;
            if (targets == null || targets.Count == 0) return;

            var core = _view.CoreWebView2;
            await CdpAsync(core, "DOM.enable", new JObject()).ConfigureAwait(true);
            await CdpAsync(core, "CSS.enable", new JObject()).ConfigureAwait(true);
            var document = await CdpAsync(core, "DOM.getDocument", new JObject { ["depth"] = -1 }).ConfigureAwait(true);
            var rootNodeId = (int?)document["root"]?["nodeId"];
            if (rootNodeId == null) throw new InvalidOperationException("无法读取验证页面 DOM。");

            var query = await CdpAsync(core, "DOM.querySelectorAll", new JObject
            {
                ["nodeId"] = rootNodeId.Value,
                ["selector"] = "[data-nx-validation-font]"
            }).ConfigureAwait(true);
            var nodeIds = query["nodeIds"] as JArray;
            if (nodeIds == null || nodeIds.Count != targets.Count)
            {
                throw new InvalidOperationException("字体验证目标与 DOM 节点数量不一致。");
            }

            var emojiFamilies = new HashSet<string>(
                _fontPolicy.Families.Where(pair => pair.Value.EmojiOnly).Select(pair => pair.Key),
                StringComparer.OrdinalIgnoreCase);
            foreach (var group in targets.Select((target, index) => new { target, index }).GroupBy(p => p.target.Slide))
            {
                var activate = "(async()=>{if(window.nixerStage&&window.nixerStage.go)window.nixerStage.go("
                    + group.Key.ToString(CultureInfo.InvariantCulture)
                    + ");await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)));return true;})()";
                await WithTimeout(
                    CdpAsync(core, "Runtime.evaluate", new JObject
                    {
                        ["expression"] = activate,
                        ["awaitPromise"] = true,
                        ["returnByValue"] = true
                    }),
                    timeoutMilliseconds,
                    "字体验证页面切换超时。").ConfigureAwait(true);

                foreach (var item in group)
                {
                    var result = await CdpAsync(core, "CSS.getPlatformFontsForNode", new JObject
                    {
                        ["nodeId"] = (int)nodeIds[item.index]
                    }).ConfigureAwait(true);
                    var platformFonts = result["fonts"] as JArray;
                    if (platformFonts == null) continue;

                    var declared = ParseFontFamilies(item.target.DeclaredFamily);
                    var actual = platformFonts
                        .Where(font => (int?)font["glyphCount"] > 0)
                        .Select(font => ((string)font["familyName"] ?? string.Empty).Trim())
                        .Where(name => name.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var unexpected = actual.Where(name =>
                        !declared.Any(declaredName => _fontPolicy.AreEquivalentFamilyNames(declaredName, name))
                        && !emojiFamilies.Contains(name)).ToList();
                    if (declared.Count == 0 || unexpected.Count == 0) continue;

                    report.Issues.Add(new DeckValidationIssue
                    {
                        Code = "font.fallback",
                        Severity = "error",
                        Slide = item.target.Slide,
                        Selector = item.target.Selector,
                        Text = item.target.Text,
                        Message = "声明字体为 " + string.Join(", ", declared) + "，实际使用 " + string.Join(", ", unexpected) + "。"
                    });
                }
            }
        }

        private static List<string> ParseFontFamilies(string fontFamily)
        {
            return (fontFamily ?? string.Empty)
                .Split(',')
                .Select(name => name.Trim().Trim('"', '\''))
                .Where(name => name.Length > 0 && !FontPolicy.IsGenericFamily(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<byte[]> PrintPdfAsync(CoreWebView2 core, string url, int timeoutMilliseconds)
        {
            await PreparePageAsync(core, url, timeoutMilliseconds).ConfigureAwait(true);
            return await PrintCurrentPageAsync(core, timeoutMilliseconds).ConfigureAwait(true);
        }

        private async Task<byte[]> PrintCurrentPageAsync(CoreWebView2 core, int timeoutMilliseconds)
        {
            await CdpAsync(core, "Emulation.setEmulatedMedia", new JObject { ["media"] = "print" }).ConfigureAwait(true);
            try
            {
                try
                {
                    var result = await WithTimeout(
                        CdpAsync(core, "Page.printToPDF", new JObject
                        {
                            ["preferCSSPageSize"] = true,
                            ["printBackground"] = true,
                            ["displayHeaderFooter"] = false,
                            ["paperWidth"] = 20,
                            ["paperHeight"] = 11.25,
                            ["marginTop"] = 0,
                            ["marginBottom"] = 0,
                            ["marginLeft"] = 0,
                            ["marginRight"] = 0
                        }),
                        timeoutMilliseconds,
                        "PDF 分页检查超时。").ConfigureAwait(true);
                    var data = (string)result["data"];
                    if (string.IsNullOrWhiteSpace(data)) throw new InvalidOperationException("PDF 分页检查没有返回数据。");
                    return Convert.FromBase64String(data);
                }
                catch (Exception cdpError)
                {
                    try
                    {
                        return await PrintToPdfFallbackAsync(core, timeoutMilliseconds).ConfigureAwait(true);
                    }
                    catch (Exception fallbackError)
                    {
                        throw new InvalidOperationException(
                            "CDP 与 WebView2 PDF 打印均失败。",
                            new AggregateException(cdpError, fallbackError));
                    }
                }
            }
            finally
            {
                await CdpAsync(core, "Emulation.setEmulatedMedia", new JObject { ["media"] = "screen" }).ConfigureAwait(true);
            }
        }

        private static async Task<byte[]> PrintToPdfFallbackAsync(CoreWebView2 core, int timeoutMilliseconds)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nixer-validate-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                var settings = core.Environment.CreatePrintSettings();
                settings.PageWidth = 20;
                settings.PageHeight = 11.25;
                settings.MarginTop = 0;
                settings.MarginBottom = 0;
                settings.MarginLeft = 0;
                settings.MarginRight = 0;
                settings.ShouldPrintBackgrounds = true;
                settings.ShouldPrintHeaderAndFooter = false;
                var printed = await WithTimeout(core.PrintToPdfAsync(path, settings), timeoutMilliseconds, "WebView2 PDF 回退打印超时.")
                    .ConfigureAwait(true);
                if (!printed || !System.IO.File.Exists(path)) throw new InvalidOperationException("WebView2 PDF 回退打印未生成文件。");
                return System.IO.File.ReadAllBytes(path);
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                }
                catch (Exception)
                {
                }
            }
        }

        private async Task SetOnlySlideAsync(CoreWebView2 core, int slide, int timeoutMilliseconds)
        {
            var expression = "(()=>{const slides=Array.from(document.querySelectorAll('.slide'));"
                + "slides.forEach((item,index)=>item.classList.toggle('nx-only',index===" + (slide - 1).ToString(CultureInfo.InvariantCulture) + "));"
                + "document.body.setAttribute('data-only','true');return slides.length;})()";
            var response = await WithTimeout(
                CdpAsync(core, "Runtime.evaluate", new JObject { ["expression"] = expression, ["returnByValue"] = true }),
                timeoutMilliseconds,
                "隔离页面失败。").ConfigureAwait(true);
            var count = (int?)response["result"]?["value"];
            if (count == null || slide > count.Value) throw new InvalidOperationException("验证文档中的页面数量发生变化。");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
        }

        private Task EnsureInitializedAsync()
        {
            if (_initialization == null || _initialization.IsFaulted || _initialization.IsCanceled)
            {
                Reset();
                _initialization = InitializeAsync();
            }

            return _initialization;
        }

        private async Task PreparePageAsync(CoreWebView2 core, string url, int timeoutMilliseconds)
        {
            const int width = DeckAssembler.DesignWidth;
            const int height = DeckAssembler.DesignHeight;
            await CdpAsync(core, "Emulation.setDeviceMetricsOverride", new JObject
            {
                ["width"] = width,
                ["height"] = height,
                ["deviceScaleFactor"] = 1,
                ["mobile"] = false
            }).ConfigureAwait(true);
            await CdpAsync(core, "Emulation.setEmulatedMedia", new JObject { ["media"] = "screen" }).ConfigureAwait(true);

            var navigated = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = (s, e) => navigated.TrySetResult(e);
            core.NavigationCompleted += handler;
            CoreWebView2NavigationCompletedEventArgs result;
            try
            {
                core.Navigate(WithNonce(url));
                result = await WithTimeout(navigated.Task, timeoutMilliseconds, "页面加载超时。").ConfigureAwait(true);
            }
            finally
            {
                core.NavigationCompleted -= handler;
            }

            if (!result.IsSuccess)
            {
                throw new InvalidOperationException("页面加载失败：" + result.WebErrorStatus + "。");
            }

            await HideScrollbarsAsync(core).ConfigureAwait(true);
            await WithTimeout(
                CdpAsync(core, "Runtime.evaluate", new JObject
                {
                    ["expression"] = ReadyScript,
                    ["awaitPromise"] = true,
                    ["returnByValue"] = true
                }),
                timeoutMilliseconds,
                "等待字体与图片超时。").ConfigureAwait(true);
        }

        private async Task InitializeAsync()
        {
            _form = new OffscreenForm();
            _view = new WebView2 { Dock = DockStyle.Fill };
            _form.Controls.Add(_view);
            _form.Show();

            var environment = await RenderEnvironment.GetAsync().ConfigureAwait(true);
            await _view.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            _isolation.Attach(_view.CoreWebView2);
            _view.CoreWebView2.ProcessFailed += (s, e) => _initialization = null;
        }

        private void Reset()
        {
            var view = _view;
            var form = _form;
            _view = null;
            _form = null;
            _initialization = null;
            try
            {
                view?.Dispose();
            }
            catch (Exception)
            {
                // WebView2 进程已退出时 Dispose 可能抛出，忽略。
            }

            try
            {
                form?.Close();
                form?.Dispose();
            }
            catch (Exception)
            {
                // 同上。
            }
        }

        private string WithNonce(string url)
        {
            // 每次都改 query，避免只差 hash 时变成同文档导航而收不到 NavigationCompleted。
            var hashIndex = url.IndexOf('#');
            var main = hashIndex >= 0 ? url.Substring(0, hashIndex) : url;
            var hash = hashIndex >= 0 ? url.Substring(hashIndex) : string.Empty;
            var separator = main.IndexOf('?') >= 0 ? "&" : "?";
            _sequence++;
            return main + separator + "nxr=" + _sequence.ToString(CultureInfo.InvariantCulture) + hash;
        }

        private static string WithHash(string url, int slide)
        {
            var index = url.IndexOf('#');
            var main = index >= 0 ? url.Substring(0, index) : url;
            return main + "#" + slide.ToString(CultureInfo.InvariantCulture);
        }

        private static string WithQueryParameter(string url, string name, string value)
        {
            var hashIndex = url.IndexOf('#');
            var main = hashIndex >= 0 ? url.Substring(0, hashIndex) : url;
            var hash = hashIndex >= 0 ? url.Substring(hashIndex) : string.Empty;
            var separator = main.IndexOf('?') >= 0 ? "&" : "?";
            return main + separator + Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value) + hash;
        }

        /// <summary>
        /// 关闭原生滚动条，避免滚动条被画进截图（背景图）或预览窗口边缘。
        /// 舞台已按视口等比缩放，隐藏后不影响内容显示。
        /// </summary>
        internal static Task<JObject> HideScrollbarsAsync(CoreWebView2 core)
        {
            return CdpAsync(core, "Emulation.setScrollbarsHidden", new JObject { ["hidden"] = true });
        }

        private static async Task<JObject> CdpAsync(CoreWebView2 core, string method, JObject parameters)
        {
            string json;
            try
            {
                json = await core.CallDevToolsProtocolMethodAsync(method, parameters.ToString(Formatting.None)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Runtime.InteropServices.COMException)
            {
                // WebView2 把 CDP 错误统一报成 E_INVALIDARG，没有方法名就无法定位。
                throw new InvalidOperationException("CDP " + method + " 调用失败：" + ex.Message, ex);
            }

            return string.IsNullOrEmpty(json) ? new JObject() : JObject.Parse(json);
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int milliseconds, string message)
        {
            var finished = await Task.WhenAny(task, Task.Delay(milliseconds)).ConfigureAwait(true);
            if (finished != task) throw new TimeoutException(message);
            return await task.ConfigureAwait(true);
        }

        private sealed class OffscreenForm : Form
        {
            private const int WsExToolWindow = 0x00000080;
            private const int WsExNoActivate = 0x08000000;

            public OffscreenForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-20000, -20000);
                Size = new Size(640, 360);
                Text = "Nixer Slide Renderer";
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var parameters = base.CreateParams;
                    parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
                    return parameters;
                }
            }
        }
    }
}
