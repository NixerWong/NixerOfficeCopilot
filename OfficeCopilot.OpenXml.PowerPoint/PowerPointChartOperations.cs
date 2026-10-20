using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Drawing = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using OfficeCopilot.OpenXml.Core;

namespace OfficeCopilot.OpenXml.PowerPoint
{
    /// <summary>
    /// Chart operations for the PowerPoint file engine. Ported from officecli
    /// PowerPointHandler.Add.Chart (BuildChartGraphicFrame) and reduced to a
    /// single-series clustered column chart embedded in a GraphicFrame.
    /// </summary>
    internal static class PowerPointChartOperations
    {
        internal static int AddChart(
            SlidePart slidePart,
            IReadOnlyList<string> categories,
            IReadOnlyList<double> values,
            string seriesName,
            string title)
        {
            var chartSpace = ChartBuilder.BuildColumnChartSpace(title, categories, values, seriesName);

            var chartPart = slidePart.AddNewPart<ChartPart>();
            chartPart.ChartSpace = chartSpace;
            chartPart.ChartSpace.Save();
            var relId = slidePart.GetIdOfPart(chartPart);

            var shapeTree = PowerPointShapeOperations.GetShapeTree(slidePart);
            uint id = PowerPointShapeOperations.GetNextShapeId(shapeTree);

            var graphicFrame = new GraphicFrame();
            graphicFrame.NonVisualGraphicFrameProperties = new NonVisualGraphicFrameProperties(
                new NonVisualDrawingProperties { Id = id, Name = "Chart" },
                new NonVisualGraphicFrameDrawingProperties(),
                new ApplicationNonVisualDrawingProperties());
            graphicFrame.Transform = new Transform(
                new Drawing.Offset { X = 914400L, Y = 1600200L },
                new Drawing.Extents { Cx = 8229600L, Cy = 4114800L });
            graphicFrame.AppendChild(new Drawing.Graphic(
                new Drawing.GraphicData(new C.ChartReference { Id = relId })
                {
                    Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart"
                }));

            shapeTree.AppendChild(graphicFrame);
            slidePart.Slide.Save();

            return shapeTree.Elements<GraphicFrame>().Count(gf => gf.Descendants<C.ChartReference>().Any());
        }
    }
}
