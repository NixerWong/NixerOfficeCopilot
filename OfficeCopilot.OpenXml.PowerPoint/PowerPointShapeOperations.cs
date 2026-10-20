using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace OfficeCopilot.OpenXml.PowerPoint
{
    /// <summary>
    /// Text-shape operations for the PowerPoint file engine. Ported from
    /// officecli PowerPointHandler.Add.Shape / Set.Shape / NodeBuilder
    /// (CreateTextShape, GetShapeText) and reduced to the explicit-API surface.
    /// </summary>
    internal static class PowerPointShapeOperations
    {
        internal static uint AddTextBox(SlidePart slidePart, string text)
        {
            var shapeTree = GetShapeTree(slidePart);
            uint id = GetNextShapeId(shapeTree);
            var shape = BuildTextShape(
                id,
                "TextBox " + (shapeTree.Elements<Shape>().Count() + 1),
                text,
                isTitle: false,
                isTextBox: true);
            shapeTree.AppendChild(shape);
            slidePart.Slide.Save();
            return id;
        }

        internal static bool SetShapeText(SlidePart slidePart, string shapeNameOrId, string text)
        {
            var shapeTree = GetShapeTree(slidePart);
            var shape = FindShape(shapeTree, shapeNameOrId);
            if (shape == null)
                return false;

            shape.TextBody = BuildTextBody(text);
            slidePart.Slide.Save();
            return true;
        }

        internal static List<PowerPointShapeInfo> ReadShapes(SlidePart slidePart)
        {
            var result = new List<PowerPointShapeInfo>();
            var shapeTree = GetShapeTree(slidePart);

            foreach (var shape in shapeTree.Elements<Shape>())
            {
                var nv = shape.NonVisualShapeProperties;
                var cNvPr = nv != null ? nv.NonVisualDrawingProperties : null;
                var cNvSpPr = nv != null ? nv.NonVisualShapeDrawingProperties : null;
                var appNvPr = nv != null ? nv.ApplicationNonVisualDrawingProperties : null;
                var placeholder = appNvPr != null ? appNvPr.GetFirstChild<PlaceholderShape>() : null;

                bool isTitle = placeholder != null && placeholder.Type != null && placeholder.Type.Value == PlaceholderValues.Title;
                bool isTextBox = cNvSpPr != null && cNvSpPr.TextBox != null && cNvSpPr.TextBox.Value;

                result.Add(new PowerPointShapeInfo
                {
                    Id = cNvPr != null && cNvPr.Id != null ? cNvPr.Id.Value : 0,
                    Name = cNvPr != null && cNvPr.Name != null ? cNvPr.Name.Value : string.Empty,
                    Type = isTitle ? "title" : (isTextBox ? "textbox" : "shape"),
                    Text = ExtractText(shape)
                });
            }
            return result;
        }

        internal static ShapeTree GetShapeTree(SlidePart slidePart)
        {
            if (slidePart == null || slidePart.Slide == null || slidePart.Slide.CommonSlideData == null)
                throw new InvalidOperationException("Slide has no common slide data.");

            var shapeTree = slidePart.Slide.CommonSlideData.ShapeTree;
            if (shapeTree == null)
                throw new InvalidOperationException("Slide has no shape tree.");
            return shapeTree;
        }

        internal static uint GetNextShapeId(ShapeTree shapeTree)
        {
            uint next = 2;
            foreach (var cNvPr in shapeTree.Descendants<NonVisualDrawingProperties>())
            {
                if (cNvPr.Id != null && cNvPr.Id.Value >= next)
                    next = cNvPr.Id.Value + 1;
            }
            return next;
        }

        private static Shape FindShape(ShapeTree shapeTree, string nameOrId)
        {
            uint id;
            if (uint.TryParse(nameOrId, out id))
            {
                foreach (var shape in shapeTree.Elements<Shape>())
                {
                    var cNvPr = shape.NonVisualShapeProperties != null
                        ? shape.NonVisualShapeProperties.NonVisualDrawingProperties
                        : null;
                    if (cNvPr != null && cNvPr.Id != null && cNvPr.Id.Value == id)
                        return shape;
                }
                return null;
            }

            foreach (var shape in shapeTree.Elements<Shape>())
            {
                var cNvPr = shape.NonVisualShapeProperties != null
                    ? shape.NonVisualShapeProperties.NonVisualDrawingProperties
                    : null;
                if (cNvPr != null && cNvPr.Name != null && string.Equals(cNvPr.Name.Value, nameOrId, StringComparison.OrdinalIgnoreCase))
                    return shape;
            }
            return null;
        }

        internal static Shape BuildTextShape(uint id, string name, string text, bool isTitle, bool isTextBox)
        {
            var appNvPr = new ApplicationNonVisualDrawingProperties();
            if (isTitle)
                appNvPr.AppendChild(new PlaceholderShape { Type = PlaceholderValues.Title });

            var cNvSpPr = new NonVisualShapeDrawingProperties();
            if (isTextBox)
                cNvSpPr.TextBox = true;

            return new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = id, Name = name },
                    cNvSpPr,
                    appNvPr),
                new ShapeProperties(
                    new Drawing.Transform2D
                    {
                        Offset = new Drawing.Offset { X = 838200L, Y = isTitle ? 365125L : 1825625L },
                        Extents = new Drawing.Extents { Cx = 7315200L, Cy = isTitle ? 914400L : 4114800L }
                    }),
                BuildTextBody(text));
        }

        internal static TextBody BuildTextBody(string text)
        {
            var body = new TextBody(
                new Drawing.BodyProperties(),
                new Drawing.ListStyle());

            if (string.IsNullOrEmpty(text))
            {
                body.AppendChild(new Drawing.Paragraph());
            }
            else
            {
                var lines = text.Replace("\r\n", "\n").Split('\n');
                foreach (var line in lines)
                {
                    var para = new Drawing.Paragraph();
                    para.AppendChild(new Drawing.Run(
                        new Drawing.RunProperties { Language = "en-US" },
                        new Drawing.Text { Text = line }));
                    body.AppendChild(para);
                }
            }
            return body;
        }

        internal static string ExtractText(Shape shape)
        {
            var textBody = shape.TextBody;
            if (textBody == null)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var para in textBody.Elements<Drawing.Paragraph>())
            {
                var line = string.Concat(para.Elements<Drawing.Run>()
                    .Select(r => r.Text != null ? r.Text.Text : string.Empty));
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(line);
            }
            return sb.ToString();
        }
    }
}
