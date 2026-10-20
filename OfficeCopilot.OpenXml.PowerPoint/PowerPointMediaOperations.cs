using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace OfficeCopilot.OpenXml.PowerPoint
{
    /// <summary>
    /// Media and background operations for the PowerPoint file engine. Ported
    /// from officecli PowerPointHandler.Add.Media / Notes / Background and
    /// reduced to the explicit-API surface.
    /// </summary>
    internal static class PowerPointMediaOperations
    {
        internal static uint AddPicture(SlidePart slidePart, string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                throw new ArgumentException("imagePath is required.", "imagePath");
            if (!File.Exists(imagePath))
                throw new FileNotFoundException(string.Format("Image not found: {0}", imagePath), imagePath);

            var partType = ResolveImagePartType(Path.GetExtension(imagePath));
            var imagePart = slidePart.AddImagePart(partType);
            using (var stream = File.OpenRead(imagePath))
                imagePart.FeedData(stream);
            var relId = slidePart.GetIdOfPart(imagePart);

            var shapeTree = PowerPointShapeOperations.GetShapeTree(slidePart);
            uint id = PowerPointShapeOperations.GetNextShapeId(shapeTree);

            var picture = new Picture(
                new NonVisualPictureProperties(
                    new NonVisualDrawingProperties { Id = id, Name = "Picture" },
                    new NonVisualPictureDrawingProperties(new Drawing.PictureLocks { NoChangeAspect = true }),
                    new ApplicationNonVisualDrawingProperties()),
                new BlipFill(new Drawing.Blip { Embed = relId }),
                new ShapeProperties(
                    new Drawing.Transform2D(
                        new Drawing.Offset { X = 914400L, Y = 914400L },
                        new Drawing.Extents { Cx = 3657600L, Cy = 2743200L })));

            shapeTree.AppendChild(picture);
            slidePart.Slide.Save();
            return id;
        }

        internal static void AddNotes(SlidePart slidePart, string text)
        {
            var notesPart = EnsureNotesSlidePart(slidePart);
            SetNotesText(notesPart, text);
            slidePart.Slide.Save();
        }

        internal static void SetBackground(SlidePart slidePart, string colorHex)
        {
            var commonSlideData = slidePart.Slide != null ? slidePart.Slide.CommonSlideData : null;
            if (commonSlideData == null)
                throw new InvalidOperationException("Slide has no common slide data.");

            var backgroundProperties = new BackgroundProperties();
            var solidFill = new Drawing.SolidFill();
            solidFill.Append(new Drawing.RgbColorModelHex { Val = NormalizeHex(colorHex) });
            backgroundProperties.Append(solidFill);

            var background = new Background();
            background.Append(backgroundProperties);

            var existing = commonSlideData.Background;
            if (existing != null)
                existing.Remove();

            var shapeTree = commonSlideData.ShapeTree;
            if (shapeTree == null)
            {
                shapeTree = new ShapeTree(
                    new NonVisualGroupShapeProperties(
                        new NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                        new NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties());
                commonSlideData.AppendChild(shapeTree);
            }

            commonSlideData.InsertBefore(background, shapeTree);
            slidePart.Slide.Save();
        }

        private static NotesSlidePart EnsureNotesSlidePart(SlidePart slidePart)
        {
            if (slidePart.NotesSlidePart != null)
                return slidePart.NotesSlidePart;

            var notesPart = slidePart.AddNewPart<NotesSlidePart>();
            notesPart.NotesSlide = new NotesSlide(new CommonSlideData(new ShapeTree(
                new NonVisualGroupShapeProperties(
                    new NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                    new NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties())));
            return notesPart;
        }

        private static void SetNotesText(NotesSlidePart notesPart, string text)
        {
            var shapeTree = notesPart.NotesSlide != null && notesPart.NotesSlide.CommonSlideData != null
                ? notesPart.NotesSlide.CommonSlideData.ShapeTree
                : null;
            if (shapeTree == null)
                throw new InvalidOperationException("Notes slide has no shape tree.");

            Shape notesShape = null;
            foreach (var shape in shapeTree.Elements<Shape>())
            {
                var ph = shape.NonVisualShapeProperties != null && shape.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties != null
                    ? shape.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties.GetFirstChild<PlaceholderShape>()
                    : null;
                if (ph != null && ph.Index != null && ph.Index.Value == 1)
                {
                    notesShape = shape;
                    break;
                }
            }

            if (notesShape == null)
            {
                notesShape = new Shape(
                    new NonVisualShapeProperties(
                        new NonVisualDrawingProperties { Id = 2, Name = "Notes Placeholder" },
                        new NonVisualShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties(
                            new PlaceholderShape { Type = PlaceholderValues.Body, Index = 1 })),
                    new ShapeProperties(),
                    new TextBody(new Drawing.BodyProperties(), new Drawing.ListStyle(), new Drawing.Paragraph()));
                shapeTree.AppendChild(notesShape);
            }

            var textBody = notesShape.TextBody;
            if (textBody == null)
            {
                textBody = new TextBody(new Drawing.BodyProperties(), new Drawing.ListStyle());
                notesShape.TextBody = textBody;
            }

            textBody.RemoveAllChildren<Drawing.Paragraph>();
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                textBody.AppendChild(new Drawing.Paragraph(
                    new Drawing.Run(
                        new Drawing.RunProperties { Language = "en-US" },
                        new Drawing.Text { Text = line })));
            }

            notesPart.NotesSlide.Save();
        }

        private static PartTypeInfo ResolveImagePartType(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".png":
                    return ImagePartType.Png;
                case ".jpg":
                case ".jpeg":
                    return ImagePartType.Jpeg;
                case ".gif":
                    return ImagePartType.Gif;
                case ".bmp":
                    return ImagePartType.Bmp;
                case ".tif":
                case ".tiff":
                    return ImagePartType.Tiff;
                case ".emf":
                    return ImagePartType.Emf;
                case ".wmf":
                    return ImagePartType.Wmf;
                default:
                    throw new ArgumentException(string.Format(
                        "Unsupported image extension '{0}'. Supported: png, jpg, jpeg, gif, bmp, tif, tiff, emf, wmf.", extension));
            }
        }

        private static string NormalizeHex(string colorHex)
        {
            var c = (colorHex ?? "FFFFFF").Trim().TrimStart('#');
            if (c.Length != 6)
                throw new ArgumentException(string.Format("Invalid hex color '{0}'. Expected 6-digit RRGGBB.", colorHex));
            return c.ToUpperInvariant();
        }
    }
}
