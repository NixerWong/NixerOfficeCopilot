using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Drawing = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using XDR = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using OfficeCopilot.OpenXml.Core;

namespace OfficeCopilot.OpenXml.Excel
{
    /// <summary>
    /// Chart operations for the Excel file engine. Ported from officecli
    /// ExcelHandler.Add.Chart and reduced to the explicit-API surface: add a
    /// single-series clustered column chart anchored to a worksheet, and read
    /// back chart metadata.
    /// </summary>
    internal static class ExcelChartOperations
    {
        // Worksheet children that must appear after the <drawing> element.
        private static readonly Type[] DrawingAnchors = new Type[]
        {
            typeof(LegacyDrawing), typeof(LegacyDrawingHeaderFooter), typeof(Picture),
            typeof(OleObjects), typeof(Controls), typeof(WebPublishItems),
            typeof(TableParts), typeof(WorksheetExtensionList)
        };

        internal static int AddChart(
            SpreadsheetDocument doc,
            string sheetName,
            IReadOnlyList<string> categories,
            IReadOnlyList<double> values,
            string seriesName,
            string title)
        {
            var chartSpace = ChartBuilder.BuildColumnChartSpace(title, categories, values, seriesName);
            var worksheetPart = ExcelCellOperations.GetWorksheetPart(doc, sheetName);

            var drawingsPart = worksheetPart.DrawingsPart;
            if (drawingsPart == null)
                drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();

            var worksheetDrawing = drawingsPart.WorksheetDrawing;
            if (worksheetDrawing == null)
            {
                worksheetDrawing = new XDR.WorksheetDrawing();
                drawingsPart.WorksheetDrawing = worksheetDrawing;
            }

            var chartPart = drawingsPart.AddNewPart<ChartPart>();
            chartPart.ChartSpace = chartSpace;
            chartPart.ChartSpace.Save();

            var chartRelId = drawingsPart.GetIdOfPart(chartPart);

            uint frameId = 1;
            foreach (var nv in worksheetDrawing.Descendants<XDR.NonVisualDrawingProperties>())
            {
                if (nv.Id != null && nv.Id.Value >= frameId)
                    frameId = nv.Id.Value + 1;
            }

            var anchor = new XDR.TwoCellAnchor();
            anchor.Append(new XDR.FromMarker(
                new XDR.ColumnId("1"), new XDR.ColumnOffset("0"),
                new XDR.RowId("1"), new XDR.RowOffset("0")));
            anchor.Append(new XDR.ToMarker(
                new XDR.ColumnId("5"), new XDR.ColumnOffset("0"),
                new XDR.RowId("12"), new XDR.RowOffset("0")));

            var graphicFrame = new XDR.GraphicFrame();
            graphicFrame.NonVisualGraphicFrameProperties = new XDR.NonVisualGraphicFrameProperties(
                new XDR.NonVisualDrawingProperties
                {
                    Id = frameId,
                    Name = string.IsNullOrWhiteSpace(title) ? "Chart" : title
                },
                new XDR.NonVisualGraphicFrameDrawingProperties());
            graphicFrame.Transform = new XDR.Transform(
                new Drawing.Offset { X = 0L, Y = 0L },
                new Drawing.Extents { Cx = 0L, Cy = 0L });
            graphicFrame.Append(new Drawing.Graphic(
                new Drawing.GraphicData(new C.ChartReference { Id = chartRelId })
                {
                    Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart"
                }));

            anchor.Append(graphicFrame);
            anchor.Append(new XDR.ClientData());
            worksheetDrawing.Append(anchor);
            worksheetDrawing.Save();

            var worksheet = worksheetPart.Worksheet;
            var drawingElement = worksheet.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.Drawing>();
            if (drawingElement == null)
            {
                drawingElement = new DocumentFormat.OpenXml.Spreadsheet.Drawing
                {
                    Id = worksheetPart.GetIdOfPart(drawingsPart)
                };
                InsertBeforeFirst(worksheet, drawingElement, DrawingAnchors);
            }

            worksheet.Save();
            return drawingsPart.ChartParts.Count();
        }

        internal static IReadOnlyList<ExcelChartInfo> ReadCharts(SpreadsheetDocument doc, string sheetName)
        {
            var result = new List<ExcelChartInfo>();
            var worksheetPart = ExcelCellOperations.GetWorksheetPart(doc, sheetName);
            var drawingsPart = worksheetPart.DrawingsPart;
            if (drawingsPart == null)
                return result;

            int index = 0;
            foreach (var chartPart in drawingsPart.ChartParts)
            {
                index++;
                var chartSpace = chartPart.ChartSpace;
                result.Add(new ExcelChartInfo
                {
                    Index = index,
                    Type = ChartBuilder.DetectChartType(chartSpace),
                    Title = ChartBuilder.GetChartTitle(chartSpace),
                    SeriesCount = ChartBuilder.CountSeries(chartSpace)
                });
            }
            return result;
        }

        private static void InsertBeforeFirst(OpenXmlElement parent, OpenXmlElement child, Type[] anchors)
        {
            OpenXmlElement anchor = null;
            foreach (var existing in parent.ChildElements)
            {
                foreach (var type in anchors)
                {
                    if (type.IsInstanceOfType(existing))
                    {
                        anchor = existing;
                        break;
                    }
                }
                if (anchor != null)
                    break;
            }

            if (anchor != null)
                parent.InsertBefore(child, anchor);
            else
                parent.AppendChild(child);
        }
    }
}
