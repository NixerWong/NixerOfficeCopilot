using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.PowerPoint;
using OfficeCopilot.Host.PowerPoint.Tools;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Catalog;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;
using OfficeCopilot.Slides.Html;

namespace OfficeCopilot.Agent.Tests.PowerPoint
{
    [TestClass]
    public sealed class SlideDeckTests
    {
        private string _root;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "nx-deck-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        // ── 清洗 ──────────────────────────────────────────────

        [TestMethod]
        public void Sanitizer_RemovesScriptsHandlersAndExternalUrls()
        {
            var result = SlideHtmlSanitizer.SanitizeSlide(
                "<section class=\"slide active\" data-layout=\"cover\" onclick=\"x()\">"
                + "<script>alert(1)</script><iframe src=\"https://evil\"></iframe>"
                + "<h1>  Hello \n World </h1>"
                + "<img src=\"https://evil.example/a.png\"><img src=\"assets/ok.png\"><img src=\"../../secret.png\">"
                + "<a href=\"javascript:alert(1)\">x</a><a href=\"#s02\">y</a>"
                + "<div style=\"background:url(https://evil/x.png);position:fixed\"></div>"
                + "<!-- secret --></section>");

            var html = result.Html;
            Assert.IsFalse(html.Contains("<script"), html);
            Assert.IsFalse(html.Contains("<iframe"), html);
            Assert.IsFalse(html.Contains("onclick"), html);
            Assert.IsFalse(html.Contains("evil"), html);
            Assert.IsFalse(html.Contains("secret"), html);
            Assert.IsFalse(html.Contains("javascript"), html);
            StringAssert.Contains(html, "src=\"assets/ok.png\"");
            StringAssert.Contains(html, "href=\"#s02\"");
            StringAssert.Contains(html, "position:absolute");
            Assert.IsFalse(html.Contains("active"), "active 类应移除");
            Assert.AreEqual("Hello World", result.Title);
            Assert.AreEqual("cover", result.Layout);
            Assert.IsTrue(result.Removed.Count > 0);
        }

        [TestMethod]
        public void Sanitizer_WrapsBareContent_AndMovesStrayStyles()
        {
            var bare = SlideHtmlSanitizer.SanitizeSlide("<style>.a{color:red}</style><h2>T</h2><p>x</p>");
            StringAssert.StartsWith(bare.Html, "<section class=\"slide\">");
            StringAssert.Contains(bare.Html, ".a{color:red}");

            var stray = SlideHtmlSanitizer.SanitizeSlide("<style>.b{}</style><section class=\"slide\"><h1>A</h1></section><p>after</p>");
            StringAssert.StartsWith(stray.Html, "<section class=\"slide\"><style>.b{}</style>");
            StringAssert.Contains(stray.Html, "<p>after</p></section>");

            var wrapped = SlideHtmlSanitizer.SanitizeSlide("<div class=\"deck\"><section class=\"slide\"><h1>Only</h1></section></div>");
            StringAssert.StartsWith(wrapped.Html, "<section class=\"slide\"><h1>Only</h1>");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Sanitizer_RejectsMultipleSlides()
        {
            SlideHtmlSanitizer.SanitizeSlide("<section class=\"slide\"></section><section class=\"slide\"></section>");
        }

        [TestMethod]
        public void Sanitizer_ExpandsSupportedNxIconToBundledSvg()
        {
            var result = SlideHtmlSanitizer.SanitizeSlide(
                "<section class=\"slide\"><p>Before <i class=\"nx-icon\" data-icon=\"leaf\"></i> after</p></section>");

            StringAssert.Contains(result.Html, "data-nx-icon=\"leaf\"");
            StringAssert.Contains(result.Html, "viewBox=\"0 0 24 24\"");
            StringAssert.Contains(result.Html, "stroke=\"currentColor\"");
            StringAssert.Contains(result.Html, "M11 20a10 10 0 0010-10");
            Assert.IsFalse(result.Html.Contains("data-icon="));
            StringAssert.Contains(result.Html, "\u2003");
        }

        [TestMethod]
        public void Sanitizer_ResanitizesAlreadyExpandedNxIcon()
        {
            var firstPass = SlideHtmlSanitizer.SanitizeSlide(
                "<section class=\"slide\"><i class=\"nx-icon\" data-icon=\"leaf\"></i></section>");
            var secondPass = SlideHtmlSanitizer.SanitizeSlide(firstPass.Html);

            StringAssert.Contains(secondPass.Html, "data-nx-icon=\"leaf\"");
            Assert.AreEqual(1, secondPass.Html.Split(new[] { "<svg" }, StringSplitOptions.None).Length - 1);
        }

        [TestMethod]
        public void Sanitizer_NormalizesLucidePrefixAndLegacyIconNames()
        {
            var result = SlideHtmlSanitizer.SanitizeSlide(
                "<section class=\"slide\"><i class=\"nx-icon\" data-icon=\"lucide-bug\"></i><i class=\"nx-icon\" data-icon=\"home\"></i></section>");

            StringAssert.Contains(result.Html, "data-nx-icon=\"bug\"");
            StringAssert.Contains(result.Html, "data-nx-icon=\"house\"");
        }

        [TestMethod]
        public void Sanitizer_SubstitutesUnknownIconsInsteadOfRejecting()
        {
            var result = SlideHtmlSanitizer.SanitizeSlide(
                "<section class=\"slide\"><i class=\"nx-icon\" data-icon=\"map-pins\"></i>"
                + "<i class=\"nx-icon\" data-icon=\"zzqx-unknown\"></i>"
                + "<i class=\"nx-icon\" data-icon=\"package\"></i></section>");

            StringAssert.Contains(result.Html, "data-nx-icon=\"map-pin\"");
            StringAssert.Contains(result.Html, "data-nx-icon=\"sparkle\"");
            StringAssert.Contains(result.Html, "data-nx-icon=\"package\"");
            Assert.IsTrue(result.Removed.Any(r => r.Contains("map-pins") && r.Contains("map-pin")));
            Assert.IsTrue(result.Removed.Any(r => r.Contains("zzqx-unknown") && r.Contains("sparkle")));
            Assert.IsFalse(result.Removed.Any(r => r.Contains("随包无“package”")));
        }

        [TestMethod]
        public void CssSanitizer_PinsMissingEdgeOnChromeBar()
        {
            var css = CssSanitizer.Sanitize(
                ".nx-chrome{position:absolute;bottom:40px;display:flex}"
                + ".nx-chrome .pg{bottom:0}"
                + ".slide .nx-chrome.top{top:40px}"
                + ".nx-content{bottom:96px}"
                + ".nx-chrome.full{top:48px;bottom:48px}",
                new List<string>());

            StringAssert.Contains(css, ".nx-chrome{position:absolute;bottom:40px;display:flex;top:auto}");
            StringAssert.Contains(css, ".nx-chrome .pg{bottom:0}");
            StringAssert.Contains(css, ".slide .nx-chrome.top{top:40px;bottom:auto}");
            StringAssert.Contains(css, ".nx-content{bottom:96px}");
            StringAssert.Contains(css, ".nx-chrome.full{top:48px;bottom:48px}");

            var slide = SlideHtmlSanitizer.SanitizeSlide(
                "<section class=\"slide\"><div class=\"nx-chrome\" style=\"bottom:40px\">p</div></section>");
            StringAssert.Contains(slide.Html, "bottom:40px;top:auto");
        }

        [TestMethod]
        public void Sanitizer_RejectsExternalIconClasses()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                SlideHtmlSanitizer.SanitizeSlide(
                    "<section class=\"slide\"><i class=\"fa fa-leaf\"></i></section>"));
        }

        [TestMethod]
        public void CssSanitizer_StripsImportsAndDangerousConstructs()
        {
            var removed = new List<string>();
            var css = CssSanitizer.Sanitize(
                "@import url('https://x/y.css'); .a{background:url(\"assets/bg.png\")} .b{background:url(//cdn/x.png)} .c{width:expression(alert(1))} .d{position: fixed}",
                removed);

            Assert.IsFalse(css.Contains("@import"), css);
            StringAssert.Contains(css, "url(\"assets/bg.png\")");
            Assert.IsFalse(css.Contains("cdn"), css);
            Assert.IsFalse(css.Contains("expression("), css);
            StringAssert.Contains(css, "position:absolute");
            Assert.IsTrue(removed.Count >= 4);
        }

        [TestMethod]
        public void CssSanitizer_SnapsUnsupportedBundledWeightsAndReportsChange()
        {
            var assets = FindShippedAssets();
            Assert.IsNotNull(assets, "未找到 SlideAssets。");
            var fonts = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var removed = new List<string>();
            var css = CssSanitizer.Sanitize(
                ".a{font-family:'Noto Sans SC',sans-serif;font-weight:900}"
                    + ".b{font-family:'Unknown Font','Albert Sans',sans-serif;font-weight:900!important}"
                    + ".c{font-family:'Unknown Font',sans-serif;font-weight:900}"
                    + ".d{font-family:'Albert Sans',sans-serif;font-weight:550}"
                    + ".e{font-family:'Albert Sans',sans-serif;font-weight:600;font-style:italic}",
                removed,
                fonts);

            Assert.IsTrue(css.Contains("font-weight:700}"), css);
            Assert.IsTrue(css.Contains("font-weight:700!important}"), css);
            Assert.IsTrue(css.Contains(".c{font-family:'Unknown Font',sans-serif;font-weight:900}"), css);
            Assert.IsTrue(css.Contains(".d{font-family:'Albert Sans',sans-serif;font-weight:600}"), css);
            Assert.IsTrue(css.Contains(".e{font-family:'Albert Sans',sans-serif;font-weight:600;font-style:italic}"), css);
            Assert.AreEqual(2, removed.Count(item => item.StartsWith("font-weight 900 → 700", StringComparison.Ordinal)), string.Join(" | ", removed));
            Assert.AreEqual(1, removed.Count(item => item.StartsWith("font-weight 550 → 600", StringComparison.Ordinal)), string.Join(" | ", removed));
        }

        [TestMethod]
        public void SlideSanitizer_SnapsInlineAndEmbeddedStyleWeights()
        {
            var assets = FindShippedAssets();
            Assert.IsNotNull(assets, "未找到 SlideAssets。");
            var fonts = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var sanitized = SlideHtmlSanitizer.SanitizeSlide(
                "<style>.title{font-family:'Noto Sans SC';font-weight:900}</style>"
                    + "<section class=\"slide\"><h1 style=\"font-family:'Noto Sans SC';font-weight:900\">Title</h1></section>",
                fonts);

            StringAssert.Contains(sanitized.Html, "font-weight:700");
            Assert.AreEqual(1, sanitized.Removed.Count(item =>
                item.StartsWith("font-weight 900 → 700", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void SafeUrl_OnlyAllowsWorkspaceRelativePaths()
        {
            Assert.IsTrue(SafeUrl.IsWorkspaceRelative("assets/a.png"));
            Assert.IsTrue(SafeUrl.IsWorkspaceRelative("#frag"));
            Assert.IsTrue(SafeUrl.IsWorkspaceRelative("a/b.png?v=1"));
            Assert.IsFalse(SafeUrl.IsWorkspaceRelative("../a.png"));
            Assert.IsFalse(SafeUrl.IsWorkspaceRelative("a/%2e%2e/b.png"));
            Assert.IsFalse(SafeUrl.IsWorkspaceRelative("/abs.png"));
            Assert.IsFalse(SafeUrl.IsWorkspaceRelative("//cdn/x.png"));
            Assert.IsFalse(SafeUrl.IsWorkspaceRelative("a\\b.png"));
            Assert.IsFalse(SafeUrl.IsWorkspaceRelative("C:/x.png"));
            Assert.IsFalse(SafeUrl.IsWorkspaceRelative("javascript:alert(1)"));
            Assert.IsTrue(SafeUrl.IsDataImage("data:image/png;base64,AAAA"));
            Assert.IsFalse(SafeUrl.IsDataImage("data:text/html,<script>"));
        }

        // ── 工作区 ────────────────────────────────────────────

        [TestMethod]
        public void Workspace_PutRemoveReorder_KeepsManifestInSync()
        {
            var store = new DeckWorkspaceStore(_root);
            var ws = store.Create("alpha", "high", null, "Deck");
            Assert.IsTrue(DeckIds.IsValidDeckId(ws.Id), ws.Id);
            Assert.AreEqual("high", ws.Manifest.Density);
            Assert.AreEqual("zh-CN", ws.Manifest.Language);

            var put = ws.PutSlides(new[]
            {
                new DeckSlideInput { Html = "<section class=\"slide\"><h1>One</h1></section>" },
                new DeckSlideInput { Html = "<h1>Two</h1>" },
                new DeckSlideInput { Html = "<h1>Zero</h1>", Position = 1 }
            });
            CollectionAssert.AreEqual(new[] { "s01", "s02", "s03" }, put.Select(r => r.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "s03", "s01", "s02" }, ws.Slides.Select(s => s.Id).ToArray());
            Assert.AreEqual(1, put[2].Position);

            var hashBefore = ws.Slides[1].Hash;
            ws.PutSlides(new[] { new DeckSlideInput { Id = "S01", Html = "<h1>One v2</h1>" } });
            Assert.AreNotEqual(hashBefore, ws.Slides[1].Hash);
            Assert.AreEqual("One v2", ws.Slides[1].Title);

            ws.RemoveSlides(new[] { "s03" });
            ws.Reorder(new[] { "s02", "s01" });

            var reopened = store.Open(ws.Id);
            CollectionAssert.AreEqual(new[] { "s02", "s01" }, reopened.Slides.Select(s => s.Id).ToArray());
            StringAssert.Contains(reopened.GetSlideHtml("s01"), "One v2");
            Assert.IsFalse(File.Exists(Path.Combine(reopened.SlidesDir, "s03.html")));

            var next = reopened.PutSlides(new[] { new DeckSlideInput { Html = "<h1>Four</h1>" } });
            Assert.AreEqual("s03", next[0].Id, "新 id 取当前最大编号 + 1");
        }

        [TestMethod]
        public void Workspace_ValidatesAllBeforeWriting()
        {
            var ws = new DeckWorkspaceStore(_root).Create(null, null, null, null);
            ws.PutSlides(new[] { new DeckSlideInput { Html = "<h1>A</h1>" } });

            AssertDeckError("ppt.deck_invalid_html", () => ws.PutSlides(new[]
            {
                new DeckSlideInput { Html = "<h1>B</h1>" },
                new DeckSlideInput { Html = "<section class=\"slide\"></section><section class=\"slide\"></section>" }
            }));
            Assert.AreEqual(1, ws.Slides.Count, "校验失败时不得部分写入");

            AssertDeckError("ppt.deck_slide_not_found", () => ws.PutSlides(new[] { new DeckSlideInput { Id = "s09", Html = "<h1>x</h1>" } }));
            AssertDeckError("ppt.deck_invalid_order", () => ws.Reorder(new[] { "s01", "s01" }));
            AssertDeckError("ppt.deck_slide_not_found", () => ws.RemoveSlides(new[] { "s02" }));
        }

        [TestMethod]
        public void Store_RejectsBadIdsAndPurgesOldWorkspaces()
        {
            var store = new DeckWorkspaceStore(_root);
            AssertDeckError("ppt.deck_invalid_argument", () => store.Open("..\\..\\Windows"));
            AssertDeckError("ppt.deck_not_found", () => store.Open("20000101-000000-abcdef"));

            var old = store.Create(null, null, null, null);
            File.SetLastWriteTimeUtc(Path.Combine(old.Directory, DeckWorkspace.ManifestFileName), DateTime.UtcNow.AddDays(-40));
            var fresh = store.Create(null, null, null, null);

            Assert.AreEqual(1, store.Purge(TimeSpan.FromDays(30)));
            Assert.IsFalse(Directory.Exists(old.Directory));
            Assert.IsTrue(Directory.Exists(fresh.Directory));
        }

        [TestMethod]
        public void ThemeCss_IsSanitized()
        {
            var ws = new DeckWorkspaceStore(_root).Create(null, null, null, null);
            var removed = ws.WriteThemeCss("@import 'x.css'; :root{--c:#123}");
            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual(" :root{--c:#123}", ws.ReadThemeCss());
        }

        // ── URL 映射 ─────────────────────────────────────────

        [TestMethod]
        public void UrlResolver_MapsOwnHostOnly_AndBlocksTraversal()
        {
            var builtIn = Path.Combine(_root, "lib");
            var user = Path.Combine(_root, "user");
            var decks = Path.Combine(_root, "decks");
            var resolver = new DeckUrlResolver(builtIn, user, decks);
            string path;

            Assert.IsTrue(resolver.TryResolve("https://nixer-deck.local/lib/runtime/nixer-stage.js", out path));
            Assert.AreEqual(Path.Combine(builtIn, "runtime", "nixer-stage.js"), path);
            Assert.IsTrue(resolver.TryResolve("https://nixer-deck.local/decks/20250101-120000-abc123/build/deck.html?mode=capture#3", out path));
            Assert.AreEqual(Path.Combine(decks, "20250101-120000-abc123", "build", "deck.html"), path);
            Assert.IsTrue(resolver.TryResolve("https://nixer-deck.local/user/fonts/My%20Font.ttf", out path));
            Assert.AreEqual(Path.Combine(user, "fonts", "My Font.ttf"), path);

            Assert.IsFalse(resolver.TryResolve("http://nixer-deck.local/lib/a.css", out path));
            Assert.IsFalse(resolver.TryResolve("https://evil.example/lib/a.css", out path));
            Assert.IsFalse(resolver.TryResolve("https://nixer-deck.local:8443/lib/a.css", out path));
            Assert.IsFalse(resolver.TryResolve("https://nixer-deck.local/lib/%2e%2e/%2e%2e/secret.css", out path));
            Assert.IsFalse(resolver.TryResolve("https://nixer-deck.local/lib/a%5c..%5csecret.css", out path));
            Assert.IsFalse(resolver.TryResolve("https://nixer-deck.local/decks/not-an-id/x.html", out path));
            Assert.IsFalse(resolver.TryResolve("https://nixer-deck.local/other/x.css", out path));
            Assert.IsFalse(resolver.TryResolve("https://nixer-deck.local/lib/tool.exe", out path));
        }

        // ── 组装 ─────────────────────────────────────────────

        [TestMethod]
        public void Assembler_TemplatePath_AbsolutizesHeadAndPrunesChrome()
        {
            var builtIn = Path.Combine(_root, "lib");
            var dir = Path.Combine(builtIn, "templates", "demo");
            Write(Path.Combine(dir, "template.html"),
                "<!DOCTYPE html><html><head><title>T</title>"
                + "<link rel=\"stylesheet\" href=\"../../fonts/fonts.css\">"
                + "<script src=\"deck-stage.js\"></script><script src=\"../../runtime/vendor/chart.umd.min.js\"></script>"
                + "<style>.slide{background:url(img/bg.png)}</style></head><body>"
                + "<svg width=\"0\" height=\"0\"><defs><linearGradient id=\"g\"></linearGradient></defs></svg>"
                + "<nav class=\"dots\">…</nav><div class=\"counter\">1/2</div>"
                + "<div class=\"deck\"><div class=\"progress\"></div>"
                + "<section class=\"slide active\"><h1>Template A</h1></section><section class=\"slide\"><h1>Template B</h1></section>"
                + "</div><script>initDeck()</script></body></html>");

            var style = new StyleEntry { Slug = "demo", Kind = StyleKinds.Template, Directory = dir, Origin = "builtin" };
            var html = DeckAssembler.Assemble(new DeckAssemblyOptions
            {
                DeckId = "20250101-120000-abc123",
                Style = style,
                Slides = new[] { "<section class=\"slide\"><h1>Mine 1</h1></section>", "<section class=\"slide\"><h1>Mine 2</h1></section>" },
                ThemeCss = ":root{--x:1}</style><script>",
                Language = "en",
                Title = "My Deck"
            });

            StringAssert.Contains(html, "<base href=\"https://nixer-deck.local/decks/20250101-120000-abc123/\">");
            StringAssert.Contains(html, "<title>My Deck</title>");
            StringAssert.Contains(html, "href=\"https://nixer-deck.local/lib/fonts/fonts.css\"");
            StringAssert.Contains(html, "src=\"https://nixer-deck.local/lib/runtime/vendor/chart.umd.min.js\"");
            StringAssert.Contains(html, "url(https://nixer-deck.local/lib/templates/demo/img/bg.png)");
            StringAssert.Contains(html, "src=\"https://nixer-deck.local/lib/runtime/nixer-stage.js\"");
            StringAssert.Contains(html, "lang=\"en\"");
            StringAssert.Contains(html, "<div class=\"deck\">");
            StringAssert.Contains(html, "linearGradient");
            StringAssert.Contains(html, "Mine 1");
            StringAssert.Contains(html, "Mine 2");
            Assert.IsTrue(html.IndexOf("Mine 1", StringComparison.Ordinal) < html.IndexOf("Mine 2", StringComparison.Ordinal));
            Assert.IsFalse(html.Contains("Template A"), "模板页应被替换");
            Assert.IsFalse(html.Contains("deck-stage.js"));
            Assert.IsFalse(html.Contains("initDeck"));
            Assert.IsFalse(html.Contains("class=\"dots\""));
            Assert.IsFalse(html.Contains("class=\"counter\""));
            Assert.IsFalse(html.Contains("class=\"progress\""));
            Assert.IsFalse(html.Contains("</style><script>"), "主题 CSS 不得闭合 style");
            Assert.AreEqual(1, CountOf(html, "fonts/fonts.css"));

            var validationHtml = DeckAssembler.AssembleForValidation(new DeckAssemblyOptions
            {
                DeckId = "20250101-120000-abc123",
                Style = style,
                Slides = new[] { "<section class=\"slide\"><h1>Validation</h1></section>" }
            });
            Assert.IsFalse(validationHtml.Contains("runtime/nixer-stage.js"));
            StringAssert.Contains(validationHtml, "height:1080px!important;min-height:0!important;overflow:hidden!important");
            StringAssert.Contains(validationHtml, "body[data-only] .slide.nx-only");
            StringAssert.Contains(validationHtml, "body[data-only] .slide.nx-only{display:flow-root!important;break-after:auto!important");
            StringAssert.Contains(validationHtml, "Validation");
        }

        [TestMethod]
        public void Assembler_TemplateCss_KeepsStructureAndDropsSampleContentRules()
        {
            var dir = Path.Combine(_root, "lib", "templates", "iso");
            Write(Path.Combine(dir, "template.html"),
                "<!DOCTYPE html><html><head><title>T</title><style>"
                + "/* comment {x} */@import url('x.css');"
                + "@font-face{font-family:Demo;src:url(f.woff2)}"
                + ":root{--ink:#111}*,*::before{box-sizing:border-box}html,body{margin:0;font-family:Demo}"
                + ".presentation{position:relative}.slide{position:absolute;inset:0;opacity:0}.slide.active{opacity:1}"
                + ".slide:hover{color:red}.slide::after{content:''}"
                + ".pill{font-family:'Space Grotesk';border-radius:999px}.kicker,.slide{letter-spacing:2px}"
                + ".slide-1 h1{font-size:9vw}.slide h2{color:blue}.nav-dots{display:flex}"
                + "@media (max-width:800px){.pill{padding:0}.slide{padding:8px}}"
                + "@keyframes fade{from{opacity:0}to{opacity:1}}"
                + "</style></head><body><nav class=\"nav-dots\"></nav><div class=\"presentation\">"
                + "<div class=\"slide slide-1 active\"><h1 class=\"kicker\">A</h1><span class=\"pill\">p</span></div>"
                + "<div class=\"slide slide-2\"><h2>B</h2></div></div></body></html>");

            var style = new StyleEntry { Slug = "iso", Kind = StyleKinds.Template, Directory = dir, Origin = "builtin" };
            var html = DeckAssembler.Assemble(new DeckAssemblyOptions
            {
                DeckId = "20250101-120000-abc123",
                Style = style,
                Slides = new[] { "<section class=\"slide\"><p class=\"pill\">Mine</p></section>" }
            });

            var head = html.Substring(0, html.IndexOf("</head>", StringComparison.Ordinal));
            foreach (var kept in new[] { "@font-face", ":root{--ink:#111}", "*, *::before{", "html, body{", ".presentation{", ".slide{position:absolute",
                ".slide.active{", ".slide:hover{", ".slide::after{", ".slide{letter-spacing:2px}", "@media (max-width:800px){", ".slide{padding:8px}", "@keyframes fade", "@import" })
            {
                StringAssert.Contains(head, kept);
            }

            foreach (var dropped in new[] { ".pill", ".kicker", ".slide-1", ".slide h2", ".nav-dots", "comment" })
            {
                Assert.IsFalse(head.Contains(dropped), "模板内容规则应被移除：" + dropped);
            }

            StringAssert.Contains(html, "<p class=\"pill\">Mine</p>");
            CollectionAssert.AreEqual(new[] { "active", "presentation", "slide" }, DeckAssembler.GetTemplateReservedClasses(style).ToArray());
        }

        [TestMethod]
        public void Assembler_PresetOrMissingStyle_UsesShell()
        {
            var html = DeckAssembler.Assemble(new DeckAssemblyOptions
            {
                DeckId = "20250101-120000-abc123",
                Style = new StyleEntry { Slug = "p", Kind = StyleKinds.Preset },
                Slides = new[] { "<section class=\"slide\"><h1>X</h1></section>" }
            });

            StringAssert.Contains(html, "<div class=\"deck-viewport\"><div class=\"deck-stage\">");
            StringAssert.Contains(html, "runtime/viewport-base.css");
            StringAssert.Contains(html, "<h1>X</h1>");
            StringAssert.Contains(html, "lang=\"zh-CN\"");
            StringAssert.Contains(html, "--nx-safe-x:128px");
            StringAssert.Contains(html, "--nx-action-x:64px");
            StringAssert.Contains(html, ":where(.nx-safe){position:absolute");
            StringAssert.Contains(html, "--nx-layer:background");
        }

        [TestMethod]
        public void Assembler_DeclarativeChartLoadsBundledRuntimeAndPreservesPayload()
        {
            const string payload = "{&quot;type&quot;:&quot;bar&quot;,&quot;labels&quot;:[&quot;Q1&quot;],&quot;series&quot;:[{&quot;name&quot;:&quot;Revenue&quot;,&quot;values&quot;:[12]}]}";
            var html = DeckAssembler.Assemble(new DeckAssemblyOptions
            {
                DeckId = "20250101-120000-abc123",
                Slides = new[] { "<section class=\"slide\"><canvas data-nx-chart=\"" + payload + "\"></canvas></section>" }
            });

            StringAssert.Contains(html, "runtime/vendor/chart.umd.min.js");
            StringAssert.Contains(html, "data-nx-chart=");
            StringAssert.Contains(html, "&quot;type&quot;:&quot;bar&quot;");
        }

        [TestMethod]
        public void Assembler_ShippedTemplates_ProduceSlidesWithoutTemplateScripts()
        {
            var root = FindShippedAssets();
            if (root == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalogs = new SlideAssetCatalogs(new SlideAssetPaths(root, Path.Combine(_root, "no-user")));
            foreach (var entry in catalogs.Styles.Entries.Where(e => e.Kind == StyleKinds.Template && e.IsValid))
            {
                var html = DeckAssembler.Assemble(new DeckAssemblyOptions
                {
                    DeckId = "20250101-120000-abc123",
                    Style = entry,
                    Slides = new[] { "<section class=\"slide\"><h1>NX-PROBE</h1></section>" }
                });

                Assert.AreEqual(1, CountOf(html, "NX-PROBE"), entry.Slug);
                Assert.AreEqual(1, CountOf(html, "nixer-stage.js"), entry.Slug);
                Assert.IsFalse(html.Contains("deck-stage.js"), entry.Slug);
                Assert.IsFalse(html.Contains("nx-slides-marker"), entry.Slug);
                Assert.IsTrue(
                    html.Contains("https://nixer-deck.local/lib/fonts/fonts.css"),
                    "缺少模板公共字体资源地址：" + entry.Slug);
            }
        }

        // ── 工具 ─────────────────────────────────────────────

        [TestMethod]
        public void DeckGroup_IsRegistered_WithBuildAsDocumentWritingTool()
        {
            var tools = PowerPointBuiltInToolCatalog.CreateAllTools();
            var deck = tools.Where(t => t.Definition.Name.StartsWith("ppt_deck_", StringComparison.Ordinal)).ToList();
            Assert.AreEqual(8, deck.Count);
            foreach (var t in deck.Where(t => t.Definition.Name != PowerPointToolNames.DeckBuild))
            {
                string group;
                Assert.IsTrue(ToolCatalogBuilder.TryGetGroupName(t.Definition, out group));
                Assert.AreEqual(PowerPointToolGroups.Deck, group);
                Assert.IsFalse(t.Definition.AffectsDocument, t.Definition.Name);
            }

            Assert.IsTrue(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckGetSlides).Definition.IsReadOnly);
            Assert.IsTrue(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckPreview).Definition.IsReadOnly);
            Assert.IsTrue(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckValidate).Definition.IsReadOnly);
            Assert.IsFalse(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckValidate).Definition.AffectsDocument);
            StringAssert.Contains(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckValidate).Definition.Description, "global CSS class conflicts");
            Assert.IsFalse(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.IsReadOnly);
            Assert.IsTrue(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.AffectsDocument);
            Assert.IsTrue(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.RequiresBackup);
            Assert.IsTrue(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.RequiresConfirmation);
            StringAssert.Contains(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.Description, "does not run ppt_deck_validate or perform a PowerPoint read-back or visual verification");
            StringAssert.DoesNotMatch(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.Description, new System.Text.RegularExpressions.Regex("构建前先用 ppt_deck_validate"));
            StringAssert.Contains(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.InputSchemaJson, "replace");
            StringAssert.Contains(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.InputSchemaJson, "fromIndex");
            StringAssert.Contains(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.InputSchemaJson, "^[0-9]{8}-[0-9]{6}-[a-z0-9]{6}$");
            StringAssert.Contains(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.InputSchemaJson, "\"workspaceTitle\"");
            StringAssert.Contains(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.InputSchemaJson, "\"slideCount\"");
            StringAssert.Contains(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.Description, "complete ID returned by ppt_deck_create");
            StringAssert.Contains(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.Description, "deckId may be omitted");
            StringAssert.DoesNotMatch(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckBuild).Definition.InputSchemaJson, new System.Text.RegularExpressions.Regex("\"required\""));
            Assert.IsFalse(deck.Single(t => t.Definition.Name == PowerPointToolNames.DeckPutSlides).Definition.IsReadOnly);
            Assert.IsFalse(tools.Single(t => t.Definition.Name == PowerPointToolNames.StylePreview).Definition.AffectsDocument);
        }

        [TestMethod]
        public void DeckBuild_ReplaceArgumentResolvesToReplaceTarget()
        {
            var tool = new BuildToolProbe();
            var target = tool.Resolve(new JObject
            {
                ["replace"] = true,
                ["fromIndex"] = 3,
                ["toIndex"] = 8
            });

            Assert.IsTrue(target.IsReplace);
            Assert.AreEqual(3, target.ReplaceFrom);
            Assert.AreEqual(8, target.ReplaceTo);
            Assert.ThrowsException<DeckException>(() => tool.Resolve(new JObject
            {
                ["replace"] = true,
                ["target"] = "new"
            }));
        }

        [TestMethod]
        public void DeckBuild_MissingTargetIsLeftUndecided()
        {
            var tool = new BuildToolProbe();
            var target = tool.Resolve(new JObject());

            Assert.IsNull(target.Kind);
            Assert.IsFalse(target.IsNew || target.IsReplace || target.IsReuseBlank);
            Assert.IsTrue(tool.Resolve(new JObject { ["target"] = "append" }).Kind == PowerPointDeckBuildTarget.AppendKind);
            StringAssert.Contains(new PowerPointDeckBuildTool().Definition.Description, "runtime.ask_user_question");
        }

        [TestMethod]
        public void WriteAndVbaGroups_AreRegisteredWithSafetyFlags()
        {
            var tools = PowerPointBuiltInToolCatalog.CreateAllTools();
            Func<string, ToolDefinition> def = name => tools.Single(t => t.Definition.Name == name).Definition;
            foreach (var name in new[]
            {
                PowerPointToolNames.SetShapeText, PowerPointToolNames.FormatShapes, PowerPointToolNames.ArrangeShapes,
                PowerPointToolNames.DeleteShapes, PowerPointToolNames.AddTextBox, PowerPointToolNames.InsertPicture,
                PowerPointToolNames.SetNotes, PowerPointToolNames.AddSlides, PowerPointToolNames.DeleteSlides, PowerPointToolNames.MoveSlide
            })
            {
                string group;
                Assert.IsTrue(ToolCatalogBuilder.TryGetGroupName(def(name), out group), name);
                Assert.AreEqual(PowerPointToolGroups.Write, group, name);
                Assert.IsTrue(def(name).AffectsDocument, name);
                Assert.IsTrue(def(name).RequiresBackup, name);
            }

            Assert.IsTrue(def(PowerPointToolNames.DeleteSlides).RequiresConfirmation);
            Assert.IsTrue(def(PowerPointToolNames.ExecuteVba).RequiresConfirmation);
            Assert.AreEqual("high", def(PowerPointToolNames.ExecuteVba).RiskLevel);
            Assert.IsFalse(def(PowerPointToolNames.SaveWithFonts).AffectsDocument);
            Assert.IsTrue(def(PowerPointToolNames.ExportSlideImage).IsReadOnly);
            Assert.IsTrue(def(PowerPointToolNames.ExtractContent).IsReadOnly);
            Assert.IsTrue(def(PowerPointToolNames.CheckFonts).IsReadOnly);
            CollectionAssert.Contains(PowerPointToolGroups.All.ToList(), PowerPointToolGroups.Vba);
        }

        [TestMethod]
        public async Task DeckTools_CreatePutGetReorderRemove()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var catalogs = CreateCatalogs();
            Func<DeckWorkspaceStore> s = () => store;
            Func<SlideAssetCatalogs> c = () => catalogs;
            var ctx = new ToolExecutionContext();

            var create = await new PowerPointDeckCreateTool(s, c).ExecuteAsync(ctx, Req("{\"style\":\"alpha\",\"title\":\"Q3\",\"css\":\":root{--a:1}\"}"));
            Assert.IsTrue(create.Success, create.Content);
            var deckId = store.Open(Directory.GetDirectories(store.Root).Single().Split('\\').Last()).Id;
            StringAssert.Contains(create.Content, deckId);

            var missingStyle = await new PowerPointDeckCreateTool(s, c).ExecuteAsync(ctx, Req("{\"style\":\"nope\"}"));
            Assert.IsFalse(missingStyle.Success);
            Assert.AreEqual("ppt.style_not_found", missingStyle.ErrorCode);

            var put = await new PowerPointDeckPutSlidesTool(s, c).ExecuteAsync(ctx, Req(
                "{\"deckId\":\"" + deckId + "\",\"slides\":[{\"html\":\"<h1>A</h1>\"},{\"html\":\"<h1>B</h1><script>x</script>\"}]}"));
            Assert.IsTrue(put.Success, put.Content);
            StringAssert.Contains(put.Content, "s02");
            StringAssert.Contains(put.Content, "<script>");

            var get = await new PowerPointDeckGetSlidesTool(s, c).ExecuteAsync(ctx, Req("{\"deckId\":\"" + deckId + "\",\"ids\":[\"s02\"],\"includeCss\":true}"));
            Assert.IsTrue(get.Success, get.Content);
            StringAssert.Contains(get.Content, "<h1>B</h1>");
            StringAssert.Contains(get.Content, "--a:1");

            var reorder = await new PowerPointDeckReorderTool(s, c).ExecuteAsync(ctx, Req("{\"deckId\":\"" + deckId + "\",\"order\":[\"s02\",\"s01\"]}"));
            Assert.IsTrue(reorder.Success, reorder.Content);

            var remove = await new PowerPointDeckRemoveSlidesTool(s, c).ExecuteAsync(ctx, Req("{\"deckId\":\"" + deckId + "\",\"ids\":[\"s01\"]}"));
            Assert.IsTrue(remove.Success, remove.Content);
            CollectionAssert.AreEqual(new[] { "s02" }, store.Open(deckId).Slides.Select(p => p.Id).ToArray());

            var badDeck = await new PowerPointDeckGetSlidesTool(s, c).ExecuteAsync(ctx, Req("{\"deckId\":\"../x\"}"));
            Assert.IsFalse(badDeck.Success);
            Assert.AreEqual("ppt.deck_invalid_argument", badDeck.ErrorCode);
        }

        [TestMethod]
        public async Task DeckPreview_BuildsDeckAndOpensWindow()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var catalogs = CreateCatalogs();
            var ws = store.Create("alpha", null, null, "T");
            ws.PutSlides(new[] { new DeckSlideInput { Html = "<h1>A</h1>" }, new DeckSlideInput { Html = "<h1>B</h1>" } });
            var renderer = new FakeRenderer();
            var ctx = CreateContext(renderer);

            var result = await new PowerPointDeckPreviewTool(() => store, () => catalogs).ExecuteAsync(ctx, Req("{\"deckId\":\"" + ws.Id + "\",\"slide\":9}"));

            Assert.IsTrue(result.Success, result.Content);
            Assert.AreEqual("https://nixer-deck.local/decks/" + ws.Id + "/build/deck.html#2", renderer.PreviewUrl);
            Assert.IsTrue(File.Exists(Path.Combine(ws.BuildDir, "deck.html")));

            var noRenderer = await new PowerPointDeckPreviewTool(() => store, () => catalogs).ExecuteAsync(new ToolExecutionContext(), Req("{\"deckId\":\"" + ws.Id + "\"}"));
            Assert.IsFalse(noRenderer.Success);
            Assert.AreEqual("ppt.deck_render_unavailable", noRenderer.ErrorCode);
        }

        [TestMethod]
        public void DeckBuildPreflight_ReopensWorkspaceAndDetectsRevisionChanges()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var workspace = store.Create("alpha", "low", "zh-CN", "Preflight");
            workspace.PutSlides(new[] { new DeckSlideInput { Html = "<h1>Before</h1>" } });
            var tool = new PowerPointDeckBuildTool(() => store, () => CreateCatalogs());
            var request = Req("{\"deckId\":\"" + workspace.Id + "\"}");

            var initial = tool.Preflight(new ToolExecutionContext(), request);

            Assert.IsTrue(initial.Success, initial.ErrorMessage);
            var initialDetails = JObject.Parse(initial.DetailsJson);
            Assert.AreEqual(workspace.Id, (string)initialDetails["deckId"]);
            Assert.AreEqual("Preflight", (string)initialDetails["title"]);
            Assert.AreEqual(1, (int)initialDetails["slideCount"]);
            var initialRevision = (string)initialDetails["workspaceRevision"];
            Assert.IsFalse(string.IsNullOrWhiteSpace(initialRevision));

            workspace.PutSlides(new[]
            {
                new DeckSlideInput { Id = workspace.Slides[0].Id, Html = "<h1>After</h1>" }
            });
            var changed = tool.Preflight(new ToolExecutionContext(), request);
            var changedDetails = JObject.Parse(changed.DetailsJson);

            Assert.IsTrue(changed.Success, changed.ErrorMessage);
            Assert.AreNotEqual(initialRevision, (string)changedDetails["workspaceRevision"]);
        }

        [TestMethod]
        public async Task DeckValidate_ReturnsL1IssuesAndPersistsScreenshots()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var catalogs = CreateCatalogs();
            var workspace = store.Create("alpha", "low", null, "Validate");
            workspace.PutSlides(new[] { new DeckSlideInput { Html = "<h1>Cover</h1>" } });
            var renderer = new FakeRenderer
            {
                ValidationReport = new OfficeCopilot.Slides.Rendering.DeckValidationCapture
                {
                    SlideCount = 1,
                    PdfPageCount = 1,
                    PdfPaginationChecked = true,
                    Issues = new List<OfficeCopilot.Slides.Rendering.DeckValidationIssue>
                    {
                        new OfficeCopilot.Slides.Rendering.DeckValidationIssue
                        {
                            Code = "text.clipped",
                            Severity = "error",
                            Slide = 1,
                            Message = "文本容器内容超出可视区域。",
                            Selector = "h1",
                            Text = "Cover"
                        },
                        new OfficeCopilot.Slides.Rendering.DeckValidationIssue
                        {
                            Code = "font.fallback",
                            Severity = "error",
                            Slide = 1,
                            Message = "声明字体为 Example Sans，实际使用 Arial。",
                            Selector = "h1",
                            Text = "Cover"
                        }
                    },
                    Screenshots = new List<OfficeCopilot.Slides.Rendering.DeckValidationScreenshot>
                    {
                        new OfficeCopilot.Slides.Rendering.DeckValidationScreenshot { Slide = 1, Png = new byte[] { 1, 2, 3 } }
                    }
                }
            };

            var result = await new PowerPointDeckValidateTool(() => store, () => catalogs)
                .ExecuteAsync(CreateContext(renderer), Req("{\"deckId\":\"" + workspace.Id + "\"}"));

            Assert.IsTrue(result.Success, result.Content);
            StringAssert.Contains(result.Content, "L1 检查 1 页：2 项错误");
            StringAssert.Contains(result.Content, "L2 PDF 分页：1 页（预期 1 页）。");
            StringAssert.Contains(result.Content, "[text.clipped]");
            StringAssert.Contains(result.Content, "[font.fallback]");
            StringAssert.Contains(result.Content, "https://nixer-decks.local/" + workspace.Id + "/previews/validation-s01.png?v=");
            Assert.IsTrue(File.Exists(workspace.PreviewPath("validation-s01", ".png")));
        }

        [TestMethod]
        public void PdfPaginationChecker_RejectsEmptyPdf()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                OfficeCopilot.Slides.Rendering.PdfPaginationChecker.Inspect(Array.Empty<byte>(), 1));
        }

        [TestMethod]
        public void BuildPlanner_ConvertsCaptureAndSeparatesBuildModes()
        {
            var capture = new OfficeCopilot.Slides.Deck.SlideCapture
            {
                Width = 1920,
                Height = 1080,
                BackgroundImagePath = "background.png",
                TextBlocks = new List<OfficeCopilot.Slides.Deck.CapturedTextBlock>
                {
                    new OfficeCopilot.Slides.Deck.CapturedTextBlock
                    {
                        Rectangle = new OfficeCopilot.Slides.Deck.CapturedRectangle { Left = 40, Top = 80, Width = 400, Height = 120 },
                        Text = "Revenue Growth",
                        Lines = new List<string> { "Revenue", "Growth" },
                        LineDetails = new List<OfficeCopilot.Slides.Deck.CapturedTextLine>
                        {
                            new OfficeCopilot.Slides.Deck.CapturedTextLine { Text = "Revenue", Rectangle = new OfficeCopilot.Slides.Deck.CapturedRectangle { Left = 40, Top = 80, Width = 200, Height = 56 } },
                            new OfficeCopilot.Slides.Deck.CapturedTextLine { Text = "Growth", Rectangle = new OfficeCopilot.Slides.Deck.CapturedRectangle { Left = 40, Top = 136, Width = 160, Height = 56 } }
                        },
                        FontFamily = "\"Avenir Next\", sans-serif",
                        FontSize = 48,
                        FontWeight = "700",
                        Color = "rgb(10, 20, 30)",
                        LineHeight = 56,
                        LetterSpacing = 2,
                        TextAlign = "center",
                        VerticalAlign = "middle"
                    }
                },
                Images = new List<OfficeCopilot.Slides.Deck.CapturedImage>
                {
                    new OfficeCopilot.Slides.Deck.CapturedImage
                    {
                        Rectangle = new OfficeCopilot.Slides.Deck.CapturedRectangle { Left = 100, Top = 200, Width = 300, Height = 180 },
                        Source = "assets/chart.png",
                        CropLeft = 0.1,
                        CropRight = 0.2
                    }
                },
                Shapes = new List<OfficeCopilot.Slides.Deck.CapturedShape>
                {
                    new OfficeCopilot.Slides.Deck.CapturedShape
                    {
                        Rectangle = new OfficeCopilot.Slides.Deck.CapturedRectangle { Left = 20, Top = 30, Width = 100, Height = 40 },
                        Kind = "rounded-rect",
                        Fill = "#ffffff",
                        FillOpacity = 0.5,
                        Stroke = "#000000",
                        StrokeOpacity = 0.25,
                        StrokeWidth = 2,
                        Radius = 8
                    },
                    new OfficeCopilot.Slides.Deck.CapturedShape
                    {
                        Rectangle = new OfficeCopilot.Slides.Deck.CapturedRectangle { Left = 0, Top = 0, Width = 10, Height = 10 },
                        Kind = "ellipse"
                    }
                },
                Tables = new List<CapturedTable>
                {
                    new CapturedTable
                    {
                        Rectangle = new CapturedRectangle { Left = 40, Top = 50, Width = 600, Height = 200 },
                        Rows = new List<CapturedTableRow>
                        {
                            new CapturedTableRow { Cells = new List<CapturedTableCell> { new CapturedTableCell { Text = "Quarter", FontSize = 24, FontFamily = "Avenir Next", Bold = true }, new CapturedTableCell { Text = "Revenue" } } },
                            new CapturedTableRow { Cells = new List<CapturedTableCell> { new CapturedTableCell { Text = "Q1" }, new CapturedTableCell { Text = "$10M" } } }
                        }
                    }
                },
                Charts = new List<CapturedChart>
                {
                    new CapturedChart
                    {
                        Rectangle = new CapturedRectangle { Left = 100, Top = 120, Width = 800, Height = 500 },
                        Type = "BAR",
                        Title = "Revenue",
                        Labels = new List<string> { "Q1", "Q2" },
                        Series = new List<CapturedChartSeries>
                        {
                            new CapturedChartSeries { Name = "2025", Values = new List<double> { 10, 20 }, Color = "#123456" }
                        }
                    }
                }
            };

            var fidelity = OfficeCopilot.Slides.Deck.BuildPlanner.Create(capture, OfficeCopilot.Slides.Deck.SlideBuildModes.Fidelity);
            var editable = OfficeCopilot.Slides.Deck.BuildPlanner.Create(capture, OfficeCopilot.Slides.Deck.SlideBuildModes.Editable);

            Assert.AreEqual(960, fidelity.WidthPoints);
            Assert.AreEqual(540, fidelity.HeightPoints);
            Assert.AreEqual(20, fidelity.TextBlocks[0].Rectangle.Left);
            Assert.AreEqual(60, fidelity.TextBlocks[0].Rectangle.Height);
            Assert.AreEqual("Revenue\vGrowth", fidelity.TextBlocks[0].Text);
            Assert.AreEqual("center", fidelity.TextBlocks[0].TextAlign);
            Assert.AreEqual("middle", fidelity.TextBlocks[0].VerticalAlign);
            Assert.AreEqual(2, fidelity.TextBlocks[0].LineDetails.Count);
            Assert.AreEqual(68, fidelity.TextBlocks[0].LineDetails[1].Rectangle.Top);
            Assert.AreEqual("Avenir Next", fidelity.TextBlocks[0].FontName);
            Assert.AreEqual("Microsoft YaHei", fidelity.TextBlocks[0].FontNameFarEast);
            Assert.AreEqual(24, fidelity.TextBlocks[0].FontSizePoints);
            Assert.IsTrue(fidelity.TextBlocks[0].Bold);
            Assert.AreEqual(1, fidelity.TextBlocks[0].LetterSpacingPoints);
            Assert.AreEqual(0, fidelity.Shapes.Count);
            Assert.AreEqual(1, editable.Shapes.Count);
            Assert.AreEqual(1, editable.Shapes[0].StrokeWidthPoints);
            Assert.AreEqual(0.5, editable.Shapes[0].FillOpacity);
            Assert.AreEqual(0.25, editable.Shapes[0].StrokeOpacity);
            Assert.AreEqual("assets/chart.png", editable.Images[0].Source);
            Assert.AreEqual(0.1, editable.Images[0].CropLeft);
            Assert.AreEqual(0.2, editable.Images[0].CropRight);
            Assert.AreEqual(1, fidelity.Tables.Count);
            Assert.AreEqual(1, fidelity.Charts.Count);
            Assert.AreEqual(1, editable.Tables.Count);
            Assert.AreEqual(20, editable.Tables[0].Rectangle.Left);
            Assert.AreEqual(12, editable.Tables[0].Rows[0][0].FontSizePoints);
            Assert.AreEqual("Avenir Next", editable.Tables[0].Rows[0][0].FontName);
            Assert.AreEqual(1, editable.Charts.Count);
            Assert.AreEqual("bar", editable.Charts[0].Type);
            Assert.AreEqual(400, editable.Charts[0].Rectangle.Width);
            Assert.AreEqual(10, editable.Charts[0].Series[0].Values[0]);
        }

        [TestMethod]
        public void BuildPlanner_ReservesSafeAreaWhenWideningText()
        {
            var capture = new SlideCapture
            {
                Width = 1920,
                Height = 1080,
                SafeAreaX = 128,
                SafeAreaY = 96,
                TextBlocks = new List<CapturedTextBlock>
                {
                    new CapturedTextBlock
                    {
                        Rectangle = new CapturedRectangle { Left = 129, Top = 200, Width = 100, Height = 60 },
                        Text = "centered title",
                        Lines = new List<string> { "centered title" },
                        TextAlign = "center"
                    },
                    new CapturedTextBlock
                    {
                        Rectangle = new CapturedRectangle { Left = 1690, Top = 200, Width = 100, Height = 60 },
                        Text = "near edge body",
                        Lines = new List<string> { "near edge body" },
                        TextAlign = "left"
                    },
                    new CapturedTextBlock
                    {
                        Rectangle = new CapturedRectangle { Left = 129, Top = 300, Width = 100, Height = 60 },
                        Text = "right aligned",
                        Lines = new List<string> { "right aligned" },
                        TextAlign = "right"
                    },
                    new CapturedTextBlock
                    {
                        Rectangle = new CapturedRectangle { Left = 1800, Top = 400, Width = 100, Height = 60 },
                        Text = "already outside",
                        Lines = new List<string> { "already outside" },
                        TextAlign = "left"
                    }
                }
            };

            var plan = BuildPlanner.Create(capture, SlideBuildModes.Editable);

            Assert.AreEqual(64, plan.SafeAreaXPoints);
            Assert.AreEqual(48, plan.SafeAreaYPoints);
            Assert.AreEqual(64, plan.TextBlocks[0].Rectangle.Left);
            Assert.AreEqual(51, plan.TextBlocks[0].Rectangle.Width);
            Assert.AreEqual(51, plan.TextBlocks[1].Rectangle.Width);
            Assert.AreEqual(64, plan.TextBlocks[2].Rectangle.Left);
            Assert.AreEqual(50.5, plan.TextBlocks[2].Rectangle.Width);
            Assert.AreEqual(50, plan.TextBlocks[3].Rectangle.Width);
        }

        [TestMethod]
        public void BuildPlanner_UsesTextSpecificActionSafeAreaWhenWideningText()
        {
            var capture = new SlideCapture
            {
                Width = 1920,
                Height = 1080,
                SafeAreaX = 128,
                SafeAreaY = 96,
                TextBlocks = new List<CapturedTextBlock>
                {
                    new CapturedTextBlock
                    {
                        Rectangle = new CapturedRectangle { Left = 1750, Top = 200, Width = 100, Height = 60 },
                        Text = "action area label",
                        Lines = new List<string> { "action area label" },
                        TextAlign = "left",
                        SafeAreaX = 64,
                        SafeAreaY = 48
                    }
                }
            };

            var plan = BuildPlanner.Create(capture, SlideBuildModes.Editable);

            Assert.AreEqual(32, plan.TextBlocks[0].SafeAreaXPoints);
            Assert.AreEqual(24, plan.TextBlocks[0].SafeAreaYPoints);
            Assert.AreEqual(875, plan.TextBlocks[0].Rectangle.Left);
            Assert.AreEqual(51.5, plan.TextBlocks[0].Rectangle.Width);
        }

        [TestMethod]
        public void BuildPlanner_ReflowsLongBodyTextButLocksEmojiOverlayLineBreaks()
        {
            var longText = new string('a', 90) + " " + new string('b', 90);
            var textBlock = new CapturedTextBlock
            {
                Rectangle = new CapturedRectangle { Left = 200, Top = 100, Width = 600, Height = 120 },
                Text = longText,
                Lines = new List<string> { new string('a', 90), new string('b', 90) },
                LineDetails = new List<CapturedTextLine>
                {
                    new CapturedTextLine
                    {
                        Text = new string('a', 90),
                        Rectangle = new CapturedRectangle { Left = 200, Top = 100, Width = 580, Height = 50 }
                    },
                    new CapturedTextLine
                    {
                        Text = new string('b', 90),
                        Rectangle = new CapturedRectangle { Left = 200, Top = 150, Width = 580, Height = 50 }
                    }
                }
            };
            var capture = new SlideCapture
            {
                Width = 1920,
                Height = 1080,
                TextBlocks = new List<CapturedTextBlock> { textBlock }
            };

            var bodyPlan = BuildPlanner.Create(capture, SlideBuildModes.Editable);
            capture.Images = new List<CapturedImage>
            {
                new CapturedImage
                {
                    Rectangle = new CapturedRectangle { Left = 210, Top = 110, Width = 24, Height = 24 },
                    Source = "data:image/png;base64,AA==",
                    OverlayOnText = true
                }
            };
            var emojiPlan = BuildPlanner.Create(capture, SlideBuildModes.Editable);

            Assert.AreEqual(longText, bodyPlan.TextBlocks[0].Text);
            Assert.IsFalse(bodyPlan.TextBlocks[0].Text.Contains("\v"));
            Assert.IsTrue(emojiPlan.TextBlocks[0].Text.Contains("\v"));
            Assert.IsTrue(emojiPlan.Images[0].OverlayOnText);
        }

        [TestMethod]
        public void BuildPlanner_RejectsRaggedNativeTable()
        {
            var capture = new SlideCapture
            {
                Width = 1920,
                Height = 1080,
                Tables = new List<CapturedTable>
                {
                    new CapturedTable
                    {
                        Rectangle = new CapturedRectangle { Width = 200, Height = 100 },
                        Rows = new List<CapturedTableRow>
                        {
                            new CapturedTableRow { Cells = new List<CapturedTableCell> { new CapturedTableCell(), new CapturedTableCell() } },
                            new CapturedTableRow { Cells = new List<CapturedTableCell> { new CapturedTableCell() } }
                        }
                    }
                }
            };

            Assert.ThrowsException<ArgumentException>(() => BuildPlanner.Create(capture, SlideBuildModes.Editable));
        }

        [TestMethod]
        public void BuildPlanner_CarriesNativeTableGeometryAndCellStyle()
        {
            var capture = new SlideCapture
            {
                Width = 1920,
                Height = 1080,
                Tables = new List<CapturedTable>
                {
                    new CapturedTable
                    {
                        Rectangle = new CapturedRectangle { Left = 40, Top = 50, Width = 600, Height = 120 },
                        ColumnWidths = new List<double> { 400, 200 },
                        Rows = new List<CapturedTableRow>
                        {
                            new CapturedTableRow
                            {
                                Height = 80,
                                Cells = new List<CapturedTableCell>
                                {
                                    new CapturedTableCell
                                    {
                                        Text = "捕虫网、镊子",
                                        Fill = "#f5efe6",
                                        VerticalAlign = "middle",
                                        LineHeight = 32,
                                        Padding = new CapturedTableSpacing { Top = 8, Right = 16, Bottom = 8, Left = 16 },
                                        Borders = new CapturedTableBorders { Bottom = new CapturedTableBorder { Width = 2, Color = "#d9cbb8" } }
                                    },
                                    new CapturedTableCell { Text = "2 件", Fill = "transparent", VerticalAlign = "middle" }
                                }
                            },
                            new CapturedTableRow
                            {
                                Height = 40,
                                Cells = new List<CapturedTableCell>
                                {
                                    new CapturedTableCell { Text = "镊子", Fill = "#ffffff" },
                                    new CapturedTableCell { Text = "1 件", Fill = "#ffffff" }
                                }
                            }
                        }
                    }
                }
            };

            var editable = BuildPlanner.Create(capture, SlideBuildModes.Editable);
            var table = editable.Tables[0];
            var cell = table.Rows[0][0];

            CollectionAssert.AreEqual(new List<double> { 200, 100 }, table.ColumnWidthsPoints.ToList());
            CollectionAssert.AreEqual(new List<double> { 40, 20 }, table.RowHeightsPoints.ToList());
            Assert.AreEqual(8, cell.MarginPoints.Left);
            Assert.AreEqual(4, cell.MarginPoints.Top);
            Assert.AreEqual(16, cell.LineHeightPoints);
            Assert.AreEqual("middle", cell.VerticalAlign);
            Assert.AreEqual(1, cell.Borders.Bottom.WidthPoints);
            Assert.AreEqual("#d9cbb8", cell.Borders.Bottom.Color);
            Assert.AreEqual(0, cell.Borders.Top.WidthPoints);
            Assert.AreEqual("transparent", table.Rows[0][1].Fill);
        }

        [TestMethod]
        public void BuildPlanner_KeepsEqualColumnsWhenCapturedWidthsAreMissing()
        {
            var capture = new SlideCapture
            {
                Width = 1920,
                Height = 1080,
                Tables = new List<CapturedTable>
                {
                    new CapturedTable
                    {
                        Rectangle = new CapturedRectangle { Width = 200, Height = 100 },
                        ColumnWidths = new List<double> { 120 },
                        Rows = new List<CapturedTableRow>
                        {
                            new CapturedTableRow { Cells = new List<CapturedTableCell> { new CapturedTableCell(), new CapturedTableCell() } }
                        }
                    }
                }
            };

            var editable = BuildPlanner.Create(capture, SlideBuildModes.Editable);

            Assert.AreEqual(0, editable.Tables[0].ColumnWidthsPoints.Count);
        }

        [TestMethod]
        public void FontCatalog_SelectFace_MatchesCssWeightAndStyleToPowerPointNames()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));

            var medium = catalog.SelectFace("Albert Sans", 500, false);
            Assert.AreEqual("Albert Sans Medium", medium.Name);
            Assert.IsFalse(medium.Bold);

            var semiBold = catalog.SelectFace("Albert Sans", 600, false);
            Assert.AreEqual("Albert Sans SemiBold", semiBold.Name);
            Assert.IsFalse(semiBold.Bold);

            var bold = catalog.SelectFace("Albert Sans", 700, false);
            Assert.AreEqual("Albert Sans", bold.Name);
            Assert.IsTrue(bold.Bold);

            var sourceSerifRegular = catalog.SelectFace("Source Serif 4", 400, false);
            Assert.AreEqual("Source Serif 4", sourceSerifRegular.Name);
            Assert.IsFalse(sourceSerifRegular.Bold);
            var sourceSerifSemiBold = catalog.SelectFace("Source Serif 4", 600, false);
            Assert.AreEqual("Source Serif 4 SemiBold", sourceSerifSemiBold.Name);
            Assert.IsFalse(sourceSerifSemiBold.Bold);
            var sourceSerifBold = catalog.SelectFace("Source Serif 4", 700, false);
            Assert.AreEqual("Source Serif 4", sourceSerifBold.Name);
            Assert.IsTrue(sourceSerifBold.Bold);
            var sourceSerifItalic = catalog.SelectFace("Source Serif 4", 400, true);
            Assert.AreEqual("Source Serif 4", sourceSerifItalic.Name);
            Assert.IsTrue(sourceSerifItalic.Italic);

            var italic = catalog.SelectFace("Lora", 400, true);
            Assert.AreEqual("Lora", italic.Name);
            Assert.IsTrue(italic.Italic);
            Assert.IsFalse(italic.Bold);

            var smiley = catalog.SelectFace("Smiley Sans", 400, false);
            Assert.AreEqual("Smiley Sans Oblique", smiley.Name);
            Assert.IsFalse(smiley.Italic);
            Assert.IsFalse(catalog.SelectFace("Smiley Sans", 400, true).Italic);
            Assert.AreEqual(
                "Smiley Sans Oblique",
                catalog.SelectFace("Smiley Sans Oblique", 400, false).Name);

            Assert.AreEqual("Noto Sans SC", catalog.SelectFace("Noto Sans JP", 400, false).Name);
            Assert.IsNull(catalog.SelectFace("Segoe UI", 400, false));
        }

        [TestMethod]
        public void FontCatalog_SelectFace_PrefersExplicitPowerPointMetadata()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var family = catalog.Families.Single(p => p.Family == "Albert Sans");
            var face = family.Faces.Single(p => p.Weight == 700 && p.Style == "normal");
            face.PptName = "Verified Albert Sans";
            face.PptBold = false;
            face.PptItalic = false;

            var selected = catalog.SelectFace("Albert Sans", 700, true);

            Assert.AreEqual("Verified Albert Sans", selected.Name);
            Assert.IsFalse(selected.Bold);
            Assert.IsFalse(selected.Italic);
        }

        [TestMethod]
        public void FontCatalog_BuildCssIncludesAliasesAndBuiltinItalicPolicy()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var smiley = catalog.Families.Single(p => p.Family == "Smiley Sans");
            var css = FontCatalog.BuildCss(new[] { smiley }, family => family.Folder + "/");

            Assert.AreEqual("builtin", smiley.Italic);
            Assert.AreEqual("normal", smiley.Faces.Single().Style);
            StringAssert.Contains(css, "font-family: 'Smiley Sans Oblique'");
            StringAssert.Contains(css, "font-style: normal");
        }

        [TestMethod]
        public void FontCatalog_BuildCssAddsEmojiFallbackForRegularFamiliesOnly()
        {
            var regular = new FontFamilyEntry
            {
                Family = "Test Sans",
                Folder = "TestSans",
                Faces = new List<FontFaceEntry>
                {
                    new FontFaceEntry { File = "TestSans-Regular.ttf", Weight = 400, Style = "normal" }
                }
            };
            var emoji = new FontFamilyEntry
            {
                Family = "Noto Color Emoji",
                Folder = "NotoColorEmoji",
                EmojiOnly = true,
                Faces = new List<FontFaceEntry>
                {
                    new FontFaceEntry { File = "NotoColorEmoji.ttf", Weight = 400, Style = "normal" }
                }
            };

            var css = FontCatalog.BuildCss(new[] { regular, emoji }, family => family.Folder + "/");

            StringAssert.Contains(css, "font-family: 'Test Sans'");
            StringAssert.Contains(css, "src: url('NotoColorEmoji/NotoColorEmoji.ttf')");
            StringAssert.Contains(css, "unicode-range:");
            StringAssert.Contains(css, "font-family: 'Noto Color Emoji'");
        }

        [TestMethod]
        public void FontCatalog_BuildCssMatchesShippedStylesheet()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user-css")));
            var expected = FontCatalog.BuildCss(catalog.Families, family => family.Folder + "/");
            var actual = File.ReadAllText(Path.Combine(assets, "fonts", "fonts.css"), Encoding.UTF8)
                .Replace("\r\n", "\n");

            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void BuildPlanner_DoesNotSelectEmojiOnlyFamilyForPowerPointText()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            Assert.IsTrue(catalog.IsEmojiOnly("Noto Color Emoji"));
            Assert.IsTrue(FontPolicy.FromCatalog(catalog).Families["Noto Color Emoji"].EmojiOnly);

            var plan = BuildPlanner.Create(new SlideCapture
            {
                Width = 1920,
                Height = 1080,
                TextBlocks = new List<CapturedTextBlock>
                {
                    new CapturedTextBlock
                    {
                        Rectangle = new CapturedRectangle { Left = 200, Top = 100, Width = 500, Height = 100 },
                        Text = "editable text",
                        FontFamily = "'Noto Color Emoji', sans-serif",
                        FontSize = 40,
                        Lines = new List<string> { "editable text" }
                    }
                }
            }, SlideBuildModes.Editable, catalog);

            Assert.AreEqual("Arial", plan.TextBlocks[0].FontName);
        }

        [TestMethod]
        public void FontPolicy_ExportsBundledFamiliesAliasesAndFallbackLists()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var policy = FontPolicy.FromCatalog(catalog);
            var noto = policy.Families["Noto Sans SC"];

            CollectionAssert.AreEqual(new[] { 400, 500, 700 }, new List<int>(noto.Weights));
            Assert.AreEqual("none", noto.Italic);
            Assert.IsTrue(noto.Cjk);
            Assert.AreEqual("true", policy.Families["Lora"].Italic);
            Assert.AreEqual("builtin", policy.Families["Smiley Sans"].Italic);
            Assert.IsTrue(policy.AreEquivalentFamilyNames("Smiley Sans", "Smiley Sans Oblique"));
            Assert.IsTrue(policy.AreEquivalentFamilyNames("Noto Serif SC", "Noto Serif SC SemiBold"));
            Assert.IsFalse(policy.AreEquivalentFamilyNames("Smiley Sans", "Noto Sans SC"));
            Assert.IsTrue(JToken.DeepEquals(
                JObject.FromObject(noto),
                JObject.FromObject(policy.Families["Noto Sans JP"])));
            Assert.IsTrue(noto.Coverage.Any(range => range.Start <= 0x6C38 && range.End >= 0x6C38));
            Assert.IsFalse(noto.Coverage.Any(range => range.Start <= 0x1F600 && range.End >= 0x1F600));
            Assert.IsTrue(noto.EmojiCoverage.Any(range => range.Start <= 0x1F600 && range.End >= 0x1F600));
            Assert.IsTrue(policy.Families["Noto Color Emoji"].Coverage.Any(range => range.Start <= 0x1F600 && range.End >= 0x1F600));
            Assert.IsFalse(policy.Families["Noto Color Emoji"].Coverage.Any(range => range.Start <= 0x10FFFF && range.End >= 0x10FFFF));
            Assert.IsTrue(policy.System.Contains("Segoe UI"));
            Assert.IsTrue(policy.Generic.Contains("sans-serif"));
            Assert.IsTrue(FontPolicy.IsSystemFamily("microsoft yahei"));
            Assert.IsTrue(FontPolicy.IsGenericFamily("UI-MONOSPACE"));

            var json = JObject.FromObject(policy);
            Assert.IsNotNull(json["families"]["Noto Sans JP"]);
            Assert.IsNotNull(json["families"]["Noto Sans SC"]["coverage"]);
            Assert.IsTrue(json["families"]["Noto Sans SC"]["coverage"].Any(range =>
                (int)range["start"] <= 0x6C38 && (int)range["end"] >= 0x6C38));
            Assert.IsNotNull(json["system"]);
            Assert.IsNotNull(json["generic"]);
        }

        [TestMethod]
        public void BuildPlanner_UsesStackOrderForCjkAndCssFaceNamesForWeight()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var fidelity = BuildPlanner.Create(SingleTextCapture("'Albert Sans', 'Noto Sans SC', sans-serif", "500", "normal"), SlideBuildModes.Fidelity, catalog);
            var text = fidelity.TextBlocks.Single();
            Assert.AreEqual("Albert Sans Medium", text.FontName);
            Assert.AreEqual("Noto Sans SC Regular", text.FontNameFarEast);
            Assert.IsFalse(text.Bold);
            Assert.IsFalse(text.Italic);

            var cjkBold = BuildPlanner.Create(SingleTextCapture("'Noto Sans SC', sans-serif", "700", "normal"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("Noto Sans SC", cjkBold.FontName);
            Assert.AreEqual("Noto Sans SC Regular", cjkBold.FontNameFarEast);
            Assert.IsTrue(cjkBold.Bold);

            var fallback = BuildPlanner.Create(SingleTextCapture("'Source Serif 4', serif", "400", "normal"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("SimSun", fallback.FontNameFarEast);

            var sansFallback = BuildPlanner.Create(SingleTextCapture("'Albert Sans', sans-serif", "400", "normal"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("Microsoft YaHei", sansFallback.FontNameFarEast);
        }

        [TestMethod]
        public void BuildPlanner_CarriesItalicAndSemiBoldIntoPowerPointFonts()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var italic = BuildPlanner.Create(SingleTextCapture("Lora, serif", "400", "italic"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("Lora", italic.FontName);
            Assert.IsTrue(italic.Italic);

            var sourceSerifItalic = BuildPlanner.Create(SingleTextCapture("'Source Serif 4', 'Noto Sans SC', serif", "400", "italic"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("Source Serif 4", sourceSerifItalic.FontName);
            Assert.AreEqual("Noto Sans SC Regular", sourceSerifItalic.FontNameFarEast);
            Assert.IsFalse(sourceSerifItalic.Bold);
            Assert.IsTrue(sourceSerifItalic.Italic);

            var dmSansSemiBold = BuildPlanner.Create(SingleTextCapture("'DM Sans', 'Noto Sans SC', sans-serif", "600", "normal"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("DM Sans SemiBold", dmSansSemiBold.FontName);
            Assert.AreEqual("Noto Sans SC Regular", dmSansSemiBold.FontNameFarEast);
            Assert.IsFalse(dmSansSemiBold.Bold);
            Assert.IsFalse(dmSansSemiBold.Italic);

            var interSemiBold = BuildPlanner.Create(SingleTextCapture("'Inter', 'Noto Sans SC', sans-serif", "600", "normal"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("Inter SemiBold", interSemiBold.FontName);
            Assert.AreEqual("Noto Sans SC Regular", interSemiBold.FontNameFarEast);
            Assert.IsFalse(interSemiBold.Bold);

            var cjkItalic = BuildPlanner.Create(SingleTextCapture("'Noto Sans SC', sans-serif", "400", "italic"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("Noto Sans SC", cjkItalic.FontName);
            Assert.AreEqual("Noto Sans SC Regular", cjkItalic.FontNameFarEast);
            Assert.IsFalse(cjkItalic.Italic);

            var semiBold = BuildPlanner.Create(SingleTextCapture("'Albert Sans', sans-serif", "600", "normal"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("Albert Sans SemiBold", semiBold.FontName);
            Assert.IsFalse(semiBold.Bold);

            var smiley = BuildPlanner.Create(SingleTextCapture("'Smiley Sans', sans-serif", "400", "normal"), SlideBuildModes.Fidelity, catalog).TextBlocks.Single();
            Assert.AreEqual("Smiley Sans Oblique", smiley.FontName);
            Assert.AreEqual("Smiley Sans Oblique", smiley.FontNameFarEast);
            Assert.IsFalse(smiley.Italic);
        }

        private static SlideCapture SingleTextCapture(string fontFamily, string fontWeight, string fontStyle)
        {
            return new SlideCapture
            {
                Width = 1920,
                Height = 1080,
                TextBlocks = new List<CapturedTextBlock>
                {
                    new CapturedTextBlock
                    {
                        Rectangle = new CapturedRectangle { Left = 40, Top = 80, Width = 400, Height = 120 },
                        Text = "标题 Title",
                        Lines = new List<string> { "标题 Title" },
                        FontFamily = fontFamily,
                        FontSize = 48,
                        FontWeight = fontWeight,
                        FontStyle = fontStyle,
                        Color = "rgb(10, 20, 30)",
                        LineHeight = 56,
                        TextAlign = "left"
                    }
                }
            };
        }

        [TestMethod]
        public void PowerPointFontInstaller_InstallsOnlyMissingBundledFamiliesUsedByDeck()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var albert = FontCatalog.Load(paths).Families.Single(f => f.Family == "Albert Sans");
            var plan = new SlideBuildPlan
            {
                TextBlocks = new List<PlannedTextBlock>
                {
                    new PlannedTextBlock { FontName = "Albert Sans", FontNameFarEast = "Noto Sans SC" },
                    new PlannedTextBlock { FontName = "Not A Bundled Font" }
                },
                Tables = new List<PlannedTable>
                {
                    new PlannedTable
                    {
                        Rows = new List<IList<PlannedTableCell>>
                        {
                            new List<PlannedTableCell> { new PlannedTableCell { FontName = "Albert Sans Medium" } }
                        }
                    }
                }
            };
            var registrar = new FakeFontRegistrar();
            var userFonts = Path.Combine(_root, "user-fonts");

            var issues = new PowerPointFontInstaller(paths, userFonts, registrar)
                .EnsureFonts(new[] { plan }, new HashSet<string> { "Noto Sans SC" });

            Assert.AreEqual(albert.Faces.Count, registrar.Registered.Count, "同族字重变体应一并安装且只安装一次");
            Assert.IsTrue(registrar.Registered.All(path =>

                File.Exists(path)
                && string.Equals(Path.GetDirectoryName(path), userFonts, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).StartsWith(FontInstallConstants.FileNamePrefix, StringComparison.Ordinal)));
            Assert.AreEqual(1, registrar.NotifyCount);
            Assert.IsTrue(issues.Any(i => i.Code == "ppt.font_installed" && i.Expected == "Albert Sans"));
            Assert.IsFalse(issues.Any(i => i.Code == "ppt.font_install_failed"));
        }

        [TestMethod]
        public void PowerPointFontInstaller_SkipsInstallOnWindowsWithoutUserFontSupport()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var registrar = new FakeFontRegistrar { IsSupported = false };
            var userFonts = Path.Combine(_root, "user-fonts");

            var issues = new PowerPointFontInstaller(paths, userFonts, registrar)
                .EnsureFonts(new[] { PlanUsingFont("Albert Sans") }, new HashSet<string>());

            Assert.AreEqual("ppt.font_install_skipped", issues.Single().Code);
            Assert.AreEqual(0, registrar.Registered.Count);
            Assert.IsFalse(Directory.Exists(userFonts));
        }

        [TestMethod]
        public void PowerPointFontInstaller_ReportsRegistrationFailureWithoutThrowing()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var registrar = new FakeFontRegistrar { FailRegistration = true };

            var issues = new PowerPointFontInstaller(paths, Path.Combine(_root, "user-fonts"), registrar)
                .EnsureFonts(new[] { PlanUsingFont("Albert Sans") }, new HashSet<string>());

            Assert.IsTrue(issues.Any(i => i.Code == "ppt.font_install_failed" && i.Expected == "Albert Sans"));
            Assert.IsFalse(issues.Any(i => i.Code == "ppt.font_installed"));
            Assert.AreEqual(0, registrar.NotifyCount);
        }

        [TestMethod]
        public void PowerPointFontInstaller_ReusesLockedIdenticalFontFileInsteadOfOverwriting()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var albert = FontCatalog.Load(paths).Families.Single(f => f.Family == "Albert Sans");
            var userFonts = Path.Combine(_root, "user-fonts");
            Directory.CreateDirectory(userFonts);

            var locks = new List<FileStream>();
            try
            {
                foreach (var face in albert.Faces)
                {
                    var target = Path.Combine(userFonts, FontInstallConstants.FileNamePrefix + face.File);
                    File.Copy(Path.Combine(albert.Directory, face.File), target, true);
                    // 模拟 Windows Font Cache Service 占用文件：允许读取，拒绝写入。
                    locks.Add(new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read));
                }

                var registrar = new FakeFontRegistrar();
                var issues = new PowerPointFontInstaller(paths, userFonts, registrar)
                    .EnsureFonts(new[] { PlanUsingFont("Albert Sans") }, new HashSet<string>());

                Assert.IsFalse(issues.Any(i => i.Code == "ppt.font_install_failed"), "内容一致的占用文件不应被覆盖");
                Assert.AreEqual(albert.Faces.Count, registrar.Registered.Count);
            }
            finally
            {
                foreach (var stream in locks) stream.Dispose();
            }
        }

        [TestMethod]
        public void PowerPointFontInstaller_MigratesIdenticalLegacyFontsAndWritesManifest()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var family = FontCatalog.Load(paths).Families.Single(f => f.Family == "Albert Sans");
            var userFonts = Path.Combine(_root, "user-fonts");
            var manifestPath = Path.Combine(_root, "state", "installed.json");
            Directory.CreateDirectory(userFonts);
            foreach (var face in family.Faces)
            {
                File.Copy(
                    Path.Combine(family.Directory, face.File),
                    Path.Combine(userFonts, FontInstallConstants.LegacyFileNamePrefix + face.File));
            }

            var registrar = new FakeFontRegistrar();
            foreach (var face in family.Faces)
            {
                registrar.Registered.Add(Path.Combine(userFonts, FontInstallConstants.LegacyFileNamePrefix + face.File));
            }

            var issues = new PowerPointFontInstaller(
                    paths,
                    userFonts,
                    registrar,
                    sizes => InstalledFontState.ManifestFileHashes(sizes, manifestPath),
                    manifestPath)
                .EnsureFonts(new[] { PlanUsingFont("Albert Sans") }, new HashSet<string>());

            var manifest = InstalledFontManifest.Load(manifestPath);
            Assert.IsFalse(issues.Any(issue => issue.Code == "ppt.font_migration_failed"));
            Assert.AreEqual(family.Faces.Count, manifest.Fonts.Count);
            Assert.IsTrue(manifest.Fonts.All(entry => entry.Registered
                && entry.Path.StartsWith(userFonts, StringComparison.OrdinalIgnoreCase)
                && entry.FileName.StartsWith(FontInstallConstants.FileNamePrefix, StringComparison.Ordinal)));
            Assert.IsTrue(registrar.Registered.All(path =>
                Path.GetFileName(path).StartsWith(FontInstallConstants.FileNamePrefix, StringComparison.Ordinal)));
            Assert.IsTrue(family.Faces.All(face =>
                !File.Exists(Path.Combine(userFonts, FontInstallConstants.LegacyFileNamePrefix + face.File))));
        }

        [TestMethod]
        public void PowerPointFontInstaller_PreservesLegacyFileWhenItsHashDiffers()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var family = FontCatalog.Load(paths).Families.Single(f => f.Family == "Albert Sans");
            var userFonts = Path.Combine(_root, "user-fonts");
            Directory.CreateDirectory(userFonts);
            var legacyPath = Path.Combine(userFonts, FontInstallConstants.LegacyFileNamePrefix + family.Faces[0].File);
            File.WriteAllText(legacyPath, "different font bytes");

            var registrar = new FakeFontRegistrar();
            registrar.Registered.Add(legacyPath);
            var issues = new PowerPointFontInstaller(paths, userFonts, registrar)
                .EnsureFonts(new[] { PlanUsingFont("Albert Sans") }, new HashSet<string>());

            Assert.IsTrue(File.Exists(legacyPath), "哈希不同的旧字体文件不得删除");
            Assert.IsTrue(registrar.Registered.Contains(legacyPath), "哈希不同的旧注册不得解除");
            Assert.IsTrue(issues.Any(issue => issue.Code == "ppt.font_legacy_conflict"));
        }

        [TestMethod]
        public void PowerPointFontInstaller_TracksAndRetriesDeletionOfLockedLegacyFile()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var family = FontCatalog.Load(paths).Families.Single(f => f.Family == "Albert Sans");
            var userFonts = Path.Combine(_root, "user-fonts");
            var manifestPath = Path.Combine(_root, "state", "installed.json");
            Directory.CreateDirectory(userFonts);
            foreach (var face in family.Faces)
            {
                File.Copy(
                    Path.Combine(family.Directory, face.File),
                    Path.Combine(userFonts, FontInstallConstants.LegacyFileNamePrefix + face.File));
            }

            var lockedLegacyPath = Path.Combine(userFonts, FontInstallConstants.LegacyFileNamePrefix + family.Faces[0].File);
            var registrar = new FakeFontRegistrar();
            foreach (var face in family.Faces)
            {
                registrar.Registered.Add(Path.Combine(userFonts, FontInstallConstants.LegacyFileNamePrefix + face.File));
            }

            var installer = new PowerPointFontInstaller(
                paths,
                userFonts,
                registrar,
                sizes => InstalledFontState.ManifestFileHashes(sizes, manifestPath),
                manifestPath);
            using (new FileStream(lockedLegacyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                installer.EnsureFonts(new[] { PlanUsingFont("Albert Sans") }, new HashSet<string>());
                Assert.IsTrue(File.Exists(lockedLegacyPath));
                Assert.IsTrue(InstalledFontManifest.Load(manifestPath).PendingDeletionPaths.Contains(lockedLegacyPath));
                Assert.IsFalse(registrar.IsRegistered(lockedLegacyPath));
            }

            installer.EnsureFonts(new[] { PlanUsingFont("Albert Sans") }, new HashSet<string>());
            Assert.IsFalse(File.Exists(lockedLegacyPath));
            Assert.AreEqual(0, InstalledFontManifest.Load(manifestPath).PendingDeletionPaths.Count);
        }

        [TestMethod]
        public void InstalledFontState_UsesOnlyManifestEntriesWithMatchingHashes()
        {
            var fontPath = Path.Combine(_root, "font.ttf");
            File.WriteAllText(fontPath, "font bytes");
            var manifestPath = Path.Combine(_root, "state", "installed.json");
            var manifest = new InstalledFontManifest();
            manifest.Upsert(new InstalledFontManifestEntry
            {
                Family = "Test Font",
                FileName = Path.GetFileName(fontPath),
                Path = fontPath,
                Sha256 = InstalledFontState.Sha256(fontPath),
                Registered = true
            });
            manifest.Save(manifestPath);
            var sizes = new HashSet<long> { new FileInfo(fontPath).Length };

            Assert.AreEqual(1, InstalledFontState.ManifestFileHashes(sizes, manifestPath).Count);

            File.WriteAllText(fontPath, "tampered font bytes");
            Assert.AreEqual(0, InstalledFontState.ManifestFileHashes(sizes, manifestPath).Count);
        }

        [TestMethod]
        public void FontShadowDiagnostics_ReportsSystemFamilyWithDifferentBundledHash()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var family = catalog.Families.First(entry =>
                string.Equals(entry.Family, "Smiley Sans", StringComparison.OrdinalIgnoreCase));
            var expectedHashes = new HashSet<string>(family.Faces.Select(face =>
                InstalledFontState.Sha256(Path.Combine(family.Directory, face.File))), StringComparer.OrdinalIgnoreCase);
            expectedHashes.Add("additional-font-hash");
            var mismatches = FontShadowDiagnostics.FindHashMismatches(
                catalog,
                new Dictionary<string, ISet<string>>
                {
                    { "Smiley Sans", expectedHashes }
                });

            CollectionAssert.Contains(new List<string>(mismatches), "Smiley Sans");
        }

        [TestMethod]
        public void PowerPointFontIdentityResolver_RequiresPackagedHashesButAllowsAdditionalRegisteredHashes()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var family = catalog.Families.FirstOrDefault(
                entry => string.Equals(entry.Family, "Smiley Sans", StringComparison.OrdinalIgnoreCase));
            if (family == null)
            {
                Assert.Inconclusive("未找到 Smiley Sans 随包字体。");
            }

            var expectedHashes = new HashSet<string>(family.Faces.Select(face =>
                InstalledFontState.Sha256(Path.Combine(family.Directory, face.File))), StringComparer.OrdinalIgnoreCase);
            var registeredHashes = new Dictionary<string, ISet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { family.Family, new HashSet<string>(expectedHashes, StringComparer.OrdinalIgnoreCase) }
            };
            var plans = new[] { PlanUsingFont(family.Family) };

            Assert.AreEqual(
                0,
                PowerPointFontIdentityResolver.FindMismatches(catalog, plans, registeredHashes).Count);

            registeredHashes[family.Family].Add("different-font-hash");
            Assert.AreEqual(
                0,
                PowerPointFontIdentityResolver.FindMismatches(catalog, plans, registeredHashes).Count);

            registeredHashes[family.Family].Remove(expectedHashes.First());
            var mismatches = PowerPointFontIdentityResolver.FindMismatches(catalog, plans, registeredHashes);

            Assert.IsTrue(
                mismatches.Any(name => name.StartsWith(family.Family, StringComparison.OrdinalIgnoreCase)),
                string.Join("、", mismatches));
        }

        [TestMethod]
        public void PresetFontWeightGuidance_UsesOnlyBundledWeights()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var catalog = FontCatalog.Load(paths);
            var presetsRoot = Path.Combine(paths.BuiltInRoot, "presets");
            var fontWeightPattern = new System.Text.RegularExpressions.Regex(
                @"`(?<family>[^`]+)`\s*\((?<weights>\d+(?:/\d+)*)\)",
                System.Text.RegularExpressions.RegexOptions.Compiled);
            var violations = new List<string>();

            foreach (var presetPath in Directory.GetFiles(presetsRoot, "preset.md", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(presetPath, Encoding.UTF8);
                foreach (System.Text.RegularExpressions.Match match in fontWeightPattern.Matches(text))
                {
                    var familyName = match.Groups["family"].Value;
                    var family = catalog.Families.FirstOrDefault(
                        entry => string.Equals(entry.Family, familyName, StringComparison.OrdinalIgnoreCase));
                    if (family == null)
                    {
                        violations.Add(Path.GetFileName(Path.GetDirectoryName(presetPath)) + ": unknown bundled family " + familyName);
                        continue;
                    }

                    foreach (var weightText in match.Groups["weights"].Value.Split('/'))
                    {
                        int weight;
                        if (int.TryParse(weightText, out weight)
                            && !family.Faces.Any(face => face.Weight == weight))
                        {
                            violations.Add(
                                Path.GetFileName(Path.GetDirectoryName(presetPath))
                                + ": " + familyName + " requests unavailable weight " + weight);
                        }
                    }
                }
            }

            Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        }

        [TestMethod]
        public void FontResolutionTable_ClassifiesSystemInstalledAndPendingFaces()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var catalog = FontCatalog.Load(new SlideAssetPaths(assets, Path.Combine(_root, "no-user")));
            var smiley = catalog.Families.Single(f => f.Family == "Smiley Sans");
            var smileyHash = InstalledFontState.Sha256(Path.Combine(smiley.Directory, smiley.Faces[0].File));

            var table = FontResolutionTable.Build(
                catalog,
                new HashSet<string> { "Noto Sans SC" },
                new HashSet<string> { smileyHash });

            Assert.IsTrue(table.IsProvided("Noto Sans SC"));
            Assert.IsTrue(table.IsProvided("Smiley Sans"));
            Assert.IsFalse(table.IsProvided("Albert Sans"));
            Assert.IsTrue(table.Rows.Where(r => r.Family == "Noto Sans SC").All(r => r.Provision == FontFaceProvision.SystemFamily));
            Assert.IsTrue(table.Rows.Where(r => r.Family == "Smiley Sans").All(r => r.Provision == FontFaceProvision.Installed));
            Assert.IsTrue(table.Rows.Where(r => r.Family == "Albert Sans").All(r => r.Provision == FontFaceProvision.Pending));
            Assert.AreEqual("Smiley Sans Oblique", table.Rows.Single(r => r.Family == "Smiley Sans").PptName);
        }

        [TestMethod]
        public void PowerPointFontInstaller_DoesNotReinstallFamilyAlreadyProvidedByMachine()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var catalog = FontCatalog.Load(paths);
            var smiley = catalog.Families.Single(f => f.Family == "Smiley Sans");
            var smileyHash = InstalledFontState.Sha256(Path.Combine(smiley.Directory, smiley.Faces[0].File));
            var registrar = new FakeFontRegistrar();

            var issues = new PowerPointFontInstaller(paths, Path.Combine(_root, "user-fonts"), registrar, sizes => new HashSet<string> { smileyHash })
                .EnsureFonts(new[] { PlanUsingFont("Smiley Sans Oblique") }, new HashSet<string> { "Noto Sans SC" });

            Assert.AreEqual(0, registrar.Registered.Count);
            Assert.IsFalse(issues.Any(i => i.Code == "ppt.font_installed"));
            Assert.IsFalse(issues.Any(i => i.Code == "ppt.font_missing"));
        }

        [TestMethod]
        public void PowerPointFontInstaller_WarnsWhenFamilyIsNeitherBundledNorInstalled()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var registrar = new FakeFontRegistrar();

            var issues = new PowerPointFontInstaller(paths, Path.Combine(_root, "user-fonts"), registrar)
                .EnsureFonts(new[] { PlanUsingFont("Fredoka One") }, new HashSet<string> { "Calibri" });

            var missing = issues.Single(i => i.Code == "ppt.font_missing");
            StringAssert.Contains(missing.Message, "Fredoka One");
            Assert.AreEqual(0, registrar.Registered.Count);
        }

        [TestMethod]
        public void PowerPointFontInstaller_DoesNotWarnForSystemInstalledFonts()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var registrar = new FakeFontRegistrar();

            var issues = new PowerPointFontInstaller(paths, Path.Combine(_root, "user-fonts"), registrar)
                .EnsureFonts(new[] { PlanUsingFont("Microsoft YaHei") }, new HashSet<string> { "Microsoft YaHei" });

            Assert.IsFalse(issues.Any(i => i.Code == "ppt.font_missing"));
        }

        [TestMethod]
        public void SlideAssets_EveryDeclaredFontIsBundledOrWindowsSystem()
        {
            var assets = FindShippedAssets();
            if (assets == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(assets, Path.Combine(_root, "no-user"));
            var catalog = FontCatalog.Load(paths);
            var problems = new List<string>();

            foreach (var style in StyleCatalog.Load(paths, catalog).Entries)
            {
                foreach (var name in style.Fonts ?? new List<string>())
                {
                    if (!IsUsableFamily(catalog, name))
                    {
                        problems.Add("styles.json " + style.Slug + ": " + name);
                    }
                }
            }

            var declaration = new System.Text.RegularExpressions.Regex(
                @"(?:font-family|--(?:f|font)-[\w-]+)\s*:\s*([^;{}""<>]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(assets, "templates"), "*.html", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (System.Text.RegularExpressions.Match match in declaration.Matches(text))
                {
                    foreach (var raw in match.Groups[1].Value.Split(','))
                    {
                        var name = raw.Trim().Trim('\'', '"').Trim();
                        if (name.Length == 0 || name.Contains("(") || name.Contains("}"))
                        {
                            continue;
                        }

                        if (!IsUsableFamily(catalog, name))
                        {
                            problems.Add(file.Replace(assets, string.Empty) + ": " + name);
                        }
                    }
                }
            }

            Assert.AreEqual(0, problems.Count, "以下字体既未随附，也不是 Windows 系统字体：\n" + string.Join("\n", problems.Distinct()));
        }

        private static bool IsUsableFamily(FontCatalog catalog, string name)
        {
            return FontPolicy.IsGenericFamily(name) || FontPolicy.IsSystemFamily(name) || catalog.Contains(name);
        }

        [TestMethod]
        public async Task DeckBuildService_ReportsFontInstallIssuesAsBuildIssues()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var workspace = store.Create("alpha", "low", null, "Build");
            workspace.PutSlides(new[] { new DeckSlideInput { Html = "<h1>One</h1>" } });
            var installer = new FakeFontInstaller();
            var service = new PowerPointDeckBuildService(
                new FakeDeckBuildHost(),
                new FakeRenderer(),
                new SlideAssetPaths(Path.Combine(_root, "lib"), Path.Combine(_root, "user")),
                store.Root)
            {
                FontInstaller = installer
            };

            var result = await service.BuildAsync(
                workspace,
                DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture",
                SlideBuildModes.Fidelity,
                CancellationToken.None);

            Assert.AreEqual(1, installer.Calls);
            Assert.IsTrue(result.BuildIssues.Any(i => i.Code == "ppt.font_installed"));
        }

        [TestMethod]
        public async Task DeckBuildService_BlocksUnregisteredFontNamesBeforeAppendingSlides()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var workspace = store.Create("alpha", "low", null, "Build");
            workspace.PutSlides(new[] { new DeckSlideInput { Html = "<h1>One</h1>" } });
            var host = new FakeDeckBuildHost();
            var service = new PowerPointDeckBuildService(
                host,
                new FakeRenderer { FontFamily = "Unregistered Test Font" },
                new SlideAssetPaths(Path.Combine(_root, "lib"), Path.Combine(_root, "user")),
                store.Root);

            var exception = await Assert.ThrowsExceptionAsync<DeckException>(() => service.BuildAsync(
                workspace,
                DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture",
                SlideBuildModes.Fidelity,
                CancellationToken.None));

            Assert.AreEqual("ppt.font_name_unresolved", exception.Code);
            Assert.IsNull(host.Slides);
        }

        private static SlideBuildPlan PlanUsingFont(string fontName)
        {
            return new SlideBuildPlan
            {
                TextBlocks = new List<PlannedTextBlock> { new PlannedTextBlock { FontName = fontName } }
            };
        }

        [TestMethod]
        public void SettingsFactory_NormalizesPowerPointSlidesDefaultsAndBounds()
        {
            var settings = OfficeCopilot.Settings.OfficeCopilotSettingsFactory.Normalize(new OfficeCopilot.Settings.Abstractions.OfficeCopilotSettings
            {
                PowerPointSlides = new OfficeCopilot.Settings.Abstractions.PowerPointSlidesSettings
                {
                    DefaultDensity = "VERY-HIGH",
                    DefaultBuildMode = "other",
                    RepairIterationCap = 99,
                    WorkspaceRetentionDays = 0
                }
            });

            Assert.AreEqual("medium", settings.PowerPointSlides.DefaultDensity);
            Assert.AreEqual("fidelity", settings.PowerPointSlides.DefaultBuildMode);
            Assert.AreEqual(10, settings.PowerPointSlides.RepairIterationCap);
            Assert.AreEqual(1, settings.PowerPointSlides.WorkspaceRetentionDays);
            Assert.AreEqual(8, settings.ConfigurationVersion);
        }

        [TestMethod]
        public async Task DeckBuildService_CapturesPlansAndPersistsEvidenceBeforeAppend()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var workspace = store.Create("alpha", "low", null, "Build");
            workspace.PutSlides(new[]
            {
                new DeckSlideInput { Html = "<h1>One</h1>" },
                new DeckSlideInput { Html = "<h1>Two</h1>" }
            });
            var renderer = new FakeRenderer();
            var host = new FakeDeckBuildHost();
            var service = new PowerPointDeckBuildService(
                host,
                renderer,
                new SlideAssetPaths(Path.Combine(_root, "lib"), Path.Combine(_root, "user")),
                store.Root);

            var mismatch = await Assert.ThrowsExceptionAsync<DeckException>(() => service.BuildAsync(
                workspace,
                DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture",
                SlideBuildModes.Fidelity,
                CancellationToken.None,
                new PowerPointDeckBuildTarget
                {
                    Kind = PowerPointDeckBuildTarget.ReplaceKind,
                    ReplaceFrom = 3,
                    ReplaceTo = 3
                }));
            Assert.AreEqual("ppt.deck_replace_count_mismatch", mismatch.Code);
            Assert.AreEqual(0, renderer.CapturedSlides.Count, "参数错误应在渲染前拒绝");

            var result = await service.BuildAsync(
                workspace,
                DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture",
                SlideBuildModes.Fidelity,
                CancellationToken.None);

            Assert.AreEqual(2, host.Slides.Count);
            Assert.AreEqual(2, renderer.CapturedSlides.Count);
            Assert.AreEqual(SlideBuildModes.Fidelity, host.Slides[0].Plan.Mode);
            Assert.AreEqual(Path.Combine(workspace.BuildDir, "capture", "bg01.png"), host.Slides[0].Plan.BackgroundImagePath);
            Assert.AreEqual(4, result.CaptureFiles.Count);
            Assert.IsTrue(result.CaptureFiles.All(File.Exists));
            CollectionAssert.AreEqual(new[] { 7, 8 }, result.SlideIndexes.ToArray());
            Assert.AreEqual(0, host.VerifyCalls);
            Assert.AreEqual(0, result.BuildIssues.Count);

            host.ThrowOnVerify = true;
            var buildWithoutVerification = await service.BuildAsync(
                workspace,
                DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture",
                SlideBuildModes.Fidelity,
                CancellationToken.None);
            CollectionAssert.AreEqual(new[] { 7, 8 }, buildWithoutVerification.SlideIndexes.ToArray());
            Assert.AreEqual(0, host.VerifyCalls);
            Assert.AreEqual(0, buildWithoutVerification.BuildIssues.Count);

            host.ThrowOnVerify = false;
            var imageBuild = await service.BuildAsync(
                workspace,
                DeckUrls.DeckBase(workspace.Id) + "build/deck.html?mode=capture",
                SlideBuildModes.Image,
                CancellationToken.None);
            Assert.AreEqual(2, host.Slides.Count);
            Assert.AreEqual(SlideBuildModes.Image, host.Slides[0].Plan.Mode);
            Assert.AreEqual(Path.Combine(workspace.BuildDir, "capture", "s01.png"), host.Slides[0].Plan.BackgroundImagePath);
            Assert.AreEqual(0, host.Slides[0].Plan.TextBlocks.Count);
            Assert.AreEqual(0, host.Slides[0].Plan.Images.Count);
            Assert.AreEqual(0, host.Slides[0].Plan.Shapes.Count);
            Assert.AreEqual(2, imageBuild.CaptureFiles.Count);
        }

        [TestMethod]
        public async Task StylePreview_WritesHtmlAndPng_ReturnsPreviewImageIds()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var catalogs = CreateCatalogs();
            var renderer = new FakeRenderer();
            var tool = new PowerPointStylePreviewTool(() => store, () => catalogs);
            Assert.IsTrue(tool.Definition.IsReadOnly);
            StringAssert.Contains(tool.Definition.Description, "runtime.ask_user_question");
            StringAssert.Contains(tool.Definition.Description, "previewImageId");

            var result = await tool.ExecuteAsync(CreateContext(renderer), Req(
                "{\"candidates\":[{\"name\":\"safe\",\"style\":\"alpha\",\"html\":\"<h1>Cover</h1>\"},{\"name\":\"bold\",\"style\":\"beta\",\"html\":\"<h1>Bold</h1>\",\"css\":\"@import 'x';\"},{\"name\":\"calm\",\"style\":\"gamma\",\"html\":\"<h1>Calm</h1>\"}]}"));

            Assert.IsTrue(result.Success, result.Content);
            Assert.AreEqual(3, renderer.Captures.Count);
            var deckId = Path.GetFileName(Directory.GetDirectories(store.Root).Single());
            StringAssert.StartsWith(renderer.Captures[0], "https://nixer-deck.local/decks/" + deckId + "/previews/safe.html?mode=capture#1");
            var previewImageIdMatch = Regex.Match(
                result.Content,
                @"previewImageId: (" + Regex.Escape(deckId) + @"/safe/[a-f0-9]{12})");
            Assert.IsTrue(previewImageIdMatch.Success, result.Content);
            Assert.IsFalse(result.Content.Contains("https://nixer-decks.local/" + deckId + "/previews/safe.png"));
            Assert.IsTrue(result.StructuredContentJson.Contains(
                "\"previewImageId\":\"" + previewImageIdMatch.Groups[1].Value + "\""));
            Assert.IsFalse(result.StructuredContentJson.Contains("https://nixer-decks.local/" + deckId + "/previews/safe.png"));
            StringAssert.Contains(result.Content, "@import");
            var ws = store.Open(deckId);
            Assert.IsTrue(File.Exists(ws.PreviewPath("bold", ".png")));
            StringAssert.Contains(File.ReadAllText(ws.PreviewPath("bold", ".html"), Encoding.UTF8), "<h1>Bold</h1>");
            Assert.IsTrue(File.Exists(ws.PreviewPath("bold", ".slide.html")));
            Assert.IsTrue(File.Exists(ws.PreviewPath("bold", ".css")));
            Assert.IsTrue(File.Exists(ws.PreviewPath("bold", ".json")));
            var boldSource = ws.ReadPreviewSource("bold");
            Assert.AreEqual("beta", boldSource.Style);
            Assert.IsFalse(boldSource.Css.Contains("@import"), boldSource.Css);
            var sourceMetadata = JObject.Parse(File.ReadAllText(ws.PreviewPath("bold", ".json"), Encoding.UTF8));
            Assert.AreEqual(boldSource.Hash, (string)sourceMetadata["hash"]);

            var tooFew = await tool.ExecuteAsync(CreateContext(renderer), Req("{\"candidates\":[{\"name\":\"one\",\"style\":\"alpha\",\"html\":\"<h1>x</h1>\"},{\"name\":\"two\",\"style\":\"beta\",\"html\":\"<h1>y</h1>\"}]}"));
            Assert.IsFalse(tooFew.Success);
            Assert.AreEqual("ppt.deck_invalid_argument", tooFew.ErrorCode);

            var duplicateStyle = await tool.ExecuteAsync(CreateContext(renderer), Req("{\"candidates\":[{\"name\":\"one\",\"style\":\"alpha\",\"html\":\"<h1>x</h1>\"},{\"name\":\"two\",\"style\":\"alpha\",\"html\":\"<h1>y</h1>\"},{\"name\":\"three\",\"style\":\"gamma\",\"html\":\"<h1>z</h1>\"}]}"));
            Assert.IsFalse(duplicateStyle.Success);
            Assert.AreEqual("ppt.deck_invalid_argument", duplicateStyle.ErrorCode);

            var bad = await tool.ExecuteAsync(CreateContext(renderer), Req("{\"candidates\":[{\"name\":\"Bad Name\",\"style\":\"alpha\",\"html\":\"<h1>x</h1>\"},{\"name\":\"two\",\"style\":\"beta\",\"html\":\"<h1>y</h1>\"},{\"name\":\"three\",\"style\":\"gamma\",\"html\":\"<h1>z</h1>\"}]}"));
            Assert.IsFalse(bad.Success);
            Assert.AreEqual(1, Directory.GetDirectories(store.Root).Length, "校验失败不得建工作区");
        }

        [TestMethod]
        public async Task StylePreview_ReturnsFontAndLayoutErrorsWithoutBlockingCandidates()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var catalogs = CreateCatalogs();
            var renderer = new FakeRenderer
            {
                ValidationReport = new OfficeCopilot.Slides.Rendering.DeckValidationCapture
                {
                    SlideCount = 1,
                    Issues = new List<OfficeCopilot.Slides.Rendering.DeckValidationIssue>
                    {
                        new OfficeCopilot.Slides.Rendering.DeckValidationIssue
                        {
                            Code = "font.weight_unavailable",
                            Severity = "warn",
                            Slide = 1,
                            Message = "字体字重不在随包矩阵中。",
                            Text = "900"
                        },
                        new OfficeCopilot.Slides.Rendering.DeckValidationIssue
                        {
                            Code = "layout.unsafe",
                            Severity = "error",
                            Slide = 1,
                            Message = "内容超出安全区。"
                        },
                        new OfficeCopilot.Slides.Rendering.DeckValidationIssue
                        {
                            Code = "layout.tight",
                            Severity = "warn",
                            Slide = 1,
                            Message = "内容间距偏紧。"
                        },
                        new OfficeCopilot.Slides.Rendering.DeckValidationIssue
                        {
                            Code = "text.margin",
                            Severity = "error",
                            Slide = 1,
                            Message = "旧布局规则，不属于 F10 过滤范围。"
                        }
                    }
                }
            };
            var tool = new PowerPointStylePreviewTool(() => store, () => catalogs);

            var result = await tool.ExecuteAsync(CreateContext(renderer), Req(
                "{\"candidates\":[{\"name\":\"safe\",\"style\":\"alpha\",\"html\":\"<h1>Cover</h1>\"},{\"name\":\"bold\",\"style\":\"beta\",\"html\":\"<h1>Bold</h1>\"},{\"name\":\"calm\",\"style\":\"gamma\",\"html\":\"<h1>Calm</h1>\"}]}"));

            Assert.IsTrue(result.Success, result.Content);
            Assert.AreEqual(3, renderer.ValidationCalls.Count);
            CollectionAssert.AreEqual(
                new[] { "capture", "validate", "capture", "validate", "capture", "validate" },
                renderer.Operations.ToArray());
            Assert.IsTrue(renderer.ValidationCalls.All(call => call.Contains(".validate.html?mode=validation#1")));
            Assert.IsTrue(result.Content.Contains("font.weight_unavailable"));
            StringAssert.Contains(result.Content, "`font.weight_unavailable` (warn)");
            Assert.IsTrue(result.Content.Contains("layout.unsafe"));
            Assert.IsFalse(result.Content.Contains("layout.tight"));
            Assert.IsFalse(result.Content.Contains("text.margin"));
            Assert.IsTrue(result.StructuredContentJson.Contains("font.weight_unavailable"));
            Assert.IsTrue(result.StructuredContentJson.Contains("layout.unsafe"));
            Assert.IsTrue(result.StructuredContentJson.Contains("previewImageId"));
        }

        [TestMethod]
        public async Task DeckCreate_FromPreviewCopiesTheSelectedCoverAndTheme()
        {
            var store = new DeckWorkspaceStore(Path.Combine(_root, "decks"));
            var catalogs = CreateCatalogs();
            var renderer = new FakeRenderer();
            var preview = await new PowerPointStylePreviewTool(() => store, () => catalogs).ExecuteAsync(
                CreateContext(renderer),
                Req("{\"candidates\":["
                    + "{\"name\":\"selected\",\"style\":\"alpha\",\"html\":\"<section class='slide'><h1 class='cover'>Selected cover</h1></section>\",\"css\":\".cover{color:red}\"},"
                    + "{\"name\":\"other\",\"style\":\"beta\",\"html\":\"<h1>Other</h1>\"},"
                    + "{\"name\":\"third\",\"style\":\"gamma\",\"html\":\"<h1>Third</h1>\"}]}"));
            Assert.IsTrue(preview.Success, preview.Content);
            var deckId = Path.GetFileName(Directory.GetDirectories(store.Root).Single());
            var workspace = store.Open(deckId);
            var source = workspace.ReadPreviewSource("selected");
            var create = await new PowerPointDeckCreateTool(() => store, () => catalogs).ExecuteAsync(
                new ToolExecutionContext(),
                Req("{\"deckId\":\"" + deckId + "\",\"fromPreview\":\"selected\",\"title\":\"Chosen deck\"}"));

            Assert.IsTrue(create.Success, create.Content);
            workspace = store.Open(deckId);
            Assert.AreEqual("alpha", workspace.Manifest.Style);
            Assert.AreEqual("Chosen deck", workspace.Manifest.Title);
            Assert.AreEqual(1, workspace.Slides.Count);
            var coverHtml = workspace.GetSlideHtml("s01");
            StringAssert.Contains(coverHtml, "<style data-nx-cover=\"\">" + source.Css + "</style>");
            StringAssert.Contains(coverHtml, "Selected cover");
            Assert.AreEqual(string.Empty, workspace.ReadThemeCss());
            Assert.AreEqual("selected", workspace.Manifest.Origin.Preview);
            Assert.AreEqual(source.Hash, workspace.Manifest.Origin.Hash);
            StringAssert.Contains(create.Content, "从第 2 页开始");

            var put = await new PowerPointDeckPutSlidesTool(() => store, () => catalogs).ExecuteAsync(
                new ToolExecutionContext(),
                Req("{\"deckId\":\"" + deckId + "\",\"css\":\".theme-x{color:blue}\",\"slides\":[{\"html\":\"<section class='slide'><p class='theme-x'>Two</p></section>\"}]}"));
            Assert.IsTrue(put.Success, put.Content);
            workspace = store.Open(deckId);
            Assert.AreEqual(".theme-x{color:blue}", workspace.ReadThemeCss());
            Assert.AreEqual(coverHtml, workspace.GetSlideHtml("s01"), "put_slides 的 css 不得改动封面");

            var overwrite = await new PowerPointDeckPutSlidesTool(() => store, () => catalogs).ExecuteAsync(
                new ToolExecutionContext(),
                Req("{\"deckId\":\"" + deckId + "\",\"slides\":[{\"id\":\"s01\",\"html\":\"<section class='slide'><style data-nx-cover>.cover{font-family:'Noto Sans SC'}</style><h1 class='cover'>Rewritten</h1></section>\"}]}"));
            Assert.IsTrue(overwrite.Success, overwrite.Content);
            StringAssert.Contains(overwrite.Content, "已锁定");
            workspace = store.Open(deckId);
            Assert.AreEqual("s01", workspace.Manifest.Origin.Slide);
            Assert.AreEqual(coverHtml, workspace.GetSlideHtml("s01"), "锁定封面不得被 put_slides 覆盖");

            var missingDeckId = await new PowerPointDeckCreateTool(() => store, () => catalogs).ExecuteAsync(
                new ToolExecutionContext(),
                Req("{\"fromPreview\":\"selected\"}"));
            Assert.IsFalse(missingDeckId.Success);
            Assert.AreEqual("ppt.deck_invalid_argument", missingDeckId.ErrorCode);

            var missingSource = await new PowerPointDeckCreateTool(() => store, () => catalogs).ExecuteAsync(
                new ToolExecutionContext(),
                Req("{\"deckId\":\"" + deckId + "\",\"fromPreview\":\"missing\"}"));
            Assert.IsFalse(missingSource.Success);
            Assert.AreEqual("ppt.deck_preview_not_found", missingSource.ErrorCode);

            var styleConflict = await new PowerPointDeckCreateTool(() => store, () => catalogs).ExecuteAsync(
                new ToolExecutionContext(),
                Req("{\"deckId\":\"" + deckId + "\",\"fromPreview\":\"selected\",\"style\":\"beta\"}"));
            Assert.IsFalse(styleConflict.Success);
            Assert.AreEqual("ppt.deck_invalid_argument", styleConflict.ErrorCode);

            var cssConflict = await new PowerPointDeckCreateTool(() => store, () => catalogs).ExecuteAsync(
                new ToolExecutionContext(),
                Req("{\"deckId\":\"" + deckId + "\",\"fromPreview\":\"selected\",\"css\":\".x{color:blue}\"}"));
            Assert.IsFalse(cssConflict.Success);
            Assert.AreEqual("ppt.deck_invalid_argument", cssConflict.ErrorCode);

            File.WriteAllText(workspace.PreviewPath("selected", ".css"), source.Css + " ");
            var tamperedSource = await new PowerPointDeckCreateTool(() => store, () => catalogs).ExecuteAsync(
                new ToolExecutionContext(),
                Req("{\"deckId\":\"" + deckId + "\",\"fromPreview\":\"selected\"}"));
            Assert.IsFalse(tamperedSource.Success);
            Assert.AreEqual("ppt.deck_preview_invalid", tamperedSource.ErrorCode);
        }

        private SlideAssetCatalogs CreateCatalogs()
        {
            var builtIn = Path.Combine(_root, "builtin");
            Write(Path.Combine(builtIn, "catalog", "styles.json"),
                "{\"styles\":[{\"slug\":\"alpha\",\"name\":\"Alpha\",\"kind\":\"preset\",\"tier\":\"certified\"},{\"slug\":\"beta\",\"name\":\"Beta\",\"kind\":\"preset\",\"tier\":\"certified\"},{\"slug\":\"gamma\",\"name\":\"Gamma\",\"kind\":\"preset\",\"tier\":\"certified\"}]}");
            Write(Path.Combine(builtIn, "presets", "alpha", "preset.md"), "# Alpha");
            Write(Path.Combine(builtIn, "presets", "beta", "preset.md"), "# Beta");
            Write(Path.Combine(builtIn, "presets", "gamma", "preset.md"), "# Gamma");
            return new SlideAssetCatalogs(new SlideAssetPaths(builtIn, Path.Combine(_root, "none")));
        }

        private static ToolExecutionContext CreateContext(IDeckRenderService renderer)
        {
            var services = new AgentServiceProvider();
            services.AddService<IDeckRenderService>(renderer);
            return new ToolExecutionContext { Services = services };
        }

        private static ToolExecutionRequest Req(string json)
        {
            return new ToolExecutionRequest { ToolCallId = "t1", ArgumentsJson = json };
        }

        private static void AssertDeckError(string code, Action action)
        {
            try
            {
                action();
            }
            catch (DeckException ex)
            {
                Assert.AreEqual(code, ex.Code, ex.Message);
                return;
            }

            Assert.Fail("应抛出 " + code);
        }

        private static int CountOf(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static string FindShippedAssets()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "OfficeCopilot.Slides.Core", "SlideAssets");
                if (File.Exists(Path.Combine(candidate, SlideAssetPaths.CatalogFileRelative)))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return null;
        }

        private sealed class FakeFontRegistrar : IUserFontRegistrar
        {
            public bool IsSupported { get; set; } = true;

            public bool FailRegistration { get; set; }

            public List<string> Registered { get; } = new List<string>();

            public int NotifyCount { get; private set; }

            public void Register(string fontFilePath, string displayName)
            {
                if (FailRegistration) throw new IOException("拒绝访问");
                if (!IsRegistered(fontFilePath)) Registered.Add(fontFilePath);
            }

            public bool IsRegistered(string fontFilePath)
            {
                return Registered.Any(path => string.Equals(path, fontFilePath, StringComparison.OrdinalIgnoreCase));
            }

            public void Unregister(string fontFilePath)
            {
                Registered.RemoveAll(path => string.Equals(path, fontFilePath, StringComparison.OrdinalIgnoreCase));
            }

            public void NotifyFontsChanged()
            {
                NotifyCount++;
            }
        }

        private sealed class FakeFontInstaller : IPowerPointFontInstaller
        {
            public int Calls { get; private set; }

            public IList<PptVerificationIssue> EnsureFonts(IEnumerable<SlideBuildPlan> plans, ISet<string> installedFonts)
            {
                Calls++;
                return new List<PptVerificationIssue>
                {
                    new PptVerificationIssue { Code = "ppt.font_installed", Message = "test" }
                };
            }
        }

        private sealed class FakeRenderer : IDeckRenderService
        {
            public List<string> Captures { get; } = new List<string>();

            public List<int> CapturedSlides { get; } = new List<int>();

            public List<string> ValidationCalls { get; } = new List<string>();

            public List<string> Operations { get; } = new List<string>();

            public string PreviewUrl { get; private set; }

            public string FontFamily { get; set; } = "Arial";

            public OfficeCopilot.Slides.Rendering.DeckValidationCapture ValidationReport { get; set; }

            public Task<DeckRenderCapture> CaptureAsync(string url, double scale, CancellationToken cancellationToken)
            {
                Operations.Add("capture");
                Captures.Add(url);
                return Task.FromResult(new DeckRenderCapture { Png = new byte[] { 0x89, 0x50, 0x4E, 0x47, (byte)Captures.Count }, Width = 1280, Height = 720 });
            }

            public Task<OfficeCopilot.Slides.Rendering.DeckSlideCapture> CaptureSlideAsync(string url, int slide, string mode, CancellationToken cancellationToken)
            {
                CapturedSlides.Add(slide);
                return Task.FromResult(new OfficeCopilot.Slides.Rendering.DeckSlideCapture
                {
                    Data = new OfficeCopilot.Slides.Deck.SlideCapture
                    {
                        Slide = slide,
                        Width = 1920,
                        Height = 1080,
                        TextBlocks = new List<OfficeCopilot.Slides.Deck.CapturedTextBlock>
                        {
                            new OfficeCopilot.Slides.Deck.CapturedTextBlock
                            {
                                Rectangle = new OfficeCopilot.Slides.Deck.CapturedRectangle { Left = 20, Top = 20, Width = 200, Height = 50 },
                                Text = "Slide " + slide,
                                Lines = new List<string> { "Slide " + slide },
                                FontFamily = FontFamily,
                                FontSize = 32,
                                LineHeight = 36
                            }
                        }
                    },
                    SlidePng = new byte[] { (byte)slide, 1 },
                    BackgroundPng = new byte[] { (byte)slide, 2 }
                });
            }

            public Task<OfficeCopilot.Slides.Rendering.DeckValidationCapture> ValidateAsync(string url, string validationUrl, string density, CancellationToken cancellationToken)
            {
                Operations.Add("validate");
                ValidationCalls.Add(url + "|" + validationUrl);
                return Task.FromResult(ValidationReport ?? new OfficeCopilot.Slides.Rendering.DeckValidationCapture { SlideCount = 1 });
            }

            public Task ShowPreviewAsync(string key, string title, string url)
            {
                PreviewUrl = url;
                return Task.CompletedTask;
            }
        }

        private sealed class FakeDeckBuildHost : IPowerPointDeckBuildHost
        {
            public IList<PowerPointDeckBuildSlide> Slides { get; private set; }

            public int VerifyCalls { get; private set; }

            public bool ThrowOnVerify { get; set; }

            public IList<int> AppendSlides(IList<PowerPointDeckBuildSlide> slides)
            {
                Slides = slides;
                return new[] { 7, 8 };
            }

            public PptVerificationReport VerifySlides(IList<int> slideIndexes, IList<PowerPointDeckBuildSlide> slides)
            {
                VerifyCalls++;
                if (ThrowOnVerify) throw new InvalidOperationException("readback unavailable");
                return new PptVerificationReport
                {
                    TextBlocksChecked = slides.Sum(slide => slide.Plan.TextBlocks.Count)
                };
            }
        }

        private sealed class BuildToolProbe : PowerPointDeckBuildTool
        {
            public PowerPointDeckBuildTarget Resolve(JObject args)
            {
                return ResolveTarget(args);
            }
        }
    }
}
