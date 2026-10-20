using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Agent.Tests.PowerPoint
{
    [TestClass]
    public sealed class FontShadowDiagnosticsTests
    {
        private const string Css =
            "/* 由 OfficeCopilot 生成 */\n" +
            "@font-face {\n" +
            "  font-family: 'Noto Sans SC';\n" +
            "  src: url('NotoSansSC/NotoSansSC-Regular.ttf') format('truetype');\n" +
            "  font-weight: 400;\n" +
            "  font-style: normal;\n" +
            "}\n" +
            "@font-face {\n" +
            "  font-family: 'Noto Sans JP';\n" +
            "  src: url('NotoSansSC/NotoSansSC-Regular.ttf') format('truetype');\n" +
            "  font-weight: 400;\n" +
            "  font-style: normal;\n" +
            "}\n" +
            "@font-face {\n" +
            "  font-family: 'Smiley Sans';\n" +
            "  src: url('SmileySans/SmileySans-Oblique.ttf') format('truetype');\n" +
            "  font-weight: 400;\n" +
            "  font-style: oblique;\n" +
            "}\n";

        [TestMethod]
        public void FindSystemShadows_ReportsSystemProvidedFamilyAndItsAlias()
        {
            var shadows = FontShadowDiagnostics.FindSystemShadows(
                Css,
                new HashSet<string> { "Noto Sans SC" });

            CollectionAssert.AreEqual(
                new[] { "Noto Sans JP", "Noto Sans SC" },
                new List<string>(shadows));
        }

        [TestMethod]
        public void FindSystemShadows_ReturnsNoMatchesWhenSystemHasNoFamilies()
        {
            var shadows = FontShadowDiagnostics.FindSystemShadows(Css, new HashSet<string>());

            Assert.AreEqual(0, shadows.Count);
        }

        [TestMethod]
        public void FindSystemShadows_DoesNotReportUnmatchedPackagedFonts()
        {
            var shadows = FontShadowDiagnostics.FindSystemShadows(
                Css,
                new HashSet<string> { "Noto Sans SC" });

            CollectionAssert.DoesNotContain(new List<string>(shadows), "Smiley Sans");
        }

        [TestMethod]
        public void RenderIsolation_ReadFontCssReturnsPackagedBytesUnchanged()
        {
            var path = Path.Combine(SlideAssetPaths.GetDefaultBuiltInRoot(), "fonts", "fonts.css");
            var expected = File.ReadAllBytes(path);
            var renderAssembly = Assembly.Load("OfficeCopilot.Slides.Rendering");
            var renderIsolation = renderAssembly.GetType(
                "OfficeCopilot.Slides.Rendering.RenderIsolation",
                true);
            var readFontCss = renderIsolation.GetMethod(
                "ReadFontCss",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(readFontCss);
            var actual = (byte[])readFontCss.Invoke(null, new object[] { path });

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void SystemFontCatalog_ClassifiesChineseCoverageAndReusesCache()
        {
            var assets = SlideAssetPaths.GetDefaultBuiltInRoot();
            var fonts = FontCatalog.Load(new SlideAssetPaths(
                assets,
                Path.Combine(Path.GetTempPath(), "nixer-system-font-test-user")));
            var chineseFont = fonts.Families.Single(family => family.Family == "Noto Sans SC");
            var latinFont = fonts.Families.Single(family => family.Family == "Inter");
            var cacheDirectory = Path.Combine(Path.GetTempPath(), "nixer-system-font-test-" + Guid.NewGuid().ToString("N"));
            var cachePath = Path.Combine(cacheDirectory, "system-font-catalog.json");

            try
            {
                var scanned = SystemFontCatalog.Load(
                    cachePath,
                    new[]
                    {
                        Path.Combine(chineseFont.Directory, chineseFont.Faces[0].File),
                        Path.Combine(latinFont.Directory, latinFont.Faces[0].File)
                    });

                Assert.IsFalse(scanned.CacheHit);
                Assert.IsTrue(scanned.IsChineseFamily("Noto Sans SC"));
                Assert.IsFalse(scanned.IsChineseFamily("Inter"));

                var cached = SystemFontCatalog.Load(
                    cachePath,
                    new[]
                    {
                        Path.Combine(chineseFont.Directory, chineseFont.Faces[0].File),
                        Path.Combine(latinFont.Directory, latinFont.Faces[0].File)
                    });
                Assert.IsTrue(cached.CacheHit);
                CollectionAssert.AreEqual(scanned.ChineseFamilies.ToArray(), cached.ChineseFamilies.ToArray());
            }
            finally
            {
                if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, true);
            }
        }
    }
}
