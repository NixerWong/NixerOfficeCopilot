using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Drawing = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Dgm = DocumentFormat.OpenXml.Drawing.Diagrams;
using P = DocumentFormat.OpenXml.Presentation;

namespace OfficeCopilot.Documents.Markdown
{
    public sealed class PresentationMarkdownConverter : ICancellableDocumentMarkdownConverter
    {
        public bool CanConvert(string extension)
        {
            return string.Equals(extension, ".pptx", StringComparison.OrdinalIgnoreCase);
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
                throw new ArgumentException("A presentation path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The presentation does not exist.", path);
            }

            options = options ?? new MarkdownConversionOptions();
            if (options.MaxFileBytes > 0 && new FileInfo(path).Length > options.MaxFileBytes)
            {
                throw new InvalidDataException("The presentation exceeds the configured file-size limit.");
            }
            OpenXmlPackageSizeGuard.EnsureWithinLimit(path, options.MaxUncompressedPackageBytes, cancellationToken);

            var result = new MarkdownDocument { SourceFormat = "pptx" };
            var titles = new List<string>();
            var documentLength = 0;

            using (var document = PresentationDocument.Open(path, false))
            {
                var presentationPart = document.PresentationPart;
                var slideIdList = presentationPart == null || presentationPart.Presentation == null
                    ? null
                    : presentationPart.Presentation.GetFirstChild<SlideIdList>();
                if (slideIdList == null)
                {
                    throw new InvalidDataException("The presentation does not contain a readable slide list.");
                }

                var slideIndex = 0;
                foreach (var slideId in slideIdList.Elements<SlideId>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    slideIndex++;
                    var slidePart = presentationPart.GetPartById(slideId.RelationshipId.Value) as SlidePart;
                    if (slidePart == null || slidePart.Slide == null || slidePart.Slide.CommonSlideData == null)
                    {
                        result.Warnings.Add("Slide " + slideIndex.ToString(CultureInfo.InvariantCulture) + " could not be opened and was skipped.");
                        continue;
                    }

                    var markdown = ConvertSlide(slidePart, slideIndex, options.MaxSheetRows, cancellationToken, out var title, out var needsVisual);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        titles.Add("Slide " + slideIndex.ToString(CultureInfo.InvariantCulture) + ": " + title);
                    }

                    var remainingChars = options.MaxTotalChars - documentLength;
                    if (remainingChars <= 0)
                    {
                        result.Truncated = true;
                        break;
                    }

                    if (markdown.Length > remainingChars)
                    {
                        markdown = markdown.Substring(0, remainingChars).TrimEnd()
                            + "\n\n[Content truncated at the configured character limit.]";
                        result.Truncated = true;
                    }

                    result.Sections.Add(new MarkdownSection
                    {
                        Index = slideIndex,
                        Title = title,
                        Markdown = markdown,
                        NeedsVisual = needsVisual,
                        VisualReason = needsVisual ? "Slide contains images or has little extractable text." : string.Empty
                    });
                    documentLength += markdown.Length;
                    if (result.Truncated)
                    {
                        break;
                    }
                }
            }

            result.Outline = titles.Count == 0 ? string.Empty : "Slides: " + string.Join("; ", titles);
            return result;
        }

        private static string ConvertSlide(
            SlidePart slidePart,
            int slideIndex,
            int maxChartPoints,
            CancellationToken cancellationToken,
            out string title,
            out bool needsVisual)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shapeTree = slidePart.Slide.CommonSlideData.ShapeTree;
            var shapes = shapeTree == null ? new List<P.Shape>() : shapeTree.Descendants<P.Shape>().ToList();
            var shapeTexts = shapes
                .Select((shape, index) => new
                {
                    Shape = shape,
                    Text = ReadShapeText(shape),
                    Index = index,
                    X = GetShapeCoordinate(shape, true),
                    Y = GetShapeCoordinate(shape, false)
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Text))
                .OrderBy(entry => entry.Y)
                .ThenBy(entry => entry.X)
                .ThenBy(entry => entry.Index)
                .ToList();

            var titleEntry = shapeTexts.FirstOrDefault(entry => IsTitlePlaceholder(entry.Shape));
            if (titleEntry == null && shapeTexts.Count > 0)
            {
                titleEntry = shapeTexts[0];
            }

            title = titleEntry == null ? string.Empty : titleEntry.Text.Trim();
            var builder = new StringBuilder();
            builder.Append("## Slide ");
            builder.Append(slideIndex.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(title))
            {
                builder.Append(": ");
                builder.AppendLine(EscapeMarkdown(title));
            }
            else
            {
                builder.AppendLine();
            }
            builder.AppendLine();

            var contentBlocks = new List<SlideContentBlock>();
            var elementOrder = new Dictionary<OpenXmlElement, int>();
            if (shapeTree != null)
            {
                var order = 0;
                foreach (var element in shapeTree.Descendants().Where(element =>
                    element is P.Shape || element is P.Picture || element is P.GraphicFrame))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    elementOrder[element] = order++;
                }
            }

            foreach (var entry in shapeTexts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (titleEntry != null && ReferenceEquals(entry.Shape, titleEntry.Shape))
                {
                    continue;
                }

                var markdown = new StringBuilder();
                foreach (var paragraph in entry.Text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    markdown.Append("- ");
                    markdown.AppendLine(EscapeMarkdown(paragraph.Trim()));
                }

                contentBlocks.Add(new SlideContentBlock
                {
                    X = entry.X,
                    Y = entry.Y,
                    Order = elementOrder[entry.Shape],
                    Markdown = markdown.ToString()
                });
            }

            if (shapeTree != null)
            {
                foreach (var picture in shapeTree.Descendants<P.Picture>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    contentBlocks.Add(new SlideContentBlock
                    {
                        X = GetPictureCoordinate(picture, true),
                        Y = GetPictureCoordinate(picture, false),
                        Order = elementOrder[picture],
                        Markdown = "![Image: " + EscapeMarkdown(GetPictureDescription(picture)) + "]()" + Environment.NewLine
                    });
                }
            }

            var renderedCharts = new HashSet<ChartPart>();
            var renderedDiagrams = new HashSet<DiagramDataPart>();
            if (shapeTree != null)
            {
                foreach (var graphicFrame in shapeTree.Descendants<P.GraphicFrame>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var frameOrder = elementOrder[graphicFrame];
                    var x = GetFrameCoordinate(graphicFrame, true);
                    var y = GetFrameCoordinate(graphicFrame, false);
                    var subOrder = 0;
                    var table = graphicFrame.Descendants<Drawing.Table>().FirstOrDefault();
                    if (table != null)
                    {
                        var tableMarkdown = new StringBuilder();
                        AppendTable(tableMarkdown, table, cancellationToken);
                        contentBlocks.Add(new SlideContentBlock
                        {
                            X = x,
                            Y = y,
                            Order = frameOrder,
                            SubOrder = subOrder++,
                            Markdown = tableMarkdown.ToString()
                        });
                    }

                    foreach (var chartReference in graphicFrame.Descendants<C.ChartReference>())
                    {
                        var relationshipId = chartReference.Id == null ? string.Empty : chartReference.Id.Value;
                        var chartPart = string.IsNullOrWhiteSpace(relationshipId)
                            ? null
                            : slidePart.GetPartById(relationshipId) as ChartPart;
                        if (chartPart == null)
                        {
                            continue;
                        }

                        renderedCharts.Add(chartPart);
                        contentBlocks.Add(new SlideContentBlock
                        {
                            X = x,
                            Y = y,
                            Order = frameOrder,
                            SubOrder = subOrder++,
                            ChartPart = chartPart
                        });
                    }

                    var relationshipIds = graphicFrame.Descendants<Dgm.RelationshipIds>().FirstOrDefault();
                    var diagramRelationshipId = relationshipIds == null || relationshipIds.DataPart == null
                        ? string.Empty
                        : relationshipIds.DataPart.Value;
                    var diagramPart = string.IsNullOrWhiteSpace(diagramRelationshipId)
                        ? null
                        : slidePart.GetPartById(diagramRelationshipId) as DiagramDataPart;
                    if (diagramPart != null && diagramPart.DataModelRoot != null)
                    {
                        renderedDiagrams.Add(diagramPart);
                        var diagramMarkdown = new StringBuilder();
                        AppendSmartArtMarkdown(diagramMarkdown, diagramPart.DataModelRoot, cancellationToken);
                        contentBlocks.Add(new SlideContentBlock
                        {
                            X = x,
                            Y = y,
                            Order = frameOrder,
                            SubOrder = subOrder,
                            Markdown = diagramMarkdown.ToString()
                        });
                    }
                }
            }

            var fallbackOrder = elementOrder.Count;
            foreach (var chartPart in slidePart.ChartParts.Where(part => !renderedCharts.Contains(part)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                contentBlocks.Add(new SlideContentBlock
                {
                    X = long.MaxValue,
                    Y = long.MaxValue,
                    Order = fallbackOrder++,
                    ChartPart = chartPart
                });
            }
            foreach (var diagramPart in slidePart.DiagramDataParts.Where(part => !renderedDiagrams.Contains(part)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (diagramPart.DataModelRoot == null)
                {
                    continue;
                }

                var diagramMarkdown = new StringBuilder();
                AppendSmartArtMarkdown(diagramMarkdown, diagramPart.DataModelRoot, cancellationToken);
                contentBlocks.Add(new SlideContentBlock
                {
                    X = long.MaxValue,
                    Y = long.MaxValue,
                    Order = fallbackOrder++,
                    Markdown = diagramMarkdown.ToString()
                });
            }

            var chartCount = 0;
            foreach (var block in contentBlocks.OrderBy(block => block.Y).ThenBy(block => block.X)
                .ThenBy(block => block.Order).ThenBy(block => block.SubOrder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (block.ChartPart != null)
                {
                    ChartMarkdownRenderer.Append(builder, new[] { block.ChartPart }, maxChartPoints, chartCount, chartCount == 0);
                    chartCount++;
                }
                else
                {
                    builder.Append(block.Markdown);
                }
            }

            var speakerNotes = ReadSpeakerNotes(slidePart, cancellationToken);
            if (!string.IsNullOrWhiteSpace(speakerNotes))
            {
                builder.AppendLine();
                builder.AppendLine("### Speaker notes");
                foreach (var paragraph in speakerNotes.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    builder.AppendLine(EscapeMarkdown(paragraph.Trim()));
                }
            }

            var hasPictures = shapeTree != null && shapeTree.Descendants<P.Picture>().Any();
            var hasText = shapeTexts.Any();
            needsVisual = hasPictures || !hasText;
            if (!hasText && !hasPictures && contentBlocks.Count == 0)
            {
                builder.AppendLine("_(No extractable text)_");
            }

            return builder.ToString().Trim();
        }

        private static void AppendSmartArtMarkdown(StringBuilder builder, Dgm.DataModelRoot model, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model == null || model.PointList == null)
            {
                return;
            }

            var nodes = model.PointList.Elements<Dgm.Point>()
                .Select(point => new SmartArtNode
                {
                    Id = point.ModelId == null ? string.Empty : point.ModelId.Value,
                    Text = point.TextBody == null
                        ? string.Empty
                        : string.Join(" ", point.TextBody.Descendants<Drawing.Paragraph>()
                            .Select(paragraph => string.Concat(paragraph.Descendants<Drawing.Text>().Select(text => text.Text)))
                            .Where(text => !string.IsNullOrWhiteSpace(text)))
                })
                .Where(node => !string.IsNullOrWhiteSpace(node.Id) && !string.IsNullOrWhiteSpace(node.Text))
                .ToList();
            if (nodes.Count == 0)
            {
                return;
            }

            foreach (var connection in model.ConnectionList == null
                ? Enumerable.Empty<Dgm.Connection>()
                : model.ConnectionList.Elements<Dgm.Connection>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (connection.Type == null || connection.Type.Value != Dgm.ConnectionValues.ParentOf
                    || connection.SourceId == null || connection.DestinationId == null)
                {
                    continue;
                }

                var child = nodes.FirstOrDefault(node => node.Id == connection.DestinationId.Value);
                if (child != null)
                {
                    child.ParentId = connection.SourceId.Value;
                    child.Order = connection.DestinationPosition == null ? 0 : (int)connection.DestinationPosition.Value;
                }
            }

            builder.AppendLine();
            builder.AppendLine("### SmartArt");
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var rootNodes = nodes.Where(node => string.IsNullOrWhiteSpace(node.ParentId)
                || !nodes.Any(candidate => candidate.Id == node.ParentId));
            foreach (var node in rootNodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendSmartArtNode(builder, nodes, node, 0, visited, 0, cancellationToken);
            }
            foreach (var node in nodes)
            {
                if (!visited.Contains(node.Id))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AppendSmartArtNode(builder, nodes, node, 0, visited, 0, cancellationToken);
                }
            }
        }

        private sealed class SlideContentBlock
        {
            public long X { get; set; }
            public long Y { get; set; }
            public int Order { get; set; }
            public int SubOrder { get; set; }
            public string Markdown { get; set; }
            public ChartPart ChartPart { get; set; }
        }

        private static void AppendSmartArtNode(
            StringBuilder builder,
            IList<SmartArtNode> nodes,
            SmartArtNode node,
            int depth,
            ISet<string> visited,
            int depthLimit,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node == null || depthLimit > nodes.Count || !visited.Add(node.Id))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(node.Text))
            {
                builder.Append(' ', depth * 2);
                builder.Append("- ");
                builder.AppendLine(EscapeMarkdown(node.Text));
            }

            foreach (var child in nodes.Where(candidate => candidate.ParentId == node.Id).OrderBy(candidate => candidate.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendSmartArtNode(builder, nodes, child, depth + 1, visited, depthLimit + 1, cancellationToken);
            }
        }

        private sealed class SmartArtNode
        {
            public string Id { get; set; }
            public string ParentId { get; set; }
            public string Text { get; set; }
            public int Order { get; set; }
        }

        private static string ReadSpeakerNotes(SlidePart slidePart, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var notesPart = slidePart == null ? null : slidePart.NotesSlidePart;
            var shapeTree = notesPart == null || notesPart.NotesSlide == null || notesPart.NotesSlide.CommonSlideData == null
                ? null
                : notesPart.NotesSlide.CommonSlideData.ShapeTree;
            if (shapeTree == null)
            {
                return string.Empty;
            }

            var notesShape = shapeTree.Elements<P.Shape>().FirstOrDefault(shape =>
            {
                var placeholder = shape.NonVisualShapeProperties == null
                    || shape.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties == null
                    ? null
                    : shape.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties.GetFirstChild<PlaceholderShape>();
                return placeholder != null
                    && placeholder.Type != null
                    && placeholder.Type.Value == PlaceholderValues.Body
                    && (placeholder.Index == null || placeholder.Index.Value == 1);
            });
            return ReadShapeText(notesShape);
        }

        private static long GetShapeCoordinate(P.Shape shape, bool horizontal)
        {
            var transform = shape == null || shape.ShapeProperties == null ? null : shape.ShapeProperties.Transform2D;
            var offset = transform == null ? null : transform.Offset;
            return GetCoordinate(offset, horizontal);
        }

        private static long GetFrameCoordinate(P.GraphicFrame graphicFrame, bool horizontal)
        {
            var offset = graphicFrame == null || graphicFrame.Transform == null ? null : graphicFrame.Transform.Offset;
            return GetCoordinate(offset, horizontal);
        }

        private static long GetPictureCoordinate(P.Picture picture, bool horizontal)
        {
            var transform = picture == null || picture.ShapeProperties == null ? null : picture.ShapeProperties.Transform2D;
            return GetCoordinate(transform == null ? null : transform.Offset, horizontal);
        }

        private static string GetPictureDescription(P.Picture picture)
        {
            var properties = picture == null || picture.NonVisualPictureProperties == null
                ? null
                : picture.NonVisualPictureProperties.NonVisualDrawingProperties;
            if (properties == null)
            {
                return "image";
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
                ? "image"
                : properties.Name.Value;
        }

        private static long GetCoordinate(Drawing.Offset offset, bool horizontal)
        {
            if (offset == null)
            {
                return long.MaxValue;
            }

            var coordinate = horizontal ? offset.X : offset.Y;
            return coordinate == null ? long.MaxValue : coordinate.Value;
        }

        private static bool IsTitlePlaceholder(P.Shape shape)
        {
            var placeholder = shape == null || shape.NonVisualShapeProperties == null
                || shape.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties == null
                ? null
                : shape.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties.GetFirstChild<PlaceholderShape>();
            if (placeholder == null || placeholder.Type == null)
            {
                return false;
            }

            var type = placeholder.Type.Value.ToString();
            return string.Equals(type, "Title", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "CenteredTitle", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadShapeText(P.Shape shape)
        {
            if (shape == null || shape.TextBody == null)
            {
                return string.Empty;
            }

            return string.Join("\n", shape.TextBody.Elements<Drawing.Paragraph>()
                .Select(paragraph => string.Concat(paragraph.Descendants<Drawing.Text>().Select(text => text.Text)))
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        private static void AppendTable(StringBuilder builder, Drawing.Table table, CancellationToken cancellationToken)
        {
            var rows = table.Elements<Drawing.TableRow>().ToList();
            if (rows.Count == 0)
            {
                return;
            }

            var columnCount = rows.Max(row => row.Elements<Drawing.TableCell>().Count());
            if (columnCount == 0)
            {
                return;
            }

            builder.AppendLine();
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cells = rows[rowIndex].Elements<Drawing.TableCell>().ToList();
                var rowValues = new string[columnCount];
                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var text = columnIndex < cells.Count && cells[columnIndex].TextBody != null
                        ? string.Join("<br>", cells[columnIndex].TextBody.Elements<Drawing.Paragraph>()
                            .Select(paragraph => string.Concat(paragraph.Descendants<Drawing.Text>().Select(item => item.Text)))
                            .Where(value => !string.IsNullOrWhiteSpace(value)))
                        : string.Empty;
                    rowValues[columnIndex] = text;
                }
                MarkdownWriter.AppendRow(builder, rowValues, cancellationToken);

                if (rowIndex == 0)
                {
                    MarkdownWriter.AppendSeparator(builder, columnCount, null, cancellationToken);
                }
            }
        }

        private static string EscapeMarkdown(string value)
        {
            return MarkdownWriter.Escape(value);
        }
    }
}
