using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Office.Interop.PowerPoint;
using PowerPointInterop = Microsoft.Office.Interop.PowerPoint;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Host.PowerPoint
{
    public sealed class PptVerificationIssue
    {
        public string Code { get; set; }

        public int Slide { get; set; }

        public string ShapeName { get; set; }

        public string Message { get; set; }

        public string Expected { get; set; }

        public string Actual { get; set; }
    }

    public sealed class PptVerificationReport
    {
        public int TextBlocksChecked { get; set; }

        public int VisualDiffSlidesChecked { get; set; }

        public IList<PptVerificationIssue> Issues { get; set; } = new List<PptVerificationIssue>();

        /// <summary>L3 自动修复记录（蓝图 §5.5.4）：加宽 / 字距 / 字号。</summary>
        public IList<PptVerificationFix> Fixes { get; set; } = new List<PptVerificationFix>();
    }

    public sealed class PptVerificationFix
    {
        public int Slide { get; set; }

        public string ShapeName { get; set; }

        public string Action { get; set; }
    }

    /// <summary>COM 回读文本框几何与字体，并比较 PowerPoint 导出图像。</summary>
    internal static class PptVerifier
    {
        private const double OverflowTolerancePoints = 2;
        private const int DiffWidth = 480;
        private const int DiffHeight = 270;
        private const int DiffGridSize = 8;
        private const double TileDifferenceThreshold = 24;
        private const int MaximumDifferentTiles = 8;
        private const double WidenRatio = 0.03;
        private const float SpacingTightenPoints = 0.5f;

        /// <summary>PowerPoint「减小字号」的档位。</summary>
        private static readonly float[] FontSizeSteps = { 8, 9, 10, 10.5f, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 44, 48, 54, 60, 66, 72, 80, 88, 96 };

        public static PptVerificationReport Verify(
            PowerPointInterop.Presentation presentation,
            IList<int> slideIndexes,
            IList<PowerPointDeckBuildSlide> inputs)
        {
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));
            if (slideIndexes == null) throw new ArgumentNullException(nameof(slideIndexes));
            if (inputs == null || slideIndexes.Count != inputs.Count) throw new ArgumentException("验证页与构建计划数量不一致。");

            var report = new PptVerificationReport();
            var slideWidth = presentation.PageSetup.SlideWidth;
            var installedFonts = GetInstalledFonts();
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                var slideIndex = slideIndexes[inputIndex];
                if (slideIndex < 1 || slideIndex > presentation.Slides.Count)
                {
                    report.Issues.Add(new PptVerificationIssue
                    {
                        Code = "ppt.slide_missing",
                        Slide = slideIndex,
                        Message = "构建后找不到对应的 PowerPoint 页面。"
                    });
                    continue;
                }

                var slide = presentation.Slides[slideIndex];
                var input = inputs[inputIndex];
                for (var textIndex = 0; textIndex < input.Plan.TextBlocks.Count; textIndex++)
                {
                    report.TextBlocksChecked++;
                    var expected = input.Plan.TextBlocks[textIndex];
                    var shapeName = ComSlideBuilder.TextShapeName(textIndex + 1);
                    var shape = FindShape(slide, shapeName);
                    if (shape == null)
                    {
                        report.Issues.Add(new PptVerificationIssue
                        {
                            Code = "ppt.text_missing",
                            Slide = slideIndex,
                            ShapeName = shapeName,
                            Message = "构建后缺少计划中的原生文本框。"
                        });
                        continue;
                    }

                    VerifyTextShape(report, slideIndex, shape, shapeName, expected, slideWidth, installedFonts, input.Plan.RepairIterationCap);
                }

                VerifyVisualDiff(report, slide, slideIndex, input);
            }

            return report;
        }

        private static void VerifyVisualDiff(PptVerificationReport report, PowerPointInterop.Slide slide, int slideIndex, PowerPointDeckBuildSlide input)
        {
            if (string.IsNullOrWhiteSpace(input.CaptureImagePath) || !File.Exists(input.CaptureImagePath))
            {
                report.Issues.Add(new PptVerificationIssue
                {
                    Code = "ppt.visual_diff_failed",
                    Slide = slideIndex,
                    Message = "找不到对应的 HTML 采集截图，无法执行视觉比对。"
                });
                return;
            }

            var exportedPath = Path.Combine(Path.GetTempPath(), "nixer-ppt-visual-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                slide.Export(exportedPath, "PNG", DiffWidth, DiffHeight);
                var differentTiles = CountDifferentTiles(input.CaptureImagePath, exportedPath);
                report.VisualDiffSlidesChecked++;
                if (differentTiles > MaximumDifferentTiles)
                {
                    report.Issues.Add(new PptVerificationIssue
                    {
                        Code = "ppt.visual_diff",
                        Slide = slideIndex,
                        Message = "PowerPoint 与 HTML 采集图的灰度差异超过阈值。",
                        Expected = "差异格 ≤ " + MaximumDifferentTiles.ToString(CultureInfo.InvariantCulture) + "/64",
                        Actual = differentTiles.ToString(CultureInfo.InvariantCulture) + "/64"
                    });
                }
            }
            catch (Exception ex)
            {
                report.Issues.Add(new PptVerificationIssue
                {
                    Code = "ppt.visual_diff_failed",
                    Slide = slideIndex,
                    Message = "视觉像素差异检查失败：" + ex.Message
                });
            }
            finally
            {
                try
                {
                    if (File.Exists(exportedPath)) File.Delete(exportedPath);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static int CountDifferentTiles(string capturePath, string exportedPath)
        {
            int[] expectedGray;
            int[] actualGray;
            using (var expected = new Bitmap(capturePath))
            using (var actual = new Bitmap(exportedPath))
            {
                expectedGray = ToGrayPixels(expected);
                actualGray = ToGrayPixels(actual);
            }

            var differentTiles = 0;
            for (var gridY = 0; gridY < DiffGridSize; gridY++)
            {
                var top = gridY * DiffHeight / DiffGridSize;
                var bottom = (gridY + 1) * DiffHeight / DiffGridSize;
                for (var gridX = 0; gridX < DiffGridSize; gridX++)
                {
                    var left = gridX * DiffWidth / DiffGridSize;
                    var right = (gridX + 1) * DiffWidth / DiffGridSize;
                    long difference = 0;
                    var pixels = (right - left) * (bottom - top);
                    for (var y = top; y < bottom; y++)
                    {
                        var row = y * DiffWidth;
                        for (var x = left; x < right; x++)
                        {
                            difference += Math.Abs(expectedGray[row + x] - actualGray[row + x]);
                        }
                    }

                    if ((double)difference / pixels > TileDifferenceThreshold) differentTiles++;
                }
            }

            return differentTiles;
        }

        /// <summary>缩放到 DiffWidth×DiffHeight 并一次性 LockBits 读出灰度（避免逐像素 GetPixel）。</summary>
        private static int[] ToGrayPixels(System.Drawing.Image source)
        {
            using (var normalized = new Bitmap(DiffWidth, DiffHeight, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(normalized))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, 0, 0, DiffWidth, DiffHeight);
                }

                var data = normalized.LockBits(new System.Drawing.Rectangle(0, 0, DiffWidth, DiffHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var bytes = new byte[data.Stride * DiffHeight];
                    Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                    var gray = new int[DiffWidth * DiffHeight];
                    for (var y = 0; y < DiffHeight; y++)
                    {
                        var offset = y * data.Stride;
                        for (var x = 0; x < DiffWidth; x++)
                        {
                            var i = offset + x * 4; // BGRA
                            gray[y * DiffWidth + x] = (bytes[i + 2] * 299 + bytes[i + 1] * 587 + bytes[i] * 114) / 1000;
                        }
                    }

                    return gray;
                }
                finally
                {
                    normalized.UnlockBits(data);
                }
            }
        }

        private static void VerifyTextShape(
            PptVerificationReport report,
            int slideIndex,
            PowerPointInterop.Shape shape,
            string shapeName,
            PlannedTextBlock expected,
            float slideWidth,
            ISet<string> installedFonts,
            int repairIterationCap)
        {
            var frame = ComLate.Get(shape, "TextFrame2");
            var range = ComLate.Get(frame, "TextRange");
            var font = ComLate.Get(range, "Font");
            var expectedLines = expected.LineDetails == null ? 0 : expected.LineDetails.Count;

            // 蓝图 §5.5.4：溢出或重排时依次加宽 3% → 收紧字距 → 降 1 级字号，每步后重测。
            var measure = Measure(shape, range);
            for (var step = 0; step < Math.Max(0, repairIterationCap) && measure.NeedsFix(shape, expectedLines); step++)
            {
                var action = step == 0 ? Widen(shape, expected.TextAlign, slideWidth, (float)expected.SafeAreaXPoints)
                    : step == 1 ? TightenSpacing(font)
                    : ShrinkFont(font);
                if (action == null) continue;
                report.Fixes.Add(new PptVerificationFix { Slide = slideIndex, ShapeName = shapeName, Action = action });
                measure = Measure(shape, range);
            }

            if (measure.Height.HasValue && measure.Height.Value > shape.Height + OverflowTolerancePoints)
            {
                report.Issues.Add(new PptVerificationIssue
                {
                    Code = "ppt.text_overflow",
                    Slide = slideIndex,
                    ShapeName = shapeName,
                    Message = "文本实际高度超过文本框高度。",
                    Expected = "≤ " + shape.Height.ToString("0.##", CultureInfo.InvariantCulture) + " pt",
                    Actual = measure.Height.Value.ToString("0.##", CultureInfo.InvariantCulture) + " pt"
                });
            }

            if (expectedLines > 0 && measure.Lines.HasValue && measure.Lines.Value > expectedLines)
            {
                report.Issues.Add(new PptVerificationIssue
                {
                    Code = "ppt.line_reflow",
                    Slide = slideIndex,
                    ShapeName = shapeName,
                    Message = "PowerPoint 换行与 HTML 采集的行数不一致。",
                    Expected = expectedLines.ToString(CultureInfo.InvariantCulture) + " 行",
                    Actual = measure.Lines.Value.ToString(CultureInfo.InvariantCulture) + " 行"
                });
            }

            VerifyFont(report, slideIndex, shapeName, "Font.Name", expected.FontName, ReadString(font, "Name"), installedFonts);
            var expectedFarEast = expected.FontNameFarEast ?? expected.FontName;
            if (!string.Equals(expectedFarEast, expected.FontName, StringComparison.OrdinalIgnoreCase))
            {
                VerifyFarEastFont(report, slideIndex, shapeName, range, expected.Text, expectedFarEast, installedFonts);
            }
        }

        private static void VerifyFarEastFont(
            PptVerificationReport report,
            int slideIndex,
            string shapeName,
            object textRange,
            string text,
            string expected,
            ISet<string> installedFonts)
        {
            foreach (var textRun in CjkTextRanges.Find(text))
            {
                object characters = null;
                object font = null;
                try
                {
                    characters = ComLate.Get(textRange, "Characters", textRun.Start + 1, textRun.Length);
                    font = ComLate.Get(characters, "Font");
                    VerifyFont(report, slideIndex, shapeName, "Font.NameFarEast", expected, ReadString(font, "NameFarEast"), installedFonts);
                }
                finally
                {
                    ReleaseComObject(font);
                    ReleaseComObject(characters);
                }
            }
        }

        private struct TextMeasure
        {
            public double? Height;
            public int? Lines;

            public bool NeedsFix(PowerPointInterop.Shape shape, int expectedLines)
            {
                return (Height.HasValue && Height.Value > shape.Height + OverflowTolerancePoints)
                    || (expectedLines > 0 && Lines.HasValue && Lines.Value > expectedLines);
            }
        }

        private static TextMeasure Measure(PowerPointInterop.Shape shape, object range)
        {
            int? lines = null;
            try
            {
                // TextRange2.Lines(Start=-1, Length=-1) 返回全部行。
                var all = ComLate.Call(range, "Lines", -1, -1);
                var count = ComLate.GetInt(all, "Count", -1);
                if (count >= 0) lines = count;
            }
            catch (Exception ex) when (ex is COMException || ex is MissingMemberException)
            {
            }

            return new TextMeasure { Height = ReadDouble(range, "BoundHeight"), Lines = lines };
        }

        private static string Widen(PowerPointInterop.Shape shape, string textAlign, float slideWidth, float safeInset)
        {
            var delta = (float)(shape.Width * WidenRatio);
            var right = shape.Left + shape.Width;
            var safeRight = slideWidth - safeInset;
            if (shape.Left < safeInset || right > safeRight) return null;

            var left = shape.Left;
            if (string.Equals(textAlign, "center", StringComparison.OrdinalIgnoreCase))
            {
                delta = Math.Min(delta, 2f * Math.Min(shape.Left - safeInset, safeRight - right));
                left -= delta / 2;
            }
            else if (string.Equals(textAlign, "right", StringComparison.OrdinalIgnoreCase))
            {
                delta = Math.Min(delta, shape.Left - safeInset);
                left -= delta;
            }
            else
            {
                delta = Math.Min(delta, safeRight - right);
            }

            if (delta <= 0f) return null;
            var width = shape.Width + delta;
            if (width <= shape.Width + 0.1f) return null;
            shape.Left = left;
            shape.Width = width;
            return "文本框加宽至 " + width.ToString("0.##", CultureInfo.InvariantCulture) + " pt";
        }

        private static string TightenSpacing(object font)
        {
            var current = ReadDouble(font, "Spacing") ?? 0;
            var next = (float)current - SpacingTightenPoints;
            ComLate.Set(font, "Spacing", next);
            return "字距收紧至 " + next.ToString("0.##", CultureInfo.InvariantCulture) + " pt";
        }

        private static string ShrinkFont(object font)
        {
            var current = ReadDouble(font, "Size");
            if (!current.HasValue || current.Value <= 0) return null;
            var next = NextSmallerFontSize((float)current.Value);
            ComLate.Set(font, "Size", next);
            return "字号从 " + current.Value.ToString("0.##", CultureInfo.InvariantCulture) + " 降至 " + next.ToString("0.##", CultureInfo.InvariantCulture) + " pt";
        }

        internal static float NextSmallerFontSize(float size)
        {
            for (var i = FontSizeSteps.Length - 1; i >= 0; i--)
            {
                if (FontSizeSteps[i] < size - 0.01f) return FontSizeSteps[i];
            }

            return Math.Max(1f, size - 1f);
        }

        internal static ISet<string> GetInstalledFonts()
        {
            return InstalledFontState.RegisteredFontNames();
        }

        private static void VerifyFont(PptVerificationReport report, int slideIndex, string shapeName, string property, string expected, string actual, ISet<string> installedFonts)
        {
            if (string.IsNullOrWhiteSpace(expected)) return;

            string message = null;
            if (string.IsNullOrWhiteSpace(actual))
            {
                message = property + " 无法从 PowerPoint 回读。";
            }
            else if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                message = property + " 与 HTML 采集结果不一致。";
            }
            else if (!IsInstalledFontName(expected, installedFonts) && !FontPolicy.IsGenericFamily(expected))
            {
                message = property + " 不匹配 Windows 注册字体字典中的名称，PowerPoint 可能以替代字体显示。";
            }

            if (message == null) return;
            report.Issues.Add(new PptVerificationIssue
            {
                Code = "ppt.font_substituted",
                Slide = slideIndex,
                ShapeName = shapeName,
                Message = message,
                Expected = expected,
                Actual = actual
            });
        }

        internal static bool IsInstalledFontName(string expected, ISet<string> installedFonts)
        {
            return !string.IsNullOrWhiteSpace(expected)
                && installedFonts != null
                && installedFonts.Contains(expected);
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        private static PowerPointInterop.Shape FindShape(PowerPointInterop.Slide slide, string name)
        {
            var shapes = slide.Shapes;
            for (var index = 1; index <= shapes.Count; index++)
            {
                var shape = shapes[index];
                if (string.Equals(shape.Name, name, StringComparison.Ordinal)) return shape;
            }

            return null;
        }

        private static double? ReadDouble(object target, string property)
        {
            try
            {
                var value = ComLate.Get(target, property);
                return value == null ? (double?)null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ReadString(object target, string property)
        {
            try
            {
                return Convert.ToString(ComLate.Get(target, property), CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
