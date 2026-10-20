using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Drawing = DocumentFormat.OpenXml.Drawing;
using OfficeCopilot.OpenXml.Core;

namespace OfficeCopilot.OpenXml.PowerPoint
{
    /// <summary>
    /// File-level engine for .pptx documents (Open XML SDK — no PowerPoint host).
    /// Register once at host startup so any Office host can create/edit .pptx.
    /// </summary>
    public sealed class PowerPointDocumentEngine : DocumentEngineBase
    {
        /// <summary>Typed access to the underlying package.</summary>
        public PresentationDocument Document => (PresentationDocument)Package;

        public override string Format => "pptx";

        public PowerPointDocumentEngine(string filePath, bool editable)
            : base(filePath, editable, PresentationDocument.Open(filePath, editable))
        {
        }

        /// <summary>Register the .pptx engine. Call once per host process.</summary>
        public static void Register()
        {
            DocumentEngineRegistry.Register(
                ".pptx",
                (path, editable) => new PowerPointDocumentEngine(path, editable),
                PowerPointDocumentEngine.CreateBlank,
                PowerPointDocumentEngine.MergeTemplate);
        }

        /// <summary>Create a minimal .pptx with a slide master, layout and one blank slide.</summary>
        public static void CreateBlank(string path)
        {
            using (var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation))
            {
                var presentationPart = doc.AddPresentationPart();
                var presentation = new Presentation();
                presentationPart.Presentation = presentation;

                var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
                var slideMaster = new SlideMaster(
                    new CommonSlideData(CreateShapeTree()),
                    CreateColorMap());
                slideMasterPart.SlideMaster = slideMaster;
                AddDefaultTheme(slideMasterPart);

                var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
                slideLayoutPart.SlideLayout = new SlideLayout(new CommonSlideData(CreateShapeTree()));

                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = new Slide(new CommonSlideData(CreateShapeTree()));

                // Required reference lists and sizes (schema order:
                // sldMasterIdLst → sldIdLst → sldSz → notesSz).
                slideMaster.Append(new SlideLayoutIdList(new SlideLayoutId
                {
                    Id = 2147483649U,
                    RelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart)
                }));

                presentation.Append(new SlideMasterIdList(new SlideMasterId
                {
                    Id = 2147483648U,
                    RelationshipId = presentationPart.GetIdOfPart(slideMasterPart)
                }));

                presentation.Append(new SlideIdList(new SlideId
                {
                    Id = 256U,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart)
                }));

                presentation.Append(new SlideSize
                {
                    Cx = 9144000,
                    Cy = 6858000,
                    Type = SlideSizeValues.Screen4x3
                });

                presentation.Append(new NotesSize
                {
                    Cx = 6858000,
                    Cy = 9144000
                });

                presentationPart.Presentation.Save();
            }
        }

                private static void AddDefaultTheme(SlideMasterPart slideMasterPart)
                {
                        const string themeXml = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<a:theme xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" name=""Office Theme""><a:themeElements>
    <a:clrScheme name=""Office""><a:dk1><a:sysClr val=""windowText"" lastClr=""000000""/></a:dk1><a:lt1><a:sysClr val=""window"" lastClr=""FFFFFF""/></a:lt1><a:dk2><a:srgbClr val=""1F497D""/></a:dk2><a:lt2><a:srgbClr val=""EEECE1""/></a:lt2><a:accent1><a:srgbClr val=""4F81BD""/></a:accent1><a:accent2><a:srgbClr val=""C0504D""/></a:accent2><a:accent3><a:srgbClr val=""9BBB59""/></a:accent3><a:accent4><a:srgbClr val=""8064A2""/></a:accent4><a:accent5><a:srgbClr val=""4BACC6""/></a:accent5><a:accent6><a:srgbClr val=""F79646""/></a:accent6><a:hlink><a:srgbClr val=""0000FF""/></a:hlink><a:folHlink><a:srgbClr val=""800080""/></a:folHlink></a:clrScheme>
    <a:fontScheme name=""Office""><a:majorFont><a:latin typeface=""Arial""/><a:ea typeface=""""/><a:cs typeface=""""/></a:majorFont><a:minorFont><a:latin typeface=""Arial""/><a:ea typeface=""""/><a:cs typeface=""""/></a:minorFont></a:fontScheme>
    <a:fmtScheme name=""Office""><a:fillStyleLst><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w=""6350""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:prstDash val=""solid""/></a:ln><a:ln w=""12700""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:prstDash val=""solid""/></a:ln><a:ln w=""19050""><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:prstDash val=""solid""/></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill><a:solidFill><a:schemeClr val=""phClr""/></a:solidFill></a:bgFillStyleLst></a:fmtScheme>
</a:themeElements></a:theme>";
                        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(themeXml)))
                        {
                                var themePart = slideMasterPart.AddNewPart<ThemePart>();
                                themePart.FeedData(stream);
                        }
                }

        private static ShapeTree CreateShapeTree()
        {
            return new ShapeTree(
                new NonVisualGroupShapeProperties(
                    new NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                    new NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties(new Drawing.TransformGroup()));
        }

        private static ColorMap CreateColorMap()
        {
            return new ColorMap
            {
                Background1 = Drawing.ColorSchemeIndexValues.Light1,
                Text1 = Drawing.ColorSchemeIndexValues.Dark1,
                Background2 = Drawing.ColorSchemeIndexValues.Light2,
                Text2 = Drawing.ColorSchemeIndexValues.Dark2,
                Accent1 = Drawing.ColorSchemeIndexValues.Accent1,
                Accent2 = Drawing.ColorSchemeIndexValues.Accent2,
                Accent3 = Drawing.ColorSchemeIndexValues.Accent3,
                Accent4 = Drawing.ColorSchemeIndexValues.Accent4,
                Accent5 = Drawing.ColorSchemeIndexValues.Accent5,
                Accent6 = Drawing.ColorSchemeIndexValues.Accent6,
                Hyperlink = Drawing.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = Drawing.ColorSchemeIndexValues.FollowedHyperlink
            };
        }

        /// <summary>
        /// Append a new slide to the end of the deck (or at the end of the
        /// existing slide id list) and return its 1-based index. The slide is
        /// linked to the first available slide layout. Optional title/body text
        /// are placed into title/content shapes. Call Save() to persist.
        /// </summary>
        public int AddSlide(string title, string body)
        {
            EnsureEditable();

            var presentationPart = Document.PresentationPart;
            if (presentationPart == null || presentationPart.Presentation == null)
                throw new InvalidOperationException("Presentation not found.");

            var presentation = presentationPart.Presentation;
            var slideIdList = presentation.GetFirstChild<SlideIdList>();
            if (slideIdList == null)
            {
                slideIdList = new SlideIdList();
                var masterList = presentation.GetFirstChild<SlideMasterIdList>();
                if (masterList != null)
                    masterList.InsertAfterSelf(slideIdList);
                else
                    presentation.InsertAt(slideIdList, 0);
            }

            var newSlidePart = presentationPart.AddNewPart<SlidePart>();
            var layoutPart = FindFirstSlideLayout(presentationPart);
            if (layoutPart != null)
                newSlidePart.AddPart(layoutPart);

            var shapeTree = new ShapeTree(
                new NonVisualGroupShapeProperties(
                    new NonVisualDrawingProperties { Id = 1, Name = string.Empty },
                    new NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties());

            uint nextShapeId = 2;
            if (!string.IsNullOrEmpty(title))
                shapeTree.AppendChild(CreateTextShape(nextShapeId++, "Title", title, true));
            if (!string.IsNullOrEmpty(body))
                shapeTree.AppendChild(CreateTextShape(nextShapeId++, "Content", body, false));

            newSlidePart.Slide = new Slide(new CommonSlideData(shapeTree));
            newSlidePart.Slide.Save();

            uint maxId = 256;
            foreach (var id in slideIdList.Elements<SlideId>())
            {
                if (id.Id != null && id.Id.Value >= maxId)
                    maxId = id.Id.Value + 1;
            }

            slideIdList.AppendChild(new SlideId
            {
                Id = maxId,
                RelationshipId = presentationPart.GetIdOfPart(newSlidePart)
            });
            presentation.Save();

            return slideIdList.Elements<SlideId>().Count();
        }

        /// <summary>
        /// Read all text from the slide at the given 1-based index, concatenated
        /// across its text shapes. Returns empty string when the slide has no
        /// text.
        /// </summary>
        public string ReadSlideText(int slideIndex)
        {
            var slidePart = GetSlidePart(slideIndex);
            return ReadSlideTextInternal(slidePart);
        }

        /// <summary>Enumerate every slide with its 1-based index and text.</summary>
        public IReadOnlyList<PowerPointSlideInfo> ListSlides()
        {
            var result = new List<PowerPointSlideInfo>();
            var presentationPart = Document.PresentationPart;
            if (presentationPart == null || presentationPart.Presentation == null)
                return result;

            var slideIdList = presentationPart.Presentation.GetFirstChild<SlideIdList>();
            if (slideIdList == null)
                return result;

            int index = 0;
            foreach (var slideId in slideIdList.Elements<SlideId>())
            {
                index++;
                SlidePart slidePart = null;
                if (slideId.RelationshipId != null && slideId.RelationshipId.Value != null)
                    slidePart = presentationPart.GetPartById(slideId.RelationshipId.Value) as SlidePart;

                result.Add(new PowerPointSlideInfo
                {
                    Index = index,
                    Text = ReadSlideTextInternal(slidePart)
                });
            }
            return result;
        }

        /// <summary>Append a text box to a slide (1-based index) and return its shape id.</summary>
        public uint AddTextBox(int slideIndex, string text)
        {
            EnsureEditable();
            return PowerPointShapeOperations.AddTextBox(GetSlidePart(slideIndex), text);
        }

        /// <summary>
        /// Replace the text of a shape identified by its name or numeric shape
        /// id. Returns false when no matching shape exists.
        /// </summary>
        public bool SetShapeText(int slideIndex, string shapeNameOrId, string text)
        {
            EnsureEditable();
            return PowerPointShapeOperations.SetShapeText(GetSlidePart(slideIndex), shapeNameOrId, text);
        }

        /// <summary>Enumerate the text shapes of a slide (1-based index).</summary>
        public IReadOnlyList<PowerPointShapeInfo> ReadShapes(int slideIndex)
        {
            return PowerPointShapeOperations.ReadShapes(GetSlidePart(slideIndex));
        }

        /// <summary>
        /// Append a table with <paramref name="rows"/> x <paramref name="columns"/>
        /// cells to a slide (1-based index), optionally seeded from row-major
        /// <paramref name="data"/>. Returns the 1-based table index on the slide.
        /// </summary>
        public int AddTable(int slideIndex, int rows, int columns, IReadOnlyList<IReadOnlyList<string>> data)
        {
            EnsureEditable();
            return PowerPointTableOperations.AddTable(GetSlidePart(slideIndex), rows, columns, data);
        }

        /// <summary>
        /// Replace the text of a single cell (1-based row/column) of a slide
        /// table (1-based table index). Returns false when no such table exists.
        /// </summary>
        public bool SetTableCellText(int slideIndex, int tableIndex, int row, int column, string text)
        {
            EnsureEditable();
            return PowerPointTableOperations.SetCellText(GetSlidePart(slideIndex), tableIndex, row, column, text);
        }

        /// <summary>
        /// Insert a picture from a local file into a slide (1-based index) and
        /// return its shape id. Supported formats: png, jpg, jpeg, gif, bmp,
        /// tif, tiff, emf, wmf.
        /// </summary>
        public uint AddPicture(int slideIndex, string imagePath)
        {
            EnsureEditable();
            return PowerPointMediaOperations.AddPicture(GetSlidePart(slideIndex), imagePath);
        }

        /// <summary>Set (or create) the speaker notes of a slide (1-based index).</summary>
        public void AddNotes(int slideIndex, string text)
        {
            EnsureEditable();
            PowerPointMediaOperations.AddNotes(GetSlidePart(slideIndex), text);
        }

        /// <summary>Set a solid color background on a slide (1-based index) via RRGGBB hex.</summary>
        public void SetBackground(int slideIndex, string colorHex)
        {
            EnsureEditable();
            PowerPointMediaOperations.SetBackground(GetSlidePart(slideIndex), colorHex);
        }

        /// <summary>
        /// Add a single-series clustered column chart to a slide (1-based index)
        /// and return its 1-based chart index on that slide.
        /// </summary>
        public int AddChart(int slideIndex, IReadOnlyList<string> categories, IReadOnlyList<double> values, string seriesName, string title)
        {
            EnsureEditable();
            return PowerPointChartOperations.AddChart(GetSlidePart(slideIndex), categories, values, seriesName, title);
        }

        private SlidePart GetSlidePart(int slideIndex)
        {
            var presentationPart = Document.PresentationPart;
            if (presentationPart == null || presentationPart.Presentation == null)
                throw new InvalidOperationException("Presentation not found.");

            var slideIdList = presentationPart.Presentation.GetFirstChild<SlideIdList>();
            if (slideIdList == null)
                throw new ArgumentOutOfRangeException("slideIndex", "Presentation has no slides.");

            var slideIds = slideIdList.Elements<SlideId>().ToList();
            if (slideIndex < 1 || slideIndex > slideIds.Count)
                throw new ArgumentOutOfRangeException("slideIndex", string.Format(
                    "Slide index {0} out of range (1..{1}).", slideIndex, slideIds.Count));

            var slideId = slideIds[slideIndex - 1];
            if (slideId.RelationshipId == null || slideId.RelationshipId.Value == null)
                throw new InvalidOperationException(string.Format("Slide {0} has no relationship id.", slideIndex));
            return presentationPart.GetPartById(slideId.RelationshipId.Value) as SlidePart;
        }

        private static SlideLayoutPart FindFirstSlideLayout(PresentationPart presentationPart)
        {
            var masterPart = presentationPart.SlideMasterParts.FirstOrDefault();
            if (masterPart == null)
                return null;
            return masterPart.SlideLayoutParts.FirstOrDefault();
        }

        private (long Width, long Height) GetSlideSize()
        {
            var presentationPart = Document.PresentationPart;
            var slideSize = presentationPart != null && presentationPart.Presentation != null
                ? presentationPart.Presentation.GetFirstChild<SlideSize>()
                : null;

            long width = slideSize != null && slideSize.Cx != null ? slideSize.Cx.Value : 12192000L;
            long height = slideSize != null && slideSize.Cy != null ? slideSize.Cy.Value : 6858000L;
            if (width <= 0) width = 12192000L;
            if (height <= 0) height = 6858000L;
            return (width, height);
        }

        private Shape CreateTextShape(uint id, string name, string text, bool isTitle)
        {
            var appNvPr = new ApplicationNonVisualDrawingProperties();
            if (isTitle)
                appNvPr.AppendChild(new PlaceholderShape { Type = PlaceholderValues.Title });

            var size = GetSlideSize();
            var transform = new Drawing.Transform2D
            {
                Offset = new Drawing.Offset
                {
                    X = size.Width * 7 / 100,
                    Y = isTitle ? size.Height * 5 / 100 : size.Height * 22 / 100
                },
                Extents = new Drawing.Extents
                {
                    Cx = size.Width * 86 / 100,
                    Cy = isTitle ? size.Height * 13 / 100 : size.Height * 70 / 100
                }
            };

            return new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = id, Name = name },
                    new NonVisualShapeDrawingProperties(),
                    appNvPr),
                new ShapeProperties(transform),
                BuildTextBody(text));
        }

        private static TextBody BuildTextBody(string text)
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

        private static string ReadSlideTextInternal(SlidePart slidePart)
        {
            if (slidePart == null || slidePart.Slide == null)
                return string.Empty;

            var shapeTree = slidePart.Slide.CommonSlideData != null
                ? slidePart.Slide.CommonSlideData.ShapeTree
                : null;
            if (shapeTree == null)
                return string.Empty;

            var parts = new List<string>();
            foreach (var shape in shapeTree.Elements<Shape>())
            {
                var text = ExtractShapeText(shape);
                if (!string.IsNullOrEmpty(text))
                    parts.Add(text);
            }
            return string.Join("\n", parts);
        }

        private static string ExtractShapeText(Shape shape)
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

        private void EnsureEditable()
        {
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be modified.");
        }

        /// <summary>
        /// Merge a template with JSON data, replacing {{key}} placeholders in
        /// slide and notes shape text. Ported from officecli TemplateMerger.MergePptx.
        /// </summary>
        public static MergeResult MergeTemplate(string templatePath, string outputPath, MergeData data)
        {
            if (!File.Exists(templatePath))
                throw new FileNotFoundException(string.Format("Template not found: {0}", templatePath), templatePath);

            File.Copy(templatePath, outputPath, true);

            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalReplacements = 0;

            using (var doc = PresentationDocument.Open(outputPath, true))
            {
                var presentationPart = doc.PresentationPart;
                if (presentationPart != null)
                {
                    foreach (var slidePart in presentationPart.SlideParts)
                    {
                        var shapeTree = slidePart.Slide != null && slidePart.Slide.CommonSlideData != null
                            ? slidePart.Slide.CommonSlideData.ShapeTree
                            : null;
                        if (shapeTree != null)
                        {
                            foreach (var shape in shapeTree.Elements<Shape>())
                                totalReplacements += ReplaceInTextBody(shape.TextBody, data, usedKeys);
                        }

                        var notesPart = slidePart.NotesSlidePart;
                        if (notesPart != null && notesPart.NotesSlide != null)
                        {
                            var notesShapeTree = notesPart.NotesSlide.CommonSlideData != null
                                ? notesPart.NotesSlide.CommonSlideData.ShapeTree
                                : null;
                            if (notesShapeTree != null)
                            {
                                foreach (var shape in notesShapeTree.Elements<Shape>())
                                    totalReplacements += ReplaceInTextBody(shape.TextBody, data, usedKeys);
                            }
                            notesPart.NotesSlide.Save();
                        }

                        if (slidePart.Slide != null)
                            slidePart.Slide.Save();
                    }
                }
            }

            var unresolved = ScanUnresolvedPptx(outputPath);
            return new MergeResult(totalReplacements, unresolved, usedKeys);
        }

        private static int ReplaceInTextBody(OpenXmlElement textBody, MergeData data, ISet<string> usedKeys)
        {
            if (textBody == null)
                return 0;

            int replacements = 0;
            foreach (var para in textBody.Elements<Drawing.Paragraph>())
                replacements += ReplaceInParagraph(para, data, usedKeys);
            return replacements;
        }

        private static int ReplaceInParagraph(Drawing.Paragraph para, MergeData data, ISet<string> usedKeys)
        {
            var runs = para.Elements<Drawing.Run>().ToList();
            if (runs.Count == 0)
                return 0;

            var fullText = string.Concat(runs.Select(r => r.Text != null ? r.Text.Text : string.Empty));
            if (!fullText.Contains("{{"))
                return 0;

            int replacements = 0;
            bool matched;
            var newText = MergeTextHelper.SinglePassReplace(fullText, data, out matched, usedKeys, count => replacements += count);
            if (replacements == 0)
                return 0;

            var firstRun = runs[0];
            if (firstRun.Text == null)
                firstRun.Text = new Drawing.Text(newText);
            else
                firstRun.Text.Text = newText;

            for (int i = 1; i < runs.Count; i++)
                runs[i].Remove();

            return replacements;
        }

        private static List<string> ScanUnresolvedPptx(string filePath)
        {
            var unresolved = new HashSet<string>(StringComparer.Ordinal);
            using (var doc = PresentationDocument.Open(filePath, false))
            {
                var presentationPart = doc.PresentationPart;
                if (presentationPart != null)
                {
                    foreach (var slidePart in presentationPart.SlideParts)
                    {
                        var shapeTree = slidePart.Slide != null && slidePart.Slide.CommonSlideData != null
                            ? slidePart.Slide.CommonSlideData.ShapeTree
                            : null;
                        if (shapeTree != null)
                        {
                            foreach (var shape in shapeTree.Elements<Shape>())
                                ScanTextBody(shape.TextBody, unresolved);
                        }

                        var notesPart = slidePart.NotesSlidePart;
                        if (notesPart != null && notesPart.NotesSlide != null)
                        {
                            var notesShapeTree = notesPart.NotesSlide.CommonSlideData != null
                                ? notesPart.NotesSlide.CommonSlideData.ShapeTree
                                : null;
                            if (notesShapeTree != null)
                            {
                                foreach (var shape in notesShapeTree.Elements<Shape>())
                                    ScanTextBody(shape.TextBody, unresolved);
                            }
                        }
                    }
                }
            }

            var list = unresolved.ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static void ScanTextBody(OpenXmlElement textBody, ISet<string> unresolved)
        {
            if (textBody == null)
                return;

            foreach (var para in textBody.Elements<Drawing.Paragraph>())
            {
                var text = string.Concat(para.Elements<Drawing.Run>().Select(r => r.Text != null ? r.Text.Text : string.Empty));
                foreach (Match match in MergeTextHelper.PlaceholderPattern.Matches(text))
                    unresolved.Add(match.Groups[1].Value);
            }
        }
    }
}
