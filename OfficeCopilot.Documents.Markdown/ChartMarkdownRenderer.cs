using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace OfficeCopilot.Documents.Markdown
{
    internal static class ChartMarkdownRenderer
    {
        internal static void Append(
            StringBuilder builder,
            IEnumerable<ChartPart> sourceParts,
            int maxChartPoints,
            int chartIndexOffset = 0,
            bool includeSectionHeader = true)
        {
            var chartParts = sourceParts == null ? new List<ChartPart>() : sourceParts.ToList();
            if (chartParts.Count == 0)
            {
                return;
            }

            maxChartPoints = Math.Max(0, maxChartPoints);
            if (includeSectionHeader)
            {
                builder.AppendLine("**Charts**");
                builder.AppendLine();
            }
            for (var chartIndex = 0; chartIndex < chartParts.Count; chartIndex++)
            {
                var chartSpace = chartParts[chartIndex].ChartSpace;
                var chartTypeElement = chartSpace == null
                    ? null
                    : chartSpace.Descendants().FirstOrDefault(element => element.LocalName.EndsWith("Chart", StringComparison.Ordinal));
                var titleElement = chartSpace == null
                    ? null
                    : chartSpace.Descendants().FirstOrDefault(element => element.LocalName == "title");
                var title = titleElement == null
                    ? string.Empty
                    : string.Concat(titleElement.Descendants().Where(element => element.LocalName == "t").Select(element => element.InnerText));

                builder.Append("### Chart ");
                builder.Append((chartIndexOffset + chartIndex + 1).ToString(CultureInfo.InvariantCulture));
                builder.Append(": ");
                builder.Append(EscapeMarkdown(string.IsNullOrWhiteSpace(title) ? "Untitled" : title));
                builder.Append(" (");
                builder.Append(GetChartType(chartTypeElement));
                builder.AppendLine(")");

                var seriesElements = chartTypeElement == null
                    ? new List<OpenXmlElement>()
                    : chartTypeElement.Descendants().Where(element => element.LocalName == "ser").ToList();
                for (var seriesIndex = 0; seriesIndex < seriesElements.Count; seriesIndex++)
                {
                    var series = seriesElements[seriesIndex];
                    var seriesName = GetChartSeriesName(series);
                    var categoryData = GetDirectChild(series, "cat") ?? GetDirectChild(series, "xVal");
                    var valueData = GetDirectChild(series, "val") ?? GetDirectChild(series, "yVal");
                    var categories = GetChartPoints(categoryData);
                    var values = GetChartPoints(valueData);

                    builder.Append("#### Series ");
                    builder.Append((seriesIndex + 1).ToString(CultureInfo.InvariantCulture));
                    builder.Append(": ");
                    builder.AppendLine(EscapeMarkdown(seriesName));

                    if (values.Count == 0)
                    {
                        var categoryFormula = GetChartFormula(categoryData);
                        var valueFormula = GetChartFormula(valueData);
                        if (!string.IsNullOrWhiteSpace(categoryFormula) || !string.IsNullOrWhiteSpace(valueFormula))
                        {
                            builder.Append("> Source formulas: ");
                            builder.Append(string.IsNullOrWhiteSpace(categoryFormula) ? "(category range unavailable)" : categoryFormula);
                            builder.Append("; ");
                            builder.AppendLine(string.IsNullOrWhiteSpace(valueFormula) ? "(value range unavailable)" : valueFormula);
                        }
                        builder.AppendLine("_(Cached series values unavailable)_");
                        builder.AppendLine();
                        continue;
                    }

                    MarkdownWriter.AppendRow(builder, new[] { "Category", "Value" }, CancellationToken.None);
                    MarkdownWriter.AppendSeparator(builder, 2, new[] { 1 }, CancellationToken.None);
                    foreach (var point in values.Take(maxChartPoints))
                    {
                        string category;
                        if (!categories.TryGetValue(point.Key, out category))
                        {
                            category = (point.Key + 1).ToString(CultureInfo.InvariantCulture);
                        }
                        MarkdownWriter.AppendRow(builder, new[] { category, point.Value }, CancellationToken.None);
                    }
                    if (values.Count > maxChartPoints)
                    {
                        builder.Append("> Chart series truncated to the first ");
                        builder.Append(maxChartPoints.ToString(CultureInfo.InvariantCulture));
                        builder.AppendLine(" points.");
                    }
                    builder.AppendLine();
                }
            }
        }

        private static string GetChartType(OpenXmlElement chartTypeElement)
        {
            if (chartTypeElement == null)
            {
                return "unknown";
            }

            if (chartTypeElement.LocalName == "barChart")
            {
                var direction = GetDirectChild(chartTypeElement, "barDir");
                return direction != null && string.Equals(direction.GetAttribute("val", string.Empty).Value, "bar", StringComparison.OrdinalIgnoreCase)
                    ? "bar"
                    : "column";
            }

            return chartTypeElement.LocalName.Substring(0, chartTypeElement.LocalName.Length - "Chart".Length).ToLowerInvariant();
        }

        private static string GetChartSeriesName(OpenXmlElement series)
        {
            var title = GetDirectChild(series, "tx");
            var value = title == null ? null : title.Descendants().FirstOrDefault(element => element.LocalName == "v");
            return value == null || string.IsNullOrWhiteSpace(value.InnerText) ? "Series" : value.InnerText;
        }

        private static SortedDictionary<int, string> GetChartPoints(OpenXmlElement data)
        {
            var points = new SortedDictionary<int, string>();
            if (data == null)
            {
                return points;
            }

            var cache = data.Descendants().FirstOrDefault(element =>
                element.LocalName == "strLit" || element.LocalName == "numLit"
                || element.LocalName == "strCache" || element.LocalName == "numCache");
            if (cache == null)
            {
                return points;
            }

            var fallbackIndex = 0;
            foreach (var point in cache.Descendants().Where(element => element.LocalName == "pt"))
            {
                int pointIndex;
                var indexValue = GetAttributeValue(point, "idx");
                if (!int.TryParse(indexValue, NumberStyles.None, CultureInfo.InvariantCulture, out pointIndex))
                {
                    pointIndex = fallbackIndex;
                }
                var value = point.Elements().FirstOrDefault(element => element.LocalName == "v");
                if (value != null)
                {
                    points[pointIndex] = value.InnerText;
                }
                fallbackIndex = pointIndex + 1;
            }
            return points;
        }

        private static string GetChartFormula(OpenXmlElement data)
        {
            var formula = data == null ? null : data.Descendants().FirstOrDefault(element => element.LocalName == "f");
            return formula == null ? string.Empty : formula.InnerText;
        }

        private static OpenXmlElement GetDirectChild(OpenXmlElement parent, string localName)
        {
            return parent == null ? null : parent.ChildElements.FirstOrDefault(element => element.LocalName == localName);
        }

        private static string GetAttributeValue(OpenXmlElement element, string localName)
        {
            var attribute = element.GetAttributes().FirstOrDefault(item => item.LocalName == localName);
            return attribute.Value ?? string.Empty;
        }

        private static string EscapeMarkdown(string value)
        {
            return MarkdownWriter.Escape(value);
        }
    }
}