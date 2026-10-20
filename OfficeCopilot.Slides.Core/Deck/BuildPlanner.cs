using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Slides.Deck
{
    public static class SlideBuildModes
    {
        public const string Fidelity = "fidelity";
        public const string Editable = "editable";

        /// <summary>整页图片：每页只放 HTML 完整截图（含文字），不生成原生文字/形状/图标，完全规避字体与形状差异；内容不可编辑。</summary>
        public const string Image = "image";

        public static bool IsValid(string mode)
        {
            return string.Equals(mode, Fidelity, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, Editable, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, Image, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsImage(string mode)
        {
            return string.Equals(mode, Image, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class SlideCapture
    {
        public int Slide { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public double? SafeAreaX { get; set; }

        public double? SafeAreaY { get; set; }

        public string BackgroundImagePath { get; set; }

        public IList<CapturedTextBlock> TextBlocks { get; set; } = new List<CapturedTextBlock>();

        public IList<CapturedImage> Images { get; set; } = new List<CapturedImage>();

        public IList<CapturedShape> Shapes { get; set; } = new List<CapturedShape>();

        public IList<CapturedTable> Tables { get; set; } = new List<CapturedTable>();

        public IList<CapturedChart> Charts { get; set; } = new List<CapturedChart>();

        /// <summary>页内 aside.notes 的纯文本，构建时写入演讲者备注。</summary>
        public string Notes { get; set; }
    }

    public sealed class CapturedRectangle
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }

    /// <summary>字体规划结果：PowerPoint 的拉丁与中日韩字体名，以及共享的粗体/斜体标记。</summary>
    internal sealed class PlannedFont
    {
        public string Name { get; set; }

        public string FarEastName { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }
    }

    public sealed class CapturedTextBlock
    {
        public CapturedRectangle Rectangle { get; set; } = new CapturedRectangle();

        public string Text { get; set; }

        public IList<string> Lines { get; set; } = new List<string>();

        public IList<CapturedTextLine> LineDetails { get; set; } = new List<CapturedTextLine>();

        public string FontFamily { get; set; }

        public double FontSize { get; set; }

        public string FontWeight { get; set; }

        public string FontStyle { get; set; }

        public string Color { get; set; }

        public double LineHeight { get; set; }

        public double LetterSpacing { get; set; }

        public string TextAlign { get; set; }

        public string VerticalAlign { get; set; }

        public double? SafeAreaX { get; set; }

        public double? SafeAreaY { get; set; }
    }

    public sealed class CapturedTextLine
    {
        public string Text { get; set; }

        public CapturedRectangle Rectangle { get; set; } = new CapturedRectangle();
    }

    public sealed class CapturedImage
    {
        public CapturedRectangle Rectangle { get; set; } = new CapturedRectangle();

        public string Source { get; set; }

        public bool OverlayOnText { get; set; }

        public double CropLeft { get; set; }

        public double CropTop { get; set; }

        public double CropRight { get; set; }

        public double CropBottom { get; set; }
    }

    public sealed class CapturedShape
    {
        public CapturedRectangle Rectangle { get; set; } = new CapturedRectangle();

        public string Kind { get; set; }

        public string Fill { get; set; }

        public double FillOpacity { get; set; } = 1;

        public string Stroke { get; set; }

        public double StrokeOpacity { get; set; } = 1;

        public double StrokeWidth { get; set; }

        public double Radius { get; set; }
    }

    public sealed class SlideBuildPlan
    {
        public string Mode { get; set; }

        public int RepairIterationCap { get; set; } = 3;

        public double WidthPoints { get; set; }

        public double HeightPoints { get; set; }

        public double SafeAreaXPoints { get; set; }

        public double SafeAreaYPoints { get; set; }

        public string BackgroundImagePath { get; set; }

        public IList<PlannedTextBlock> TextBlocks { get; set; } = new List<PlannedTextBlock>();

        public IList<PlannedImage> Images { get; set; } = new List<PlannedImage>();

        public IList<PlannedShape> Shapes { get; set; } = new List<PlannedShape>();

        public IList<PlannedTable> Tables { get; set; } = new List<PlannedTable>();

        public IList<PlannedChart> Charts { get; set; } = new List<PlannedChart>();

        public string Notes { get; set; }
    }

    public sealed class PlannedTextBlock
    {
        public CapturedRectangle Rectangle { get; set; }

        public string Text { get; set; }

        public IList<PlannedTextLine> LineDetails { get; set; } = new List<PlannedTextLine>();

        public string FontName { get; set; }

        public string FontNameFarEast { get; set; }

        public double FontSizePoints { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        public string Color { get; set; }

        public double LineHeightPoints { get; set; }

        public double LetterSpacingPoints { get; set; }

        public string TextAlign { get; set; }

        public string VerticalAlign { get; set; }

        public double SafeAreaXPoints { get; set; }

        public double SafeAreaYPoints { get; set; }
    }

    public sealed class PlannedTextLine
    {
        public string Text { get; set; }

        public CapturedRectangle Rectangle { get; set; }
    }

    public sealed class PlannedImage
    {
        public CapturedRectangle Rectangle { get; set; }

        public string Source { get; set; }

        public bool OverlayOnText { get; set; }

        public double CropLeft { get; set; }

        public double CropTop { get; set; }

        public double CropRight { get; set; }

        public double CropBottom { get; set; }
    }

    public sealed class PlannedShape
    {
        public CapturedRectangle Rectangle { get; set; }

        public string Kind { get; set; }

        public string Fill { get; set; }

        public double FillOpacity { get; set; } = 1;

        public string Stroke { get; set; }

        public double StrokeOpacity { get; set; } = 1;

        public double StrokeWidthPoints { get; set; }

        public double RadiusPoints { get; set; }
    }

    /// <summary>纯数据规划：DOM 像素几何转 PowerPoint 磅值，COM 写入由宿主构建器负责。</summary>
    public static class BuildPlanner
    {
        private const double PointsPerPixel = 0.5;
        private const int ShortTextLimit = 80;
        private const double BodyWidenRatio = 0.03;
        private const int MaxTableRows = 20;
        private const int MaxTableColumns = 12;
        private const int MaxChartLabels = 24;
        private const int MaxChartSeries = 8;
        private static readonly HashSet<string> SupportedChartTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bar", "line", "pie", "doughnut"
        };

        /// <summary>Windows 上常见的中日韩字体，字体栈中出现即视为 CJK 字体。</summary>
        private static readonly HashSet<string> SystemCjkFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft YaHei", "Microsoft JhengHei", "SimSun", "NSimSun", "SimHei", "KaiTi", "FangSong",
            "DengXian", "PingFang SC", "Noto Sans CJK SC", "Source Han Sans SC"
        };

        public static SlideBuildPlan Create(SlideCapture capture, string mode, FontCatalog fonts = null)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (capture.Width <= 0 || capture.Height <= 0) throw new ArgumentOutOfRangeException(nameof(capture), "页面像素尺寸必须大于零。");
            if (!SlideBuildModes.IsValid(mode))
            {
                throw new ArgumentException("mode 必须是 fidelity、editable 或 image。", nameof(mode));
            }

            var plan = new SlideBuildPlan
            {
                Mode = mode.ToLowerInvariant(),
                WidthPoints = ToPoints(capture.Width),
                HeightPoints = ToPoints(capture.Height),
                SafeAreaXPoints = ToPoints(ResolveSafeInset(capture.SafeAreaX, capture.Width, 128, nameof(capture.SafeAreaX))),
                SafeAreaYPoints = ToPoints(ResolveSafeInset(capture.SafeAreaY, capture.Height, 96, nameof(capture.SafeAreaY))),
                BackgroundImagePath = capture.BackgroundImagePath,
                Notes = string.IsNullOrWhiteSpace(capture.Notes) ? null : capture.Notes.Trim()
            };

            // 整页图片模式：调用方把完整截图作为 BackgroundImagePath，这里只保留页面尺寸与备注。
            if (plan.Mode == SlideBuildModes.Image)
            {
                return plan;
            }

            foreach (var text in capture.TextBlocks ?? new List<CapturedTextBlock>())
            {
                if (text == null) continue;
                var rectangle = ToPoints(text.Rectangle);
                var lineDetails = text.LineDetails == null ? new List<CapturedTextLine>() : text.LineDetails.Where(line => line != null).ToList();
                var lines = lineDetails.Select(line => line.Text).Where(line => line != null).ToList();
                if (lines.Count == 0) lines = text.Lines == null ? new List<string>() : text.Lines.Where(line => line != null).ToList();
                var hasEmojiOverlay = (capture.Images ?? new List<CapturedImage>()).Any(image =>
                    image != null && image.OverlayOnText && IsInside(text.Rectangle, image.Rectangle));
                var locked = lines.Count > 1 && ((text.Text ?? string.Empty).Length <= ShortTextLimit || hasEmojiOverlay);
                var content = locked
                    ? string.Join("\v", lines)
                    : text.Text ?? string.Join("\n", lines);
                var safeAreaX = ToPoints(ResolveSafeInset(text.SafeAreaX, capture.Width, capture.SafeAreaX ?? 128, nameof(text.SafeAreaX)));
                var safeAreaY = ToPoints(ResolveSafeInset(text.SafeAreaY, capture.Height, capture.SafeAreaY ?? 96, nameof(text.SafeAreaY)));
                if (!locked)
                {
                    // 蓝图 §5.6：正文保持自动换行，加宽 3% 吸收 PowerPoint 与浏览器的字宽差异。
                    rectangle = Widen(rectangle, text.TextAlign, plan.WidthPoints, safeAreaX);
                }
                var font = PlanFont(text.FontFamily, ParseWeight(text.FontWeight, 400), IsItalic(text.FontStyle), fonts);
                plan.TextBlocks.Add(new PlannedTextBlock
                {
                    Rectangle = rectangle,
                    Text = content,
                    LineDetails = lineDetails.Select(line => new PlannedTextLine
                    {
                        Text = line.Text,
                        Rectangle = ToPoints(line.Rectangle)
                    }).ToList(),
                    FontName = font.Name,
                    FontNameFarEast = font.FarEastName,
                    FontSizePoints = ToPoints(text.FontSize),
                    Bold = font.Bold,
                    Italic = font.Italic,
                    Color = text.Color,
                    LineHeightPoints = ToPoints(text.LineHeight),
                    LetterSpacingPoints = ToPoints(text.LetterSpacing),
                    TextAlign = text.TextAlign,
                    VerticalAlign = text.VerticalAlign,
                    SafeAreaXPoints = safeAreaX,
                    SafeAreaYPoints = safeAreaY
                });
            }

            foreach (var image in capture.Images ?? new List<CapturedImage>())
            {
                if (image == null) continue;
                plan.Images.Add(new PlannedImage
                {
                    Rectangle = ToPoints(image.Rectangle),
                    Source = image.Source,
                    OverlayOnText = image.OverlayOnText,
                    CropLeft = image.CropLeft,
                    CropTop = image.CropTop,
                    CropRight = image.CropRight,
                    CropBottom = image.CropBottom
                });
            }

            foreach (var table in capture.Tables ?? new List<CapturedTable>())
            {
                if (table == null) continue;
                var rows = table.Rows ?? new List<CapturedTableRow>();
                if (rows.Count == 0 || rows.Count > MaxTableRows)
                    throw new ArgumentOutOfRangeException(nameof(capture), "原生表格行数必须为 1–" + MaxTableRows + "。");

                var columnCount = rows[0]?.Cells?.Count ?? 0;
                if (columnCount == 0 || columnCount > MaxTableColumns
                    || rows.Any(row => row == null || row.Cells == null || row.Cells.Count != columnCount))
                    throw new ArgumentException("原生表格必须是 1–" + MaxTableColumns + " 列的规则网格。", nameof(capture));

                var columnWidths = table.ColumnWidths ?? new List<double>();
                var hasColumnWidths = columnWidths.Count == columnCount && columnWidths.All(width => width > 0);

                plan.Tables.Add(new PlannedTable
                {
                    Rectangle = ToPoints(table.Rectangle),
                    ColumnWidthsPoints = hasColumnWidths ? columnWidths.Select(width => ToPoints(width)).ToList() : new List<double>(),
                    RowHeightsPoints = rows.Select(row => ToPoints(row.Height)).ToList(),
                    Rows = rows.Select(row => (IList<PlannedTableCell>)row.Cells.Select(cell => PlanTableCell(cell ?? new CapturedTableCell(), fonts)).ToList()).ToList()
                });

            }

            foreach (var chart in capture.Charts ?? new List<CapturedChart>())
            {
                if (chart == null) continue;
                var type = (chart.Type ?? string.Empty).Trim().ToLowerInvariant();
                if (!SupportedChartTypes.Contains(type))
                    throw new ArgumentException("不支持的原生图表类型：" + chart.Type + "。", nameof(capture));
                if (chart.Labels == null || chart.Labels.Count == 0 || chart.Labels.Count > MaxChartLabels
                    || chart.Series == null || chart.Series.Count == 0 || chart.Series.Count > MaxChartSeries
                    || ((type == "pie" || type == "doughnut") && chart.Series.Count != 1)
                    || chart.Series.Any(series => series == null || series.Values == null || series.Values.Count != chart.Labels.Count
                        || series.Values.Any(value => double.IsNaN(value) || double.IsInfinity(value))))
                    throw new ArgumentException("原生图表需要 1–" + MaxChartLabels + " 个标签以及 1–" + MaxChartSeries + " 组等长有限数值。", nameof(capture));

                plan.Charts.Add(new PlannedChart
                {
                    Rectangle = ToPoints(chart.Rectangle),
                    Type = type,
                    Title = chart.Title,
                    Labels = chart.Labels.Select(label => label ?? string.Empty).ToList(),
                    Series = chart.Series.Select(series => new PlannedChartSeries
                    {
                        Name = series.Name,
                        Values = series.Values.ToList(),
                        Color = series.Color
                    }).ToList()
                });
            }

            if (plan.Mode == SlideBuildModes.Editable)
            {
                foreach (var shape in capture.Shapes ?? new List<CapturedShape>())
                {
                    if (shape == null || !IsNativeShape(shape.Kind)) continue;
                    plan.Shapes.Add(new PlannedShape
                    {
                        Rectangle = ToPoints(shape.Rectangle),
                        Kind = shape.Kind,
                        Fill = shape.Fill,
                        FillOpacity = Math.Max(0, Math.Min(1, shape.FillOpacity)),
                        Stroke = shape.Stroke,
                        StrokeOpacity = Math.Max(0, Math.Min(1, shape.StrokeOpacity)),
                        StrokeWidthPoints = ToPoints(shape.StrokeWidth),
                        RadiusPoints = ToPoints(shape.Radius)
                    });
                }
            }

            return plan;
        }

        private static PlannedTableCell PlanTableCell(CapturedTableCell cell, FontCatalog fonts)
        {
            var padding = cell.Padding ?? new CapturedTableSpacing();
            var borders = cell.Borders ?? new CapturedTableBorders();
            var font = PlanFont(cell.FontFamily, ParseWeight(cell.FontWeight, cell.Bold ? 700 : 400), IsItalic(cell.FontStyle), fonts);
            return new PlannedTableCell
            {
                Text = cell.Text ?? string.Empty,
                Fill = cell.Fill,
                Color = cell.Color,
                FontName = font.Name,
                FontNameFarEast = font.FarEastName,
                FontSizePoints = cell.FontSize > 0 ? ToPoints(cell.FontSize) : 0,
                Bold = font.Bold,
                Italic = font.Italic,
                TextAlign = cell.TextAlign,
                LineHeightPoints = ToPoints(cell.LineHeight),
                VerticalAlign = cell.VerticalAlign,
                MarginPoints = new PlannedTableSpacing
                {
                    Top = ToPoints(padding.Top),
                    Right = ToPoints(padding.Right),
                    Bottom = ToPoints(padding.Bottom),
                    Left = ToPoints(padding.Left)
                },
                Borders = new PlannedTableBorders
                {
                    Top = PlanTableBorder(borders.Top),
                    Right = PlanTableBorder(borders.Right),
                    Bottom = PlanTableBorder(borders.Bottom),
                    Left = PlanTableBorder(borders.Left)
                }
            };
        }

        private static PlannedTableBorder PlanTableBorder(CapturedTableBorder border)
        {
            border = border ?? new CapturedTableBorder();
            return new PlannedTableBorder { WidthPoints = ToPoints(border.Width), Color = border.Color };
        }

        private static CapturedRectangle ToPoints(CapturedRectangle rectangle)
        {
            if (rectangle == null) throw new ArgumentException("元素缺少矩形几何信息。");
            if (rectangle.Width <= 0 || rectangle.Height <= 0) throw new ArgumentOutOfRangeException(nameof(rectangle), "元素宽高必须大于零。");
            return new CapturedRectangle
            {
                Left = ToPoints(rectangle.Left),
                Top = ToPoints(rectangle.Top),
                Width = ToPoints(rectangle.Width),
                Height = ToPoints(rectangle.Height)
            };
        }

        private static CapturedRectangle Widen(CapturedRectangle rectangle, string textAlign, double slideWidth, double safeInset)
        {
            var delta = rectangle.Width * BodyWidenRatio;
            var right = rectangle.Left + rectangle.Width;
            var safeRight = slideWidth - safeInset;
            if (rectangle.Left < safeInset || right > safeRight) return rectangle;

            var left = rectangle.Left;
            if (string.Equals(textAlign, "center", StringComparison.OrdinalIgnoreCase))
            {
                delta = Math.Min(delta, 2 * Math.Min(rectangle.Left - safeInset, safeRight - right));
                left -= delta / 2;
            }
            else if (string.Equals(textAlign, "right", StringComparison.OrdinalIgnoreCase))
            {
                delta = Math.Min(delta, rectangle.Left - safeInset);
                left -= delta;
            }
            else
            {
                delta = Math.Min(delta, safeRight - right);
            }

            if (delta <= 0) return rectangle;
            var width = rectangle.Width + delta;
            return new CapturedRectangle { Left = left, Top = rectangle.Top, Width = width, Height = rectangle.Height };
        }

        private static double ResolveSafeInset(double? configured, double dimension, double fallback, string name)
        {
            var inset = configured ?? fallback;
            if (double.IsNaN(inset) || double.IsInfinity(inset) || inset < 0 || inset * 2 >= dimension)
            {
                throw new ArgumentOutOfRangeException(name, "安全区边距必须非负且小于页面尺寸的一半。");
            }
            return inset;
        }

        private static double ToPoints(double pixels)
        {
            return pixels * PointsPerPixel;
        }

        private static bool IsInside(CapturedRectangle outer, CapturedRectangle inner)
        {
            return outer != null && inner != null
                && inner.Left >= outer.Left && inner.Top >= outer.Top
                && inner.Left + inner.Width <= outer.Left + outer.Width
                && inner.Top + inner.Height <= outer.Top + outer.Height;
        }

        /// <summary>
        /// 按浏览器的字体回退规则确定 PowerPoint 字体：拉丁字符取字体栈中第一个可用族，
        /// 中日韩字符取字体栈中第一个 CJK 族（没有则使用系统默认 CJK 回退字体），
        /// 字重与斜体按 CSS 规则选面，与预览所用的字体文件保持一致。
        /// </summary>
        private static PlannedFont PlanFont(string familyStack, int weight, bool italic, FontCatalog fonts)
        {
            var families = ParseFamilies(familyStack);
            var latin = families.FirstOrDefault(name => fonts != null && fonts.Contains(name) && !fonts.IsEmojiOnly(name))
                ?? families.FirstOrDefault(name => fonts == null || !fonts.IsEmojiOnly(name))
                ?? "Arial";
            var cjk = families.FirstOrDefault(name => IsCjkFamily(name, fonts)) ?? DefaultCjkFamily(familyStack);
            var latinFace = ResolveFace(latin, weight, italic, fonts);
            var cjkFace = ResolveFace(cjk, weight, italic, fonts);
            return new PlannedFont
            {
                Name = latinFace.Name,
                FarEastName = cjkFace.FarEastName ?? cjkFace.Name,
                Bold = latinFace.Bold,
                Italic = latinFace.Italic
            };
        }

        private static PowerPointFace ResolveFace(string family, int weight, bool italic, FontCatalog fonts)
        {
            var face = string.IsNullOrEmpty(family) || fonts == null ? null : fonts.SelectFace(family, weight, italic);
            return face ?? new PowerPointFace { Name = family, Bold = weight >= 600, Italic = italic };
        }

        private static IList<string> ParseFamilies(string familyStack)
        {
            if (string.IsNullOrWhiteSpace(familyStack)) return new List<string>();
            return familyStack.Split(',')
                .Select(name => name.Trim().Trim('"', '\''))
                .Where(name => name.Length > 0 && !FontPolicy.IsGenericFamily(name))
                .ToList();
        }

        private static bool IsCjkFamily(string family, FontCatalog fonts)
        {
            return (fonts != null && fonts.IsCjk(family)) || SystemCjkFamilies.Contains(family);
        }

        private static string DefaultCjkFamily(string familyStack)
        {
            var stack = familyStack ?? string.Empty;
            if (stack.IndexOf("sans-serif", StringComparison.OrdinalIgnoreCase) < 0
                && stack.IndexOf("serif", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SimSun";
            }

            return "Microsoft YaHei";
        }

        private static bool IsItalic(string fontStyle)
        {
            return !string.IsNullOrWhiteSpace(fontStyle)
                && (fontStyle.StartsWith("italic", StringComparison.OrdinalIgnoreCase)
                    || fontStyle.StartsWith("oblique", StringComparison.OrdinalIgnoreCase));
        }

        private static int ParseWeight(string weight, int fallback)
        {
            if (string.IsNullOrWhiteSpace(weight)) return fallback;
            if (string.Equals(weight, "bold", StringComparison.OrdinalIgnoreCase)) return 700;
            if (string.Equals(weight, "normal", StringComparison.OrdinalIgnoreCase)) return 400;
            int numeric;
            return int.TryParse(weight, out numeric) ? numeric : fallback;
        }

        private static bool IsNativeShape(string kind)
        {
            return string.Equals(kind, "rect", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "rounded-rect", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "line", StringComparison.OrdinalIgnoreCase);
        }
    }
}
