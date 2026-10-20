using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace OfficeCopilot.OpenXml.PowerPoint
{
    /// <summary>
    /// Table (GraphicFrame + Drawing.Table) operations for the PowerPoint file
    /// engine. Ported from officecli PowerPointHandler.Add.Table and reduced to
    /// the explicit-API surface.
    /// </summary>
    internal static class PowerPointTableOperations
    {
        internal static int AddTable(SlidePart slidePart, int rows, int columns, IReadOnlyList<IReadOnlyList<string>> data)
        {
            if (rows < 1)
                throw new ArgumentOutOfRangeException("rows", "rows must be >= 1.");
            if (columns < 1)
                throw new ArgumentOutOfRangeException("columns", "columns must be >= 1.");

            var shapeTree = PowerPointShapeOperations.GetShapeTree(slidePart);
            uint id = PowerPointShapeOperations.GetNextShapeId(shapeTree);

            long cellWidth = 8229600L / columns;
            long rowHeight = 370840L;

            var graphicFrame = new GraphicFrame();
            graphicFrame.NonVisualGraphicFrameProperties = new NonVisualGraphicFrameProperties(
                new NonVisualDrawingProperties { Id = id, Name = "Table" },
                new NonVisualGraphicFrameDrawingProperties(),
                new ApplicationNonVisualDrawingProperties());
            graphicFrame.Transform = new Transform(
                new Drawing.Offset { X = 457200L, Y = 1600200L },
                new Drawing.Extents { Cx = cellWidth * columns, Cy = rowHeight * rows });

            var table = new Drawing.Table();
            table.Append(new Drawing.TableProperties { FirstRow = true, BandRow = true });

            var grid = new Drawing.TableGrid();
            for (int c = 0; c < columns; c++)
                grid.Append(new Drawing.GridColumn { Width = cellWidth });
            table.Append(grid);

            for (int r = 0; r < rows; r++)
            {
                var tableRow = new Drawing.TableRow { Height = rowHeight };
                for (int c = 0; c < columns; c++)
                {
                    var text = data != null && r < data.Count && c < data[r].Count ? data[r][c] : string.Empty;
                    var cell = new Drawing.TableCell();
                    var para = new Drawing.Paragraph();
                    para.Append(new Drawing.Run(
                        new Drawing.RunProperties { Language = "en-US" },
                        new Drawing.Text { Text = text ?? string.Empty }));
                    cell.Append(new Drawing.TextBody(
                        new Drawing.BodyProperties(),
                        new Drawing.ListStyle(),
                        para));
                    cell.Append(new Drawing.TableCellProperties());
                    tableRow.Append(cell);
                }
                table.Append(tableRow);
            }

            var graphic = new Drawing.Graphic(
                new Drawing.GraphicData(table) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/table" });
            graphicFrame.Append(graphic);
            shapeTree.AppendChild(graphicFrame);
            slidePart.Slide.Save();

            return shapeTree.Elements<GraphicFrame>().Count(gf => gf.Descendants<Drawing.Table>().Any());
        }

        internal static bool SetCellText(SlidePart slidePart, int tableIndex, int row, int column, string text)
        {
            var shapeTree = PowerPointShapeOperations.GetShapeTree(slidePart);
            var tables = shapeTree.Elements<GraphicFrame>()
                .Where(gf => gf.Descendants<Drawing.Table>().Any())
                .ToList();
            if (tableIndex < 1 || tableIndex > tables.Count)
                return false;

            var table = tables[tableIndex - 1].Descendants<Drawing.Table>().First();
            var rowsList = table.Elements<Drawing.TableRow>().ToList();
            if (row < 1 || row > rowsList.Count)
                throw new ArgumentOutOfRangeException("row", string.Format("Row {0} out of range (1..{1}).", row, rowsList.Count));

            var cells = rowsList[row - 1].Elements<Drawing.TableCell>().ToList();
            if (column < 1 || column > cells.Count)
                throw new ArgumentOutOfRangeException("column", string.Format("Column {0} out of range (1..{1}).", column, cells.Count));

            var cell = cells[column - 1];
            var textBody = cell.GetFirstChild<Drawing.TextBody>();
            if (textBody != null)
                textBody.Remove();

            var para = new Drawing.Paragraph();
            para.Append(new Drawing.Run(
                new Drawing.RunProperties { Language = "en-US" },
                new Drawing.Text { Text = text ?? string.Empty }));
            cell.InsertAt(new Drawing.TextBody(
                new Drawing.BodyProperties(),
                new Drawing.ListStyle(),
                para), 0);

            slidePart.Slide.Save();
            return true;
        }
    }
}
