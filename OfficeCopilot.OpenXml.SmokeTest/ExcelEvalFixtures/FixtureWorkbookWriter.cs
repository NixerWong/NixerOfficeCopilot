using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace OfficeCopilot.OpenXml.Word.SmokeTest.ExcelEvalFixtures
{
    internal enum FixtureCellKind
    {
        Text,
        Number,
        Date,
        Formula,
        Error,
        Blank
    }

    internal sealed class FixtureCell
    {
        private FixtureCell(FixtureCellKind kind, string text, decimal number, DateTime date, string formula)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            Number = number;
            Date = date;
            Formula = formula ?? string.Empty;
        }

        public FixtureCellKind Kind { get; private set; }
        public string Text { get; private set; }
        public decimal Number { get; private set; }
        public DateTime Date { get; private set; }
        public string Formula { get; private set; }

        public static FixtureCell TextValue(string value) => new FixtureCell(FixtureCellKind.Text, value, 0, default(DateTime), null);
        public static FixtureCell NumberValue(decimal value) => new FixtureCell(FixtureCellKind.Number, null, value, default(DateTime), null);
        public static FixtureCell DateValue(DateTime value) => new FixtureCell(FixtureCellKind.Date, null, 0, value, null);
        public static FixtureCell FormulaValue(string formula, decimal cachedValue) => new FixtureCell(FixtureCellKind.Formula, cachedValue.ToString(CultureInfo.InvariantCulture), cachedValue, default(DateTime), formula);
        public static FixtureCell ErrorValue(string error) => new FixtureCell(FixtureCellKind.Error, error, 0, default(DateTime), null);
        public static FixtureCell BlankValue() => new FixtureCell(FixtureCellKind.Blank, null, 0, default(DateTime), null);
    }

    internal sealed class FixtureRow
    {
        public FixtureRow(int index, IReadOnlyList<FixtureCell> cells, bool hidden = false)
        {
            Index = index;
            Cells = cells ?? throw new ArgumentNullException(nameof(cells));
            Hidden = hidden;
        }

        public int Index { get; private set; }
        public IReadOnlyList<FixtureCell> Cells { get; private set; }
        public bool Hidden { get; private set; }
    }

    internal sealed class FixtureSheet
    {
        public FixtureSheet(string name, IEnumerable<FixtureRow> rows, string autoFilterReference = null, IEnumerable<string> mergedRanges = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
            AutoFilterReference = autoFilterReference;
            MergedRanges = mergedRanges ?? Array.Empty<string>();
        }

        public string Name { get; private set; }
        public IEnumerable<FixtureRow> Rows { get; private set; }
        public string AutoFilterReference { get; private set; }
        public IEnumerable<string> MergedRanges { get; private set; }
    }

    internal static class FixtureWorkbookWriter
    {
        public static void Write(string path, IReadOnlyList<FixtureSheet> sheets)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A workbook path is required.", nameof(path));
            if (sheets == null || sheets.Count == 0)
                throw new ArgumentException("At least one worksheet is required.", nameof(sheets));

            using (var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                AddStyles(workbookPart);

                var sheetList = workbookPart.Workbook.AppendChild(new Sheets());
                uint sheetId = 1;
                foreach (var sheet in sheets)
                {
                    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                    WriteWorksheet(worksheetPart, sheet);
                    sheetList.Append(new Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = sheetId++,
                        Name = sheet.Name
                    });
                }

                workbookPart.Workbook.AppendChild(new CalculationProperties
                {
                    FullCalculationOnLoad = true,
                    ForceFullCalculation = true
                });
                workbookPart.Workbook.Save();
            }
        }

        private static void WriteWorksheet(WorksheetPart part, FixtureSheet sheet)
        {
            using (var writer = OpenXmlWriter.Create(part))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteStartElement(new SheetData());

                foreach (var fixtureRow in sheet.Rows)
                {
                    var row = new Row
                    {
                        RowIndex = (uint)fixtureRow.Index,
                        Hidden = fixtureRow.Hidden
                    };

                    for (var column = 0; column < fixtureRow.Cells.Count; column++)
                    {
                        var fixtureCell = fixtureRow.Cells[column];
                        if (fixtureCell == null || fixtureCell.Kind == FixtureCellKind.Blank)
                            continue;

                        row.Append(CreateCell(fixtureRow.Index, column + 1, fixtureCell));
                    }

                    writer.WriteElement(row);
                }

                writer.WriteEndElement();

                if (!string.IsNullOrWhiteSpace(sheet.AutoFilterReference))
                    writer.WriteElement(new AutoFilter { Reference = sheet.AutoFilterReference });

                var mergeCells = new MergeCells();
                foreach (var range in sheet.MergedRanges)
                {
                    if (!string.IsNullOrWhiteSpace(range))
                        mergeCells.AppendChild(new MergeCell { Reference = range });
                }
                if (mergeCells.ChildElements.Count > 0)
                {
                    mergeCells.Count = (uint)mergeCells.ChildElements.Count;
                    writer.WriteElement(mergeCells);
                }

                writer.WriteEndElement();
            }
        }

        private static Cell CreateCell(int row, int column, FixtureCell fixtureCell)
        {
            var cell = new Cell { CellReference = CellReference(row, column) };
            switch (fixtureCell.Kind)
            {
                case FixtureCellKind.Text:
                    cell.DataType = CellValues.InlineString;
                    cell.InlineString = new InlineString(new Text(fixtureCell.Text)
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    });
                    break;
                case FixtureCellKind.Number:
                    cell.CellValue = new CellValue(fixtureCell.Number.ToString(CultureInfo.InvariantCulture));
                    break;
                case FixtureCellKind.Date:
                    cell.StyleIndex = 1U;
                    cell.CellValue = new CellValue(fixtureCell.Date.ToOADate().ToString("R", CultureInfo.InvariantCulture));
                    break;
                case FixtureCellKind.Formula:
                    cell.CellFormula = new CellFormula(fixtureCell.Formula);
                    cell.CellValue = new CellValue(fixtureCell.Number.ToString(CultureInfo.InvariantCulture));
                    break;
                case FixtureCellKind.Error:
                    cell.DataType = CellValues.Error;
                    cell.CellValue = new CellValue(fixtureCell.Text);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fixtureCell), fixtureCell.Kind, "Unsupported fixture cell kind.");
            }

            return cell;
        }

        private static string CellReference(int row, int column)
        {
            var letters = string.Empty;
            while (column > 0)
            {
                column--;
                letters = (char)('A' + column % 26) + letters;
                column /= 26;
            }

            return letters + row.ToString(CultureInfo.InvariantCulture);
        }

        private static void AddStyles(WorkbookPart workbookPart)
        {
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            var stylesheet = new Stylesheet();
            stylesheet.AppendChild(new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164U, FormatCode = "yyyy-mm-dd" })
            {
                Count = 1U
            });
            stylesheet.AppendChild(new Fonts(new Font()) { Count = 1U });
            stylesheet.AppendChild(new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
            {
                Count = 2U
            });
            stylesheet.AppendChild(new Borders(new Border()) { Count = 1U });
            stylesheet.AppendChild(new CellStyleFormats(new CellFormat
            {
                FontId = 0U,
                FillId = 0U,
                BorderId = 0U
            })
            {
                Count = 1U
            });
            stylesheet.AppendChild(new CellFormats(
                new CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U },
                new CellFormat
                {
                    FontId = 0U,
                    FillId = 0U,
                    BorderId = 0U,
                    NumberFormatId = 164U,
                    ApplyNumberFormat = true
                })
            {
                Count = 2U
            });
            stylesheet.AppendChild(new CellStyles(new CellStyle
            {
                Name = "Normal",
                FormatId = 0U,
                BuiltinId = 0U
            })
            {
                Count = 1U
            });
            stylesPart.Stylesheet = stylesheet;
            stylesheet.Save();
        }
    }
}
