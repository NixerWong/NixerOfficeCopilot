using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml;
using Drawing = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;

namespace OfficeCopilot.OpenXml.Core
{
    /// <summary>
    /// Shared chart construction/readback helpers used by the Excel and
    /// PowerPoint file engines. Ported from officecli Core/Chart/ChartHelper
    /// (BuildChartSpace / BuildBarChart / BuildCategoryAxis / BuildValueAxis /
    /// BuildSeriesText / DetectChartType) and reduced to a single-series
    /// clustered column chart plus read-only introspection.
    /// </summary>
    public static class ChartBuilder
    {
        /// <summary>
        /// Build a single-series clustered column ChartSpace from literal
        /// category labels and values. Categories default to 1..N when omitted.
        /// </summary>
        public static C.ChartSpace BuildColumnChartSpace(
            string title,
            IReadOnlyList<string> categories,
            IReadOnlyList<double> values,
            string seriesName)
        {
            if (values == null || values.Count == 0)
                throw new ArgumentException("values must contain at least one point.", "values");

            var cats = categories != null && categories.Count > 0
                ? categories
                : (IReadOnlyList<string>)Enumerable.Range(1, values.Count).Select(i => i.ToString()).ToArray();
            if (cats.Count != values.Count)
                throw new ArgumentException("categories and values must have the same length.", "categories");

            var chartSpace = new C.ChartSpace(
                new C.Date1904 { Val = false },
                new C.Style { Val = (byte)2 });

            var chart = new C.Chart();
            if (!string.IsNullOrWhiteSpace(title) && !title.Equals("none", StringComparison.OrdinalIgnoreCase))
                chart.AppendChild(BuildTitle(title));

            var plotArea = new C.PlotArea(new C.Layout());
            plotArea.AppendChild(BuildColumnChart(cats, values, string.IsNullOrWhiteSpace(seriesName) ? "Series 1" : seriesName));
            plotArea.AppendChild(BuildCategoryAxis(1, 2));
            plotArea.AppendChild(BuildValueAxis(2, 1, C.AxisPositionValues.Left));
            chart.AppendChild(plotArea);

            chart.AppendChild(new C.Legend(
                new C.LegendPosition { Val = C.LegendPositionValues.Bottom },
                new C.Overlay { Val = false }));
            chart.AppendChild(new C.PlotVisibleOnly { Val = true });
            chart.AppendChild(new C.DisplayBlanksAs { Val = C.DisplayBlanksAsValues.Gap });

            chartSpace.AppendChild(chart);
            return chartSpace;
        }

        /// <summary>Best-effort chart type token ("column", "bar", "line", …).</summary>
        public static string DetectChartType(C.ChartSpace chartSpace)
        {
            if (chartSpace == null)
                return "unknown";

            var chart = chartSpace.GetFirstChild<C.Chart>();
            if (chart == null || chart.PlotArea == null)
                return "unknown";

            var plotArea = chart.PlotArea;
            var barChart = plotArea.GetFirstChild<C.BarChart>();
            if (barChart != null)
            {
                if (barChart.BarDirection != null && barChart.BarDirection.Val != null
                    && barChart.BarDirection.Val.Value == C.BarDirectionValues.Bar)
                    return "bar";
                return "column";
            }
            if (plotArea.GetFirstChild<C.LineChart>() != null) return "line";
            if (plotArea.GetFirstChild<C.PieChart>() != null) return "pie";
            if (plotArea.GetFirstChild<C.DoughnutChart>() != null) return "doughnut";
            if (plotArea.GetFirstChild<C.AreaChart>() != null) return "area";
            if (plotArea.GetFirstChild<C.ScatterChart>() != null) return "scatter";
            if (plotArea.GetFirstChild<C.RadarChart>() != null) return "radar";
            if (plotArea.GetFirstChild<C.BubbleChart>() != null) return "bubble";
            if (plotArea.GetFirstChild<C.StockChart>() != null) return "stock";
            return "unknown";
        }

        /// <summary>Number of chart series, or 0 when there is no plot area.</summary>
        public static int CountSeries(C.ChartSpace chartSpace)
        {
            if (chartSpace == null)
                return 0;
            var chart = chartSpace.GetFirstChild<C.Chart>();
            if (chart == null || chart.PlotArea == null)
                return 0;
            return chart.PlotArea.Descendants().Count(e => e.LocalName == "ser");
        }

        /// <summary>Chart title text, or null when there is no literal title.</summary>
        public static string GetChartTitle(C.ChartSpace chartSpace)
        {
            var chart = chartSpace == null ? null : chartSpace.GetFirstChild<C.Chart>();
            var title = chart != null ? chart.Title : null;
            if (title == null)
                return null;
            var text = string.Concat(title.Descendants<Drawing.Text>().Select(t => t.Text ?? string.Empty));
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static C.BarChart BuildColumnChart(IReadOnlyList<string> categories, IReadOnlyList<double> values, string seriesName)
        {
            var barChart = new C.BarChart(
                new C.BarDirection { Val = C.BarDirectionValues.Column },
                new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
                new C.VaryColors { Val = false });

            barChart.AppendChild(BuildBarSeries(0, seriesName, categories, values));
            barChart.AppendChild(new C.GapWidth { Val = (ushort)150 });
            barChart.AppendChild(new C.AxisId { Val = 1 });
            barChart.AppendChild(new C.AxisId { Val = 2 });
            return barChart;
        }

        private static C.BarChartSeries BuildBarSeries(
            uint index, string name, IReadOnlyList<string> categories, IReadOnlyList<double> values)
        {
            var series = new C.BarChartSeries(
                new C.Index { Val = index },
                new C.Order { Val = index },
                BuildSeriesText(name));

            series.AppendChild(new C.InvertIfNegative { Val = false });
            series.AppendChild(BuildCategoryData(categories));
            series.AppendChild(BuildValues(values));
            return series;
        }

        private static C.CategoryAxisData BuildCategoryData(IReadOnlyList<string> categories)
        {
            var stringLiteral = new C.StringLiteral(new C.PointCount { Val = (uint)categories.Count });
            for (int i = 0; i < categories.Count; i++)
                stringLiteral.AppendChild(new C.StringPoint(new C.NumericValue(categories[i])) { Index = (uint)i });
            return new C.CategoryAxisData(stringLiteral);
        }

        private static C.Values BuildValues(IReadOnlyList<double> values)
        {
            var numberLiteral = new C.NumberLiteral(
                new C.FormatCode("General"),
                new C.PointCount { Val = (uint)values.Count });
            for (int i = 0; i < values.Count; i++)
                numberLiteral.AppendChild(new C.NumericPoint(new C.NumericValue(values[i].ToString("G"))) { Index = (uint)i });
            return new C.Values(numberLiteral);
        }

        private static C.SeriesText BuildSeriesText(string name)
        {
            return new C.SeriesText(new C.NumericValue(name));
        }

        private static C.CategoryAxis BuildCategoryAxis(uint axisId, uint crossAxisId)
        {
            return new C.CategoryAxis(
                new C.AxisId { Val = axisId },
                new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                new C.Delete { Val = false },
                new C.AxisPosition { Val = C.AxisPositionValues.Bottom },
                new C.MajorTickMark { Val = C.TickMarkValues.Outside },
                new C.MinorTickMark { Val = C.TickMarkValues.None },
                new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
                new C.CrossingAxis { Val = crossAxisId },
                new C.Crosses { Val = C.CrossesValues.AutoZero },
                new C.AutoLabeled { Val = true },
                new C.LabelAlignment { Val = C.LabelAlignmentValues.Center },
                new C.LabelOffset { Val = 100 });
        }

        private static C.ValueAxis BuildValueAxis(uint axisId, uint crossAxisId, C.AxisPositionValues position)
        {
            return new C.ValueAxis(
                new C.AxisId { Val = axisId },
                new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
                new C.Delete { Val = false },
                new C.AxisPosition { Val = position },
                new C.MajorGridlines(),
                new C.NumberingFormat { FormatCode = "General", SourceLinked = true },
                new C.MajorTickMark { Val = C.TickMarkValues.Outside },
                new C.MinorTickMark { Val = C.TickMarkValues.None },
                new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
                new C.CrossingAxis { Val = crossAxisId },
                new C.Crosses { Val = C.CrossesValues.AutoZero },
                new C.CrossBetween { Val = C.CrossBetweenValues.Between });
        }

        private static C.Title BuildTitle(string titleText)
        {
            return new C.Title(
                new C.ChartText(
                    new C.RichText(
                        new Drawing.BodyProperties(),
                        new Drawing.ListStyle(),
                        new Drawing.Paragraph(
                            new Drawing.ParagraphProperties(
                                new Drawing.DefaultRunProperties { FontSize = 1400, Bold = true }),
                            new Drawing.Run(
                                new Drawing.RunProperties { Language = "en-US", FontSize = 1400, Bold = true },
                                new Drawing.Text(titleText))))),
                new C.Overlay { Val = false });
        }
    }
}
