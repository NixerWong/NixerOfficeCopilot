using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OfficeCopilot.OpenXml.Core;

namespace OfficeCopilot.OpenXml.Word
{
    /// <summary>
    /// File-level engine for .docx documents (Open XML SDK — no Word host).
    /// Register once at host startup so any Office host can create/edit .docx.
    /// </summary>
    public sealed class WordDocumentEngine : DocumentEngineBase
    {
        /// <summary>Typed access to the underlying package.</summary>
        public WordprocessingDocument Document => (WordprocessingDocument)Package;

        public override string Format => "docx";

        public WordDocumentEngine(string filePath, bool editable)
            : base(filePath, editable, WordprocessingDocument.Open(filePath, editable))
        {
        }

        /// <summary>Register the .docx engine. Call once per host process.</summary>
        public static void Register()
        {
            DocumentEngineRegistry.Register(
                ".docx",
                (path, editable) => new WordDocumentEngine(path, editable),
                WordDocumentEngine.CreateBlank,
                WordDocumentEngine.MergeTemplate);
        }

        /// <summary>Create a blank .docx with a single empty paragraph.</summary>
        public static void CreateBlank(string path)
        {
            using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text(string.Empty)))));
                mainPart.Document.Save();
            }
        }

        private static readonly Regex PlaceholderPattern =
            new Regex(@"\{\{\s*(\w[\w.\-\[\] ]*?)\s*\}\}", RegexOptions.Compiled);

        /// <summary>
        /// Merge a .docx template with JSON data, replacing {{key}} placeholders
        /// in the body, headers, footers, footnotes, endnotes and comments.
        /// Ported from officecli Core/TemplateMerger.MergeDocx.
        /// </summary>
        public static MergeResult MergeTemplate(string templatePath, string outputPath, MergeData data)
        {
            if (!File.Exists(templatePath))
                throw new FileNotFoundException(string.Format("Template not found: {0}", templatePath), templatePath);

            // The registry already refuses to overwrite an existing output; the
            // overwrite flag here is defensive only.
            File.Copy(templatePath, outputPath, true);

            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalReplacements = 0;

            using (var doc = WordprocessingDocument.Open(outputPath, true))
            {
                ReplacePlaceholdersInDocx(doc, data, usedKeys, count => totalReplacements += count);
            }

            var unresolved = ScanUnresolvedDocx(outputPath);
            return new MergeResult(totalReplacements, unresolved, usedKeys);
        }

        /// <summary>
        /// Regenerate every TOC field in the open document from its headings.
        /// Emits TOC1/TOC2/... entry paragraphs with hyperlinks and PAGEREF
        /// fields (page-number placeholders are marked dirty so Word fills them
        /// on open). Call Save() afterwards to persist.
        /// </summary>
        public void RegenerateAllTocs()
        {
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be modified.");
            WordTocBuilder.RegenerateAllTocs(Document);
        }

        /// <summary>Add (or replace) a classic VML text watermark. Call Save() to persist.</summary>
        public void AddWatermark(WatermarkOptions options)
        {
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be modified.");
            WordWatermark.AddWatermark(Document, options);
        }

        /// <summary>Remove every watermark header part. Call Save() to persist.</summary>
        public void RemoveWatermark()
        {
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be modified.");
            WordWatermark.RemoveWatermark(Document);
        }

        /// <summary>Return the current text watermark, or null when none exists.</summary>
        public string FindWatermark()
        {
            return WordWatermark.FindWatermark(Document);
        }

        /// <summary>
        /// Create a numbering list definition (abstractNum + num) and return the
        /// numId to apply to paragraphs. Call Save() to persist.
        /// </summary>
        public int AddNumberingList(NumberingListOptions options)
        {
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be modified.");
            return WordNumbering.AddNumberingList(Document, options);
        }

        /// <summary>
        /// Apply a numbering id + level to a body paragraph by 0-based index.
        /// Call Save() to persist.
        /// </summary>
        public void ApplyNumberingToBodyParagraph(int paragraphIndex, int numId, int level = 0)
        {
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be modified.");
            WordNumbering.ApplyNumberingToBodyParagraph(Document, paragraphIndex, numId, level);
        }

        /// <summary>
        /// Insert a complex Word field (PAGE, DATE, MERGEFIELD, IF, ...) at the
        /// end of the body paragraph at <paramref name="paragraphIndex"/>.
        /// Call Save() to persist.
        /// </summary>
        public void AddFieldToParagraph(int paragraphIndex, FieldOptions options)
        {
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be modified.");
            WordField.AddFieldToParagraph(Document, paragraphIndex, options);
        }

        /// <summary>Append a new body paragraph containing a Word field. Call Save() to persist.</summary>
        public void AppendFieldParagraph(FieldOptions options)
        {
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be modified.");
            WordField.AppendFieldParagraph(Document, options);
        }

        private static void ReplacePlaceholdersInDocx(WordprocessingDocument doc, MergeData data, HashSet<string> usedKeys, Action<int> bumpReplacements)
        {
            var mainPart = doc.MainDocumentPart;
            if (mainPart == null)
                return;

            var parts = new List<KeyValuePair<OpenXmlPart, OpenXmlPartRootElement>>();
            if (mainPart.Document != null)
                parts.Add(new KeyValuePair<OpenXmlPart, OpenXmlPartRootElement>(mainPart, mainPart.Document));
            foreach (var headerPart in mainPart.HeaderParts)
                if (headerPart.Header != null)
                    parts.Add(new KeyValuePair<OpenXmlPart, OpenXmlPartRootElement>(headerPart, headerPart.Header));
            foreach (var footerPart in mainPart.FooterParts)
                if (footerPart.Footer != null)
                    parts.Add(new KeyValuePair<OpenXmlPart, OpenXmlPartRootElement>(footerPart, footerPart.Footer));
            if (mainPart.FootnotesPart != null && mainPart.FootnotesPart.Footnotes != null)
                parts.Add(new KeyValuePair<OpenXmlPart, OpenXmlPartRootElement>(mainPart.FootnotesPart, mainPart.FootnotesPart.Footnotes));
            if (mainPart.EndnotesPart != null && mainPart.EndnotesPart.Endnotes != null)
                parts.Add(new KeyValuePair<OpenXmlPart, OpenXmlPartRootElement>(mainPart.EndnotesPart, mainPart.EndnotesPart.Endnotes));
            if (mainPart.WordprocessingCommentsPart != null && mainPart.WordprocessingCommentsPart.Comments != null)
                parts.Add(new KeyValuePair<OpenXmlPart, OpenXmlPartRootElement>(mainPart.WordprocessingCommentsPart, mainPart.WordprocessingCommentsPart.Comments));

            foreach (var pair in parts)
            {
                var root = pair.Value;
                bool changed = false;
                foreach (var text in root.Descendants<Text>())
                {
                    var original = text.Text ?? string.Empty;
                    if (original.Length == 0 || !original.Contains("{{"))
                        continue;

                    bool matched;
                    var replaced = SinglePassReplace(original, data, out matched, usedKeys, bumpReplacements);
                    if (matched && replaced != original)
                    {
                        text.Text = replaced;
                        text.Space = SpaceProcessingModeValues.Preserve;
                        changed = true;
                    }
                }
                if (changed)
                    root.Save();
            }
        }

        /// <summary>
        /// Replace every {{key}} in one left-to-right scan. Substituted values are
        /// never re-scanned, so a value containing {{other}} stays literal. Tracks
        /// used keys and the substitution count.
        /// </summary>
        private static string SinglePassReplace(string input, MergeData data, out bool matched, HashSet<string> usedKeys, Action<int> bumpReplacements)
        {
            matched = false;
            if (string.IsNullOrEmpty(input) || !input.Contains("{{"))
                return input;

            int localCount = 0;
            var result = PlaceholderPattern.Replace(input, m =>
            {
                var key = m.Groups[1].Value;
                string replacement;
                if (data.TryGet(key, out replacement))
                {
                    usedKeys.Add(key);
                    localCount++;
                    return replacement;
                }
                return m.Value;
            });

            if (localCount > 0)
                bumpReplacements(localCount);
            matched = localCount > 0;
            return result;
        }

        private static List<string> ScanUnresolvedDocx(string filePath)
        {
            var unresolved = new HashSet<string>(StringComparer.Ordinal);
            using (var doc = WordprocessingDocument.Open(filePath, false))
            {
                var mainPart = doc.MainDocumentPart;
                if (mainPart != null)
                {
                    ScanRootForPlaceholders(mainPart.Document, unresolved);
                    foreach (var headerPart in mainPart.HeaderParts)
                        ScanRootForPlaceholders(headerPart.Header, unresolved);
                    foreach (var footerPart in mainPart.FooterParts)
                        ScanRootForPlaceholders(footerPart.Footer, unresolved);
                    if (mainPart.FootnotesPart != null)
                        ScanRootForPlaceholders(mainPart.FootnotesPart.Footnotes, unresolved);
                    if (mainPart.EndnotesPart != null)
                        ScanRootForPlaceholders(mainPart.EndnotesPart.Endnotes, unresolved);
                    if (mainPart.WordprocessingCommentsPart != null)
                        ScanRootForPlaceholders(mainPart.WordprocessingCommentsPart.Comments, unresolved);
                }
            }

            var list = unresolved.ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static void ScanRootForPlaceholders(OpenXmlPartRootElement root, HashSet<string> unresolved)
        {
            if (root == null)
                return;

            foreach (var paragraph in root.Descendants<Paragraph>())
            {
                var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text ?? string.Empty));
                foreach (Match match in PlaceholderPattern.Matches(text))
                    unresolved.Add(match.Groups[1].Value);
            }
        }
    }
}
