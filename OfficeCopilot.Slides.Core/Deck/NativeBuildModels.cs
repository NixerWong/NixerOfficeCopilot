using System.Collections.Generic;

namespace OfficeCopilot.Slides.Deck
{
    public sealed class CapturedTable
    {
        public CapturedRectangle Rectangle { get; set; } = new CapturedRectangle();

        public IList<double> ColumnWidths { get; set; } = new List<double>();

        public IList<CapturedTableRow> Rows { get; set; } = new List<CapturedTableRow>();
    }

    public sealed class CapturedTableRow
    {
        public double Height { get; set; }

        public IList<CapturedTableCell> Cells { get; set; } = new List<CapturedTableCell>();
    }

    public sealed class CapturedTableCell
    {
        public string Text { get; set; }

        public string Fill { get; set; }

        public string Color { get; set; }

        public string FontFamily { get; set; }

        public double FontSize { get; set; }

        public string FontWeight { get; set; }

        public string FontStyle { get; set; }

        public bool Bold { get; set; }

        public string TextAlign { get; set; }

        public double LineHeight { get; set; }

        public string VerticalAlign { get; set; }

        public CapturedTableSpacing Padding { get; set; } = new CapturedTableSpacing();

        public CapturedTableBorders Borders { get; set; } = new CapturedTableBorders();
    }

    public sealed class CapturedTableSpacing
    {
        public double Top { get; set; }

        public double Right { get; set; }

        public double Bottom { get; set; }

        public double Left { get; set; }
    }

    public sealed class CapturedTableBorders
    {
        public CapturedTableBorder Top { get; set; } = new CapturedTableBorder();

        public CapturedTableBorder Right { get; set; } = new CapturedTableBorder();

        public CapturedTableBorder Bottom { get; set; } = new CapturedTableBorder();

        public CapturedTableBorder Left { get; set; } = new CapturedTableBorder();
    }

    public sealed class CapturedTableBorder
    {
        public double Width { get; set; }

        public string Color { get; set; }
    }

    public sealed class CapturedChart
    {
        public CapturedRectangle Rectangle { get; set; } = new CapturedRectangle();

        public string Type { get; set; }

        public string Title { get; set; }

        public IList<string> Labels { get; set; } = new List<string>();

        public IList<CapturedChartSeries> Series { get; set; } = new List<CapturedChartSeries>();
    }

    public sealed class CapturedChartSeries
    {
        public string Name { get; set; }

        public IList<double> Values { get; set; } = new List<double>();

        public string Color { get; set; }
    }

    public sealed class PlannedTable
    {
        public CapturedRectangle Rectangle { get; set; }

        public IList<double> ColumnWidthsPoints { get; set; } = new List<double>();

        public IList<double> RowHeightsPoints { get; set; } = new List<double>();

        public IList<IList<PlannedTableCell>> Rows { get; set; } = new List<IList<PlannedTableCell>>();
    }

    public sealed class PlannedTableCell
    {
        public string Text { get; set; }

        public string Fill { get; set; }

        public string Color { get; set; }

        public string FontName { get; set; }

        public string FontNameFarEast { get; set; }

        public double FontSizePoints { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        public string TextAlign { get; set; }

        public double LineHeightPoints { get; set; }

        public string VerticalAlign { get; set; }

        public PlannedTableSpacing MarginPoints { get; set; } = new PlannedTableSpacing();

        public PlannedTableBorders Borders { get; set; } = new PlannedTableBorders();
    }

    public sealed class PlannedTableSpacing
    {
        public double Top { get; set; }

        public double Right { get; set; }

        public double Bottom { get; set; }

        public double Left { get; set; }
    }

    public sealed class PlannedTableBorders
    {
        public PlannedTableBorder Top { get; set; } = new PlannedTableBorder();

        public PlannedTableBorder Right { get; set; } = new PlannedTableBorder();

        public PlannedTableBorder Bottom { get; set; } = new PlannedTableBorder();

        public PlannedTableBorder Left { get; set; } = new PlannedTableBorder();
    }

    public sealed class PlannedTableBorder
    {
        public double WidthPoints { get; set; }

        public string Color { get; set; }
    }

    public sealed class PlannedChart
    {
        public CapturedRectangle Rectangle { get; set; }

        public string Type { get; set; }

        public string Title { get; set; }

        public IList<string> Labels { get; set; } = new List<string>();

        public IList<PlannedChartSeries> Series { get; set; } = new List<PlannedChartSeries>();
    }

    public sealed class PlannedChartSeries
    {
        public string Name { get; set; }

        public IList<double> Values { get; set; } = new List<double>();

        public string Color { get; set; }
    }
}