using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.PowerPoint.Tools;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Catalog;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Agent.Tests.PowerPoint
{
    [TestClass]
    public sealed class SlideTemplateRewriteTests
    {
        [TestMethod]
        public void Rewrite_GoogleFontsAndChartJs_BecomeLocal()
        {
            const string html = "<html><head>\n"
                + "  <link rel=\"preconnect\" href=\"https://fonts.googleapis.com\">\n"
                + "  <link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin>\n"
                + "  <link href=\"https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;700&family=Inter:wght@400&display=swap\"\n rel=\"stylesheet\">\n"
                + "  <script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>\n"
                + "  <style>@import url('https://fonts.googleapis.com/css2?family=Bebas+Neue&display=swap');</style>\n"
                + "</head><body><svg xmlns=\"http://www.w3.org/2000/svg\"></svg><section class=\"slide\"></section></body></html>";

            var result = TemplateHtmlRewriter.Rewrite(html);

            Assert.AreEqual(0, result.Unresolved.Count, string.Join(",", result.Unresolved));
            StringAssert.Contains(result.Html, "href=\"../../fonts/fonts.css\"");
            StringAssert.Contains(result.Html, "src=\"../../runtime/vendor/chart.umd.min.js\"");
            StringAssert.Contains(result.Html, "@import url('../../fonts/fonts.css');");
            CollectionAssert.AreEquivalent(new[] { "Space Grotesk", "Inter", "Bebas Neue" }, result.Families);
        }

        [TestMethod]
        public void Scanner_ReportsUnknownCdn_IgnoresSvgNamespace()
        {
            var refs = HtmlContractScanner.FindExternalReferences(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg><img src=\"https://evil.example/x.png\"><div style=\"background:url(//cdn.example/a.png)\"></div>");

            CollectionAssert.AreEqual(new[] { "https://evil.example/x.png", "//cdn.example/a.png" }, refs.ToArray());
        }

        [TestMethod]
        public void Splitter_KeepsOneSlidePerLayout_WithBaseHref()
        {
            const string html = "<!DOCTYPE html><html><head><style>.slide{}</style></head><body>"
                + "<div class=\"slide-counter\">1/2</div>"
                + "<section class=\"slide slide--cover active\"><h1>A</h1><div class=\"slide\">nested</div></section>"
                + "<section class=\"slide\" data-label=\"Key Stats\"><h1>B</h1></section>"
                + "</body></html>";

            var layouts = TemplateLayoutSplitter.Split(html);

            Assert.AreEqual(2, layouts.Count);
            Assert.AreEqual("01-cover.html", layouts[0].FileName);
            Assert.AreEqual("02-key-stats.html", layouts[1].FileName);
            StringAssert.Contains(layouts[0].SlideHtml, "<h1>A</h1>");
            Assert.IsFalse(layouts[0].SlideHtml.Contains("<h1>B</h1>"));
            StringAssert.Contains(layouts[1].SlideHtml, "class=\"slide active\"");
            StringAssert.Contains(layouts[0].Html, "<base href=\"../\">");
            StringAssert.Contains(layouts[0].Html, "nested");
            Assert.IsFalse(layouts[0].Html.Contains("<h1>B</h1>"));
            StringAssert.Contains(layouts[1].Html, "class=\"slide active\"");
            StringAssert.Contains(layouts[1].Html, "slide-counter");
        }

        [TestMethod]
        public void FontNaming_ParsesWeightStyleAndFamily()
        {
            int weight;
            string style;
            FontFaceNaming.ParseFace("SourceSerif4-SemiBold.ttf", out weight, out style);
            Assert.AreEqual(600, weight);
            Assert.AreEqual("normal", style);

            FontFaceNaming.ParseFace("Inter-BoldItalic.ttf", out weight, out style);
            Assert.AreEqual(700, weight);
            Assert.AreEqual("italic", style);

            FontFaceNaming.ParseFace("Lora-Italic.ttf", out weight, out style);
            Assert.AreEqual(400, weight);
            Assert.AreEqual("italic", style);

            Assert.AreEqual("Source Serif 4", FontFaceNaming.FolderToFamily("SourceSerif4"));
            Assert.AreEqual("Big Shoulders Display", FontFaceNaming.FolderToFamily("BigShouldersDisplay"));
            Assert.AreEqual("IBM Plex Mono", FontFaceNaming.FolderToFamily("IBMPlexMono"));
        }
    }

    [TestClass]
    public sealed class StyleCatalogTests
    {
        private string _root;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "nixer-style-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_root, true); }
            catch (IOException) { }
        }

        [TestMethod]
        public void UserTemplate_OverridesBuiltIn_AndInvalidIsFlagged()
        {
            var builtIn = Path.Combine(_root, "builtin");
            var user = Path.Combine(_root, "user");
            Write(Path.Combine(builtIn, "catalog", "styles.json"),
                "{\"styles\":[{\"slug\":\"alpha\",\"name\":\"Alpha\",\"kind\":\"template\",\"tier\":\"certified\",\"mood\":[\"calm\"]},"
                + "{\"slug\":\"beta\",\"name\":\"Beta\",\"kind\":\"template\",\"tier\":\"experimental\",\"mood\":[\"bold\"],\"avoidFor\":\"calm finance\"}]}");
            Write(Path.Combine(user, "templates", "alpha", "template.html"), "<section class=\"slide\"></section>");
            Write(Path.Combine(user, "templates", "alpha", "template.json"),
                "{\"name\":\"My Alpha\",\"mood\":[\"calm\"],\"typography\":{\"display\":\"Nowhere Sans\"},"
                + "\"safeArea\":{\"content\":[\"section.slide\"],\"background\":[],\"chrome\":[\".toolbar\"],\"safeX\":136,\"safeY\":100}}");
            Write(Path.Combine(user, "templates", "gamma", "template.html"), "<link href=\"https://fonts.googleapis.com/css2?family=X\" rel=\"stylesheet\"><div>no slides</div>");

            var catalog = StyleCatalog.Load(new SlideAssetPaths(builtIn, user), FontCatalog.Load(new SlideAssetPaths(builtIn, user)));

            StyleEntry alpha;
            Assert.IsTrue(catalog.TryGet("alpha", out alpha));
            Assert.AreEqual("My Alpha", alpha.Name);
            Assert.AreEqual(StyleTiers.User, alpha.Tier);
            Assert.IsTrue(alpha.IsValid);
            CollectionAssert.AreEqual(new[] { "Nowhere Sans" }, alpha.MissingFonts);
            Assert.AreEqual(136, alpha.SafeArea.SafeX);
            CollectionAssert.AreEqual(new[] { "section.slide" }, alpha.SafeArea.Content);

            StyleEntry gamma;
            Assert.IsTrue(catalog.TryGet("gamma", out gamma));
            Assert.IsFalse(gamma.IsValid);
            StringAssert.Contains(gamma.InvalidReason, "外部引用");
            StringAssert.Contains(gamma.InvalidReason, "幻灯片标记");

            var hits = catalog.Search(new StyleSearchQuery { Text = "calm" });
            Assert.AreEqual("alpha", hits.First().Entry.Slug);
            Assert.IsFalse(hits.Any(h => h.Entry.Slug == "beta"), "avoidFor 命中应扣分出局");
            Assert.IsFalse(catalog.Search(new StyleSearchQuery()).Any(h => h.Entry.Slug == "gamma"));
        }

        [TestMethod]
        public void ShippedAssets_MeetP1Acceptance()
        {
            var root = FindShippedAssets();
            if (root == null)
            {
                Assert.Inconclusive("未找到 SlideAssets。");
            }

            var paths = new SlideAssetPaths(root, Path.Combine(_root, "no-user"));
            var fonts = FontCatalog.Load(paths);
            var catalog = StyleCatalog.Load(paths, fonts);

            var templates = catalog.Entries.Where(e => e.Kind == StyleKinds.Template).ToList();
            Assert.AreEqual(34, templates.Count);
            Assert.AreEqual(12, catalog.Entries.Count(e => e.Kind == StyleKinds.Preset));
            var terminalGreen = catalog.Entries.Single(e => e.Slug == "terminal-green");
            CollectionAssert.AreEqual(new[] { "JetBrains Mono", "Noto Sans SC" }, terminalGreen.Fonts);
            Assert.AreEqual(0, terminalGreen.MissingFonts.Count, string.Join(",", terminalGreen.MissingFonts));
            StringAssert.Contains(catalog.GetDetail("terminal-green", false).PresetMarkdown, "Noto Sans SC");

            var certified = templates.Where(e => e.Tier == StyleTiers.Certified).ToList();
            Assert.AreEqual(6, certified.Count);
            foreach (var e in certified)
            {
                Assert.AreEqual(0, e.MissingFonts.Count, e.Slug + " 缺字体：" + string.Join(",", e.MissingFonts));
            }

            foreach (var e in templates)
            {
                Assert.IsTrue(e.CdnFree, e.Slug);
                Assert.IsTrue(e.Layouts.Count > 0, e.Slug + " 没有版式");
                Assert.IsNotNull(e.SafeArea, e.Slug + " 缺少 safeArea 元数据");
                Assert.IsTrue(e.SafeArea.Content.Count > 0, e.Slug + " 缺少内容选择器");
                Assert.IsTrue(e.SafeArea.SafeX >= 128, e.Slug + " 水平安全边界不足");
                Assert.IsTrue(e.SafeArea.SafeY >= 96, e.Slug + " 垂直安全边界不足");
                var html = File.ReadAllText(Path.Combine(e.Directory, "template.html"), Encoding.UTF8);
                Assert.AreEqual(0, HtmlContractScanner.FindExternalReferences(html).Count, e.Slug);
            }

            Assert.IsTrue(fonts.Contains("Space Grotesk"));
            Assert.IsTrue(
                fonts.Families.Where(f => !f.EmojiOnly).All(f => f.Embeddable),
                "随包文本字体必须是可嵌入的 TrueType");
            Assert.IsTrue(
                fonts.Families.Where(f => f.EmojiOnly).All(f => !f.Embeddable),
                "emoji 专用字体不得进入 PowerPoint 字体嵌入路径");

            var detail = catalog.GetDetail("signal", true);
            Assert.IsNotNull(detail.DesignMarkdown);
            Assert.IsNotNull(detail.PreviewMarkdown);
        }

        [TestMethod]
        public async Task StyleTools_SearchAndGet()
        {
            var builtIn = Path.Combine(_root, "builtin");
            Write(Path.Combine(builtIn, "catalog", "styles.json"),
                "{\"styles\":[{\"slug\":\"alpha\",\"name\":\"Alpha\",\"kind\":\"template\",\"tier\":\"certified\",\"mood\":[\"calm\"],\"palette\":{\"ink\":\"#111\"}}]}");
            Write(Path.Combine(builtIn, "templates", "alpha", "preview.md"), "# Alpha Preview");
            var catalogs = new SlideAssetCatalogs(new SlideAssetPaths(builtIn, Path.Combine(_root, "none")));

            var search = new PowerPointStyleSearchTool(() => catalogs);
            var get = new PowerPointStyleGetTool(() => catalogs);
            Assert.IsTrue(search.Definition.IsReadOnly);
            Assert.IsTrue(get.Definition.IsReadOnly);

            var found = await search.ExecuteAsync(new ToolExecutionContext(), new ToolExecutionRequest { ArgumentsJson = "{\"query\":\"calm\"}" });
            Assert.IsTrue(found.Success, found.Content);
            StringAssert.Contains(found.Content, "alpha");

            var detail = await get.ExecuteAsync(new ToolExecutionContext(), new ToolExecutionRequest { ArgumentsJson = "{\"slug\":\"alpha\"}" });
            Assert.IsTrue(detail.Success, detail.Content);
            StringAssert.Contains(detail.Content, "Alpha Preview");
            StringAssert.Contains(detail.Content, "#111");

            var missing = await get.ExecuteAsync(new ToolExecutionContext(), new ToolExecutionRequest { ArgumentsJson = "{\"slug\":\"nope\"}" });
            Assert.IsFalse(missing.Success);
        }

        [TestMethod]
        public void StyleGroup_IsRegisteredAndReadOnly()
        {
            var tools = PowerPointBuiltInToolCatalog.CreateAllTools();
            var style = tools.Where(t => t.Definition.Name.StartsWith("ppt_style_", StringComparison.Ordinal)).ToList();
            Assert.AreEqual(3, style.Count);
            foreach (var t in style)
            {
                string group;
                Assert.IsTrue(ToolCatalogBuilder.TryGetGroupName(t.Definition, out group));
                Assert.AreEqual(PowerPointToolGroups.Style, group);
                Assert.IsTrue(t.Definition.IsReadOnly);
            }
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
    }
}
