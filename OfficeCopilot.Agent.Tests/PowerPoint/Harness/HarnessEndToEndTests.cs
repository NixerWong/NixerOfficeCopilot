using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.PowerPoint;
using OfficeCopilot.Host.PowerPoint.Tools;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Catalog;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;
using OfficeCopilot.Slides.Rendering;

namespace OfficeCopilot.Agent.Tests.PowerPoint.Harness
{
    [TestClass]
    public sealed class HarnessEndToEndTests
    {
        private const double FontWidthTolerancePercent = 3.0;

        private static readonly string[] RepresentativeStyleSlugs =
        {
            "daisy-days",
            "vellum",
            "terminal-green",
            "dark-botanical",
            "studio",
            "bold-signal"
        };

        private static readonly HashSet<string> AdditionalSafeAreaTemplateSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "8-bit-orbit",
            "biennale-yellow",
            "blue-professional",
            "capsule",
            "cartesian",
            "coral",
            "creative-mode",
            "daisy-days",
            "editorial-forest",
            "emerald-editorial",
            "grove",
            "long-table",
            "monochrome",
            "playful",
            "raw-grid",
            "retro-windows",
            "retro-zine",
            "sakura-chroma",
            "scatterbrain",
            "signal",
            "vellum"
        };

        [TestMethod]
        [TestCategory("Harness")]
        public async Task RunFullHarness_WhenExplicitlyEnabled()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("NIXER_PPT_HARNESS"), "1", StringComparison.Ordinal))
            {
                Assert.Inconclusive("Set NIXER_PPT_HARNESS=1 to launch WebView2 and PowerPoint for the full harness.");
            }

            var assets = FindShippedAssets();
            Assert.IsNotNull(assets, "Could not locate SlideAssets.");
            var output = Path.Combine(Path.GetTempPath(), "nixer-harness", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);

            try
            {
                await RunOnStaUiThread(async () =>
                {
                    await RunHarnessAsync(assets, output).ConfigureAwait(true);
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Assert.Fail(exception.ToString());
            }
        }

        [TestMethod]
        [TestCategory("Harness")]
        public async Task RunTemplateMarginInventory_WhenExplicitlyEnabled()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("NIXER_PPT_HARNESS"), "1", StringComparison.Ordinal))
            {
                Assert.Inconclusive("Set NIXER_PPT_HARNESS=1 to launch WebView2 for the template margin inventory.");
            }

            var assets = FindShippedAssets();
            Assert.IsNotNull(assets, "Could not locate SlideAssets.");
            var output = Path.Combine(Path.GetTempPath(), "nixer-harness-inventory", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);

            try
            {
                await RunOnStaUiThread(async () =>
                {
                    await RunTemplateMarginInventoryAsync(assets, output).ConfigureAwait(true);
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Assert.Fail(exception.ToString());
            }
        }

        [TestMethod]
        [TestCategory("Harness")]
        public async Task RunTemplateGallerySafeAreaInventory_WhenExplicitlyEnabled()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("NIXER_PPT_HARNESS"), "1", StringComparison.Ordinal))
            {
                Assert.Inconclusive("Set NIXER_PPT_HARNESS=1 to render original template galleries for the safe-area inventory.");
            }

            var assets = FindShippedAssets();
            Assert.IsNotNull(assets, "Could not locate SlideAssets.");
            var output = Path.Combine(Path.GetTempPath(), "nixer-harness-gallery-inventory", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);

            try
            {
                await RunOnStaUiThread(async () =>
                {
                    await RunTemplateGallerySafeAreaInventoryAsync(assets, output).ConfigureAwait(true);
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Assert.Fail(exception.ToString());
            }
        }

        [TestMethod]
        [TestCategory("Harness")]
        public async Task RunPreviewRoundTrip_WhenExplicitlyEnabled()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("NIXER_PPT_HARNESS"), "1", StringComparison.Ordinal))
            {
                Assert.Inconclusive("Set NIXER_PPT_HARNESS=1 to launch WebView2 for the preview round-trip harness.");
            }

            var assets = FindShippedAssets();
            Assert.IsNotNull(assets, "Could not locate SlideAssets.");
            var output = Path.Combine(Path.GetTempPath(), "nixer-harness-preview-roundtrip", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);

            try
            {
                await RunOnStaUiThread(async () =>
                {
                    await RunPreviewRoundTripAsync(assets, output).ConfigureAwait(true);
                }).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Assert.Fail(exception.ToString());
            }
        }

        private static async Task RunPreviewRoundTripAsync(string assets, string output)
        {
            if (SynchronizationContext.Current == null)
            {
                throw new InvalidOperationException("The preview round-trip UI thread has no Windows Forms synchronization context.");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(output, "user-assets"));
            var catalogs = new SlideAssetCatalogs(paths);
            var store = new DeckWorkspaceStore(Path.Combine(output, "decks"));
            var renderer = new PowerPointDeckRenderService(SynchronizationContext.Current, paths, store.Root);
            try
            {
                var services = new AgentServiceProvider();
                services.AddService<IDeckRenderService>(renderer);
                var context = new ToolExecutionContext { Services = services };
                var preview = await new PowerPointStylePreviewTool(() => store, () => catalogs).ExecuteAsync(
                    context,
                    new ToolExecutionRequest
                    {
                        ToolCallId = "f9-preview",
                        ArgumentsJson = "{\"candidates\":["
                            + "{\"name\":\"chosen\",\"style\":\"daisy-days\",\"html\":\"<section class='slide'><h1 class='f9-title'>PPT Harness Preview 😀 👩🏽‍🚀 🇨🇳 1️⃣ 🫠 🩷 🫶 <i class='nx-icon' data-icon='leaf'></i></h1><p class='f9-probe'>Unmapped "
                            + char.ConvertFromUtf32(0x10FFFF)
                            + " unsupported emoji "
                            + char.ConvertFromUtf32(0x1F02C)
                            + "</p><div class='f9-cjk'>中文字体采样</div><span class='f9-pseudo'></span><span style='display:flex;align-items:center;justify-content:center;width:56px;height:56px;font-size:28px'>1</span></section>\",\"css\":\".f9-title{font-family:'Smiley Sans','Noto Sans SC';font-size:72px;color:#123456}.f9-probe{font-family:'Smiley Sans','Noto Sans SC';font-size:24px;color:#123456}.f9-cjk{font-family:'Space Grotesk','Noto Serif SC',serif;font-size:24px;font-weight:600;color:#123456}.f9-pseudo::before{content:'"
                            + char.ConvertFromUtf32(0x10FFFF)
                            + "';font-family:'Smiley Sans','Noto Sans SC';font-size:24px;color:#123456}\"},"
                            + "{\"name\":\"alternate-one\",\"style\":\"vellum\",\"html\":\"<h1>Alternate one</h1>\"},"
                            + "{\"name\":\"alternate-two\",\"style\":\"studio\",\"html\":\"<h1>Alternate two</h1>\"}]}"
                    });
                if (!preview.Success)
                {
                    throw new InvalidOperationException(
                        "Style preview failed (" + preview.ErrorCode + "): " + preview.ErrorMessage + " " + preview.Content);
                }

                var deckId = Path.GetFileName(Directory.GetDirectories(store.Root).Single());
                var workspace = store.Open(deckId);
                var panelPreviewPath = workspace.PreviewPath("chosen", ".png");
                var cover = await new PowerPointDeckCreateTool(() => store, () => catalogs).ExecuteAsync(
                    new ToolExecutionContext(),
                    new ToolExecutionRequest
                    {
                        ToolCallId = "f9-create",
                        ArgumentsJson = "{\"deckId\":\"" + deckId + "\",\"fromPreview\":\"chosen\",\"title\":\"F9 Round Trip\"}"
                    });
                if (!cover.Success)
                {
                    throw new InvalidOperationException(
                        "Creating a deck from the selected preview failed (" + cover.ErrorCode + "): "
                            + cover.ErrorMessage + Environment.NewLine + cover.Content);
                }

                workspace = store.Open(deckId);
                StyleEntry style;
                if (!catalogs.Styles.TryGet(workspace.Manifest.Style, out style))
                {
                    throw new InvalidOperationException("The selected preview style could not be resolved: " + workspace.Manifest.Style);
                }

                var options = new DeckAssemblyOptions
                {
                    DeckId = workspace.Id,
                    Style = style,
                    Slides = workspace.GetAllSlideHtml(),
                    ThemeCss = workspace.ReadThemeCss(),
                    Language = workspace.Manifest.Language,
                    Title = workspace.Manifest.Title
                };
                workspace.WriteBuildFile("deck.html", DeckAssembler.Assemble(options));
                workspace.WriteBuildFile("validate.html", DeckAssembler.AssembleForValidation(options));
                var deckUrl = DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture#1";
                var slideCapture = await renderer.CaptureSlideAsync(deckUrl, 1, "editable", CancellationToken.None).ConfigureAwait(true);
                var marker = slideCapture.Data.TextBlocks.SingleOrDefault(block => block.Text == "1");
                if (marker == null
                    || marker.TextAlign != "center"
                    || marker.VerticalAlign != "middle"
                    || marker.Rectangle == null
                    || Math.Abs(marker.Rectangle.Width - 56) > 0.5
                    || Math.Abs(marker.Rectangle.Height - 56) > 0.5)
                {
                    throw new InvalidOperationException("Flex-centered slide labels should preserve their 56-by-56 geometry and horizontal/vertical alignment in native text capture.");
                }

                var overlays = slideCapture.Data.Images.Where(image => image.OverlayOnText).ToList();
                    if (overlays.Count != 9 || overlays.Any(image => !image.Source.StartsWith("data:image/png;base64,", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Emoji and Lucide icon should each be captured as an independent transparent PNG overlay.");
                }
                    var overlayIndex = 0;
                    foreach (var overlay in overlays)
                    {
                        var png = Convert.FromBase64String(overlay.Source.Substring("data:image/png;base64,".Length));
                        using (var stream = new MemoryStream(png))
                        using (var bitmap = new System.Drawing.Bitmap(stream))
                        {
                            var hasVisiblePixel = false;
                            var hasTransparentPixel = false;
                            var visibleColors = new HashSet<int>();
                            for (var y = 0; y < bitmap.Height; y++)
                            {
                                for (var x = 0; x < bitmap.Width; x++)
                                {
                                    var pixel = bitmap.GetPixel(x, y);
                                    var alpha = pixel.A;
                                    hasVisiblePixel |= alpha > 0;
                                    hasTransparentPixel |= alpha < 255;
                                    if (alpha > 64)
                                    {
                                        visibleColors.Add((pixel.R << 16) | (pixel.G << 8) | pixel.B);
                                    }
                                }
                            }

                            if (!hasVisiblePixel || !hasTransparentPixel)
                            {
                                throw new InvalidOperationException("Captured emoji and icon overlays must contain visible content and transparent pixels.");
                            }
                            if (overlayIndex >= 4 && overlayIndex <= 6 && visibleColors.Count < 3)
                            {
                                throw new InvalidOperationException("Recent emoji overlays must use the bundled color emoji font instead of a monochrome missing-glyph box.");
                            }
                        }
                        overlayIndex++;
                    }
                    if (!slideCapture.Data.TextBlocks.Any(block =>
                        {
                            var text = block.Text ?? string.Empty;
                            return text.Contains("PPT Harness Preview")
                                && !text.Contains("😀")
                                && !text.Contains("👩🏽‍🚀")
                                && !text.Contains("🇨🇳")
                                && !text.Contains("1️⃣")
                                && !text.Contains("🫠")
                                && !text.Contains("🩷")
                                && !text.Contains("🫶");
                    }))
                {
                    throw new InvalidOperationException("Text surrounding emoji sequences and icon should remain a native editable text block.");
                }
                var validationUrl = DeckUrls.DeckBase(workspace.Id) + "build/validate.html";
                var validation = await renderer.ValidateAsync(deckUrl, validationUrl, "medium", CancellationToken.None).ConfigureAwait(true);
                var classNamespaceOptions = new DeckAssemblyOptions
                {
                    DeckId = workspace.Id,
                    Style = style,
                    Slides = new List<string>
                    {
                        "<section class=\"slide\"><style data-nx-cover>.d1{font-size:150px}</style><div class=\"nx-content\"><span class=\"cover-d1 d1\">Cover</span></div></section>",
                        "<section class=\"slide\"><div class=\"nx-content\"><span class=\"day d1 theme-shared card\">Day 1</span></div></section>"
                    },
                    ThemeCss = ".cover-d1{font-size:32px}.theme-shared{font-size:24px}.card{padding:8px}",
                    Language = workspace.Manifest.Language,
                    Title = workspace.Manifest.Title
                };
                workspace.WriteBuildFile("class-namespace.html", DeckAssembler.Assemble(classNamespaceOptions));
                workspace.WriteBuildFile("class-namespace-validate.html", DeckAssembler.AssembleForValidation(classNamespaceOptions));
                var classNamespaceValidation = await renderer.ValidateAsync(
                    DeckUrls.DeckBase(workspace.Id) + "build/class-namespace.html?mode=capture#1",
                    DeckUrls.DeckBase(workspace.Id) + "build/class-namespace-validate.html",
                    "medium",
                    CancellationToken.None).ConfigureAwait(true);
                if (!classNamespaceValidation.Issues.Any(issue =>
                        issue.Code == "theme.class_collision"
                        && issue.Slide == 2
                        && (issue.Message ?? string.Empty).Contains(".d1 "))
                    || classNamespaceValidation.Issues.Any(issue =>
                        issue.Code == "theme.class_collision"
                        && ((issue.Message ?? string.Empty).Contains(".theme-shared ")
                            || (issue.Message ?? string.Empty).Contains(".card "))))
                {
                    throw new InvalidOperationException(
                        "Validation should reject a cover-page class on a content slide and allow classes defined in theme.css.");
                }

                var glyphIssues = validation.Issues.Where(issue => issue.Code == "glyph.missing").ToList();
                var fontFallbackIssues = validation.Issues.Where(issue => issue.Code == "font.fallback").ToList();
                if (fontFallbackIssues.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Bundled Smiley Sans must not be reported as a fallback when Chromium reports its registered platform alias: "
                            + string.Join(" | ", fontFallbackIssues.Select(issue => issue.Message)));
                }
                if (!validation.FontTargets.Any(target => target.Selector == "div.f9-cjk")
                    || validation.Issues.Any(issue => issue.Code == "font.weight_unavailable"
                        && issue.Selector == "div.f9-cjk"
                        && issue.Message.Contains("Space Grotesk")))
                {
                    throw new InvalidOperationException(
                        "Font fallback sampling should include plain div text and validate its CJK face rather than the unused Latin primary.");
                }

                if (!glyphIssues.Any(issue => issue.Message.Contains("U+10FFFF"))
                    || !glyphIssues.Any(issue => issue.Message.Contains("U+10FFFF")
                        && (issue.Selector ?? string.Empty).EndsWith("::before", StringComparison.Ordinal))
                    || !glyphIssues.Any(issue => issue.Message.Contains("U+1F02C")
                        && issue.Message.Contains("语义相近且随包 emoji 字体覆盖"))
                    || glyphIssues.Any(issue => new[] { "😀", "👩🏽‍🚀", "🇨🇳", "1️⃣", "🫠", "🩷", "🫶" }
                        .Any(emoji => (issue.Text ?? string.Empty).Contains(emoji))))
                {
                    throw new InvalidOperationException("Glyph validation should flag the unsupported probe code point without reporting bundled emoji sequences.");
                }

                var performanceSlide = workspace.GetAllSlideHtml().Single();
                var performanceOptions = new DeckAssemblyOptions
                {
                    DeckId = workspace.Id,
                    Style = style,
                    Slides = Enumerable.Repeat(performanceSlide, 20).ToList(),
                    ThemeCss = workspace.ReadThemeCss(),
                    Language = workspace.Manifest.Language,
                    Title = workspace.Manifest.Title
                };
                workspace.WriteBuildFile("glyph-performance.html", DeckAssembler.Assemble(performanceOptions));
                workspace.WriteBuildFile("glyph-performance-validate.html", DeckAssembler.AssembleForValidation(performanceOptions));
                var performanceDeckUrl = DeckUrls.DeckBase(workspace.Id) + "build/glyph-performance.html?mode=capture#1";
                var performanceValidationUrl = DeckUrls.DeckBase(workspace.Id) + "build/glyph-performance-validate.html";
                var performanceReport = await renderer.ValidateAsync(
                    performanceDeckUrl,
                    performanceValidationUrl,
                    "medium",
                    CancellationToken.None).ConfigureAwait(true);
                if (performanceReport.SlideCount != 20
                    || !(performanceReport.GlyphCoverageMilliseconds > 0)
                    || performanceReport.GlyphCoverageMilliseconds >= 300)
                {
                    throw new InvalidOperationException(
                        "Glyph coverage checks should process 20 slides in less than 300 ms; processed "
                        + performanceReport.SlideCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " slides and measured "
                        + performanceReport.GlyphCoverageMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                        + " ms.");
                }

                var deckCapture = await renderer.CaptureAsync(deckUrl, 2.0 / 3.0, CancellationToken.None).ConfigureAwait(true);
                var deckCapturePath = Path.Combine(output, "deck-cover.png");
                File.WriteAllBytes(deckCapturePath, deckCapture.Png);
                var panelPreview = File.ReadAllBytes(panelPreviewPath);
                File.WriteAllBytes(Path.Combine(output, "panel-preview.png"), panelPreview);

                double similarity;
                using (var previewImage = new System.Drawing.Bitmap(new MemoryStream(panelPreview)))
                using (var deckImage = new System.Drawing.Bitmap(new MemoryStream(deckCapture.Png)))
                {
                    if (previewImage.Width != deckImage.Width || previewImage.Height != deckImage.Height)
                    {
                        throw new InvalidOperationException("Panel preview and deck capture dimensions differ.");
                    }

                    similarity = HarnessImageMetrics.StructuralSimilarity(previewImage, deckImage);
                }

                File.WriteAllText(
                    Path.Combine(output, "preview-roundtrip.txt"),
                    "Preview/deck cover SSIM: " + similarity.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                    new UTF8Encoding(false));
                Assert.IsTrue(
                    similarity >= 0.98,
                    "Selected panel preview and the first deck page must reach SSIM >= 0.98; actual "
                        + similarity.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
                        + ". Evidence: " + output);
            }
            finally
            {
                renderer.Dispose();
            }
        }

        private static async Task RunHarnessAsync(string assets, string output)
        {
            if (SynchronizationContext.Current == null)
            {
                throw new InvalidOperationException("The harness UI thread has no Windows Forms synchronization context.");
            }

            var decksRoot = Path.Combine(output, "decks");
            var userAssets = Path.Combine(output, "user-assets");
            var paths = new SlideAssetPaths(assets, userAssets);
            var store = new DeckWorkspaceStore(decksRoot);
            var fonts = FontCatalog.Load(paths);
            var supportedFontRows = new HashSet<string>(
                fonts.Families
                    .Where(family => !family.EmojiOnly)
                    .SelectMany(family => family.Faces.Select(face => FontMatrixRowText(family.Family, face.Weight, face.Style))),
                StringComparer.Ordinal);
            var catalog = StyleCatalog.Load(paths, fonts);
            var fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerPoint", "Fixtures", "Harness");
            var slideHtml = new List<string>
            {
                ReadFixture(fixtureRoot, "cover.html"),
                ReadFixture(fixtureRoot, "body.html"),
                ReadFixture(fixtureRoot, "glyphs.html"),
                ReadFixture(fixtureRoot, "safe-area.html")
            };
            var fontMatrixPages = HarnessFixtureTests.BuildFontMatrixFromAssets(assets);
            var fontMatrixFirstSlide = slideHtml.Count + 1;
            slideHtml.AddRange(fontMatrixPages);
            var renderer = new PowerPointDeckRenderService(SynchronizationContext.Current, paths, store.Root);
            object application = null;
            try
            {
                var applicationType = Type.GetTypeFromProgID("PowerPoint.Application");
                if (applicationType == null)
                {
                    throw new InvalidOperationException("PowerPoint.Application COM registration was not found.");
                }

                application = Activator.CreateInstance(applicationType);
                applicationType.InvokeMember("Visible", BindingFlags.SetProperty, null, application, new object[] { -1 });

                var buildService = new PowerPointDeckBuildService(
                    new PowerPointHostAdapter(application),
                    renderer,
                    paths,
                    store.Root);
                buildService.FontInstaller = PowerPointFontInstaller.CreateDefault(paths);
                var results = new List<StyleHarnessResult>();
                foreach (var styleSlug in RepresentativeStyleSlugs)
                {
                    StyleEntry style;
                    if (!catalog.TryGet(styleSlug, out style))
                    {
                        throw new InvalidOperationException("The " + styleSlug + " harness style is missing.");
                    }

                    var workspace = store.Create(style.Slug, "medium", "zh-CN", "PPT Harness Baseline");
                    workspace.PutSlides(slideHtml.ConvertAll(html => new DeckSlideInput { Html = html }));
                    var options = new DeckAssemblyOptions
                    {
                        DeckId = workspace.Id,
                        Style = style,
                        Slides = workspace.Slides.Select(slide => workspace.GetSlideHtml(slide.Id)).ToList(),
                        ThemeCss = ".harness-cover{font-family:'Smiley Sans','Noto Sans SC',sans-serif}"
                            + ".harness-cover__title{font-weight:400}"
                            + ".harness-body{font-family:'Inter','Noto Sans SC',sans-serif}"
                            + ".harness-font-matrix{box-sizing:border-box;padding:120px 128px}"
                            + ".harness-font-matrix h1{font-size:36px;line-height:1.2;margin:0 0 18px}"
                            + ".harness-font-matrix .font-row{font-size:22px;line-height:1.25;margin:4px 0}",
                        Language = workspace.Manifest.Language,
                        Title = workspace.Manifest.Title
                    };
                    workspace.WriteThemeCss(options.ThemeCss);
                    options.ThemeCss = workspace.ReadThemeCss();
                    workspace.WriteBuildFile("deck.html", DeckAssembler.Assemble(options));
                    workspace.WriteBuildFile("validate.html", DeckAssembler.AssembleForValidation(options));

                    var deckUrl = DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture#1";
                    var validationUrl = DeckUrls.DeckBase(workspace.Id) + "build/validate.html";
                    var preview = await renderer.CaptureAsync(deckUrl, 1.0, CancellationToken.None).ConfigureAwait(true);
                    File.WriteAllBytes(Path.Combine(output, "panel-preview-" + styleSlug + ".png"), preview.Png);
                    var validation = await renderer.ValidateAsync(deckUrl, validationUrl, "medium", CancellationToken.None).ConfigureAwait(true);
                    foreach (var screenshot in validation.Screenshots)
                    {
                        File.WriteAllBytes(
                            Path.Combine(output, styleSlug + "-validation-s" + screenshot.Slide.ToString("D2") + ".png"),
                            screenshot.Png);
                    }

                    var fontCaptures = new Dictionary<int, DeckSlideCapture>();
                    for (var slideIndex = fontMatrixFirstSlide;
                        slideIndex < fontMatrixFirstSlide + fontMatrixPages.Count;
                        slideIndex++)
                    {
                        fontCaptures.Add(
                            slideIndex,
                            await renderer.CaptureSlideAsync(
                                deckUrl,
                                slideIndex,
                                SlideBuildModes.Fidelity,
                                CancellationToken.None).ConfigureAwait(true));
                    }

                    var build = await buildService.BuildAsync(
                        workspace,
                        deckUrl,
                        SlideBuildModes.Fidelity,
                        CancellationToken.None,
                        new PowerPointDeckBuildTarget { Kind = PowerPointDeckBuildTarget.NewKind }).ConfigureAwait(true);

                    object presentation = null;
                    object slides = null;
                    try
                    {
                        presentation = GetComProperty(application, "ActivePresentation");
                        slides = GetComProperty(presentation, "Slides");
                        var slideCount = Convert.ToInt32(GetComProperty(slides, "Count"));
                        var result = new StyleHarnessResult
                        {
                            StyleSlug = styleSlug,
                            PreviewWidth = preview.Width,
                            PreviewHeight = preview.Height,
                            Validation = validation,
                            Build = build
                        };
                        dynamic powerPointSlides = slides;
                        for (var slideIndex = 1; slideIndex <= slideCount; slideIndex++)
                        {
                            object slide = powerPointSlides[slideIndex];
                            var exportPath = Path.Combine(output, styleSlug + "-ppt-slide-" + slideIndex.ToString("D2") + ".png");
                            dynamic powerPointSlide = slide;
                            powerPointSlide.Export(exportPath, "PNG", DeckAssembler.DesignWidth, DeckAssembler.DesignHeight);

                            var capturePath = Path.Combine(workspace.BuildDir, "capture", "s" + slideIndex.ToString("D2") + ".png");
                            File.Copy(capturePath, Path.Combine(output, styleSlug + "-web-slide-" + slideIndex.ToString("D2") + ".png"), true);
                            result.TextReadbacks.AddRange(CaptureTextReadbacks(slide, styleSlug, slideIndex));
                            using (var webImage = new System.Drawing.Bitmap(capturePath))
                            using (var pptImage = new System.Drawing.Bitmap(exportPath))
                            {
                                result.Similarities.Add(HarnessImageMetrics.StructuralSimilarity(webImage, pptImage));
                                HarnessImageMetrics.SaveDifferenceHeatmap(
                                    webImage,
                                    pptImage,
                                    Path.Combine(output, styleSlug + "-diff-s" + slideIndex.ToString("D2") + ".png"));
                                if (slideIndex >= fontMatrixFirstSlide)
                                {
                                    result.FontVisualMetrics.AddRange(CaptureFontVisualMetrics(
                                        styleSlug,
                                        slideIndex,
                                        fontCaptures[slideIndex],
                                        result.TextReadbacks,
                                        supportedFontRows,
                                        webImage,
                                        pptImage));
                                }
                            }

                            ReleaseComObject(slide);
                            if (slideIndex >= fontMatrixFirstSlide)
                            {
                                if (slideIndex == fontMatrixFirstSlide + fontMatrixPages.Count - 1)
                                {
                                    result.FontScriptProbeMetrics.AddRange(CaptureFontScriptProbeMetrics(
                                        styleSlug,
                                        slideIndex,
                                        fontCaptures[slideIndex],
                                        result.TextReadbacks));
                                }
                            }

                        }

                        if (result.TextReadbacks.Count == 0)
                        {
                            throw new InvalidOperationException(
                                styleSlug + " did not yield any text blocks for the independent harness readback.");
                        }

                        results.Add(result);
                    }
                    finally
                    {
                        ReleaseComObject(slides);
                        if (presentation != null)
                        {
                            try
                            {
                                InvokeCom(presentation, "Close");
                            }
                            finally
                            {
                                ReleaseComObject(presentation);
                            }
                        }
                    }
                }

                var fontIssues = results.SelectMany(result => result.Validation.Issues)
                    .Where(issue => issue.Code.StartsWith("font.", StringComparison.Ordinal))
                    .ToList();
                var unsupportedWeightIssue = fontIssues.FirstOrDefault(issue =>
                    issue.Code == "font.weight_unavailable"
                    && (issue.Text ?? string.Empty).Contains("Noto Sans SC 900"));
                Assert.IsNotNull(unsupportedWeightIssue, "The font matrix must report the unsupported Noto Sans SC 900 probe.");
                Assert.AreEqual("warn", unsupportedWeightIssue.Severity, "Unsupported font weights must remain warnings, not errors.");
                Assert.IsTrue(fontIssues.Any(issue =>
                    issue.Code == "font.style_synthesized"
                    && (issue.Text ?? string.Empty).Contains("Noto Sans SC 400 italic")),
                    "The font matrix must report CJK italic synthesis.");
                Assert.IsTrue(fontIssues.Any(issue =>
                    issue.Code == "font.style_synthesized"
                    && (issue.Text ?? string.Empty).Contains("Smiley Sans 400 italic")),
                    "The font matrix must report the builtin-italic misuse.");
                Assert.IsFalse(fontIssues.Any(issue =>
                    (issue.Text ?? string.Empty).Contains("Segoe UI 400 normal")
                    && issue.Code != "font.fallback"),
                    "A system font in primary position is allowed.");
                Assert.IsTrue(fontIssues.Any(issue =>
                    issue.Code == "font.not_bundled"
                    && (issue.Text ?? string.Empty).Contains("Unbundled Display 400 normal")),
                    "The font matrix must report an unknown family.");
                Assert.IsTrue(fontIssues.Any(issue =>
                    issue.Code == "font.no_cjk_fallback"
                    && (issue.Text ?? string.Empty).Contains("Albert Sans 400 normal")),
                    "The font matrix must report missing bundled CJK fallback.");

                var expectedFontMetricCount = supportedFontRows.Count * results.Count;
                var actualFontMetricCount = results.Sum(result => result.FontVisualMetrics.Count);
                var expectedScriptProbeCount = 4 * results.Count;
                var actualScriptProbeCount = results.Sum(result => result.FontScriptProbeMetrics.Count);

                WriteTextReadbackCsv(output, results);
                WriteFontVisualMetricsCsv(output, results);
                WriteFontScriptProbeCsv(output, results);
                var report = BuildReport(output, results);
                File.WriteAllText(Path.Combine(output, "report.md"), report, new UTF8Encoding(false));

                Assert.AreEqual(
                    expectedFontMetricCount,
                    actualFontMetricCount,
                    "Every bundled font face in every style must have a Web/PPT text-width measurement.");
                Assert.AreEqual(
                    expectedScriptProbeCount,
                    actualScriptProbeCount,
                    "Both isolated script probes for DM Sans and Source Serif 4 must be measured in every style.");
                var fontWidthOutliers = results
                    .SelectMany(result => result.FontVisualMetrics)
                    .Where(metric => Math.Abs(metric.WidthDeltaPercent) > FontWidthTolerancePercent)
                    .ToList();
                Assert.AreEqual(
                    0,
                    fontWidthOutliers.Count,
                    "Font row width API deltas must remain within the pragmatic ±"
                    + FontWidthTolerancePercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                    + "% regression tolerance.");
            }
            finally
            {
                renderer.Dispose();
                if (application != null)
                {
                    try
                    {
                        InvokeCom(application, "Quit");
                    }
                    finally
                    {
                        ReleaseComObject(application);
                    }
                }
            }
        }

        private static async Task RunTemplateMarginInventoryAsync(string assets, string output)
        {
            if (SynchronizationContext.Current == null)
            {
                throw new InvalidOperationException("The harness UI thread has no Windows Forms synchronization context.");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(output, "user-assets"));
            var store = new DeckWorkspaceStore(Path.Combine(output, "decks"));
            var fonts = FontCatalog.Load(paths);
            var catalog = StyleCatalog.Load(paths, fonts);
            var fixtureRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerPoint", "Fixtures", "Harness");
            var slides = new[]
            {
                ReadFixture(fixtureRoot, "cover.html"),
                ReadFixture(fixtureRoot, "body.html"),
                ReadFixture(fixtureRoot, "glyphs.html"),
                ReadFixture(fixtureRoot, "safe-area.html")
            };
            var renderer = new PowerPointDeckRenderService(SynchronizationContext.Current, paths, store.Root);
            try
            {
                var templates = catalog.Entries
                    .Where(entry => string.Equals(entry.Kind, StyleKinds.Template, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Slug, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var results = new List<TemplateMarginResult>();
                foreach (var style in templates)
                {
                    var workspace = store.Create(style.Slug, "medium", "zh-CN", "PPT margin inventory");
                    workspace.PutSlides(slides.Select(html => new DeckSlideInput { Html = html }).ToList());
                    var options = new DeckAssemblyOptions
                    {
                        DeckId = workspace.Id,
                        Style = style,
                        Slides = workspace.Slides.Select(slide => workspace.GetSlideHtml(slide.Id)).ToList(),
                        ThemeCss = ".harness-cover{font-family:'Smiley Sans','Noto Sans SC',sans-serif}"
                            + ".harness-cover__title{font-weight:400}"
                            + ".harness-body{font-family:'Inter','Noto Sans SC',sans-serif}",
                        Language = workspace.Manifest.Language,
                        Title = workspace.Manifest.Title
                    };
                    workspace.WriteThemeCss(options.ThemeCss);
                    options.ThemeCss = workspace.ReadThemeCss();
                    workspace.WriteBuildFile("deck.html", DeckAssembler.Assemble(options));
                    workspace.WriteBuildFile("validate.html", DeckAssembler.AssembleForValidation(options));
                    var deckUrl = DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture#1";
                    var validationUrl = DeckUrls.DeckBase(workspace.Id) + "build/validate.html";
                    var validation = await renderer.ValidateAsync(
                        deckUrl,
                        validationUrl,
                        "medium",
                        CancellationToken.None).ConfigureAwait(true);
                    if (validation.SlideCount != slides.Length || validation.PdfPageCount != slides.Length)
                    {
                        throw new InvalidOperationException(
                            style.Slug + " expected " + slides.Length + " slides and PDF pages but received "
                            + validation.SlideCount + " slides and " + validation.PdfPageCount + " PDF pages.");
                    }

                    results.Add(new TemplateMarginResult
                    {
                        Style = style,
                        SourceInset = DescribeTemplateInset(style),
                        Validation = validation
                    });
                }

                WriteTemplateMarginInventory(output, results);
                WriteTemplateFontInventory(output, results);
            }
            finally
            {
                renderer.Dispose();
            }
        }

        private static async Task RunTemplateGallerySafeAreaInventoryAsync(string assets, string output)
        {
            if (SynchronizationContext.Current == null)
            {
                throw new InvalidOperationException("The harness UI thread has no Windows Forms synchronization context.");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(output, "user-assets"));
            var store = new DeckWorkspaceStore(Path.Combine(output, "decks"));
            var fonts = FontCatalog.Load(paths);
            var catalog = StyleCatalog.Load(paths, fonts);
            var templates = catalog.Entries
                .Where(entry => string.Equals(entry.Kind, StyleKinds.Template, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Slug, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var renderer = new PowerPointDeckRenderService(SynchronizationContext.Current, paths, store.Root);
            try
            {
                var results = new List<TemplateGallerySafeAreaResult>();
                var geometryFindings = new List<string>();
                foreach (var style in templates)
                {
                    var templatePath = Path.Combine(style.Directory ?? string.Empty, "template.html");
                    if (!File.Exists(templatePath))
                    {
                        throw new FileNotFoundException("Template gallery source is missing.", templatePath);
                    }

                    var layouts = TemplateLayoutSplitter.Split(File.ReadAllText(templatePath, Encoding.UTF8));
                    if (layouts.Count == 0 || layouts.Any(layout => string.IsNullOrWhiteSpace(layout.SlideHtml)))
                    {
                        throw new InvalidOperationException(style.Slug + " did not produce isolated gallery slide fragments.");
                    }

                    var workspace = store.Create(style.Slug, "medium", "zh-CN", "PPT gallery safe-area inventory");
                    workspace.PutSlides(layouts
                        .Select(layout => new DeckSlideInput { Html = layout.SlideHtml })
                        .ToList());
                    var options = new DeckAssemblyOptions
                    {
                        DeckId = workspace.Id,
                        Style = style,
                        Slides = workspace.Slides.Select(slide => workspace.GetSlideHtml(slide.Id)).ToList(),
                        ThemeCss = workspace.ReadThemeCss(),
                        Language = workspace.Manifest.Language,
                        Title = workspace.Manifest.Title
                    };
                    workspace.WriteBuildFile("deck.html", DeckAssembler.Assemble(options));
                    workspace.WriteBuildFile("validate.html", DeckAssembler.AssembleForValidation(options));
                    var deckUrl = DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture#1";
                    var validationUrl = DeckUrls.DeckBase(workspace.Id) + "build/validate.html";
                    var validation = await renderer.ValidateAsync(
                        deckUrl,
                        validationUrl,
                        "medium",
                        CancellationToken.None).ConfigureAwait(true);
                    if (validation.SlideCount != layouts.Count || validation.PdfPageCount != layouts.Count)
                    {
                        throw new InvalidOperationException(
                            style.Slug + " expected " + layouts.Count + " original gallery slides and PDF pages but received "
                            + validation.SlideCount + " slides and " + validation.PdfPageCount + " PDF pages.");
                    }
                    var sourceInset = DescribeTemplateInset(style);
                    var isNeoGridBold = string.Equals(style.Slug, "neo-grid-bold", StringComparison.OrdinalIgnoreCase);
                    var isBroadside = string.Equals(style.Slug, "broadside", StringComparison.OrdinalIgnoreCase);
                    var isPinAndPaper = string.Equals(style.Slug, "pin-and-paper", StringComparison.OrdinalIgnoreCase);
                    var isStencilTablet = string.Equals(style.Slug, "stencil-tablet", StringComparison.OrdinalIgnoreCase);
                    var isStudio = string.Equals(style.Slug, "studio", StringComparison.OrdinalIgnoreCase);
                    var isEditorialTriTone = string.Equals(style.Slug, "editorial-tri-tone", StringComparison.OrdinalIgnoreCase);
                    var isSoftEditorial = string.Equals(style.Slug, "soft-editorial", StringComparison.OrdinalIgnoreCase);
                    var isMat = string.Equals(style.Slug, "mat", StringComparison.OrdinalIgnoreCase);
                    var isPinkScript = string.Equals(style.Slug, "pink-script", StringComparison.OrdinalIgnoreCase);
                    var isPeoplesPlatform = string.Equals(style.Slug, "peoples-platform", StringComparison.OrdinalIgnoreCase);
                    var isBlockFrame = string.Equals(style.Slug, "block-frame", StringComparison.OrdinalIgnoreCase);
                    var isBoldPoster = string.Equals(style.Slug, "bold-poster", StringComparison.OrdinalIgnoreCase);
                    var isCobaltGrid = string.Equals(style.Slug, "cobalt-grid", StringComparison.OrdinalIgnoreCase);
                    var isAdditionalSafeAreaTemplate = AdditionalSafeAreaTemplateSlugs.Contains(style.Slug);
                    var usesStandardSafeArea = isPinAndPaper || isStencilTablet || isStudio || isSoftEditorial
                        || isPinkScript || isPeoplesPlatform || isBlockFrame || isBoldPoster || isCobaltGrid
                        || isAdditionalSafeAreaTemplate;
                    if (isNeoGridBold || isBroadside || isEditorialTriTone || isMat || usesStandardSafeArea)
                    {
                        var hasExpectedInset = isNeoGridBold
                            ? sourceInset.IndexOf(".frame inset 100px 136px", StringComparison.OrdinalIgnoreCase) >= 0
                            : isBroadside
                                ? sourceInset.IndexOf("--inset-x 7.1vw", StringComparison.OrdinalIgnoreCase) >= 0
                                    && sourceInset.IndexOf("--inset-y 9.3vh", StringComparison.OrdinalIgnoreCase) >= 0
                                : isEditorialTriTone
                                    ? sourceInset.IndexOf("--nx-safe-x 136px", StringComparison.OrdinalIgnoreCase) >= 0
                                        && sourceInset.IndexOf("--nx-safe-y 128px", StringComparison.OrdinalIgnoreCase) >= 0
                                        && sourceInset.IndexOf("--nx-action-x 64px", StringComparison.OrdinalIgnoreCase) >= 0
                                        && sourceInset.IndexOf("--nx-action-y 48px", StringComparison.OrdinalIgnoreCase) >= 0
                                    : isMat
                                        ? sourceInset.IndexOf("--nx-safe-x 7.1vw", StringComparison.OrdinalIgnoreCase) >= 0
                                            && sourceInset.IndexOf("--nx-safe-y 9.3vh", StringComparison.OrdinalIgnoreCase) >= 0
                                        : usesStandardSafeArea
                                            && sourceInset.IndexOf("--nx-safe-x 136px", StringComparison.OrdinalIgnoreCase) >= 0
                                            && sourceInset.IndexOf("--nx-safe-y 100px", StringComparison.OrdinalIgnoreCase) >= 0
                                            && sourceInset.IndexOf("--nx-action-x 64px", StringComparison.OrdinalIgnoreCase) >= 0
                                            && sourceInset.IndexOf("--nx-action-y 48px", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!hasExpectedInset)
                        {
                            throw new InvalidOperationException(
                                style.Slug + " gallery inventory did not detect its shared safe inset: " + sourceInset);
                        }

                        var thumbnailSlides = isNeoGridBold
                            ? new[] { 1, 3, 8 }
                            : isBroadside ? new[] { 1, 4, 13 }
                            : isStencilTablet ? new[] { 1, 3, 5, 8 }
                            : isEditorialTriTone ? new[] { 1, 3, 4, 8 }
                            : isStudio ? new[] { 1, 4, 8, 12 }
                            : isSoftEditorial ? new[] { 1, 3, 6, 8, 12 }
                            : isMat ? new[] { 1, 3, 6, 9 }
                            : isPinkScript ? new[] { 1, 3, 5, 7, 9 }
                            : isPeoplesPlatform ? new[] { 1, 2, 3, 4, 6, 8, 9, 10 }
                            : isBlockFrame ? new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }
                            : isBoldPoster ? new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }
                            : isCobaltGrid ? new[] { 1, 2, 3, 4, 5, 6, 7, 8 }
                            : isAdditionalSafeAreaTemplate ? GetGalleryThumbnailSlideNumbers(style.Directory, layouts.Count)
                            : new[] { 1, 3, 11 };
                        var inspectionSlides = string.Equals(style.Slug, "creative-mode", StringComparison.OrdinalIgnoreCase)
                            ? new[] { 2 }
                            : string.Equals(style.Slug, "editorial-forest", StringComparison.OrdinalIgnoreCase)
                                ? new[] { 1, 2, 4, 6, 7, 8 }
                                : string.Equals(style.Slug, "emerald-editorial", StringComparison.OrdinalIgnoreCase)
                                    ? new[] { 4, 5 }
                                    : new int[0];
                        thumbnailSlides = thumbnailSlides.Concat(inspectionSlides).Distinct().OrderBy(number => number).ToArray();
                        foreach (var slideNumber in thumbnailSlides)
                        {
                            var thumbnailUrl = DeckUrls.DeckBase(workspace.Id)
                                + "build/deck.html?mode=capture#" + slideNumber;
                            var thumbnail = await renderer.CaptureAsync(
                                thumbnailUrl,
                                1.0,
                                CancellationToken.None).ConfigureAwait(true);
                            File.WriteAllBytes(
                                Path.Combine(output, style.Slug + "-thumb-" + slideNumber.ToString("00") + ".png"),
                                thumbnail.Png);
                        }

                        var geometryIssues = (validation.Issues ?? new List<DeckValidationIssue>())
                            .Where(issue => issue.Code == "layout.unsafe"
                                || issue.Code == "layout.tight"
                                || issue.Code == "layout.chrome_unsafe"
                                || issue.Code == "layout.unlayered"
                                || issue.Code == "text.clipped"
                                || issue.Code == "text.outside")
                            .ToList();
                        if (geometryIssues.Count > 0)
                        {
                            geometryFindings.Add(
                                style.Slug + ": " + geometryIssues.Count + " layout or clipping finding(s), including "
                                + string.Join("; ", geometryIssues.Take(5).Select(issue =>
                                    issue.Code + " slide " + issue.Slide + " " + issue.Selector
                                    + " - " + issue.Message + " Text: " + issue.Text)));
                        }
                    }

                    results.Add(new TemplateGallerySafeAreaResult
                    {
                        Style = style,
                        SlideCount = layouts.Count,
                        SourceInset = sourceInset,
                        Validation = validation
                    });
                }

                WriteTemplateGallerySafeAreaInventory(output, results);
                if (geometryFindings.Count > 0)
                {
                    throw new InvalidOperationException(
                        "Original-gallery geometry findings were written to the inventory report: "
                        + Path.Combine(output, "gallery-safe-area-inventory.md") + Environment.NewLine
                        + string.Join(Environment.NewLine, geometryFindings));
                }
            }
            finally
            {
                renderer.Dispose();
            }
        }

        private static void WriteTemplateGallerySafeAreaInventory(string output, IList<TemplateGallerySafeAreaResult> templates)
        {
            var report = new StringBuilder();
            report.AppendLine("# PPT Harness original-gallery safe-area inventory");
            report.AppendLine();
            report.AppendLine("- Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("- Templates rendered: " + templates.Count);
            report.AppendLine("- Original gallery slides rendered: " + templates.Sum(template => template.SlideCount));
            report.AppendLine("- Policy: target 128/96 design px; lower bounds 112/80; action bounds 64/48.");
            report.AppendLine("- Classification is based on actual gallery text geometry; source inset declarations remain a separate, non-computed hint.");
            report.AppendLine();
            report.AppendLine("| Template | Slides | Source inset | Below lower bound | Below target | Chrome violations | Unlayered | Truncated | Probe status |");
            report.AppendLine("|---|---:|---|---:|---:|---:|---:|---|---|");
            foreach (var template in templates)
            {
                var issues = template.Validation.Issues ?? new List<DeckValidationIssue>();
                var belowLowerBound = issues.Count(issue => issue.Code == "layout.unsafe");
                var belowTarget = issues.Count(issue => issue.Code == "layout.tight");
                var chrome = issues.Count(issue => issue.Code == "layout.chrome_unsafe");
                var unlayered = issues.Count(issue => issue.Code == "layout.unlayered");
                var status = belowLowerBound > 0 || chrome > 0
                    ? "Crosses lower bound; review layout"
                    : belowTarget > 0
                        ? "Within minimum; below target"
                        : "No sampled geometry warnings";
                report.Append("| ").Append(template.Style.Slug)
                    .Append(" | ").Append(template.SlideCount)
                    .Append(" | ").Append(EscapeMarkdown(template.SourceInset))
                    .Append(" | ").Append(belowLowerBound)
                    .Append(" | ").Append(belowTarget)
                    .Append(" | ").Append(chrome)
                    .Append(" | ").Append(unlayered)
                    .Append(" | ").Append(template.Validation.Truncated ? "yes" : "no")
                    .Append(" | ").Append(status)
                    .AppendLine(" |");
            }

            report.AppendLine();
            report.AppendLine("## Geometry issue selectors");
            foreach (var template in templates)
            {
                var geometryIssues = (template.Validation.Issues ?? new List<DeckValidationIssue>())
                    .Where(issue => issue.Code == "layout.unsafe"
                        || issue.Code == "layout.tight"
                        || issue.Code == "layout.chrome_unsafe")
                    .ToList();
                if (geometryIssues.Count == 0) continue;

                report.AppendLine("### " + template.Style.Slug);
                foreach (var issue in geometryIssues
                    .GroupBy(item => item.Selector ?? "(unknown selector)")
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(10))
                {
                    report.Append("- `").Append(EscapeMarkdown(issue.Key)).Append("`: ")
                        .Append(issue.Count()).AppendLine(" finding(s)");
                }

                if (template.Validation.Truncated) report.AppendLine("- Findings were truncated by the validation issue limit; counts are lower bounds.");
            }

            File.WriteAllText(Path.Combine(output, "gallery-safe-area-inventory.md"), report.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Original-gallery safe-area inventory: " + Path.Combine(output, "gallery-safe-area-inventory.md"));
        }

        private static int[] GetGalleryThumbnailSlideNumbers(string templateDirectory, int slideCount)
        {
            var thumbnailDirectory = Path.Combine(templateDirectory ?? string.Empty, "thumbs");
            var slideNumbers = new List<int>();
            if (Directory.Exists(thumbnailDirectory))
            {
                foreach (var path in Directory.GetFiles(thumbnailDirectory, "*.png"))
                {
                    int slideNumber;
                    if (int.TryParse(Path.GetFileNameWithoutExtension(path), out slideNumber)
                        && slideNumber >= 1
                        && slideNumber <= slideCount)
                    {
                        slideNumbers.Add(slideNumber);
                    }
                }
            }

            var existingThumbnails = slideNumbers.Distinct().OrderBy(number => number).ToArray();
            return existingThumbnails.Length > 0 ? existingThumbnails : new[] { 1, slideCount };
        }

        private static string DescribeTemplateInset(StyleEntry style)
        {
            var templatePath = Path.Combine(style.Directory ?? string.Empty, "template.html");
            if (!File.Exists(templatePath))
            {
                return "No template.html";
            }

            var html = File.ReadAllText(templatePath, Encoding.UTF8);
            var safeHorizontal = Regex.Match(html, @"--nx-safe-x\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var safeVertical = Regex.Match(html, @"--nx-safe-y\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var actionHorizontal = Regex.Match(html, @"--nx-action-x\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var actionVertical = Regex.Match(html, @"--nx-action-y\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var insetHorizontal = Regex.Match(html, @"--inset-x\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var insetVertical = Regex.Match(html, @"--inset-y\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var horizontal = Regex.Match(html, @"--pad-x\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var vertical = Regex.Match(html, @"--pad-y\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            var frameRules = Regex.Matches(
                html,
                @"(?s)(?<![\w-])\.frame(?![\w-])\s*\{([^}]*)\}",
                RegexOptions.IgnoreCase);
            var slideRules = Regex.Matches(
                html,
                @"(?s)(?<![\w-])(?:section\s+)?\.slide(?![\w-])\s*\{([^}]*)\}",
                RegexOptions.IgnoreCase);
            var insets = new List<string>();
            foreach (Match rule in frameRules)
            {
                var inset = Regex.Match(rule.Groups[1].Value, @"(?:^|;)\s*inset\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                if (inset.Success)
                {
                    var value = Regex.Replace(inset.Groups[1].Value.Trim(), @"\s+", " ");
                    if (!insets.Contains(value, StringComparer.OrdinalIgnoreCase)) insets.Add(value);
                }
            }

            var paddings = new List<string>();
            foreach (Match rule in slideRules)
            {
                var padding = Regex.Match(rule.Groups[1].Value, @"(?:^|;)\s*padding\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                if (padding.Success)
                {
                    var value = Regex.Replace(padding.Groups[1].Value.Trim(), @"\s+", " ");
                    if (!paddings.Contains(value, StringComparer.OrdinalIgnoreCase)) paddings.Add(value);
                }
            }

            var variables = new List<string>();
            if (safeHorizontal.Success) variables.Add("--nx-safe-x " + Regex.Replace(safeHorizontal.Groups[1].Value.Trim(), @"\s+", " "));
            if (safeVertical.Success) variables.Add("--nx-safe-y " + Regex.Replace(safeVertical.Groups[1].Value.Trim(), @"\s+", " "));
            if (actionHorizontal.Success) variables.Add("--nx-action-x " + Regex.Replace(actionHorizontal.Groups[1].Value.Trim(), @"\s+", " "));
            if (actionVertical.Success) variables.Add("--nx-action-y " + Regex.Replace(actionVertical.Groups[1].Value.Trim(), @"\s+", " "));
            if (insetHorizontal.Success) variables.Add("--inset-x " + Regex.Replace(insetHorizontal.Groups[1].Value.Trim(), @"\s+", " "));
            if (insetVertical.Success) variables.Add("--inset-y " + Regex.Replace(insetVertical.Groups[1].Value.Trim(), @"\s+", " "));
            if (horizontal.Success) variables.Add("--pad-x " + Regex.Replace(horizontal.Groups[1].Value.Trim(), @"\s+", " "));
            if (vertical.Success) variables.Add("--pad-y " + Regex.Replace(vertical.Groups[1].Value.Trim(), @"\s+", " "));
            if (insets.Count > 0) variables.Add(".frame inset " + string.Join(" / ", insets));
            if (paddings.Count > 0) variables.Add(".slide padding " + string.Join(" / ", paddings));
            return variables.Count == 0 ? "No shared frame or padding rule detected" : string.Join("; ", variables);
        }

        private static void WriteTemplateMarginInventory(string output, IList<TemplateMarginResult> templates)
        {
            var presetsRoot = Path.Combine(FindShippedAssets(), "presets");
            var presets = Directory.GetDirectories(presetsRoot)
                .Select(path => new DirectoryInfo(path))
                .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var report = new StringBuilder();
            report.AppendLine("# PPT Harness template safe-area inventory");
            report.AppendLine();
            report.AppendLine("- Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("- Output: `" + output + "`");
            report.AppendLine("- Templates measured: " + templates.Count);
            report.AppendLine("- Presets inspected: " + presets.Count);
            report.AppendLine("- Probe pages per template: cover, body, glyphs, safe-area");
            report.AppendLine("- `layout.unsafe` and `layout.tight` are counted from DOM validation; the safe-area page intentionally tests edge conditions.");
            report.AppendLine();
            report.AppendLine("| Template | Source inset declarations | Cover | Body | Glyphs | Safe-area | Total margin | Outside |");
            report.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
            foreach (var template in templates)
            {
                var layoutBySlide = Enumerable.Range(1, 4)
                    .Select(slide => template.Validation.Issues.Count(issue => issue.Slide == slide
                        && (issue.Code == "layout.unsafe" || issue.Code == "layout.tight" || issue.Code == "layout.chrome_unsafe")))
                    .ToArray();
                var outside = template.Validation.Issues.Count(issue => issue.Code == "text.outside");
                report.Append("| ").Append(template.Style.Slug)
                    .Append(" | ").Append(EscapeMarkdown(template.SourceInset))
                    .Append(" | ").Append(layoutBySlide[0])
                    .Append(" | ").Append(layoutBySlide[1])
                    .Append(" | ").Append(layoutBySlide[2])
                    .Append(" | ").Append(layoutBySlide[3])
                    .Append(" | ").Append(layoutBySlide.Sum())
                    .Append(" | ").Append(outside)
                    .AppendLine(" |");
            }

            report.AppendLine();
            report.AppendLine("## Description-only presets");
            report.AppendLine();
            report.AppendLine("Preset Markdown has no fixed slide HTML or numeric content inset; layout is generated per deck. A margin hit count for a preset without an actual generated HTML deck would be misleading.");
            report.AppendLine();
            report.AppendLine("| Preset | Measured fixed inset |");
            report.AppendLine("|---|---|");
            foreach (var preset in presets)
            {
                report.Append("| ").Append(preset.Name).AppendLine(" | N/A — generated HTML defines the inset |");
            }

            report.AppendLine();
            report.AppendLine("## Safe-area issue details");
            foreach (var template in templates)
            {
                var layoutIssues = template.Validation.Issues.Where(issue =>
                    issue.Code == "layout.unsafe" || issue.Code == "layout.tight" || issue.Code == "layout.chrome_unsafe").ToList();
                if (layoutIssues.Count == 0) continue;
                report.AppendLine("### " + template.Style.Slug);
                foreach (var issue in layoutIssues)
                {
                    report.Append("- Slide ").Append(issue.Slide)
                        .Append(" [").Append(issue.Code).Append("] `").Append(EscapeMarkdown(issue.Selector ?? ""))
                        .Append("`: ").AppendLine(EscapeMarkdown(issue.Text ?? ""));
                }
            }

            var ranked = templates
                .OrderBy(template => template.Validation.Issues.Count(issue =>
                    issue.Code == "layout.unsafe" || issue.Code == "layout.tight" || issue.Code == "layout.chrome_unsafe"))
                .ToList();
            var selected = new List<TemplateMarginResult>();
            foreach (var candidate in new[] { ranked.First(), ranked[ranked.Count / 2], ranked.Last() })
            {
                if (!selected.Any(template => string.Equals(
                    template.Style.Slug,
                    candidate.Style.Slug,
                    StringComparison.OrdinalIgnoreCase)))
                {
                    selected.Add(candidate);
                }
            }
            foreach (var template in selected)
            {
                foreach (var screenshot in template.Validation.Screenshots.Where(item => item.Slide == 1 || item.Slide == 4))
                {
                    File.WriteAllBytes(
                        Path.Combine(output, template.Style.Slug + "-s" + screenshot.Slide.ToString("D2") + ".png"),
                        screenshot.Png);
                }
            }

            File.WriteAllText(Path.Combine(output, "margin-inventory.md"), report.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Template margin inventory: " + Path.Combine(output, "margin-inventory.md"));
        }

        private static void WriteTemplateFontInventory(string output, IList<TemplateMarginResult> templates)
        {
            var fontIssues = templates
                .SelectMany(template => template.Validation.Issues
                    .Where(issue => issue.Code.StartsWith("font.", StringComparison.Ordinal))
                    .Select(issue => new { Template = template, Issue = issue }))
                .ToList();
            var knownCodes = new[]
            {
                "font.fallback",
                "font.not_bundled",
                "font.weight_unavailable",
                "font.style_synthesized",
                "font.no_cjk_fallback"
            };
            var codes = knownCodes
                .Concat(fontIssues
                .Select(item => item.Issue.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(code => !knownCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var report = new StringBuilder();
            report.AppendLine("# PPT Harness template font-policy inventory");
            report.AppendLine();
            report.AppendLine("- Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("- Templates rendered: " + templates.Count);
            report.AppendLine("- Probe pages per template: cover, body, glyphs, safe-area");
            report.AppendLine("- Font-policy issues: " + fontIssues.Count);
            report.AppendLine();
            report.AppendLine("| Template | Total font issues | " + string.Join(" | ", codes) + " |");
            report.Append("|---|---:|")
                .Append(string.Join("|", codes.Select(code => "---:")))
                .AppendLine("|");
            foreach (var template in templates)
            {
                var issues = template.Validation.Issues
                    .Where(issue => issue.Code.StartsWith("font.", StringComparison.Ordinal))
                    .ToList();
                var counts = codes.Select(code =>
                    issues.Count(issue => string.Equals(issue.Code, code, StringComparison.OrdinalIgnoreCase)).ToString())
                    .ToList();
                report.Append("| ").Append(template.Style.Slug)
                    .Append(" | ").Append(issues.Count)
                    .Append(" | ").Append(string.Join(" | ", counts))
                    .AppendLine(" |");
            }

            report.AppendLine();
            report.AppendLine("## Issue details");
            if (fontIssues.Count == 0)
            {
                report.AppendLine("No font-policy issues were reported for the rendered template probes.");
            }
            else
            {
                foreach (var item in fontIssues
                    .OrderBy(item => item.Template.Style.Slug, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Issue.Slide)
                    .ThenBy(item => item.Issue.Code, StringComparer.OrdinalIgnoreCase))
                {
                    report.Append("- ").Append(item.Template.Style.Slug)
                        .Append(" / slide ").Append(item.Issue.Slide)
                        .Append(" / `").Append(item.Issue.Code).Append("`");
                    if (!string.IsNullOrWhiteSpace(item.Issue.Selector))
                    {
                        report.Append(" / `").Append(EscapeMarkdown(item.Issue.Selector)).Append("`");
                    }
                    report.Append(": ").Append(EscapeMarkdown(item.Issue.Message ?? string.Empty));
                    if (!string.IsNullOrWhiteSpace(item.Issue.Text))
                    {
                        report.Append(" Text: `").Append(EscapeMarkdown(item.Issue.Text)).Append('`');
                    }
                    report.AppendLine();
                }
            }

            var presets = Directory.GetDirectories(Path.Combine(FindShippedAssets(), "presets"))
                .Select(path => new DirectoryInfo(path))
                .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            report.AppendLine();
            report.AppendLine("## Description-only presets");
            report.AppendLine();
            report.AppendLine("Preset Markdown has no fixed HTML to render, so runtime font issues cannot be measured here. Explicit family/weight recommendations are covered by the `PresetFontWeightGuidance_UsesOnlyBundledWeights` unit test; generated slides are checked by normal validate/build flows.");
            report.AppendLine();
            report.AppendLine("| Preset | Runtime HTML inventory |");
            report.AppendLine("|---|---|");
            foreach (var preset in presets)
            {
                report.Append("| ").Append(preset.Name).AppendLine(" | N/A — generated HTML |");
            }

            var path = Path.Combine(output, "font-policy-inventory.md");
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Template font-policy inventory: " + path);
        }

        private static string EscapeMarkdown(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private static string FontMatrixRowText(string family, int weight, string style)
        {
            return FontMatrixRowText(family, weight, style, "Hamburgefonstiv 0123 汉字永和");
        }

        private static string FontMatrixRowText(string family, int weight, string style, string sampleText)
        {
            return NormalizeWhitespace(family + " " + weight.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " " + style + " — " + sampleText);
        }

        private static IList<FontVisualMetric> CaptureFontVisualMetrics(
            string styleSlug,
            int slideIndex,
            DeckSlideCapture capture,
            IEnumerable<TextShapeReadback> readbacks,
            ISet<string> supportedFontRows,
            System.Drawing.Bitmap webImage,
            System.Drawing.Bitmap pptImage)
        {
            if (capture == null || capture.Data == null)
            {
                throw new InvalidOperationException("Web capture data is missing for font matrix slide " + slideIndex + ".");
            }

            var metrics = new List<FontVisualMetric>();
            foreach (var readback in readbacks.Where(item =>
                item.Slide == slideIndex && supportedFontRows.Contains(NormalizeWhitespace(item.Text))))
            {
                var expectedText = NormalizeWhitespace(readback.Text);
                var browserMetric = MeasureBrowserFontLine(styleSlug, slideIndex, capture, readback, expectedText);
                metrics.Add(new FontVisualMetric
                {
                    Style = styleSlug,
                    Slide = slideIndex,
                    Text = expectedText,
                    BrowserFontSizePixels = browserMetric.FontSizePixels,
                    PowerPointFontSizePoints = readback.FontSizePoints,
                    BrowserLetterSpacingPixels = browserMetric.LetterSpacingPixels,
                    PowerPointFontSpacingPoints = readback.FontSpacingPoints,
                    TextFrameAutoSize = readback.TextFrameAutoSize,
                    BrowserWidthPoints = browserMetric.WidthPoints,
                    PowerPointBoundWidthPoints = readback.BoundWidthPoints,
                    WidthDeltaPercent = (readback.BoundWidthPoints - browserMetric.WidthPoints) / browserMetric.WidthPoints * 100,
                    RowSimilarity = MeasureFontRowSimilarity(webImage, pptImage, browserMetric.Rectangle)
                });
            }

            return metrics;
        }

        private static double MeasureFontRowSimilarity(
            System.Drawing.Bitmap webImage,
            System.Drawing.Bitmap pptImage,
            CapturedRectangle rowRectangle)
        {
            if (webImage == null) throw new ArgumentNullException(nameof(webImage));
            if (pptImage == null) throw new ArgumentNullException(nameof(pptImage));
            if (rowRectangle == null) throw new ArgumentNullException(nameof(rowRectangle));
            if (webImage.Size != pptImage.Size)
            {
                throw new ArgumentException("Font-row images must have the same dimensions.");
            }

            const int horizontalPadding = 64;
            const int verticalPadding = 12;
            var left = Math.Max(0, (int)Math.Floor(rowRectangle.Left) - horizontalPadding);
            var top = Math.Max(0, (int)Math.Floor(rowRectangle.Top) - verticalPadding);
            var right = Math.Min(webImage.Width, (int)Math.Ceiling(rowRectangle.Left + rowRectangle.Width) + horizontalPadding);
            var bottom = Math.Min(webImage.Height, (int)Math.Ceiling(rowRectangle.Top + rowRectangle.Height) + verticalPadding);
            if (right <= left || bottom <= top)
            {
                throw new InvalidOperationException("The font-row crop is outside the slide image.");
            }

            var crop = new System.Drawing.Rectangle(left, top, right - left, bottom - top);
            using (var webCrop = webImage.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var pptCrop = pptImage.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                return HarnessImageMetrics.StructuralSimilarity(webCrop, pptCrop);
            }
        }

        private static IList<FontScriptProbeMetric> CaptureFontScriptProbeMetrics(
            string styleSlug,
            int slideIndex,
            DeckSlideCapture capture,
            IEnumerable<TextShapeReadback> readbacks)
        {
            var pageRows = readbacks
                .Where(item => item.Slide == slideIndex)
                .ToDictionary(item => NormalizeWhitespace(item.Text), StringComparer.Ordinal);
            var metrics = new List<FontScriptProbeMetric>();
            foreach (var family in new[] { "DM Sans", "Source Serif 4", "IBM Plex Mono", "JetBrains Mono" })
            {
                var baseline = MeasureProbeLine(styleSlug, slideIndex, capture, pageRows, family, "X");
                var latin = MeasureProbeLine(styleSlug, slideIndex, capture, pageRows, family, "X Hamburgefonstiv 0123");
                var cjk = MeasureProbeLine(styleSlug, slideIndex, capture, pageRows, family, "X 汉字永和");
                var browserLatinWidth = latin.BrowserWidthPoints - baseline.BrowserWidthPoints;
                var powerpointLatinWidth = latin.PowerPointWidthPoints - baseline.PowerPointWidthPoints;
                var browserCjkWidth = cjk.BrowserWidthPoints - baseline.BrowserWidthPoints;
                var powerpointCjkWidth = cjk.PowerPointWidthPoints - baseline.PowerPointWidthPoints;
                if (browserLatinWidth <= 0 || browserCjkWidth <= 0)
                {
                    throw new InvalidOperationException(styleSlug + " produced an invalid browser script-probe width for " + family + ".");
                }

                metrics.Add(new FontScriptProbeMetric
                {
                    Style = styleSlug,
                    Family = family,
                    BrowserFontSizePixels = latin.BrowserFontSizePixels,
                    PowerPointFontSizePoints = latin.PowerPointFontSizePoints,
                    SharedLabelWidthDeltaPercent = (baseline.PowerPointWidthPoints - baseline.BrowserWidthPoints)
                        / baseline.BrowserWidthPoints * 100,
                    BrowserLatinWidthPoints = browserLatinWidth,
                    PowerPointLatinWidthPoints = powerpointLatinWidth,
                    LatinWidthDeltaPercent = (powerpointLatinWidth - browserLatinWidth) / browserLatinWidth * 100,
                    BrowserCjkWidthPoints = browserCjkWidth,
                    PowerPointCjkWidthPoints = powerpointCjkWidth,
                    CjkWidthDeltaPercent = (powerpointCjkWidth - browserCjkWidth) / browserCjkWidth * 100
                });
            }

            return metrics;
        }

        private static FontLineMeasurement MeasureProbeLine(
            string styleSlug,
            int slideIndex,
            DeckSlideCapture capture,
            IDictionary<string, TextShapeReadback> pageRows,
            string family,
            string sampleText)
        {
            var expectedText = FontMatrixRowText(family, 400, "normal", sampleText);
            TextShapeReadback readback;
            if (!pageRows.TryGetValue(expectedText, out readback))
            {
                throw new InvalidOperationException(
                    styleSlug + " slide " + slideIndex + " is missing the script-probe row \"" + expectedText + "\".");
            }

            var browserMetric = MeasureBrowserFontLine(styleSlug, slideIndex, capture, readback, expectedText);
            return new FontLineMeasurement
            {
                BrowserFontSizePixels = browserMetric.FontSizePixels,
                PowerPointFontSizePoints = readback.FontSizePoints,
                BrowserWidthPoints = browserMetric.WidthPoints,
                PowerPointWidthPoints = readback.BoundWidthPoints
            };
        }

        private static BrowserFontLineMeasurement MeasureBrowserFontLine(
            string styleSlug,
            int slideIndex,
            DeckSlideCapture capture,
            TextShapeReadback readback,
            string expectedText)
        {
            if (capture == null || capture.Data == null || capture.Data.TextBlocks == null)
            {
                throw new InvalidOperationException("Web capture data is missing for font matrix slide " + slideIndex + ".");
            }

            var textBlocks = capture.Data.TextBlocks
                .Where(block => block.LineDetails != null
                    && block.LineDetails.Any(line => NormalizeWhitespace(line.Text) == expectedText))
                .ToList();
            if (textBlocks.Count != 1)
            {
                throw new InvalidOperationException(
                    styleSlug + " slide " + slideIndex + " expected one browser text block for \""
                    + expectedText + "\", found " + textBlocks.Count + ".");
            }

            var lines = textBlocks[0].LineDetails
                .Where(line => NormalizeWhitespace(line.Text) == expectedText)
                .ToList();
            if (lines.Count != 1 || lines[0].Rectangle == null || lines[0].Rectangle.Width <= 0)
            {
                throw new InvalidOperationException(
                    styleSlug + " slide " + slideIndex + " expected one measurable browser text line for \""
                    + expectedText + "\".");
            }

            if (textBlocks[0].FontSize <= 0 || readback.BoundWidthPoints <= 0 || readback.FontSizePoints <= 0)
            {
                throw new InvalidOperationException(
                    styleSlug + " slide " + slideIndex + " returned invalid text metrics for \""
                    + expectedText + "\".");
            }

            return new BrowserFontLineMeasurement
            {
                FontSizePixels = textBlocks[0].FontSize,
                LetterSpacingPixels = textBlocks[0].LetterSpacing,
                WidthPoints = lines[0].Rectangle.Width * 0.5,
                Rectangle = lines[0].Rectangle
            };
        }

        private static string NormalizeWhitespace(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        }

        private static string BuildReport(string output, IList<StyleHarnessResult> results)
        {
            var report = new StringBuilder();
            report.AppendLine("# PPT Harness baseline");
            report.AppendLine();
            report.AppendLine("- Generated (UTC): " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("- Output: `" + output + "`");
            report.AppendLine("- Styles: " + results.Count);
            report.AppendLine("- Panel preview size: " + results[0].PreviewWidth + "×" + results[0].PreviewHeight);
            report.AppendLine("- Slides validated: " + results.Sum(result => result.Validation.SlideCount));
            report.AppendLine("- PDF pages: " + results.Sum(result => result.Validation.PdfPageCount));
            report.AppendLine("- Text blocks read back by the independent harness: " + results.Sum(result => result.TextReadbacks.Count));
            report.AppendLine("- Text readback: `text-readback.csv` (font names, bold/italic and shape geometry)");
            report.AppendLine("- Font-row image and width diagnostics: `font-visual-metrics.csv` (row-crop SSIM is exploratory until calibrated for cross-engine antialiasing; DOM Range vs PowerPoint `BoundWidth` is diagnostic only)");
            report.AppendLine("- Isolated script API diagnostics: `font-script-probes.csv` (Latin and CJK width increments after subtracting a shared baseline)");
            report.AppendLine("- Difference heatmaps: `{style}-diff-s{slide}.png`");
            report.AppendLine();
            report.AppendLine("| Style | Slides | PDF pages | Text blocks | Validation issues | Minimum SSIM |");
            report.AppendLine("|---|---:|---:|---:|---:|---:|");
            foreach (var result in results)
            {
                report.Append("| ").Append(result.StyleSlug)
                    .Append(" | ").Append(result.Validation.SlideCount)
                    .Append(" | ").Append(result.Validation.PdfPageCount)
                    .Append(" | ").Append(result.TextReadbacks.Count)
                    .Append(" | ").Append(result.Validation.Issues.Count)
                    .Append(" | ").Append(result.Similarities.Min().ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture))
                    .AppendLine(" |");
            }

            report.AppendLine();
            report.AppendLine("| Style | Slide | DOM/PDF issues | Web capture vs PowerPoint SSIM |");
            report.AppendLine("|---|---:|---:|---:|");
            foreach (var result in results)
            {
                for (var index = 0; index < result.Similarities.Count; index++)
                {
                    var slide = index + 1;
                    var issueCount = result.Validation.Issues.Count(issue => issue.Slide == slide);

                    report.Append("| ").Append(result.StyleSlug)
                        .Append(" | ").Append(slide).Append(" | ").Append(issueCount)
                        .Append(" | ").Append(result.Similarities[index].ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture))
                        .AppendLine(" |");
                }
            }

            report.AppendLine();
            report.AppendLine("## Font matrix width API diagnostics");
            report.AppendLine();
            report.AppendLine("Browser width is the union of per-grapheme DOM Range rectangles, converted from design pixels to points at 0.5 pt/px; PowerPoint width is `TextRange2.BoundWidth`. These are different engine/API bounds, not measurements of rasterized glyph ink. The signed delta `(PowerPoint - browser) / browser` is used only as a pragmatic ±3% regression guard, not proof of exact ink-width equality. Row-crop SSIM is exploratory: cross-engine antialiasing and subpixel rasterization have not been calibrated against known-good and known-bad controls, so it is not a pass/fail threshold.");
            report.AppendLine();
            report.AppendLine("| Style | Bundled faces measured | API deltas within 3% (regression guard) | Maximum absolute API delta | Minimum row-crop SSIM (exploratory) |");
            report.AppendLine("|---|---:|---:|---:|");
            foreach (var result in results)
            {
                var metrics = result.FontVisualMetrics;
                var withinThreshold = metrics.Count(metric => Math.Abs(metric.WidthDeltaPercent) <= FontWidthTolerancePercent);
                var maximumDelta = metrics.Count == 0 ? 0 : metrics.Max(metric => Math.Abs(metric.WidthDeltaPercent));
                var minimumRowSimilarity = metrics.Count == 0 ? 0 : metrics.Min(metric => metric.RowSimilarity);
                report.Append("| ").Append(result.StyleSlug)
                    .Append(" | ").Append(metrics.Count)
                    .Append(" | ").Append(withinThreshold)
                    .Append(" | ").Append(maximumDelta.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("% | ").Append(minimumRowSimilarity.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture))
                    .AppendLine(" |");
            }

            report.AppendLine();
            report.AppendLine("## Isolated font-script probes");
            report.AppendLine();
            report.AppendLine("Each API width increment is calculated by subtracting the shared `family/weight/style — X` baseline from the matching `baseline + sample` line. These increments are diagnostic and do not replace same-coordinate rendered-row comparison.");
            report.AppendLine();
            report.AppendLine("| Style | Family | Shared label + X delta | Latin sample delta | CJK sample delta |");
            report.AppendLine("|---|---|---:|---:|---:|");
            foreach (var metric in results.SelectMany(result => result.FontScriptProbeMetrics))
            {
                report.Append("| ").Append(metric.Style)
                    .Append(" | ").Append(metric.Family)
                    .Append(" | ").Append(metric.SharedLabelWidthDeltaPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("%")
                    .Append(" | ").Append(metric.LatinWidthDeltaPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("% | ").Append(metric.CjkWidthDeltaPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                    .AppendLine("% |");
            }

            report.AppendLine();
            report.AppendLine("## Validation issues");
            foreach (var result in results)
            {
                report.AppendLine("### " + result.StyleSlug);
                if (result.Validation.Issues.Count == 0)
                {
                    report.AppendLine("None.");
                }
                else
                {
                    foreach (var issue in result.Validation.Issues)
                    {
                        report.Append("- Slide ").Append(issue.Slide).Append(" `").Append(issue.Code).Append("` (")
                            .Append(issue.Severity).Append("): ").AppendLine(issue.Message);
                    }
                }
            }

            report.AppendLine();
            report.AppendLine("## PowerPoint build issues");
            foreach (var result in results)
            {
                report.AppendLine("### " + result.StyleSlug);
                if (result.Build.BuildIssues.Count == 0)
                {
                    report.AppendLine("None.");
                }
                else
                {
                    foreach (var issue in result.Build.BuildIssues)
                    {
                        report.Append("- Slide ").Append(issue.Slide).Append(" `").Append(issue.Code).Append("`: ")
                            .AppendLine(issue.Message);
                    }
                }
            }

            return report.ToString();
        }

        private static IList<TextShapeReadback> CaptureTextReadbacks(object slide, string styleSlug, int slideIndex)
        {
            var result = new List<TextShapeReadback>();
            var shapes = GetComProperty(slide, "Shapes");
            try
            {
                var count = Convert.ToInt32(GetComProperty(shapes, "Count"));
                dynamic powerPointShapes = shapes;
                for (var shapeIndex = 1; shapeIndex <= count; shapeIndex++)
                {
                    object shape = powerPointShapes[shapeIndex];
                    object textFrame = null;
                    object textRange = null;
                    object font = null;
                    try
                    {
                        if (Convert.ToInt32(GetComProperty(shape, "HasTextFrame")) == 0) continue;
                        textFrame = GetComProperty(shape, "TextFrame2");
                        if (Convert.ToInt32(GetComProperty(textFrame, "HasText")) == 0) continue;
                        textRange = GetComProperty(textFrame, "TextRange");
                        font = GetComProperty(textRange, "Font");
                        var text = Convert.ToString(GetComProperty(textRange, "Text"));
                        result.Add(new TextShapeReadback
                        {
                            Style = styleSlug,
                            Slide = slideIndex,
                            Shape = Convert.ToString(GetComProperty(shape, "Name")),
                            Text = text,
                            FontName = Convert.ToString(GetComProperty(font, "Name")),
                            FontNameFarEast = ReadCjkFarEastFontNames(textRange, text),
                            Bold = Convert.ToInt32(GetComProperty(font, "Bold")) != 0,
                            Italic = Convert.ToInt32(GetComProperty(font, "Italic")) != 0,
                            FontSizePoints = Convert.ToDouble(GetComProperty(font, "Size")),
                            FontSpacingPoints = Convert.ToDouble(GetComProperty(font, "Spacing")),
                            TextFrameAutoSize = Convert.ToInt32(GetComProperty(textFrame, "AutoSize")),
                            BoundWidthPoints = Convert.ToDouble(GetComProperty(textRange, "BoundWidth")),
                            Left = Convert.ToDouble(GetComProperty(shape, "Left")),
                            Top = Convert.ToDouble(GetComProperty(shape, "Top")),
                            Width = Convert.ToDouble(GetComProperty(shape, "Width")),
                            Height = Convert.ToDouble(GetComProperty(shape, "Height"))
                        });
                    }
                    finally
                    {
                        ReleaseComObject(font);
                        ReleaseComObject(textRange);
                        ReleaseComObject(textFrame);
                        ReleaseComObject(shape);
                    }
                }
            }
            finally
            {
                ReleaseComObject(shapes);
            }

            return result;
        }

        private static string ReadCjkFarEastFontNames(object textRange, string text)
        {
            var names = new List<string>();
            foreach (var textRun in CjkTextRanges.Find(text))
            {
                object characters = null;
                object font = null;
                try
                {
                    characters = GetComProperty(textRange, "Characters", textRun.Start + 1, textRun.Length);
                    font = GetComProperty(characters, "Font");
                    var name = Convert.ToString(GetComProperty(font, "NameFarEast"));
                    if (!string.IsNullOrWhiteSpace(name)
                        && !names.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        names.Add(name);
                    }
                }
                finally
                {
                    ReleaseComObject(font);
                    ReleaseComObject(characters);
                }
            }

            return string.Join(" | ", names);
        }

        private static void WriteTextReadbackCsv(string output, IEnumerable<StyleHarnessResult> results)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Style,Slide,Shape,Text,Font.Name,Font.NameFarEast(CJK runs),Bold,Italic,Font.SizePt,Font.SpacingPt,TextFrame.AutoSize,TextRange.BoundWidthPt,LeftPt,TopPt,WidthPt,HeightPt");
            foreach (var item in results.SelectMany(result => result.TextReadbacks))
            {
                csv.Append(Csv(item.Style)).Append(',')
                    .Append(item.Slide.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(Csv(item.Shape)).Append(',')
                    .Append(Csv(item.Text)).Append(',')
                    .Append(Csv(item.FontName)).Append(',')
                    .Append(Csv(item.FontNameFarEast)).Append(',')
                    .Append(item.Bold).Append(',')
                    .Append(item.Italic).Append(',')
                    .Append(FormatNumber(item.FontSizePoints)).Append(',')
                    .Append(FormatNumber(item.FontSpacingPoints)).Append(',')
                    .Append(item.TextFrameAutoSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(FormatNumber(item.BoundWidthPoints)).Append(',')
                    .Append(FormatNumber(item.Left)).Append(',')
                    .Append(FormatNumber(item.Top)).Append(',')
                    .Append(FormatNumber(item.Width)).Append(',')
                    .Append(FormatNumber(item.Height)).AppendLine();
            }

            File.WriteAllText(Path.Combine(output, "text-readback.csv"), csv.ToString(), new UTF8Encoding(false));
        }

        private static void WriteFontVisualMetricsCsv(string output, IEnumerable<StyleHarnessResult> results)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Style,Slide,Text,BrowserFontSizePx,PowerPointFontSizePt,BrowserLetterSpacingPx,PowerPointFontSpacingPt,TextFrameAutoSize,BrowserWidthPt,PowerPointBoundWidthPt,WidthDeltaPercent,RowCropSsim");
            foreach (var metric in results.SelectMany(result => result.FontVisualMetrics))
            {
                csv.Append(Csv(metric.Style)).Append(',')
                    .Append(metric.Slide.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(Csv(metric.Text)).Append(',')
                    .Append(FormatMetric(metric.BrowserFontSizePixels)).Append(',')
                    .Append(FormatMetric(metric.PowerPointFontSizePoints)).Append(',')
                    .Append(FormatMetric(metric.BrowserLetterSpacingPixels)).Append(',')
                    .Append(FormatMetric(metric.PowerPointFontSpacingPoints)).Append(',')
                    .Append(metric.TextFrameAutoSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(FormatMetric(metric.BrowserWidthPoints)).Append(',')
                    .Append(FormatMetric(metric.PowerPointBoundWidthPoints)).Append(',')
                    .Append(FormatMetric(metric.WidthDeltaPercent)).Append(',')
                    .Append(FormatMetric(metric.RowSimilarity)).AppendLine();
            }

            File.WriteAllText(Path.Combine(output, "font-visual-metrics.csv"), csv.ToString(), new UTF8Encoding(false));
        }

        private static void WriteFontScriptProbeCsv(string output, IEnumerable<StyleHarnessResult> results)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Style,Family,BrowserFontSizePx,PowerPointFontSizePt,SharedLabelWidthDeltaPercent,BrowserLatinWidthPt,PowerPointLatinWidthPt,LatinWidthDeltaPercent,BrowserCjkWidthPt,PowerPointCjkWidthPt,CjkWidthDeltaPercent");
            foreach (var metric in results.SelectMany(result => result.FontScriptProbeMetrics))
            {
                csv.Append(Csv(metric.Style)).Append(',')
                    .Append(Csv(metric.Family)).Append(',')
                    .Append(FormatMetric(metric.BrowserFontSizePixels)).Append(',')
                    .Append(FormatMetric(metric.PowerPointFontSizePoints)).Append(',')
                    .Append(FormatMetric(metric.SharedLabelWidthDeltaPercent)).Append(',')
                    .Append(FormatMetric(metric.BrowserLatinWidthPoints)).Append(',')
                    .Append(FormatMetric(metric.PowerPointLatinWidthPoints)).Append(',')
                    .Append(FormatMetric(metric.LatinWidthDeltaPercent)).Append(',')
                    .Append(FormatMetric(metric.BrowserCjkWidthPoints)).Append(',')
                    .Append(FormatMetric(metric.PowerPointCjkWidthPoints)).Append(',')
                    .Append(FormatMetric(metric.CjkWidthDeltaPercent)).AppendLine();
            }

            File.WriteAllText(Path.Combine(output, "font-script-probes.csv"), csv.ToString(), new UTF8Encoding(false));
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatMetric(double value)
        {
            return value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed class StyleHarnessResult
        {
            public string StyleSlug { get; set; }
            public int PreviewWidth { get; set; }
            public int PreviewHeight { get; set; }
            public DeckValidationCapture Validation { get; set; }
            public PowerPointDeckBuildResult Build { get; set; }
            public IList<double> Similarities { get; } = new List<double>();
            public List<TextShapeReadback> TextReadbacks { get; } = new List<TextShapeReadback>();
            public List<FontVisualMetric> FontVisualMetrics { get; } = new List<FontVisualMetric>();
            public List<FontScriptProbeMetric> FontScriptProbeMetrics { get; } = new List<FontScriptProbeMetric>();
        }

        private sealed class TextShapeReadback
        {
            public string Style { get; set; }
            public int Slide { get; set; }
            public string Shape { get; set; }
            public string Text { get; set; }
            public string FontName { get; set; }
            public string FontNameFarEast { get; set; }
            public bool Bold { get; set; }
            public bool Italic { get; set; }
            public double FontSizePoints { get; set; }
            public double FontSpacingPoints { get; set; }
            public int TextFrameAutoSize { get; set; }
            public double BoundWidthPoints { get; set; }
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
        }

        private sealed class FontVisualMetric
        {
            public string Style { get; set; }
            public int Slide { get; set; }
            public string Text { get; set; }
            public double BrowserFontSizePixels { get; set; }
            public double PowerPointFontSizePoints { get; set; }
            public double BrowserLetterSpacingPixels { get; set; }
            public double PowerPointFontSpacingPoints { get; set; }
            public int TextFrameAutoSize { get; set; }
            public double BrowserWidthPoints { get; set; }
            public double PowerPointBoundWidthPoints { get; set; }
            public double WidthDeltaPercent { get; set; }
            public double RowSimilarity { get; set; }
        }

        private sealed class FontScriptProbeMetric
        {
            public string Style { get; set; }
            public string Family { get; set; }
            public double BrowserFontSizePixels { get; set; }
            public double PowerPointFontSizePoints { get; set; }
            public double SharedLabelWidthDeltaPercent { get; set; }
            public double BrowserLatinWidthPoints { get; set; }
            public double PowerPointLatinWidthPoints { get; set; }
            public double LatinWidthDeltaPercent { get; set; }
            public double BrowserCjkWidthPoints { get; set; }
            public double PowerPointCjkWidthPoints { get; set; }
            public double CjkWidthDeltaPercent { get; set; }
        }

        private sealed class FontLineMeasurement
        {
            public double BrowserFontSizePixels { get; set; }
            public double PowerPointFontSizePoints { get; set; }
            public double BrowserWidthPoints { get; set; }
            public double PowerPointWidthPoints { get; set; }
        }

        private sealed class BrowserFontLineMeasurement
        {
            public double FontSizePixels { get; set; }
            public double LetterSpacingPixels { get; set; }
            public double WidthPoints { get; set; }
            public CapturedRectangle Rectangle { get; set; }
        }

        private sealed class TemplateMarginResult
        {
            public StyleEntry Style { get; set; }
            public string SourceInset { get; set; }
            public DeckValidationCapture Validation { get; set; }
        }

        private sealed class TemplateGallerySafeAreaResult
        {
            public StyleEntry Style { get; set; }
            public int SlideCount { get; set; }
            public string SourceInset { get; set; }
            public DeckValidationCapture Validation { get; set; }
        }

        private static object GetComProperty(object target, string name)
        {
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
        }

        private static object GetComProperty(object target, string name, params object[] arguments)
        {
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, arguments);
        }

        private static object InvokeCom(object target, string name, params object[] arguments)
        {
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments);
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }

        private static string ReadFixture(string root, string name)
        {
            return File.ReadAllText(Path.Combine(root, name), Encoding.UTF8);
        }

        private static string FindShippedAssets()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "OfficeCopilot.Slides.Core", "SlideAssets");
                if (File.Exists(Path.Combine(candidate, SlideAssetPaths.CatalogFileRelative))) return candidate;
                directory = directory.Parent;
            }

            return null;
        }

        private static Task RunOnStaUiThread(Func<Task> action)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                using (var form = new Form
                {
                    ShowInTaskbar = false,
                    Opacity = 0,
                    WindowState = FormWindowState.Minimized
                })
                {
                    form.Shown += async (sender, args) =>
                    {
                        try
                        {
                            await action().ConfigureAwait(true);
                            completion.TrySetResult(true);
                        }
                        catch (Exception exception)
                        {
                            completion.TrySetException(exception);
                        }
                        finally
                        {
                            form.Close();
                        }
                    };

                    Application.Run(form);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return completion.Task;
        }
    }
}
