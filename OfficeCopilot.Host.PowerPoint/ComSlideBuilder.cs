using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using OfficeCopilot.Slides.Deck;
using PowerPointInterop = Microsoft.Office.Interop.PowerPoint;

namespace OfficeCopilot.Host.PowerPoint
{
    internal static class ComSlideBuilder
    {
        private const int MsoFalse = 0;
        private const int MsoTrue = -1;
        private const int MsoShapeRectangle = 1;
        private const int MsoShapeRoundedRectangle = 5;
        private const int MsoTextOrientationHorizontal = 1;
        private const int MsoAutoSizeNone = 0;
        private const int MsoZOrderSendToBack = 1;
        private const int MsoAnchorTop = 1;
        private const int MsoAnchorMiddle = 3;
        private const int MsoAnchorBottom = 4;
        private const int PpBorderTop = 1;
        private const int PpBorderLeft = 2;
        private const int PpBorderBottom = 3;
        private const int PpBorderRight = 4;
        private const string NoStyleNoGridTableStyle = "{2D5ABB26-0587-4C30-8999-92F81FD0307C}";
        private const int XlColumnClustered = 51;
        private const int XlLine = 4;
        private const int XlPie = 5;
        private const int XlDoughnut = -4120;
        private const int XlColumns = 2;

        private static readonly Regex RgbColor = new Regex(
            @"^rgba?\(\s*(?<r>\d+)\s*,\s*(?<g>\d+)\s*,\s*(?<b>\d+)(?:\s*,\s*[\d.]+)?\s*\)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Append(PowerPointInterop.Slide slide, PowerPointDeckBuildSlide input)
        {
            if (slide == null) throw new ArgumentNullException(nameof(slide));
            if (input == null || input.Plan == null) throw new ArgumentException("构建页缺少计划。", nameof(input));
            if (input.ImagePaths == null || input.ImagePaths.Count != input.Plan.Images.Count)
            {
                throw new ArgumentException("图片路径与构建计划数量不一致。", nameof(input));
            }

            var plan = input.Plan;
            if (!string.IsNullOrWhiteSpace(plan.BackgroundImagePath))
            {
                var background = (PowerPointInterop.Shape)ComLate.Call(
                    slide.Shapes,
                    "AddPicture",
                    plan.BackgroundImagePath,
                    MsoFalse,
                    MsoTrue,
                    0,
                    0,
                    (float)plan.WidthPoints,
                    (float)plan.HeightPoints);
                background.Name = "nixer.bg";
                ComLate.Call(background, "ZOrder", MsoZOrderSendToBack);
            }

            for (var index = 0; index < plan.Shapes.Count; index++)
            {
                AddShape(slide, plan.Shapes[index], index + 1);
            }

            for (var index = 0; index < plan.Images.Count; index++)
            {
                if (!plan.Images[index].OverlayOnText)
                {
                    AddImage(slide, plan.Images[index], input.ImagePaths[index], index + 1);
                }
            }

            for (var index = 0; index < plan.Tables.Count; index++)
            {
                AddTable(slide, plan.Tables[index], index + 1);
            }

            for (var index = 0; index < plan.Charts.Count; index++)
            {
                AddChart(slide, plan.Charts[index], index + 1);
            }

            for (var index = 0; index < plan.TextBlocks.Count; index++)
            {
                AddText(slide, plan.TextBlocks[index], index + 1);
            }

            for (var index = 0; index < plan.Images.Count; index++)
            {
                if (plan.Images[index].OverlayOnText)
                {
                    AddImage(slide, plan.Images[index], input.ImagePaths[index], index + 1);
                }
            }

            if (!string.IsNullOrWhiteSpace(plan.Notes))
            {
                var body = FindNotesBody(slide);
                if (body != null) body.TextFrame.TextRange.Text = plan.Notes.Replace("\r\n", "\r").Replace('\n', '\r');
            }

            // 蓝图 §5.1：页级标签记录工作区身份，供重开工作区与追溯。
            SetTag(slide.Tags, DeckSlideTag, input.SlideKey);
            SetTag(slide.Tags, DeckHashTag, input.Hash);
        }

        public const string DeckTag = "NIXER.DECK";
        public const string DeckSlideTag = "NIXER.SLIDE";
        public const string DeckHashTag = "NIXER.HASH";

        internal static void SetTag(PowerPointInterop.Tags tags, string name, string value)
        {
            if (tags == null || string.IsNullOrWhiteSpace(value)) return;
            tags.Add(name, value);
        }

        internal static string GetTag(PowerPointInterop.Tags tags, string name)
        {
            try
            {
                var value = tags == null ? null : tags[name];
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        /// <summary>备注页的正文占位符；不存在时返回 null。</summary>
        internal static PowerPointInterop.Shape FindNotesBody(PowerPointInterop.Slide slide)
        {
            var placeholders = slide.NotesPage.Shapes.Placeholders;
            for (var i = 1; i <= placeholders.Count; i++)
            {
                var placeholder = placeholders[i];
                if (placeholder.PlaceholderFormat.Type == PowerPointInterop.PpPlaceholderType.ppPlaceholderBody)
                {
                    return placeholder;
                }
            }

            return null;
        }

        public static string TextShapeName(int index)
        {
            return "nixer.text." + index.ToString("D2", CultureInfo.InvariantCulture);
        }

        private static void AddShape(PowerPointInterop.Slide slide, PlannedShape shape, int index)
        {
            var rect = shape.Rectangle;
            PowerPointInterop.Shape result;
            if (string.Equals(shape.Kind, "line", StringComparison.OrdinalIgnoreCase))
            {
                result = slide.Shapes.AddLine(
                    (float)rect.Left,
                    (float)rect.Top,
                    (float)(rect.Left + rect.Width),
                    (float)(rect.Top + rect.Height));
            }
            else
            {
                var type = string.Equals(shape.Kind, "rounded-rect", StringComparison.OrdinalIgnoreCase)
                    ? MsoShapeRoundedRectangle
                    : MsoShapeRectangle;
                result = (PowerPointInterop.Shape)ComLate.Call(
                    slide.Shapes,
                    "AddShape",
                    type,
                    (float)rect.Left,
                    (float)rect.Top,
                    (float)rect.Width,
                    (float)rect.Height);
                SetFill(result, shape.Fill, shape.FillOpacity);
            }

            result.Name = "nixer.shape." + index.ToString(CultureInfo.InvariantCulture);
            var hasStroke = shape.StrokeWidthPoints > 0
                && shape.StrokeOpacity > 0
                && !string.Equals(shape.Stroke, "transparent", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(shape.Stroke);
            ComLate.Set(result.Line, "Visible", hasStroke ? MsoTrue : MsoFalse);
            if (!hasStroke) return;

            result.Line.ForeColor.RGB = ToOleColor(shape.Stroke);
            result.Line.Weight = (float)shape.StrokeWidthPoints;
            if (shape.StrokeOpacity < 1) ComLate.Set(result.Line, "Transparency", (float)(1 - shape.StrokeOpacity));
        }

        private static void AddImage(PowerPointInterop.Slide slide, PlannedImage image, string path, int index)
        {
            var rect = image.Rectangle;
            var shape = (PowerPointInterop.Shape)ComLate.Call(
                slide.Shapes,
                "AddPicture",
                path,
                MsoFalse,
                MsoTrue,
                (float)rect.Left,
                (float)rect.Top,
                (float)rect.Width,
                (float)rect.Height);
            shape.Name = (image.OverlayOnText ? "nixer.overlay." : "nixer.image.")
                + index.ToString(CultureInfo.InvariantCulture);
            shape.PictureFormat.CropLeft = (float)(image.CropLeft * rect.Width);
            shape.PictureFormat.CropTop = (float)(image.CropTop * rect.Height);
            shape.PictureFormat.CropRight = (float)(image.CropRight * rect.Width);
            shape.PictureFormat.CropBottom = (float)(image.CropBottom * rect.Height);
        }

        private static void AddText(PowerPointInterop.Slide slide, PlannedTextBlock text, int index)
        {
            var rect = text.Rectangle;
            var shape = (PowerPointInterop.Shape)ComLate.Call(
                slide.Shapes,
                "AddTextbox",
                MsoTextOrientationHorizontal,
                (float)rect.Left,
                (float)rect.Top,
                (float)rect.Width,
                (float)rect.Height);
            shape.Name = TextShapeName(index);
            ComLate.Set(shape.Fill, "Visible", MsoFalse);
            ComLate.Set(shape.Line, "Visible", MsoFalse);

            var frame = ComLate.Get(shape, "TextFrame2");
            ComLate.Set(frame, "MarginLeft", 0f);
            ComLate.Set(frame, "MarginRight", 0f);
            ComLate.Set(frame, "MarginTop", 0f);
            ComLate.Set(frame, "MarginBottom", 0f);
            ComLate.Set(frame, "AutoSize", MsoAutoSizeNone);
            ComLate.Set(frame, "WordWrap", MsoTrue);
            ComLate.Set(frame, "VerticalAnchor", TableAnchorFor(text.VerticalAlign));
            var range = ComLate.Get(frame, "TextRange");
            ComLate.Set(range, "Text", text.Text ?? string.Empty);
            var font = ComLate.Get(range, "Font");
            if (!string.IsNullOrWhiteSpace(text.FontName)) ComLate.Set(font, "Name", text.FontName);
            var farEast = text.FontNameFarEast ?? text.FontName;
            ComLate.Set(font, "Size", (float)text.FontSizePoints);
            ComLate.Set(font, "Bold", text.Bold ? MsoTrue : MsoFalse);
            ComLate.Set(font, "Italic", text.Italic ? MsoTrue : MsoFalse);
            ComLate.Set(font, "Spacing", (float)text.LetterSpacingPoints);
            SetFarEastFont(range, text.Text, farEast);
            var fontFill = ComLate.Get(font, "Fill");
            var fontColor = ComLate.Get(fontFill, "ForeColor");
            ComLate.Set(fontColor, "RGB", ToOleColor(text.Color));
            var paragraph = ComLate.Get(range, "ParagraphFormat");
            if (text.LineHeightPoints > 0)
            {
                // ParagraphFormat2 没有 LineSpacing：LineRuleWithin=msoFalse 时 SpaceWithin 按磅计。
                ComLate.Set(paragraph, "LineRuleWithin", MsoFalse);
                ComLate.Set(paragraph, "SpaceWithin", (float)text.LineHeightPoints);
            }
            var alignment = string.Equals(text.TextAlign, "center", StringComparison.OrdinalIgnoreCase) ? 2
                : string.Equals(text.TextAlign, "right", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
            ComLate.Set(paragraph, "Alignment", alignment);
        }

        private static void AddTable(PowerPointInterop.Slide slide, PlannedTable table, int index)
        {
            var rect = table.Rectangle;
            var shape = (PowerPointInterop.Shape)ComLate.Call(
                slide.Shapes,
                "AddTable",
                table.Rows.Count,
                table.Rows[0].Count,
                (float)rect.Left,
                (float)rect.Top,
                (float)rect.Width,
                (float)rect.Height);
            shape.Name = "nixer.table." + index.ToString(CultureInfo.InvariantCulture);
            var nativeTable = ComLate.Get(shape, "Table");
            // 默认表格带主题配色、斑马纹和网格线；无样式无网格后，外观完全由采集到的单元格样式决定。
            ComLate.Call(nativeTable, "ApplyStyle", NoStyleNoGridTableStyle, false);
            ComLate.Set(nativeTable, "FirstRow", MsoFalse);
            ComLate.Set(nativeTable, "HorizBanding", MsoFalse);

            if (table.ColumnWidthsPoints.Count == table.Rows[0].Count)
            {
                var columns = ComLate.Get(nativeTable, "Columns");
                for (var columnIndex = 0; columnIndex < table.ColumnWidthsPoints.Count; columnIndex++)
                {
                    ComLate.Set(ComLate.Call(columns, "Item", columnIndex + 1), "Width", (float)table.ColumnWidthsPoints[columnIndex]);
                }
            }

            for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                for (var columnIndex = 0; columnIndex < table.Rows[rowIndex].Count; columnIndex++)
                {
                    var plannedCell = table.Rows[rowIndex][columnIndex];
                    var cell = ComLate.Call(nativeTable, "Cell", rowIndex + 1, columnIndex + 1);
                    var cellShape = ComLate.Get(cell, "Shape");
                    var frame = ComLate.Get(cellShape, "TextFrame2");
                    var range = ComLate.Get(frame, "TextRange");
                    ComLate.Set(range, "Text", plannedCell.Text ?? string.Empty);
                    var font = ComLate.Get(range, "Font");
                    if (!string.IsNullOrWhiteSpace(plannedCell.FontName)) ComLate.Set(font, "Name", plannedCell.FontName);
                    var cellFarEast = plannedCell.FontNameFarEast ?? plannedCell.FontName;
                    if (plannedCell.FontSizePoints > 0) ComLate.Set(font, "Size", (float)plannedCell.FontSizePoints);
                    ComLate.Set(font, "Bold", plannedCell.Bold ? MsoTrue : MsoFalse);
                    ComLate.Set(font, "Italic", plannedCell.Italic ? MsoTrue : MsoFalse);
                    SetFarEastFont(range, plannedCell.Text, cellFarEast);
                    if (!string.IsNullOrWhiteSpace(plannedCell.Color))
                    {
                        ComLate.Set(ComLate.Get(ComLate.Get(font, "Fill"), "ForeColor"), "RGB", ToOleColor(plannedCell.Color));
                    }
                    SetFill((PowerPointInterop.Shape)cellShape, plannedCell.Fill);

                    var margins = plannedCell.MarginPoints ?? new PlannedTableSpacing();
                    ComLate.Set(frame, "MarginTop", (float)margins.Top);
                    ComLate.Set(frame, "MarginRight", (float)margins.Right);
                    ComLate.Set(frame, "MarginBottom", (float)margins.Bottom);
                    ComLate.Set(frame, "MarginLeft", (float)margins.Left);
                    ComLate.Set(frame, "VerticalAnchor", TableAnchorFor(plannedCell.VerticalAlign));

                    var paragraph = ComLate.Get(range, "ParagraphFormat");
                    if (plannedCell.LineHeightPoints > 0)
                    {
                        ComLate.Set(paragraph, "LineRuleWithin", MsoFalse);
                        ComLate.Set(paragraph, "SpaceWithin", (float)plannedCell.LineHeightPoints);
                    }
                    var alignment = string.Equals(plannedCell.TextAlign, "center", StringComparison.OrdinalIgnoreCase) ? 2
                        : string.Equals(plannedCell.TextAlign, "right", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
                    ComLate.Set(paragraph, "Alignment", alignment);

                    var borders = plannedCell.Borders ?? new PlannedTableBorders();
                    SetTableCellBorder(cell, PpBorderTop, borders.Top);
                    SetTableCellBorder(cell, PpBorderLeft, borders.Left);
                    SetTableCellBorder(cell, PpBorderBottom, borders.Bottom);
                    SetTableCellBorder(cell, PpBorderRight, borders.Right);
                }
            }

            // 行高放在文字写入之后设置：PowerPoint 会按文字需要自动撑高，只有显式高度才能还原 HTML 的行高。
            if (table.RowHeightsPoints.Count == table.Rows.Count)
            {
                var rows = ComLate.Get(nativeTable, "Rows");
                for (var rowIndex = 0; rowIndex < table.RowHeightsPoints.Count; rowIndex++)
                {
                    if (table.RowHeightsPoints[rowIndex] > 0)
                    {
                        ComLate.Set(ComLate.Call(rows, "Item", rowIndex + 1), "Height", (float)table.RowHeightsPoints[rowIndex]);
                    }
                }
            }
        }

        private static void SetFarEastFont(object textRange, string text, string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName)) return;

            foreach (var textRun in CjkTextRanges.Find(text))
            {
                object characters = null;
                object font = null;
                try
                {
                    characters = ComLate.Get(textRange, "Characters", textRun.Start + 1, textRun.Length);
                    font = ComLate.Get(characters, "Font");
                    ComLate.Set(font, "NameFarEast", fontName);
                }
                finally
                {
                    ReleaseComObject(font);
                    ReleaseComObject(characters);
                }
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        private static void SetTableCellBorder(object cell, int edge, PlannedTableBorder border)
        {
            if (border == null || border.WidthPoints <= 0 || string.IsNullOrWhiteSpace(border.Color)
                || string.Equals(border.Color, "transparent", StringComparison.OrdinalIgnoreCase)) return;
            var format = ComLate.Call(ComLate.Get(cell, "Borders"), "Item", edge);
            ComLate.Set(format, "Visible", MsoTrue);
            ComLate.Set(format, "Weight", (float)border.WidthPoints);
            ComLate.Set(ComLate.Get(format, "ForeColor"), "RGB", ToOleColor(border.Color));
        }

        private static int TableAnchorFor(string verticalAlign)
        {
            if (string.Equals(verticalAlign, "middle", StringComparison.OrdinalIgnoreCase)) return MsoAnchorMiddle;
            if (string.Equals(verticalAlign, "bottom", StringComparison.OrdinalIgnoreCase)) return MsoAnchorBottom;
            return MsoAnchorTop;
        }

        private static void AddChart(PowerPointInterop.Slide slide, PlannedChart chart, int index)
        {
            var rect = chart.Rectangle;
            var chartType = string.Equals(chart.Type, "line", StringComparison.OrdinalIgnoreCase) ? XlLine
                : string.Equals(chart.Type, "pie", StringComparison.OrdinalIgnoreCase) ? XlPie
                : string.Equals(chart.Type, "doughnut", StringComparison.OrdinalIgnoreCase) ? XlDoughnut
                : XlColumnClustered;
            var shape = (PowerPointInterop.Shape)ComLate.Call(
                slide.Shapes,
                "AddChart2",
                -1,
                chartType,
                (float)rect.Left,
                (float)rect.Top,
                (float)rect.Width,
                (float)rect.Height);
            shape.Name = "nixer.chart." + index.ToString(CultureInfo.InvariantCulture);
            var nativeChart = ComLate.Get(shape, "Chart");
            var chartData = ComLate.Get(nativeChart, "ChartData");
            ComLate.Call(chartData, "Activate");
            var workbook = ComLate.Get(chartData, "Workbook");
            try
            {
                FillChart(nativeChart, workbook, chart, chartType);
            }
            finally
            {
                ComLate.Call(workbook, "Close", false);
            }
        }

        private static void FillChart(object nativeChart, object workbook, PlannedChart chart, int chartType)
        {
            var worksheets = ComLate.Get(workbook, "Worksheets");
            var worksheet = ComLate.Get(worksheets, "Item", 1);
            var values = new object[chart.Labels.Count + 1, chart.Series.Count + 1];
            values[0, 0] = string.Empty;
            for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
            {
                values[0, seriesIndex + 1] = chart.Series[seriesIndex].Name ?? "系列 " + (seriesIndex + 1).ToString(CultureInfo.InvariantCulture);
            }
            for (var labelIndex = 0; labelIndex < chart.Labels.Count; labelIndex++)
            {
                values[labelIndex + 1, 0] = chart.Labels[labelIndex];
                for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
                {
                    values[labelIndex + 1, seriesIndex + 1] = chart.Series[seriesIndex].Values[labelIndex];
                }
            }
            var address = "$A$1:$" + ColumnName(chart.Series.Count + 1) + "$" + (chart.Labels.Count + 1).ToString(CultureInfo.InvariantCulture);
            var range = ComLate.Get(worksheet, "Range", address);
            ComLate.Set(range, "Value2", values);
            // PowerPoint 的 Chart.SetSourceData 只接受字符串地址，不接受 Range 对象。
            var sheetName = ((string)ComLate.Get(worksheet, "Name") ?? "Sheet1").Replace("'", "''");
            ComLate.Call(nativeChart, "SetSourceData", "='" + sheetName + "'!" + address, XlColumns);
            if (chartType == XlColumnClustered || chartType == XlLine)
            {
                for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
                {
                    var color = chart.Series[seriesIndex].Color;
                    if (string.IsNullOrWhiteSpace(color)) continue;
                    int oleColor;
                    try { oleColor = ToOleColor(color); }
                    catch (Exception) { continue; }
                    var format = ComLate.Get(ComLate.Call(nativeChart, "SeriesCollection", seriesIndex + 1), "Format");
                    var fill = ComLate.Get(format, chartType == XlLine ? "Line" : "Fill");
                    ComLate.Set(ComLate.Get(fill, "ForeColor"), "RGB", oleColor);
                }
            }
            if (!string.IsNullOrWhiteSpace(chart.Title))
            {
                ComLate.Set(nativeChart, "HasTitle", true);
                ComLate.Set(ComLate.Get(nativeChart, "ChartTitle"), "Text", chart.Title);
            }
        }

        private static string ColumnName(int column)
        {
            var name = string.Empty;
            while (column > 0)
            {
                column--;
                name = (char)('A' + column % 26) + name;
                column /= 26;
            }
            return name;
        }

        private static void SetFill(PowerPointInterop.Shape shape, string color, double opacity = 1)
        {
            if (string.IsNullOrWhiteSpace(color) || string.Equals(color, "transparent", StringComparison.OrdinalIgnoreCase))
            {
                ComLate.Set(shape.Fill, "Visible", MsoFalse);
                return;
            }

            ComLate.Set(shape.Fill, "Visible", MsoTrue);
            shape.Fill.ForeColor.RGB = ToOleColor(color);
            if (opacity < 1) ComLate.Set(shape.Fill, "Transparency", (float)(1 - Math.Max(0, opacity)));
        }

        internal static int ToOleColor(string value)
        {
            var match = RgbColor.Match(value ?? string.Empty);
            if (match.Success)
            {
                return ColorTranslator.ToOle(Color.FromArgb(
                    int.Parse(match.Groups["r"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["g"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture)));
            }

            return ColorTranslator.ToOle(ColorTranslator.FromHtml(string.IsNullOrWhiteSpace(value) ? "#000000" : value));
        }
    }
}
