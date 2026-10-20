using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace OfficeCopilot.Documents.Markdown
{
    public sealed class WordMarkdownConverter : ICancellableDocumentMarkdownConverter
    {
        private static readonly Regex HeadingStylePattern = new Regex(@"^Heading\s*([1-6])$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public bool CanConvert(string extension)
        {
            return string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase);
        }

        public MarkdownDocument Convert(string path, MarkdownConversionOptions options)
        {
            return Convert(path, options, CancellationToken.None);
        }

        public MarkdownDocument Convert(string path, MarkdownConversionOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A document path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The document does not exist.", path);
            }

            options = options ?? new MarkdownConversionOptions();
            if (options.MaxFileBytes > 0 && new FileInfo(path).Length > options.MaxFileBytes)
            {
                throw new InvalidDataException("The document exceeds the configured file-size limit.");
            }
            OpenXmlPackageSizeGuard.EnsureWithinLimit(path, options.MaxUncompressedPackageBytes, cancellationToken);

            var result = new MarkdownDocument { SourceFormat = "docx" };
            var body = new StringBuilder();
            var sections = new List<MarkdownSection>();
            var sectionTitle = string.Empty;
            var headings = new System.Collections.Generic.List<string>();

            using (var document = WordprocessingDocument.Open(path, false))
            {
                var content = document.MainDocumentPart == null || document.MainDocumentPart.Document == null
                    ? null
                    : document.MainDocumentPart.Document.Body;
                if (content == null)
                {
                    throw new InvalidDataException("The document does not contain a readable body.");
                }

                var headingLevels = BuildHeadingStyleLevels(document.MainDocumentPart.StyleDefinitionsPart);
                foreach (var element in EnumerateBlockElements(content))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var paragraph = element as Word.Paragraph;
                    if (paragraph != null)
                    {
                        var text = ReadParagraphText(paragraph, document.MainDocumentPart, cancellationToken);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        var headingLevel = GetHeadingLevel(paragraph, headingLevels);
                        if (headingLevel > 0 && headingLevel <= 2)
                        {
                            if (body.Length > 0)
                            {
                                AddSection(sections, sectionTitle, path, body);
                            }
                            sectionTitle = text.Trim();
                        }
                        if (headingLevel > 0)
                        {
                            body.Append(new string('#', headingLevel));
                            body.Append(' ');
                            body.AppendLine(EscapeMarkdown(text));
                            headings.Add(text.Trim());
                        }
                        else
                        {
                            body.Append(GetListPrefix(paragraph, document.MainDocumentPart.NumberingDefinitionsPart));
                            body.AppendLine(EscapeMarkdown(text));
                        }
                        body.AppendLine();
                        continue;
                    }

                    var table = element as Word.Table;
                    if (table != null)
                    {
                        AppendTable(body, table, document.MainDocumentPart, cancellationToken);
                        body.AppendLine();
                    }
                }

                AppendNoteDefinitions(body, document.MainDocumentPart.FootnotesPart == null
                    || document.MainDocumentPart.FootnotesPart.Footnotes == null
                    ? null
                    : document.MainDocumentPart.FootnotesPart.Footnotes.ChildElements, "Footnotes", cancellationToken);
                AppendNoteDefinitions(body, document.MainDocumentPart.EndnotesPart == null
                    || document.MainDocumentPart.EndnotesPart.Endnotes == null
                    ? null
                    : document.MainDocumentPart.EndnotesPart.Endnotes.ChildElements, "Endnotes", cancellationToken);
                AppendCommentDefinitions(body, document.MainDocumentPart.WordprocessingCommentsPart == null
                    || document.MainDocumentPart.WordprocessingCommentsPart.Comments == null
                    ? null
                    : document.MainDocumentPart.WordprocessingCommentsPart.Comments.ChildElements, cancellationToken);
            }

            if (body.Length > 0 || sections.Count == 0)
            {
                AddSection(sections, sectionTitle, path, body);
            }

            result.Outline = headings.Count == 0 ? string.Empty : "Headings: " + string.Join("; ", headings.Take(100));
            var remainingCharacters = Math.Max(0, options.MaxTotalChars);
            for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var section = sections[sectionIndex];
                if (section.Markdown.Length > remainingCharacters)
                {
                    section.Markdown = section.Markdown.Substring(0, remainingCharacters).TrimEnd()
                        + "\n\n[Content truncated at the configured character limit.]";
                    result.Truncated = true;
                    if (sectionIndex + 1 < sections.Count)
                    {
                        sections.RemoveRange(sectionIndex + 1, sections.Count - sectionIndex - 1);
                    }
                    break;
                }

                remainingCharacters -= section.Markdown.Length;
            }

            foreach (var section in sections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Sections.Add(section);
            }
            return result;
        }

        private static void AddSection(IList<MarkdownSection> sections, string title, string path, StringBuilder body)
        {
            sections.Add(new MarkdownSection
            {
                Index = sections.Count + 1,
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileName(path) : title,
                Markdown = body.ToString().Trim()
            });
            body.Clear();
        }

        private static IEnumerable<OpenXmlElement> EnumerateBlockElements(OpenXmlElement container)
        {
            foreach (var child in container.ChildElements)
            {
                // Content controls and custom XML wrap ordinary body blocks; flatten them so their text is not dropped.
                if (child is Word.SdtBlock || child is Word.CustomXmlBlock)
                {
                    var inner = child is Word.SdtBlock
                        ? (OpenXmlElement)((Word.SdtBlock)child).SdtContentBlock
                        : child;
                    if (inner == null)
                    {
                        continue;
                    }

                    foreach (var nested in EnumerateBlockElements(inner))
                    {
                        yield return nested;
                    }
                    continue;
                }

                yield return child;
            }
        }

        private static Dictionary<string, int> BuildHeadingStyleLevels(StyleDefinitionsPart stylesPart)
        {
            var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var styles = stylesPart == null || stylesPart.Styles == null
                ? Enumerable.Empty<Word.Style>()
                : stylesPart.Styles.Elements<Word.Style>();
            foreach (var style in styles)
            {
                var styleId = style.StyleId == null ? null : style.StyleId.Value;
                if (string.IsNullOrWhiteSpace(styleId))
                {
                    continue;
                }

                var styleName = style.StyleName == null || style.StyleName.Val == null ? string.Empty : style.StyleName.Val.Value;
                var level = GetHeadingLevelFromName(styleName);
                if (level == 0 && string.Equals(styleName, "Title", StringComparison.OrdinalIgnoreCase))
                {
                    level = 1;
                }

                if (level == 0)
                {
                    var outline = style.StyleParagraphProperties == null ? null : style.StyleParagraphProperties.OutlineLevel;
                    level = GetHeadingLevelFromOutline(outline);
                }

                if (level > 0)
                {
                    levels[styleId] = level;
                }
            }

            return levels;
        }

        private static int GetHeadingLevel(Word.Paragraph paragraph, IDictionary<string, int> headingStyleLevels)
        {
            var properties = paragraph.ParagraphProperties;
            var outlineLevel = GetHeadingLevelFromOutline(properties == null ? null : properties.OutlineLevel);
            if (outlineLevel > 0)
            {
                return outlineLevel;
            }

            var styleId = properties == null || properties.ParagraphStyleId == null || properties.ParagraphStyleId.Val == null
                ? string.Empty
                : properties.ParagraphStyleId.Val.Value ?? string.Empty;
            int level;
            if (headingStyleLevels != null && headingStyleLevels.TryGetValue(styleId, out level))
            {
                return level;
            }

            return GetHeadingLevelFromName(styleId);
        }

        private static int GetHeadingLevelFromName(string styleName)
        {
            var match = HeadingStylePattern.Match(styleName ?? string.Empty);
            int level;
            return match.Success && int.TryParse(match.Groups[1].Value, out level) ? level : 0;
        }

        private static int GetHeadingLevelFromOutline(Word.OutlineLevel outlineLevel)
        {
            // w:outlineLvl is zero-based; 9 means body text.
            if (outlineLevel == null || outlineLevel.Val == null)
            {
                return 0;
            }

            var value = outlineLevel.Val.Value;
            return value >= 0 && value <= 5 ? value + 1 : 0;
        }

        private static string GetListPrefix(Word.Paragraph paragraph, NumberingDefinitionsPart numberingPart)
        {
            var numberingProperties = paragraph == null || paragraph.ParagraphProperties == null
                ? null
                : paragraph.ParagraphProperties.NumberingProperties;
            var numberingId = numberingProperties == null || numberingProperties.NumberingId == null
                || numberingProperties.NumberingId.Val == null
                ? 0
                : numberingProperties.NumberingId.Val.Value;
            if (numberingId <= 0)
            {
                return numberingProperties == null ? string.Empty : "- ";
            }

            var levelIndex = numberingProperties.NumberingLevelReference == null
                || numberingProperties.NumberingLevelReference.Val == null
                ? 0
                : Math.Max(0, numberingProperties.NumberingLevelReference.Val.Value);
            var numberInstance = numberingPart == null || numberingPart.Numbering == null
                ? null
                : numberingPart.Numbering.Elements<Word.NumberingInstance>()
                    .FirstOrDefault(instance => instance.NumberID != null && instance.NumberID.Value == numberingId);
            var abstractNumberId = numberInstance == null ? null : numberInstance.GetFirstChild<Word.AbstractNumId>();
            var abstractNumber = abstractNumberId == null || abstractNumberId.Val == null || numberingPart == null
                || numberingPart.Numbering == null
                ? null
                : numberingPart.Numbering.Elements<Word.AbstractNum>()
                    .FirstOrDefault(item => item.AbstractNumberId != null
                        && item.AbstractNumberId.Value == abstractNumberId.Val.Value);
            var level = abstractNumber == null
                ? null
                : abstractNumber.Elements<Word.Level>().FirstOrDefault(item => item.LevelIndex != null
                    && item.LevelIndex.Value == levelIndex);
            var format = level == null || level.NumberingFormat == null ? null : level.NumberingFormat.Val;
            var marker = format == Word.NumberFormatValues.Bullet || format == null ? "- " : "1. ";
            return new string(' ', Math.Min(levelIndex, 16) * 2) + marker;
        }

        private static string ReadParagraphText(
            Word.Paragraph paragraph,
            MainDocumentPart mainPart = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var builder = new StringBuilder();
            var hyperlinkTexts = new HashSet<Word.Text>(paragraph.Descendants<Word.Hyperlink>()
                .SelectMany(hyperlink => hyperlink.Descendants<Word.Text>()));
            foreach (var element in EnumerateRenderedDescendants(paragraph))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var drawing = element as Word.Drawing;
                if (drawing != null)
                {
                    var graphicData = drawing.Descendants<Drawing.GraphicData>().FirstOrDefault();
                    var uri = graphicData == null || graphicData.Uri == null ? string.Empty : graphicData.Uri.Value;
                    var drawingType = uri.EndsWith("/chart", StringComparison.OrdinalIgnoreCase) ? "Chart"
                        : uri.EndsWith("/diagram", StringComparison.OrdinalIgnoreCase) ? "SmartArt"
                        : "Image";
                    var drawingProperties = drawing.Descendants<Wp.DocProperties>().FirstOrDefault();
                    var description = GetDrawingDescription(drawingProperties);
                    builder.Append("![");
                    builder.Append(drawingType);
                    builder.Append(": ");
                    builder.Append(EscapeMarkdown(description));
                    builder.Append("]()");
                    continue;
                }

                var hyperlink = element as Word.Hyperlink;
                if (hyperlink != null)
                {
                    var hyperlinkText = string.Concat(hyperlink.Descendants<Word.Text>().Select(item => item.Text));
                    var target = GetHyperlinkTarget(hyperlink, mainPart);
                    if (!string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(hyperlinkText))
                    {
                        builder.Append('[');
                        builder.Append(EscapeMarkdownLinkText(hyperlinkText));
                        builder.Append("](");
                        builder.Append(EscapeMarkdownLinkTarget(target));
                        builder.Append(')');
                    }
                    else
                    {
                        builder.Append(hyperlinkText);
                    }
                    continue;
                }

                var text = element as Word.Text;
                if (text != null)
                {
                    if (!hyperlinkTexts.Contains(text))
                    {
                        builder.Append(text.Text);
                    }
                }
                else if (element is Word.TabChar)
                {
                    builder.Append('\t');
                }
                else if (element is Word.Break)
                {
                    builder.Append('\n');
                }
                else if (element is Word.FootnoteReference)
                {
                    var reference = (Word.FootnoteReference)element;
                    if (reference.Id != null)
                    {
                        builder.Append("[^");
                        builder.Append(reference.Id.Value.ToString(CultureInfo.InvariantCulture));
                        builder.Append(']');
                    }
                }
                else if (element is Word.EndnoteReference)
                {
                    var reference = (Word.EndnoteReference)element;
                    if (reference.Id != null)
                    {
                        builder.Append("[^");
                        builder.Append(reference.Id.Value.ToString(CultureInfo.InvariantCulture));
                        builder.Append(']');
                    }
                }
                else if (element is Word.CommentRangeEnd)
                {
                    var commentEnd = (Word.CommentRangeEnd)element;
                    if (commentEnd.Id != null)
                    {
                        builder.Append("[Comment ");
                        builder.Append(commentEnd.Id.Value);
                        builder.Append(']');
                    }
                }
            }
            return builder.ToString().Trim();
        }

        private const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        /// <summary>
        /// Pre-order descendants, skipping mc:Fallback when an mc:Choice exists so textboxes stored as both
        /// DrawingML and VML are emitted once.
        /// </summary>
        private static IEnumerable<OpenXmlElement> EnumerateRenderedDescendants(OpenXmlElement root)
        {
            var stack = new Stack<OpenXmlElement>();
            for (var child = root.LastChild; child != null; child = child.PreviousSibling())
            {
                stack.Push(child);
            }

            while (stack.Count > 0)
            {
                var element = stack.Pop();
                if (IsRedundantFallback(element))
                {
                    continue;
                }

                yield return element;
                for (var child = element.LastChild; child != null; child = child.PreviousSibling())
                {
                    stack.Push(child);
                }
            }
        }

        private static bool IsRedundantFallback(OpenXmlElement element)
        {
            if (element == null
                || element.LocalName != "Fallback"
                || element.NamespaceUri != MarkupCompatibilityNamespace
                || element.Parent == null)
            {
                return false;
            }

            return element.Parent.ChildElements.Any(sibling =>
                sibling.LocalName == "Choice" && sibling.NamespaceUri == MarkupCompatibilityNamespace);
        }

        private static string GetDrawingDescription(Wp.DocProperties properties)
        {
            if (properties == null)
            {
                return "embedded content";
            }

            if (!string.IsNullOrWhiteSpace(properties.Description == null ? null : properties.Description.Value))
            {
                return properties.Description.Value;
            }
            if (!string.IsNullOrWhiteSpace(properties.Title == null ? null : properties.Title.Value))
            {
                return properties.Title.Value;
            }
            return string.IsNullOrWhiteSpace(properties.Name == null ? null : properties.Name.Value)
                ? "embedded content"
                : properties.Name.Value;
        }

        private static string GetHyperlinkTarget(Word.Hyperlink hyperlink, MainDocumentPart mainPart)
        {
            if (hyperlink.Anchor != null && !string.IsNullOrWhiteSpace(hyperlink.Anchor.Value))
            {
                return "#" + hyperlink.Anchor.Value;
            }

            if (mainPart == null || hyperlink.Id == null)
            {
                return string.Empty;
            }

            var relationship = mainPart.HyperlinkRelationships.FirstOrDefault(item => item.Id == hyperlink.Id.Value);
            return relationship == null || relationship.Uri == null ? string.Empty : relationship.Uri.OriginalString;
        }

        private static string EscapeMarkdownLinkText(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("[", "\\[")
                .Replace("]", "\\]");
        }

        private static string EscapeMarkdownLinkTarget(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "%5C")
                .Replace(" ", "%20")
                .Replace("(", "%28")
                .Replace(")", "%29")
                .Replace("<", "%3C")
                .Replace(">", "%3E");
        }

        private static void AppendNoteDefinitions(
            StringBuilder builder,
            IEnumerable<OpenXmlElement> notes,
            string heading,
            CancellationToken cancellationToken)
        {
            if (notes == null)
            {
                return;
            }

            const string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var definitions = new List<string>();
            foreach (var note in notes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int id;
                var idAttribute = note.GetAttribute("id", wordNamespace);
                if (!int.TryParse(idAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id <= 0)
                {
                    continue;
                }

                var paragraphs = new List<string>();
                foreach (var paragraph in note.Elements<Word.Paragraph>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var paragraphText = ReadParagraphText(paragraph, null, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        paragraphs.Add(paragraphText);
                    }
                }
                var text = string.Join(" ", paragraphs);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    definitions.Add("[^" + id.ToString(CultureInfo.InvariantCulture) + "]: " + EscapeMarkdown(text));
                }
            }

            if (definitions.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.Append("### ");
            builder.AppendLine(heading);
            foreach (var definition in definitions)
            {
                builder.AppendLine(definition);
            }
        }

        private static void AppendCommentDefinitions(
            StringBuilder builder,
            IEnumerable<OpenXmlElement> comments,
            CancellationToken cancellationToken)
        {
            if (comments == null)
            {
                return;
            }

            const string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var definitions = new List<string>();
            foreach (var comment in comments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = comment.GetAttribute("id", wordNamespace).Value;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var author = comment.GetAttribute("author", wordNamespace).Value;
                var paragraphs = new List<string>();
                foreach (var paragraph in comment.Elements<Word.Paragraph>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var paragraphText = ReadParagraphText(paragraph, null, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        paragraphs.Add(paragraphText);
                    }
                }
                var text = string.Join(" ", paragraphs);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    definitions.Add("- [Comment " + id + "]"
                        + (string.IsNullOrWhiteSpace(author) ? ": " : " (" + EscapeMarkdown(author) + "): ")
                        + EscapeMarkdown(text));
                }
            }

            if (definitions.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("### Comments");
            foreach (var definition in definitions)
            {
                builder.AppendLine(definition);
            }
        }

        private static void AppendTable(
            StringBuilder builder,
            Word.Table table,
            MainDocumentPart mainPart,
            CancellationToken cancellationToken)
        {
            var rows = EnumerateTableRows(table).ToList();
            if (rows.Count == 0)
            {
                return;
            }

            var tableGrid = FindChild(table, "tblGrid");
            var columnCount = tableGrid == null ? 0 : tableGrid.ChildElements.Count(element => element.LocalName == "gridCol");
            columnCount = Math.Max(columnCount, rows.Max(GetRowGridWidth));
            if (columnCount == 0)
            {
                return;
            }

            var hasMergedCells = rows.SelectMany(EnumerateRowCells).Any(IsMergedCell);
            if (hasMergedCells)
            {
                builder.AppendLine("> Merged cells retain their grid positions; text is emitted only in the leading cell.");
                builder.AppendLine();
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = rows[rowIndex];
                var cells = Enumerable.Repeat(string.Empty, columnCount).ToArray();
                var gridColumn = GetRowGridOffset(row, "gridBefore");
                foreach (var cell in EnumerateRowCells(row))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var span = GetCellGridSpan(cell);
                    if (gridColumn >= 0 && gridColumn < columnCount && !IsVerticalMergeContinuation(cell))
                    {
                        cells[gridColumn] = ReadTableCellText(cell, mainPart, cancellationToken);
                    }
                    gridColumn += span;
                }

                MarkdownWriter.AppendRow(builder, cells, cancellationToken);

                if (rowIndex == 0)
                {
                    MarkdownWriter.AppendSeparator(builder, columnCount, null, cancellationToken);
                }
            }
        }

        private static string ReadTableCellText(
            Word.TableCell cell,
            MainDocumentPart mainPart,
            CancellationToken cancellationToken)
        {
            var paragraphs = new List<string>();
            foreach (var paragraph in EnumerateRenderedDescendants(cell).OfType<Word.Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (HasParagraphAncestorWithin(paragraph, cell))
                {
                    // Textbox paragraphs are already read as part of their anchoring paragraph.
                    continue;
                }

                var text = ReadParagraphText(paragraph, mainPart, cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    paragraphs.Add(text);
                }
            }
            return string.Join("<br>", paragraphs);
        }

        private static bool HasParagraphAncestorWithin(OpenXmlElement element, OpenXmlElement boundary)
        {
            for (var parent = element.Parent; parent != null && !ReferenceEquals(parent, boundary); parent = parent.Parent)
            {
                if (parent is Word.Paragraph)
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<Word.TableRow> EnumerateTableRows(Word.Table table)
        {
            return FlattenWrapped<Word.TableRow>(table);
        }

        private static IEnumerable<Word.TableCell> EnumerateRowCells(Word.TableRow row)
        {
            return FlattenWrapped<Word.TableCell>(row);
        }

        /// <summary>
        /// Returns direct children of type T, looking through row/cell-level content controls and custom XML wrappers.
        /// </summary>
        private static IEnumerable<T> FlattenWrapped<T>(OpenXmlElement container) where T : OpenXmlElement
        {
            foreach (var child in container.ChildElements)
            {
                var typed = child as T;
                if (typed != null)
                {
                    yield return typed;
                    continue;
                }

                OpenXmlElement inner = null;
                if (child is Word.SdtRow)
                {
                    inner = ((Word.SdtRow)child).SdtContentRow;
                }
                else if (child is Word.SdtCell)
                {
                    inner = ((Word.SdtCell)child).SdtContentCell;
                }
                else if (child is Word.CustomXmlRow || child is Word.CustomXmlCell)
                {
                    inner = child;
                }

                if (inner == null)
                {
                    continue;
                }

                foreach (var nested in FlattenWrapped<T>(inner))
                {
                    yield return nested;
                }
            }
        }

        private static int GetRowGridWidth(Word.TableRow row)
        {
            return GetRowGridOffset(row, "gridBefore")
                + EnumerateRowCells(row).Sum(GetCellGridSpan)
                + GetRowGridOffset(row, "gridAfter");
        }

        private static int GetRowGridOffset(Word.TableRow row, string propertyName)
        {
            var rowProperties = FindChild(row, "trPr");
            var offset = FindChild(rowProperties, propertyName);
            return GetIntegerAttribute(offset, "val");
        }

        private static int GetCellGridSpan(Word.TableCell cell)
        {
            var cellProperties = FindChild(cell, "tcPr");
            var gridSpan = FindChild(cellProperties, "gridSpan");
            return Math.Max(1, GetIntegerAttribute(gridSpan, "val"));
        }

        private static bool IsMergedCell(Word.TableCell cell)
        {
            var cellProperties = FindChild(cell, "tcPr");
            return GetCellGridSpan(cell) > 1 || FindChild(cellProperties, "vMerge") != null;
        }

        private static bool IsVerticalMergeContinuation(Word.TableCell cell)
        {
            var cellProperties = FindChild(cell, "tcPr");
            var verticalMerge = FindChild(cellProperties, "vMerge");
            return verticalMerge != null
                && !string.Equals(GetAttributeValue(verticalMerge, "val"), "restart", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetIntegerAttribute(OpenXmlElement element, string attributeName)
        {
            if (element == null)
            {
                return 0;
            }

            int value;
            return int.TryParse(GetAttributeValue(element, attributeName), NumberStyles.None, CultureInfo.InvariantCulture, out value)
                ? value
                : 0;
        }

        private static string GetAttributeValue(OpenXmlElement element, string attributeName)
        {
            const string wordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var attribute = element.GetAttributes().FirstOrDefault(item =>
                item.LocalName == attributeName && item.NamespaceUri == wordNamespace);
            return attribute.Value ?? string.Empty;
        }

        private static OpenXmlElement FindChild(OpenXmlElement parent, string localName)
        {
            return parent == null ? null : parent.ChildElements.FirstOrDefault(element => element.LocalName == localName);
        }

        private static string EscapeMarkdown(string value)
        {
            return MarkdownWriter.Escape(value);
        }
    }
}
