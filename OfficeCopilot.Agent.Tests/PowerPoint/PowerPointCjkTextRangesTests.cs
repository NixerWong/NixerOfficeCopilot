using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.PowerPoint;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Agent.Tests.PowerPoint
{
    [TestClass]
    public sealed class PowerPointCjkTextRangesTests
    {
        [TestMethod]
        public void Find_GroupsAdjacentCjkScriptsAndPunctuation()
        {
            const string text = "A漢字かなカナ한글、B";

            var ranges = CjkTextRanges.Find(text).ToList();

            Assert.AreEqual(1, ranges.Count);
            Assert.AreEqual(1, ranges[0].Start);
            Assert.AreEqual("漢字かなカナ한글、".Length, ranges[0].Length);
        }

        [TestMethod]
        public void Find_SplitsRunsAroundLatinCharacters()
        {
            var ranges = CjkTextRanges.Find("A漢B字C").ToList();

            Assert.AreEqual(2, ranges.Count);
            Assert.AreEqual(1, ranges[0].Start);
            Assert.AreEqual(1, ranges[0].Length);
            Assert.AreEqual(3, ranges[1].Start);
            Assert.AreEqual(1, ranges[1].Length);
        }

        [TestMethod]
        public void Find_UsesUtf16OffsetsForSupplementaryIdeographs()
        {
            const string text = "A𠀀B";

            var range = CjkTextRanges.Find(text).Single();

            Assert.AreEqual(1, range.Start);
            Assert.AreEqual(2, range.Length);
        }

        [TestMethod]
        public void Find_DoesNotClassifyEmojiAsCjk()
        {
            Assert.AreEqual(0, CjkTextRanges.Find("A😀B").Count());
        }

        [TestMethod]
        public void IsInstalledFontName_RequiresAnExactRegisteredName()
        {
            var installedFonts = new HashSet<string>
            {
                "DM Sans",
                "DM Sans Medium",
                "Source Serif 4",
                "Source Serif 4 SemiBold",
                "Noto Sans SC",
                "Noto Sans SC Regular",
                "Arial"
            };

            Assert.IsTrue(PptVerifier.IsInstalledFontName("DM Sans", installedFonts));
            Assert.IsTrue(PptVerifier.IsInstalledFontName("DM Sans Medium", installedFonts));
            Assert.IsTrue(PptVerifier.IsInstalledFontName("Source Serif 4", installedFonts));
            Assert.IsTrue(PptVerifier.IsInstalledFontName("Source Serif 4 SemiBold", installedFonts));
            Assert.IsTrue(PptVerifier.IsInstalledFontName("Noto Sans SC Regular", installedFonts));
            Assert.IsFalse(PptVerifier.IsInstalledFontName("DM Sans 14pt", installedFonts));
            Assert.IsFalse(PptVerifier.IsInstalledFontName("Arial Narrow", installedFonts));
            Assert.IsFalse(PptVerifier.IsInstalledFontName("Missing Family", installedFonts));
        }

        [TestMethod]
        public void FontNameResolver_NormalizesOnlyKnownOpticalSizeTokensAgainstRegistryNames()
        {
            var registeredNames = new HashSet<string>();
            registeredNames.UnionWith(InstalledFontState.FontNamesFromRecords(new[]
            {
                Name(1, "DM Sans 14pt"),
                Name(2, "Regular"),
                Name(16, "DM Sans"),
                Name(17, "Regular")
            }));
            registeredNames.UnionWith(InstalledFontState.FontNamesFromRecords(new[]
            {
                Name(1, "DM Sans 14pt Medium"),
                Name(2, "Regular"),
                Name(16, "DM Sans"),
                Name(17, "Medium")
            }));
            registeredNames.UnionWith(InstalledFontState.FontNamesFromRecords(new[]
            {
                Name(1, "Arial"),
                Name(2, "Regular")
            }));

            Assert.IsFalse(PptVerifier.IsInstalledFontName("DM Sans 14pt", registeredNames));

            var plan = new SlideBuildPlan
            {
                TextBlocks = new List<PlannedTextBlock>
                {
                    new PlannedTextBlock
                    {
                        FontName = "DM Sans 14pt",
                        FontNameFarEast = "DM Sans 14pt Medium"
                    }
                },
                Tables = new List<PlannedTable>
                {
                    new PlannedTable
                    {
                        Rows = new List<IList<PlannedTableCell>>
                        {
                            new List<PlannedTableCell>
                            {
                                new PlannedTableCell { FontName = "Arial Narrow" },
                                new PlannedTableCell { FontNameFarEast = "DM Sans 14pt Medium" }
                            }
                        }
                    }
                }
            };

            var unresolved = PowerPointFontNameResolver.NormalizePlans(new[] { plan }, registeredNames);

            Assert.AreEqual("DM Sans", plan.TextBlocks[0].FontName);
            Assert.AreEqual("DM Sans Medium", plan.TextBlocks[0].FontNameFarEast);
            Assert.AreEqual(
                "DM Sans Medium",
                plan.Tables[0].Rows[0][1].FontNameFarEast);
            CollectionAssert.AreEqual(new[] { "Arial Narrow" }, unresolved.ToArray());
        }

        [TestMethod]
        public void ReadNameRecords_ParsesRegisteredTrueTypeCollections()
        {
            var collectionPath = InstalledFontState.RegisteredFontFiles()
                .Where(path => string.Equals(Path.GetExtension(path), ".ttc", System.StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(path => TrueTypeInfo.ReadNameRecords(path).Any(record => record.FaceIndex > 0));
            if (collectionPath == null)
            {
                Assert.Inconclusive("No registered multi-face TrueType collection is available on this machine.");
            }

            var records = TrueTypeInfo.ReadNameRecords(collectionPath);

            Assert.IsTrue(records.Any(record => record.FaceIndex == 0));
            Assert.IsTrue(records.Any(record => record.FaceIndex > 0));

            var collectionFace = records.First(record => record.FaceIndex > 0).FaceIndex;
            Assert.IsTrue(TrueTypeInfo.ReadUnicodeCoverage(collectionPath, collectionFace).Count > 0);
        }

        private static TrueTypeNameRecord Name(int id, string value)
        {
            return new TrueTypeNameRecord { NameId = id, LanguageId = 0x0409, Value = value };
        }
    }
}
