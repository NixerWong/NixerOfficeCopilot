using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerPointInterop = Microsoft.Office.Interop.PowerPoint;

namespace OfficeCopilot.Host.PowerPoint
{
    public sealed partial class PowerPointHostAdapter : IPowerPointEditHost
    {
        private const int EditMsoTrue = -1;
        private const int EditMsoFalse = 0;
        private const int PpSaveAsOpenXmlPresentation = 24;
        private const int PpAlertsNone = 1;
        private const int MaxExportImages = 8;

        public PowerPointEditResult SetShapeText(int slideIndex, string shape, string text)
        {
            return Edit(slideIndex, slide =>
            {
                var target = FindShape(slide, shape);
                if (!ComLate.GetTri(target, "HasTextFrame"))
                {
                    throw new InvalidOperationException("形状「" + target.Name + "」不含文本框，无法写入文字。");
                }

                target.TextFrame.TextRange.Text = NormalizeText(text);
                return Result("已更新形状「" + target.Name + "」的文字。", slide, target.Name);
            });
        }

        public PowerPointEditResult FormatShapes(int slideIndex, IList<string> shapes, PowerPointShapeFormat format)
        {
            if (format == null) throw new ArgumentNullException(nameof(format));
            return Edit(slideIndex, slide =>
            {
                var targets = FindShapes(slide, shapes);
                foreach (var target in targets)
                {
                    ApplyFormat(target, format);
                }

                return Result("已格式化 " + targets.Count + " 个形状。", slide, targets.Select(s => s.Name).ToArray());
            });
        }

        public PowerPointEditResult ArrangeShapes(int slideIndex, IList<string> shapes, string action, bool relativeToSlide)
        {
            var key = (action ?? string.Empty).Trim().ToLowerInvariant();
            return Edit(slideIndex, slide =>
            {
                var targets = FindShapes(slide, shapes);
                var relative = relativeToSlide ? EditMsoTrue : EditMsoFalse;
                switch (key)
                {
                    case "align_left": Align(slide, targets, 0, relative); break;
                    case "align_center": Align(slide, targets, 1, relative); break;
                    case "align_right": Align(slide, targets, 2, relative); break;
                    case "align_top": Align(slide, targets, 3, relative); break;
                    case "align_middle": Align(slide, targets, 4, relative); break;
                    case "align_bottom": Align(slide, targets, 5, relative); break;
                    case "distribute_horizontal": Distribute(slide, targets, 0, relative); break;
                    case "distribute_vertical": Distribute(slide, targets, 1, relative); break;
                    case "bring_to_front": ZOrder(targets, 0); break;
                    case "send_to_back": ZOrder(targets, 1); break;
                    case "bring_forward": ZOrder(targets, 2); break;
                    case "send_backward": ZOrder(targets, 3); break;
                    default: throw new ArgumentException("不支持的排列操作：" + action);
                }

                return Result("已执行 " + key + "（" + targets.Count + " 个形状）。", slide, targets.Select(s => s.Name).ToArray());
            });
        }

        public PowerPointEditResult DeleteShapes(int slideIndex, IList<string> shapes)
        {
            return Edit(slideIndex, slide =>
            {
                var targets = FindShapes(slide, shapes);
                var names = targets.Select(s => s.Name).ToArray();
                foreach (var target in targets)
                {
                    target.Delete();
                }

                return Result("已删除 " + names.Length + " 个形状。", slide, names);
            });
        }

        public PowerPointEditResult AddTextBox(int slideIndex, PowerPointTextBoxSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            return Edit(slideIndex, slide =>
            {
                var shape = (PowerPointInterop.Shape)ComLate.Call(
                    slide.Shapes, "AddTextbox", 1, spec.Left, spec.Top, spec.Width, spec.Height);
                if (!string.IsNullOrWhiteSpace(spec.Name)) shape.Name = spec.Name.Trim();
                shape.TextFrame.TextRange.Text = NormalizeText(spec.Text);
                if (spec.Format != null) ApplyFormat(shape, spec.Format);
                return Result("已添加文本框「" + shape.Name + "」。", slide, shape.Name);
            });
        }

        public PowerPointEditResult InsertPicture(int slideIndex, string path, float? left, float? top, float? width, float? height, string name)
        {
            var fullPath = ValidateImagePath(path);
            return Edit(slideIndex, slide =>
            {
                var shape = (PowerPointInterop.Shape)ComLate.Call(
                    slide.Shapes, "AddPicture", fullPath, EditMsoFalse, EditMsoTrue,
                    left ?? 0f, top ?? 0f, width ?? -1f, height ?? -1f);
                if (!string.IsNullOrWhiteSpace(name)) shape.Name = name.Trim();
                return Result("已插入图片「" + shape.Name + "」。", slide, shape.Name);
            });
        }

        public PowerPointEditResult SetNotes(int slideIndex, string text)
        {
            return Edit(slideIndex, slide =>
            {
                var body = ComSlideBuilder.FindNotesBody(slide)
                    ?? throw new InvalidOperationException("该页备注页缺少正文占位符，无法写入备注。");
                body.TextFrame.TextRange.Text = NormalizeText(text);
                return Result("已更新备注。", slide);
            });
        }

        public PowerPointEditResult AddSlides(int position, string layout, IList<string> titles, int count)
        {
            var total = titles != null && titles.Count > 0 ? titles.Count : Math.Max(1, count);
            if (total > 50) throw new ArgumentException("单次最多新增 50 页。");
            return _dispatcher.Invoke(() =>
            {
                var presentation = GetWritablePresentation();
                var slideCount = presentation.Slides.Count;
                var insertAt = position <= 0 || position > slideCount + 1 ? slideCount + 1 : position;
                var customLayout = FindLayout(presentation, layout);
                if (!string.IsNullOrWhiteSpace(layout) && customLayout == null)
                {
                    throw new ArgumentException("找不到版式「" + layout + "」。可用版式：" + string.Join("、", ListLayoutNames(presentation)));
                }

                BeginUndo();
                var result = new PowerPointEditResult();
                for (var i = 0; i < total; i++)
                {
                    var slide = customLayout != null
                        ? presentation.Slides.AddSlide(insertAt + i, customLayout)
                        : presentation.Slides.Add(insertAt + i, PowerPointInterop.PpSlideLayout.ppLayoutBlank);
                    var title = titles != null && i < titles.Count ? titles[i] : null;
                    if (!string.IsNullOrWhiteSpace(title) && ComLate.GetTri(slide.Shapes, "HasTitle"))
                    {
                        slide.Shapes.Title.TextFrame.TextRange.Text = NormalizeText(title);
                    }

                    result.SlideIndexes.Add(slide.SlideIndex);
                }

                result.Message = "已新增 " + total + " 页（第 " + insertAt + "–" + (insertAt + total - 1) + " 页）。";
                return result;
            });
        }

        public PowerPointEditResult DeleteSlides(IList<int> slideIndexes)
        {
            if (slideIndexes == null || slideIndexes.Count == 0) throw new ArgumentException("未指定要删除的页。");
            return _dispatcher.Invoke(() =>
            {
                var presentation = GetWritablePresentation();
                var count = presentation.Slides.Count;
                var indexes = slideIndexes.Distinct().OrderByDescending(i => i).ToList();
                foreach (var index in indexes)
                {
                    if (index < 1 || index > count) throw new ArgumentOutOfRangeException(nameof(slideIndexes), "页码超出范围：" + index + "（共 " + count + " 页）。");
                }

                if (indexes.Count >= count) throw new InvalidOperationException("不能删除全部幻灯片。");
                BeginUndo();
                foreach (var index in indexes)
                {
                    presentation.Slides[index].Delete();
                }

                var result = new PowerPointEditResult { Message = "已删除 " + indexes.Count + " 页。" };
                result.SlideIndexes.AddRange(indexes.OrderBy(i => i));
                return result;
            });
        }

        public PowerPointEditResult MoveSlide(int fromIndex, int toIndex)
        {
            return _dispatcher.Invoke(() =>
            {
                var presentation = GetWritablePresentation();
                var count = presentation.Slides.Count;
                if (fromIndex < 1 || fromIndex > count || toIndex < 1 || toIndex > count)
                {
                    throw new ArgumentOutOfRangeException(nameof(fromIndex), "页码超出范围（共 " + count + " 页）。");
                }

                BeginUndo();
                presentation.Slides[fromIndex].MoveTo(toIndex);
                var result = new PowerPointEditResult { Message = "已将第 " + fromIndex + " 页移动到第 " + toIndex + " 页。" };
                result.SlideIndexes.Add(toIndex);
                return result;
            });
        }

        public PowerPointFontReport CheckFonts()
        {
            return _dispatcher.Invoke(() => BuildFontReport(GetRequiredPresentation()));
        }

        public PowerPointSaveCopyResult SaveWithFonts(string targetPath)
        {
            return SaveCopy(targetPath, true);
        }

        public PowerPointSaveCopyResult SaveCopy(string targetPath, bool embedFonts)
        {
            return _dispatcher.Invoke(() =>
            {
                var presentation = GetRequiredPresentation();
                var path = ResolveSaveCopyPath(presentation, targetPath, embedFonts);
                var report = BuildFontReport(presentation);
                object originalAlerts = null;
                try
                {
                    try { originalAlerts = ComLate.Get(_application, "DisplayAlerts"); ComLate.Set(_application, "DisplayAlerts", PpAlertsNone); }
                    catch (Exception) { originalAlerts = null; }
                    ComLate.Call(presentation, "SaveCopyAs", path, PpSaveAsOpenXmlPresentation, embedFonts ? EditMsoTrue : EditMsoFalse);
                }
                finally
                {
                    if (originalAlerts != null)
                    {
                        try { ComLate.Set(_application, "DisplayAlerts", originalAlerts); } catch (Exception) { }
                    }
                }

                return new PowerPointSaveCopyResult
                {
                    Path = path,
                    NotEmbeddableFonts = report.Fonts.Where(f => !f.Embeddable && !f.Embedded).Select(f => f.Name).ToList(),
                    MissingFonts = report.Fonts.Where(f => !f.Installed).Select(f => f.Name).ToList()
                };
            });
        }

        public IList<PowerPointSlideImage> ExportSlideImages(IList<int> slideIndexes, int width, int height)
        {
            return _dispatcher.Invoke(() =>
            {
                var presentation = GetRequiredPresentation();
                var count = presentation.Slides.Count;
                var indexes = (slideIndexes == null || slideIndexes.Count == 0)
                    ? new List<int> { Math.Max(1, GetCurrentSlideIndex()) }
                    : slideIndexes.Distinct().ToList();
                if (indexes.Count > MaxExportImages) throw new ArgumentException("单次最多导出 " + MaxExportImages + " 页。");
                foreach (var index in indexes)
                {
                    if (index < 1 || index > count) throw new ArgumentOutOfRangeException(nameof(slideIndexes), "页码超出范围：" + index + "（共 " + count + " 页）。");
                }

                var directory = Path.Combine(Path.GetTempPath(), "NixerOfficeCopilot", "ppt-exports");
                Directory.CreateDirectory(directory);
                var exportWidth = width > 0 ? Math.Min(width, 3840) : 1280;
                var exportHeight = height > 0 ? Math.Min(height, 2160) : 720;
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                var result = new List<PowerPointSlideImage>(indexes.Count);
                foreach (var index in indexes)
                {
                    var path = Path.Combine(directory, "slide-" + index + "-" + stamp + ".png");
                    presentation.Slides[index].Export(path, "PNG", exportWidth, exportHeight);
                    result.Add(new PowerPointSlideImage { SlideIndex = index, Path = path, Png = File.ReadAllBytes(path) });
                }

                return result;
            });
        }

        public IList<PowerPointSlideContent> ExtractContent(bool includeNotes)
        {
            return _dispatcher.Invoke(() =>
            {
                var presentation = GetRequiredPresentation();
                var result = new List<PowerPointSlideContent>();
                for (var i = 1; i <= presentation.Slides.Count; i++)
                {
                    var slide = presentation.Slides[i];
                    var content = new PowerPointSlideContent
                    {
                        Index = i,
                        Title = Truncate(GetSlideTitle(slide), TitleLimit),
                        Notes = includeNotes ? Truncate(GetNotesText(slide), NotesTextLimit) : null
                    };

                    var shapes = slide.Shapes;
                    for (var j = 1; j <= shapes.Count; j++)
                    {
                        var text = ReadShapeText(shapes[j]);
                        if (text.Length > 0 && !string.Equals(text, content.Title, StringComparison.Ordinal))
                        {
                            content.Texts.Add(Truncate(text, ShapeTextLimit));
                        }
                    }

                    result.Add(content);
                }

                return result;
            });
        }

        private PowerPointEditResult Edit(int slideIndex, Func<PowerPointInterop.Slide, PowerPointEditResult> action)
        {
            return _dispatcher.Invoke(() =>
            {
                var presentation = GetWritablePresentation();
                var index = slideIndex > 0 ? slideIndex : GetCurrentSlideIndex();
                var count = presentation.Slides.Count;
                if (index < 1 || index > count)
                {
                    throw new ArgumentOutOfRangeException(nameof(slideIndex), "页码超出范围：" + index + "（共 " + count + " 页）。");
                }

                BeginUndo();
                return action(presentation.Slides[index]);
            });
        }

        private PowerPointInterop.Presentation GetWritablePresentation()
        {
            var presentation = GetRequiredPresentation();
            if (ComLate.GetTri(presentation, "ReadOnly")) throw new InvalidOperationException("当前演示文稿为只读状态，无法修改。");
            return presentation;
        }

        private void BeginUndo()
        {
            try { ComLate.Call(_application, "StartNewUndoEntry"); } catch (Exception) { }
        }

        private static PowerPointEditResult Result(string message, PowerPointInterop.Slide slide, params string[] shapeNames)
        {
            var result = new PowerPointEditResult { Message = message };
            result.SlideIndexes.Add(slide.SlideIndex);
            if (shapeNames != null) result.ShapeNames.AddRange(shapeNames);
            return result;
        }

        private static PowerPointInterop.Shape FindShape(PowerPointInterop.Slide slide, string key)
        {
            var trimmed = (key ?? string.Empty).Trim();
            if (trimmed.Length == 0) throw new ArgumentException("未指定形状（名称或 Id）。");
            int id;
            var isId = int.TryParse(trimmed, out id);
            PowerPointInterop.Shape caseInsensitive = null;
            var shapes = slide.Shapes;
            for (var i = 1; i <= shapes.Count; i++)
            {
                var shape = shapes[i];
                if (isId && SafeInt(() => shape.Id) == id) return shape;
                var name = SafeString(() => shape.Name);
                if (string.Equals(name, trimmed, StringComparison.Ordinal)) return shape;
                if (caseInsensitive == null && string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase)) caseInsensitive = shape;
            }

            if (caseInsensitive != null) return caseInsensitive;
            throw new ArgumentException("第 " + slide.SlideIndex + " 页找不到形状「" + trimmed + "」。请先用 ppt_read_slide 查看形状名称/Id。");
        }

        private static List<PowerPointInterop.Shape> FindShapes(PowerPointInterop.Slide slide, IList<string> keys)
        {
            if (keys == null || keys.Count == 0) throw new ArgumentException("未指定形状列表。");
            var result = new List<PowerPointInterop.Shape>();
            foreach (var key in keys)
            {
                var shape = FindShape(slide, key);
                if (!result.Any(s => s.Id == shape.Id)) result.Add(shape);
            }

            return result;
        }

        private static object ShapeRange(PowerPointInterop.Slide slide, List<PowerPointInterop.Shape> shapes)
        {
            var ids = shapes.Select(s => (object)s.Name).ToArray();
            return ComLate.Call(slide.Shapes, "Range", (object)ids);
        }

        private static void Align(PowerPointInterop.Slide slide, List<PowerPointInterop.Shape> shapes, int command, int relative)
        {
            if (shapes.Count < 2 && relative == EditMsoFalse) throw new ArgumentException("对齐至少需要 2 个形状，或设置 relativeToSlide=true。");
            ComLate.Call(ShapeRange(slide, shapes), "Align", command, relative);
        }

        private static void Distribute(PowerPointInterop.Slide slide, List<PowerPointInterop.Shape> shapes, int command, int relative)
        {
            if (shapes.Count < 3 && relative == EditMsoFalse) throw new ArgumentException("分布至少需要 3 个形状，或设置 relativeToSlide=true。");
            ComLate.Call(ShapeRange(slide, shapes), "Distribute", command, relative);
        }

        private static void ZOrder(List<PowerPointInterop.Shape> shapes, int command)
        {
            foreach (var shape in shapes)
            {
                ComLate.Call(shape, "ZOrder", command);
            }
        }

        private static void ApplyFormat(PowerPointInterop.Shape shape, PowerPointShapeFormat format)
        {
            if (format.NoFill == true)
            {
                ComLate.Set(ComLate.Get(shape, "Fill"), "Visible", EditMsoFalse);
            }
            else if (!string.IsNullOrWhiteSpace(format.FillColor))
            {
                var fill = ComLate.Get(shape, "Fill");
                ComLate.Set(fill, "Visible", EditMsoTrue);
                ComLate.Call(fill, "Solid");
                ComLate.Set(ComLate.Get(fill, "ForeColor"), "RGB", ComSlideBuilder.ToOleColor(format.FillColor));
            }

            var line = ComLate.Get(shape, "Line");
            if (format.NoLine == true)
            {
                ComLate.Set(line, "Visible", EditMsoFalse);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(format.LineColor))
                {
                    ComLate.Set(line, "Visible", EditMsoTrue);
                    ComLate.Set(ComLate.Get(line, "ForeColor"), "RGB", ComSlideBuilder.ToOleColor(format.LineColor));
                }

                if (format.LineWidth.HasValue)
                {
                    ComLate.Set(line, "Visible", EditMsoTrue);
                    ComLate.Set(line, "Weight", format.LineWidth.Value);
                }
            }

            if (format.Left.HasValue) shape.Left = format.Left.Value;
            if (format.Top.HasValue) shape.Top = format.Top.Value;
            if (format.Width.HasValue) shape.Width = format.Width.Value;
            if (format.Height.HasValue) shape.Height = format.Height.Value;
            if (format.Rotation.HasValue) shape.Rotation = format.Rotation.Value;

            var hasTextStyle = !string.IsNullOrWhiteSpace(format.FontName) || format.FontSize.HasValue || format.Bold.HasValue
                || format.Italic.HasValue || !string.IsNullOrWhiteSpace(format.FontColor) || !string.IsNullOrWhiteSpace(format.Align);
            if (!hasTextStyle) return;
            if (!ComLate.GetTri(shape, "HasTextFrame")) throw new InvalidOperationException("形状「" + shape.Name + "」不含文本框，无法设置字体。");

            var range = shape.TextFrame.TextRange;
            var font = ComLate.Get(range, "Font");
            if (!string.IsNullOrWhiteSpace(format.FontName))
            {
                ComLate.Set(font, "Name", format.FontName.Trim());
                try { ComLate.Set(font, "NameFarEast", format.FontName.Trim()); } catch (Exception) { }
            }

            if (format.FontSize.HasValue) ComLate.Set(font, "Size", format.FontSize.Value);
            if (format.Bold.HasValue) ComLate.Set(font, "Bold", format.Bold.Value ? EditMsoTrue : EditMsoFalse);
            if (format.Italic.HasValue) ComLate.Set(font, "Italic", format.Italic.Value ? EditMsoTrue : EditMsoFalse);
            if (!string.IsNullOrWhiteSpace(format.FontColor)) ComLate.Set(ComLate.Get(font, "Color"), "RGB", ComSlideBuilder.ToOleColor(format.FontColor));
            if (!string.IsNullOrWhiteSpace(format.Align))
            {
                int alignment;
                switch (format.Align.Trim().ToLowerInvariant())
                {
                    case "left": alignment = 1; break;
                    case "center": alignment = 2; break;
                    case "right": alignment = 3; break;
                    case "justify": alignment = 4; break;
                    default: throw new ArgumentException("不支持的对齐方式：" + format.Align);
                }

                ComLate.Set(ComLate.Get(range, "ParagraphFormat"), "Alignment", alignment);
            }
        }

        private static string NormalizeText(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\r").Replace("\n", "\r");
        }

        private static string ValidateImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new ArgumentException("图片路径必须是本机绝对路径。");
            var fullPath = Path.GetFullPath(path);
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (Array.IndexOf(new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".emf", ".wmf", ".svg" }, extension) < 0)
            {
                throw new ArgumentException("不支持的图片格式：" + extension);
            }

            if (!File.Exists(fullPath)) throw new FileNotFoundException("图片文件不存在。", fullPath);
            return fullPath;
        }

        private static PowerPointInterop.CustomLayout FindLayout(PowerPointInterop.Presentation presentation, string layout)
        {
            if (string.IsNullOrWhiteSpace(layout)) return null;
            var layouts = presentation.SlideMaster.CustomLayouts;
            int index;
            if (int.TryParse(layout.Trim(), out index) && index >= 1 && index <= layouts.Count) return layouts[index];
            for (var i = 1; i <= layouts.Count; i++)
            {
                if (string.Equals(SafeString(() => layouts[i].Name), layout.Trim(), StringComparison.OrdinalIgnoreCase)) return layouts[i];
            }

            return null;
        }

        private static List<string> ListLayoutNames(PowerPointInterop.Presentation presentation)
        {
            var names = new List<string>();
            var layouts = presentation.SlideMaster.CustomLayouts;
            for (var i = 1; i <= layouts.Count; i++)
            {
                names.Add(SafeString(() => layouts[i].Name));
            }

            return names;
        }

        private static string ReadShapeText(PowerPointInterop.Shape shape)
        {
            try
            {
                if (ComLate.GetTri(shape, "HasTable"))
                {
                    var table = shape.Table;
                    var rows = new List<string>();
                    for (var r = 1; r <= table.Rows.Count; r++)
                    {
                        var cells = new List<string>();
                        for (var c = 1; c <= table.Columns.Count; c++)
                        {
                            cells.Add((table.Cell(r, c).Shape.TextFrame.TextRange.Text ?? string.Empty).Trim());
                        }

                        rows.Add(string.Join(" | ", cells));
                    }

                    return string.Join("\n", rows);
                }

                if (ComLate.GetTri(shape, "HasTextFrame") && ComLate.GetTri(shape.TextFrame, "HasText"))
                {
                    return (shape.TextFrame.TextRange.Text ?? string.Empty).Replace("\r", "\n").Trim();
                }
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }

        private static PowerPointFontReport BuildFontReport(PowerPointInterop.Presentation presentation)
        {
            var installed = PptVerifier.GetInstalledFonts();

            var report = new PowerPointFontReport();
            var fonts = ComLate.Get(presentation, "Fonts");
            var count = ComLate.GetInt(fonts, "Count");
            for (var i = 1; i <= count; i++)
            {
                var font = ComLate.Call(fonts, "Item", i);
                var name = Convert.ToString(ComLate.Get(font, "Name")) ?? string.Empty;
                if (name.Length == 0) continue;
                report.Fonts.Add(new PowerPointFontInfo
                {
                    Name = name,
                    Embedded = SafeBool(() => ComLate.GetTri(font, "Embedded")),
                    Embeddable = SafeBool(() => ComLate.GetTri(font, "Embeddable")),
                    Installed = installed.Contains(name) || name.StartsWith("+", StringComparison.Ordinal)
                });
            }

            return report;
        }

        private static string ResolveSaveCopyPath(PowerPointInterop.Presentation presentation, string targetPath, bool embedFonts)
        {
            var saved = PowerPointBackupService.GetSavedPath(presentation);
            string path;
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                if (!Path.IsPathRooted(targetPath)) throw new ArgumentException("目标路径必须是绝对路径。");
                path = Path.GetFullPath(targetPath);
                if (!string.Equals(Path.GetExtension(path), ".pptx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("目标文件扩展名必须是 .pptx。");
            }
            else
            {
                var directory = saved.Length > 0
                    ? Path.GetDirectoryName(saved)
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var baseName = Path.GetFileNameWithoutExtension(saved.Length > 0 ? saved : SafeString(() => presentation.Name));
                if (string.IsNullOrWhiteSpace(baseName)) baseName = "演示文稿";
                path = Path.Combine(directory, baseName + (embedFonts ? "_嵌入字体_" : "_副本_") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".pptx");
            }

            var targetDirectory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory)) throw new DirectoryNotFoundException("目标目录不存在：" + targetDirectory);
            if (saved.Length > 0 && string.Equals(Path.GetFullPath(saved), path, StringComparison.OrdinalIgnoreCase)) throw new IOException("不能覆盖当前文件。");
            if (File.Exists(path)) throw new IOException("目标文件已存在，不会覆盖：" + path);
            return path;
        }
    }
}
