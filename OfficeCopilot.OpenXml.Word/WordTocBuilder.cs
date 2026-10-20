using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace OfficeCopilot.OpenXml.Word
{
    /// <summary>
    /// Regenerates TOC field entries from document headings. Ported from
    /// officecli Core/WordTocBuilder.cs (C# 7.3 / netstandard2.0 adaptation).
    /// </summary>
    internal static class WordTocBuilder
    {
        /// <summary>
        /// Regenerate every TOC field in the document. Entry page numbers are
        /// emitted as PAGEREF fields marked dirty so Word fills the real values
        /// when the document is opened / fields update.
        /// </summary>
        public static void RegenerateAllTocs(WordprocessingDocument doc)
        {
            var body = doc.MainDocumentPart != null ? doc.MainDocumentPart.Document != null ? doc.MainDocumentPart.Document.Body : null : null;
            if (body == null)
                return;

            var headings = EnumerateHeadings(doc, body);
            EnsureHeadingBookmarks(body, headings);

            foreach (var pair in FindTocFields(body))
            {
                var entries = GenerateEntries(headings, pair.Spec);
                ReplaceTocFieldContent(body, pair.TocPara, entries);
            }
        }

        // ==================== Phase 1: Heading enumeration ====================

        public sealed class HeadingInfo
        {
            public Paragraph Para { get; }
            public int Level { get; }
            public string Text { get; }
            public string BookmarkName { get; set; }

            public HeadingInfo(Paragraph para, int level, string text)
            {
                Para = para;
                Level = level;
                Text = text;
                BookmarkName = string.Empty;
            }
        }

        private static List<HeadingInfo> EnumerateHeadings(WordprocessingDocument doc, Body body)
        {
            var styleLevels = ResolveHeadingStyleLevels(doc);
            var list = new List<HeadingInfo>();
            foreach (var p in body.Descendants<Paragraph>())
            {
                // Skip paragraphs inside text boxes / floating frames — only
                // body-level headings should drive TOC generation, matching Word.
                if (p.Ancestors<TextBoxContent>().Any())
                    continue;

                var level = ResolveOutlineLevel(p, styleLevels);
                if (level < 1 || level > 9)
                    continue;

                var text = ExtractHeadingText(p);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                list.Add(new HeadingInfo(p, level, text));
            }
            return list;
        }

        private static Dictionary<string, int> ResolveHeadingStyleLevels(WordprocessingDocument doc)
        {
            var map = new Dictionary<string, int>();
            var styles = doc.MainDocumentPart != null ? doc.MainDocumentPart.StyleDefinitionsPart != null ? doc.MainDocumentPart.StyleDefinitionsPart.Styles : null : null;
            if (styles == null)
                return map;

            foreach (var style in styles.Elements<Style>())
            {
                var id = style.StyleId != null ? style.StyleId.Value : null;
                if (string.IsNullOrEmpty(id))
                    continue;

                var outlineLevel = style.StyleParagraphProperties != null && style.StyleParagraphProperties.OutlineLevel != null && style.StyleParagraphProperties.OutlineLevel.Val != null
                    ? style.StyleParagraphProperties.OutlineLevel.Val.Value
                    : (int?)null;
                if (outlineLevel.HasValue && outlineLevel.Value >= 0 && outlineLevel.Value <= 8)
                    map[id] = outlineLevel.Value + 1;
            }
            return map;
        }

        private static int ResolveOutlineLevel(Paragraph p, Dictionary<string, int> styleLevels)
        {
            var direct = p.ParagraphProperties != null && p.ParagraphProperties.OutlineLevel != null && p.ParagraphProperties.OutlineLevel.Val != null
                ? p.ParagraphProperties.OutlineLevel.Val.Value
                : (int?)null;
            if (direct.HasValue && direct.Value >= 0 && direct.Value <= 8)
                return direct.Value + 1;

            var styleId = p.ParagraphProperties != null && p.ParagraphProperties.ParagraphStyleId != null
                ? p.ParagraphProperties.ParagraphStyleId.Val != null ? p.ParagraphProperties.ParagraphStyleId.Val.Value : null
                : null;
            if (!string.IsNullOrEmpty(styleId))
            {
                int styleLevel;
                if (styleLevels.TryGetValue(styleId, out styleLevel))
                    return styleLevel;

                // Fallback: legacy Heading1-9 style names without explicit outline level.
                var m = Regex.Match(styleId, @"^Heading([1-9])$");
                if (m.Success)
                    return int.Parse(m.Groups[1].Value);
            }
            return -1;
        }

        private static string ExtractHeadingText(Paragraph p)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in p.Descendants<Text>())
                sb.Append(t.Text);
            return sb.ToString().Trim();
        }

        // ==================== Phase 2: Bookmark management ====================

        private static void EnsureHeadingBookmarks(Body body, List<HeadingInfo> headings)
        {
            int maxId = body.Descendants<BookmarkStart>()
                .Select(b => b.Id != null && b.Id.Value != null && int.TryParse(b.Id.Value, out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();

            foreach (var heading in headings)
            {
                var existing = heading.Para.Descendants<BookmarkStart>()
                    .FirstOrDefault(b => b.Name != null && b.Name.Value != null && b.Name.Value.StartsWith("_Toc", StringComparison.Ordinal));
                if (existing != null)
                {
                    heading.BookmarkName = existing.Name.Value;
                    continue;
                }

                var name = "_Toc" + Guid.NewGuid().ToString("N").Substring(0, 8);
                var bookmarkId = (++maxId).ToString();

                var bookmarkStart = new BookmarkStart { Id = bookmarkId, Name = name };
                var bookmarkEnd = new BookmarkEnd { Id = bookmarkId };
                var pPr = heading.Para.GetFirstChild<ParagraphProperties>();
                if (pPr != null)
                    pPr.InsertAfterSelf(bookmarkStart);
                else
                    heading.Para.PrependChild(bookmarkStart);
                heading.Para.AppendChild(bookmarkEnd);
                heading.BookmarkName = name;
            }
        }

        // ==================== Phase 3: Entry generation ====================

        public sealed class TocSpec
        {
            public int MinLevel { get; }
            public int MaxLevel { get; }
            public bool Hyperlinks { get; }
            public bool NoPageNum { get; }

            public TocSpec(int minLevel, int maxLevel, bool hyperlinks, bool noPageNum)
            {
                MinLevel = minLevel;
                MaxLevel = maxLevel;
                Hyperlinks = hyperlinks;
                NoPageNum = noPageNum;
            }
        }

        private static List<TocFieldLocation> FindTocFields(Body body)
        {
            var list = new List<TocFieldLocation>();
            foreach (var p in body.Elements<Paragraph>())
            {
                var instrText = p.Descendants<FieldCode>()
                    .FirstOrDefault(fc => fc.Text != null && fc.Text.TrimStart().StartsWith("TOC", StringComparison.OrdinalIgnoreCase));
                if (instrText == null)
                    continue;
                list.Add(new TocFieldLocation(p, ParseTocSwitches(instrText.Text)));
            }
            return list;
        }

        private struct TocFieldLocation
        {
            public Paragraph TocPara;
            public TocSpec Spec;

            public TocFieldLocation(Paragraph tocPara, TocSpec spec)
            {
                TocPara = tocPara;
                Spec = spec;
            }
        }

        private static TocSpec ParseTocSwitches(string instr)
        {
            var min = 1;
            var max = 3;
            var m = Regex.Match(instr, @"\\o\s+""\s*(\d+)\s*-\s*(\d+)\s*""");
            if (m.Success)
            {
                min = int.Parse(m.Groups[1].Value);
                max = int.Parse(m.Groups[2].Value);
            }
            var hyperlinks = Regex.IsMatch(instr, @"\\h\b");
            var noPageNum = Regex.IsMatch(instr, @"\\z\b") || Regex.IsMatch(instr, @"\\n\b");
            return new TocSpec(min, max, hyperlinks, noPageNum);
        }

        private static List<Paragraph> GenerateEntries(List<HeadingInfo> headings, TocSpec spec)
        {
            var paras = new List<Paragraph>();
            foreach (var heading in headings)
            {
                if (heading.Level < spec.MinLevel || heading.Level > spec.MaxLevel)
                    continue;
                paras.Add(BuildEntryParagraph(heading, spec));
            }
            return paras;
        }

        private static Paragraph BuildEntryParagraph(HeadingInfo heading, TocSpec spec)
        {
            var styleId = "TOC" + heading.Level;
            var pPr = new ParagraphProperties(new ParagraphStyleId { Val = styleId });
            var p = new Paragraph(pPr);

            var textRun = new Run(new Text(heading.Text) { Space = SpaceProcessingModeValues.Preserve });
            var tabRun = new Run(new TabChar());

            OpenXmlElement entryHost = p;
            if (spec.Hyperlinks)
            {
                var hyperlink = new Hyperlink { Anchor = heading.BookmarkName, History = OnOffValue.FromBoolean(true) };
                p.AppendChild(hyperlink);
                entryHost = hyperlink;
            }
            entryHost.AppendChild(textRun);

            if (!spec.NoPageNum)
            {
                entryHost.AppendChild(tabRun);

                // PAGEREF field: { PAGEREF _TocXXX \h }. Result starts as "0";
                // the begin char is marked dirty so Word fills the real page
                // number on open / field update.
                var begin = new FieldChar { FieldCharType = FieldCharValues.Begin, Dirty = true };
                entryHost.AppendChild(new Run(begin));
                entryHost.AppendChild(new Run(new FieldCode(" PAGEREF " + heading.BookmarkName + " \\h ") { Space = SpaceProcessingModeValues.Preserve }));
                entryHost.AppendChild(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
                entryHost.AppendChild(new Run(new Text("0") { Space = SpaceProcessingModeValues.Preserve }));
                entryHost.AppendChild(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
            }
            return p;
        }

        private static void ReplaceTocFieldContent(Body body, Paragraph tocFieldPara, List<Paragraph> entries)
        {
            var sepRun = tocFieldPara.Descendants<Run>()
                .FirstOrDefault(r =>
                {
                    var fc = r.GetFirstChild<FieldChar>();
                    return fc != null && fc.FieldCharType != null && fc.FieldCharType.Value == FieldCharValues.Separate;
                });
            if (sepRun == null)
                return;

            Paragraph endPara = null;
            int depth = 1;
            bool sawSep = false;
            foreach (var r in body.Descendants<Run>())
            {
                if (!sawSep)
                {
                    if (r == sepRun)
                        sawSep = true;
                    continue;
                }
                var fc = r.GetFirstChild<FieldChar>();
                if (fc != null && fc.FieldCharType != null)
                {
                    if (fc.FieldCharType.Value == FieldCharValues.Begin)
                        depth++;
                    else if (fc.FieldCharType.Value == FieldCharValues.End)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            endPara = r.Ancestors<Paragraph>().FirstOrDefault();
                            break;
                        }
                    }
                }
            }
            if (endPara == null)
                return;

            // 1) Remove everything in tocFieldPara strictly after sepRun's
            //    outermost ancestor inside the paragraph.
            OpenXmlElement sepRoot = sepRun;
            while (sepRoot.Parent != null && sepRoot.Parent != tocFieldPara)
                sepRoot = sepRoot.Parent;

            var afterSep = sepRoot.NextSibling();
            while (afterSep != null)
            {
                var next = afterSep.NextSibling();
                afterSep.Remove();
                afterSep = next;
            }

            // 2) Remove paragraphs from after tocFieldPara up to and including
            //    endPara (a fresh end run is synthesized below).
            if (endPara != tocFieldPara)
            {
                var p = tocFieldPara.NextSibling<Paragraph>();
                while (p != null)
                {
                    var next = p.NextSibling<Paragraph>();
                    bool wasEnd = (p == endPara);
                    p.Remove();
                    if (wasEnd)
                        break;
                    p = next;
                }
            }

            // 3) Insert generated entry paragraphs.
            OpenXmlElement insertAfter = tocFieldPara;
            foreach (var entry in entries)
            {
                insertAfter.InsertAfterSelf(entry);
                insertAfter = entry;
            }

            // 4) Append a synthetic end-fldChar paragraph closing the outer field.
            var endParaNew = new Paragraph(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
            insertAfter.InsertAfterSelf(endParaNew);
        }
    }
}
