using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Tests.PowerPoint.Harness;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Catalog;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Agent.Tests.PowerPoint
{
    [TestClass]
    public sealed class HarnessFixtureTests
    {
        private const string HarnessDeckId = "20261016-122000-harn01";
        private const int FontRowsPerSlide = 16;

        [TestMethod]
        public void FontMatrix_IsGeneratedFromEveryPowerPointFaceAndIncludesInvalidCases()
        {
            var assets = FindShippedAssets();
            Assert.IsNotNull(assets, "Could not locate SlideAssets.");

            var fontJson = File.ReadAllText(Path.Combine(assets, SlideAssetPaths.FontsJsonRelative), Encoding.UTF8);
            var families = JArray.Parse(fontJson);
            var expectedFaceCount = families
                .Where(family => (bool?)family["emojiOnly"] != true)
                .Sum(family => ((JArray)family["faces"]).Count);
            var pages = BuildFontMatrix(families);
            var html = string.Join(string.Empty, pages);

            Assert.AreEqual(4, pages.Count);
            Assert.AreEqual(expectedFaceCount + 18, CountOccurrences(html, "class=\"font-row\""));
            Assert.IsFalse(html.Contains("Noto Color Emoji 400 normal"), "Emoji-only families are not PowerPoint text faces.");
            Assert.IsFalse(html.Contains("Space Grotesk 600 normal — Hamburgefonstiv"), "The unresolved Space Grotesk 600 face must not be in the supported matrix.");
            StringAssert.Contains(html, "data-family=\"Noto Sans SC\" data-weight=\"900\" data-style=\"normal\"");
            StringAssert.Contains(html, "data-family=\"Noto Sans SC\" data-weight=\"400\" data-style=\"italic\"");
            StringAssert.Contains(html, "data-family=\"Smiley Sans\" data-weight=\"400\" data-style=\"italic\"");
            StringAssert.Contains(html, "data-family=\"Segoe UI\" data-weight=\"400\" data-style=\"normal\"");
            StringAssert.Contains(html, "data-family=\"Unbundled Display\" data-weight=\"400\" data-style=\"normal\"");
            StringAssert.Contains(html, "Albert Sans 400 normal (no CJK fallback)");
            StringAssert.Contains(html, "DM Sans 400 normal — X Hamburgefonstiv 0123");
            StringAssert.Contains(html, "Source Serif 4 400 normal — X 汉字永和");
            StringAssert.Contains(html, "IBM Plex Mono 400 normal — X Hamburgefonstiv 0123");
            StringAssert.Contains(html, "JetBrains Mono 400 normal — X 汉字永和");
            StringAssert.Contains(html, "Hamburgefonstiv 0123 汉字永和");
        }

        [TestMethod]
        public void GoldenFixtures_AssembleInRepresentativeCatalogStyles()
        {
            var assets = FindShippedAssets();
            Assert.IsNotNull(assets, "Could not locate SlideAssets.");

            var paths = new SlideAssetPaths(assets, Path.Combine(Path.GetTempPath(), "nx-harness-empty-user-assets"));
            var fonts = FontCatalog.Load(paths);
            var styles = StyleCatalog.Load(paths, fonts);
            var cover = ReadFixture("cover.html");
            var body = ReadFixture("body.html");
            var themeCss = ".harness-cover{font-family:'Noto Sans SC',sans-serif}"
                + ".harness-body{font-family:'Inter','Noto Sans SC',sans-serif}";
            var styleSlugs = new[] { "daisy-days", "vellum", "terminal-green", "dark-botanical", "studio", "bold-signal" };

            foreach (var slug in styleSlugs)
            {
                StyleEntry style;
                Assert.IsTrue(styles.TryGet(slug, out style), "Missing representative style: " + slug);

                var html = DeckAssembler.Assemble(new DeckAssemblyOptions
                {
                    DeckId = HarnessDeckId,
                    Style = style,
                    Slides = new[] { cover, body },
                    ThemeCss = themeCss,
                    Language = "zh-CN",
                    Title = "PPT harness"
                });

                StringAssert.Contains(html, "NIXER_HARNESS_COVER");
                StringAssert.Contains(html, "NIXER_HARNESS_BODY");
                Assert.AreEqual(2, CountOccurrences(html, "NIXER_HARNESS_"));
            }
        }

        [TestMethod]
        public void GlyphAndSafeAreaFixtures_AssembleInShellMode()
        {
            foreach (var fixture in new[] { "glyphs.html", "safe-area.html" })
            {
                var html = DeckAssembler.Assemble(new DeckAssemblyOptions
                {
                    DeckId = HarnessDeckId,
                    Slides = new[] { ReadFixture(fixture) },
                    Language = "zh-CN",
                    Title = fixture
                });

                Assert.IsTrue(html.IndexOf("class=\"slide", StringComparison.Ordinal) >= 0, fixture);
            }

            var glyphs = ReadFixture("glyphs.html");
            StringAssert.Contains(glyphs, "aria-hidden=\"true\"");
            StringAssert.Contains(glyphs, ".harness-pseudo::before");
            StringAssert.Contains(glyphs, "<text");
            StringAssert.Contains(glyphs, "&#xE000;");
            var safeArea = ReadFixture("safe-area.html");
            StringAssert.Contains(safeArea, "data-harness=\"safe-edge\"");
            StringAssert.Contains(safeArea, "data-harness=\"bleed\"");
        }

        [TestMethod]
        public void StructuralSimilarity_IsOneForIdenticalImagesAndDropsForChangedPixels()
        {
            using (var expected = new Bitmap(17, 17))
            using (var identical = new Bitmap(17, 17))
            using (var changed = new Bitmap(17, 17))
            {
                using (var expectedGraphics = Graphics.FromImage(expected))
                using (var identicalGraphics = Graphics.FromImage(identical))
                using (var changedGraphics = Graphics.FromImage(changed))
                {
                    expectedGraphics.Clear(Color.White);
                    identicalGraphics.Clear(Color.White);
                    changedGraphics.Clear(Color.Black);
                }

                Assert.AreEqual(1.0, HarnessImageMetrics.StructuralSimilarity(expected, identical), 0.000001);
                Assert.IsTrue(HarnessImageMetrics.StructuralSimilarity(expected, changed) < 0.1);
            }
        }

        [TestMethod]
        public void StructuralSimilarity_ConvertsIndexedPngPixelsCorrectly()
        {
            using (var indexed = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format8bppIndexed))
            using (var trueColor = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                var palette = indexed.Palette;
                palette.Entries[1] = Color.White;
                indexed.Palette = palette;
                var bounds = new Rectangle(0, 0, indexed.Width, indexed.Height);
                var data = indexed.LockBits(
                    bounds,
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
                try
                {
                    var row = Enumerable.Repeat((byte)1, indexed.Width).ToArray();
                    for (var y = 0; y < indexed.Height; y++)
                    {
                        Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length);
                    }
                }
                finally
                {
                    indexed.UnlockBits(data);
                }

                using (var graphics = Graphics.FromImage(trueColor))
                {
                    graphics.Clear(Color.White);
                }

                Assert.AreEqual(1.0, HarnessImageMetrics.StructuralSimilarity(indexed, trueColor), 0.000001);
            }
        }

        [TestMethod]
        public void DifferenceHeatmap_WritesRedPixelsForChangedRegions()
        {
            var path = Path.Combine(Path.GetTempPath(), "nixer-harness-diff-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (var expected = new Bitmap(8, 8))
                using (var actual = new Bitmap(8, 8))
                {
                    using (var expectedGraphics = Graphics.FromImage(expected))
                    using (var actualGraphics = Graphics.FromImage(actual))
                    {
                        expectedGraphics.Clear(Color.White);
                        actualGraphics.Clear(Color.Black);
                    }

                    HarnessImageMetrics.SaveDifferenceHeatmap(expected, actual, path);
                }

                using (var heatmap = new Bitmap(path))
                {
                    Assert.IsTrue(heatmap.GetPixel(0, 0).R > 200);
                    Assert.AreEqual(0, heatmap.GetPixel(0, 0).G);
                    Assert.AreEqual(0, heatmap.GetPixel(0, 0).B);
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        internal static IList<string> BuildFontMatrixFromAssets(string assets)
        {
            var fontJson = File.ReadAllText(Path.Combine(assets, SlideAssetPaths.FontsJsonRelative), Encoding.UTF8);
            return BuildFontMatrix(JArray.Parse(fontJson));
        }

        private static IList<string> BuildFontMatrix(JArray families)
        {
            var pages = new List<string>();
            var pageNumber = 1;
            var rowsOnPage = 0;
            var html = StartFontMatrixPage(pageNumber);
            foreach (var family in families)
            {
                if ((bool?)family["emojiOnly"] == true) continue;

                var familyName = (string)family["family"];
                foreach (var face in (JArray)family["faces"])
                {
                    AppendFontRow(html, familyName, (int)face["weight"], (string)face["style"], true);
                    rowsOnPage++;
                    if (rowsOnPage == FontRowsPerSlide)
                    {
                        pages.Add(FinishFontMatrixPage(html));
                        html = StartFontMatrixPage(++pageNumber);
                        rowsOnPage = 0;
                    }
                }
            }

            AppendFontRow(html, "Noto Sans SC", 900, "normal", true);
            AppendFontRow(html, "Noto Sans SC", 400, "italic", true);
            AppendFontRow(html, "Smiley Sans", 400, "italic", true);
            AppendFontRow(html, "Segoe UI", 400, "normal", true);
            AppendFontRow(html, "Unbundled Display", 400, "normal", true);
            AppendFontRow(html, "Albert Sans", 400, "normal", false, " (no CJK fallback)");
            foreach (var family in new[] { "DM Sans", "Source Serif 4", "IBM Plex Mono", "JetBrains Mono" })
            {
                AppendFontRow(html, family, 400, "normal", true, null, "X");
                AppendFontRow(html, family, 400, "normal", true, null, "X Hamburgefonstiv 0123");
                AppendFontRow(html, family, 400, "normal", true, null, "X 汉字永和");
            }

            return pages.Concat(new[] { FinishFontMatrixPage(html) }).ToList();
        }

        private static StringBuilder StartFontMatrixPage(int pageNumber)
        {
            return new StringBuilder()
                .Append("<section class=\"slide harness-font-matrix\"><h1>Font matrix ")
                .Append(pageNumber)
                .Append("</h1>");
        }

        private static string FinishFontMatrixPage(StringBuilder html)
        {
            return html.Append("</section>").ToString();
        }

        private static void AppendFontRow(
            StringBuilder html,
            string family,
            int weight,
            string style,
            bool includeCjkFallback,
            string labelSuffix = null,
            string sampleText = "Hamburgefonstiv 0123 汉字永和")
        {
            var encodedFamily = WebUtility.HtmlEncode(family);
            html.Append("<p class=\"font-row\" data-family=\"").Append(encodedFamily)
                .Append("\" data-weight=\"").Append(weight)
                .Append("\" data-style=\"").Append(WebUtility.HtmlEncode(style))
                .Append("\" style=\"font-family:'").Append(encodedFamily).Append('\'');
            if (includeCjkFallback && !string.Equals(family, "Noto Sans SC", StringComparison.OrdinalIgnoreCase))
            {
                html.Append(",'Noto Sans SC'");
            }

            html.Append(";font-weight:").Append(weight)
                .Append(";font-style:").Append(WebUtility.HtmlEncode(style))
                .Append("\">").Append(encodedFamily).Append(' ').Append(weight).Append(' ')
                .Append(WebUtility.HtmlEncode(style)).Append(labelSuffix).Append(" — ")
                .Append(WebUtility.HtmlEncode(sampleText)).Append("</p>");
        }

        private static string ReadFixture(string name)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerPoint", "Fixtures", "Harness", name);
            Assert.IsTrue(File.Exists(path), "Missing harness fixture: " + path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string FindShippedAssets()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "OfficeCopilot.Slides.Core", "SlideAssets");
                if (File.Exists(Path.Combine(candidate, SlideAssetPaths.CatalogFileRelative)))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static int CountOccurrences(string text, string value)
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
    }
}
