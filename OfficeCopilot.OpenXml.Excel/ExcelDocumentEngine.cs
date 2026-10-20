using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using OfficeCopilot.OpenXml.Core;

namespace OfficeCopilot.OpenXml.Excel
{
    /// <summary>
    /// File-level engine for .xlsx documents (Open XML SDK — no Excel host).
    /// Register once at host startup so any Office host can create/edit .xlsx.
    /// </summary>
    public sealed class ExcelDocumentEngine : DocumentEngineBase
    {
        /// <summary>Typed access to the underlying package.</summary>
        public SpreadsheetDocument Document => (SpreadsheetDocument)Package;

        public override string Format => "xlsx";

        public ExcelDocumentEngine(string filePath, bool editable)
            : base(filePath, editable, SpreadsheetDocument.Open(filePath, editable))
        {
        }

        /// <summary>Register the .xlsx engine. Call once per host process.</summary>
        public static void Register()
        {
            DocumentEngineRegistry.Register(
                ".xlsx",
                (path, editable) => new ExcelDocumentEngine(path, editable),
                ExcelDocumentEngine.CreateBlank,
                ExcelDocumentEngine.MergeTemplate);
        }

        /// <summary>Create a blank .xlsx with a single empty worksheet.</summary>
        public static void CreateBlank(string path)
        {
            using (var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                worksheetPart.Worksheet = new Worksheet(new SheetData());

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet()
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "Sheet1"
                });

                workbookPart.Workbook.Save();
            }
        }

        /// <summary>
        /// Merge a template with JSON data, replacing {{key}} placeholders in
        /// cell values (inline strings and shared strings).
        /// Ported from officecli TemplateMerger.MergeXlsx.
        /// </summary>
        public static MergeResult MergeTemplate(string templatePath, string outputPath, MergeData data)
        {
            if (!File.Exists(templatePath))
                throw new FileNotFoundException(string.Format("Template not found: {0}", templatePath), templatePath);

            File.Copy(templatePath, outputPath, true);

            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int totalReplacements = 0;

            using (var doc = SpreadsheetDocument.Open(outputPath, true))
            {
                var workbookPart = doc.WorkbookPart;
                if (workbookPart != null)
                {
                    var sstPart = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();
                    var sst = sstPart != null ? sstPart.SharedStringTable : null;

                    foreach (var worksheetPart in workbookPart.WorksheetParts)
                    {
                        var sheetData = worksheetPart.Worksheet != null
                            ? worksheetPart.Worksheet.GetFirstChild<SheetData>()
                            : null;
                        if (sheetData == null)
                            continue;

                        foreach (var row in sheetData.Elements<Row>())
                        {
                            foreach (var cell in row.Elements<Cell>())
                            {
                                var cellText = GetCellText(cell, sst);
                                if (string.IsNullOrEmpty(cellText) || !cellText.Contains("{{"))
                                    continue;

                                bool matched;
                                var newText = MergeTextHelper.SinglePassReplace(
                                    cellText, data, out matched, usedKeys,
                                    count => totalReplacements += count);

                                if (matched && newText != cellText)
                                    SetCellText(cell, newText);
                            }
                        }

                        worksheetPart.Worksheet.Save();
                    }
                }
            }

            var unresolved = ScanUnresolvedXlsx(outputPath);
            return new MergeResult(totalReplacements, unresolved, usedKeys);
        }

        /// <summary>Read a single cell value by 1-based row and column.</summary>
        public string ReadCell(string sheetName, int row, int column)
        {
            var part = ExcelCellOperations.GetWorksheetPart(Document, sheetName);
            var sheetData = part.Worksheet.GetFirstChild<SheetData>();
            if (sheetData == null)
                return string.Empty;

            var cellRef = ExcelCellOperations.CellReference(row, column);
            var cell = ExcelCellOperations.FindCell(sheetData, cellRef);
            if (cell == null)
                return string.Empty;
            return GetCellText(cell, ExcelCellOperations.GetSharedStringTable(Document));
        }

        /// <summary>
        /// Write a single cell value by 1-based row and column. Numeric-looking
        /// input is stored as a number; everything else becomes an inline string.
        /// </summary>
        public void WriteCell(string sheetName, int row, int column, string value)
        {
            EnsureEditable();
            var part = ExcelCellOperations.GetWorksheetPart(Document, sheetName);
            var sheetData = ExcelCellOperations.GetOrCreateSheetData(part);
            var cellRef = ExcelCellOperations.CellReference(row, column);
            var cell = ExcelCellOperations.FindOrCreateCell(sheetData, cellRef);
            ExcelCellOperations.SetCellValue(cell, value);
            part.Worksheet.Save();
        }

        /// <summary>
        /// Read a rectangular range by 1-based start/end row and column
        /// (inclusive). The returned grid is always rectangular; empty cells
        /// read as empty strings.
        /// </summary>
        public ExcelReadRangeResult ReadRange(string sheetName, int startRow, int startColumn, int endRow, int endColumn)
        {
            if (endRow < startRow)
                throw new ArgumentException("endRow must be >= startRow.", "endRow");
            if (endColumn < startColumn)
                throw new ArgumentException("endColumn must be >= startColumn.", "endColumn");

            var part = ExcelCellOperations.GetWorksheetPart(Document, sheetName);
            var sheetData = part.Worksheet.GetFirstChild<SheetData>();
            var sst = ExcelCellOperations.GetSharedStringTable(Document);

            var cells = new List<IReadOnlyList<string>>();
            for (int r = startRow; r <= endRow; r++)
            {
                var rowValues = new List<string>();
                for (int c = startColumn; c <= endColumn; c++)
                {
                    if (sheetData == null)
                    {
                        rowValues.Add(string.Empty);
                        continue;
                    }
                    var cell = ExcelCellOperations.FindCell(sheetData, ExcelCellOperations.CellReference(r, c));
                    rowValues.Add(cell != null ? GetCellText(cell, sst) : string.Empty);
                }
                cells.Add(rowValues);
            }

            return new ExcelReadRangeResult
            {
                Cells = cells,
                RowCount = endRow - startRow + 1,
                ColumnCount = endColumn - startColumn + 1
            };
        }

        /// <summary>
        /// Write a rectangular grid of values anchored at 1-based startRow and
        /// startColumn. <paramref name="values"/> is row-major; inner lists may
        /// be ragged (missing trailing cells are skipped).
        /// </summary>
        public void WriteRange(string sheetName, int startRow, int startColumn, IReadOnlyList<IReadOnlyList<string>> values)
        {
            if (values == null)
                throw new ArgumentNullException("values");

            EnsureEditable();
            var part = ExcelCellOperations.GetWorksheetPart(Document, sheetName);
            var sheetData = ExcelCellOperations.GetOrCreateSheetData(part);

            for (int r = 0; r < values.Count; r++)
            {
                var rowValues = values[r];
                if (rowValues == null)
                    continue;
                for (int c = 0; c < rowValues.Count; c++)
                {
                    var cellRef = ExcelCellOperations.CellReference(startRow + r, startColumn + c);
                    var cell = ExcelCellOperations.FindOrCreateCell(sheetData, cellRef);
                    ExcelCellOperations.SetCellValue(cell, rowValues[c]);
                }
            }

            part.Worksheet.Save();
        }

        /// <summary>
        /// Write a formula into a cell (with or without a leading '='). The
        /// cached value is cleared and fullCalcOnLoad is set so Excel
        /// recalculates on open. Call Save() to persist.
        /// </summary>
        public void SetCellFormula(string sheetName, int row, int column, string formula)
        {
            if (string.IsNullOrWhiteSpace(formula))
                throw new ArgumentException("formula is required.", "formula");

            EnsureEditable();
            var part = ExcelCellOperations.GetWorksheetPart(Document, sheetName);
            var sheetData = ExcelCellOperations.GetOrCreateSheetData(part);
            var cell = ExcelCellOperations.FindOrCreateCell(sheetData, ExcelCellOperations.CellReference(row, column));

            var text = formula.Trim();
            if (text.Length > 1 && text[0] == '=')
                text = text.Substring(1);

            cell.CellFormula = new CellFormula(text);
            cell.CellValue = null;
            cell.RemoveAllChildren<InlineString>();
            cell.DataType = null;

            EnsureFullCalcOnLoad();
            part.Worksheet.Save();
        }

        /// <summary>
        /// Read the formula from a cell, returned with a leading '='; null when
        /// the cell holds no formula.
        /// </summary>
        public string ReadCellFormula(string sheetName, int row, int column)
        {
            var part = ExcelCellOperations.GetWorksheetPart(Document, sheetName);
            var sheetData = part.Worksheet.GetFirstChild<SheetData>();
            if (sheetData == null)
                return null;

            var cell = ExcelCellOperations.FindCell(sheetData, ExcelCellOperations.CellReference(row, column));
            if (cell == null || cell.CellFormula == null)
                return null;
            return "=" + (cell.CellFormula.Text ?? string.Empty);
        }

        /// <summary>Read a ListObject table by name or 1-based sheet index.</summary>
        public ExcelTableData ReadTable(string sheetName, string tableNameOrIndex)
        {
            return ExcelTableOperations.ReadTable(Document, sheetName, tableNameOrIndex);
        }

        /// <summary>
        /// Create a ListObject table with a header row. <paramref name="startCell"/>
        /// is the top-left cell (e.g. "A1"); <paramref name="headers"/> sets the
        /// column count and names; <paramref name="rows"/> holds the data rows.
        /// </summary>
        public ExcelTableResult AddTable(
            string sheetName,
            string startCell,
            IReadOnlyList<string> headers,
            IReadOnlyList<IReadOnlyList<string>> rows,
            string name)
        {
            EnsureEditable();
            return ExcelTableOperations.AddTable(Document, sheetName, startCell, headers, rows, name);
        }

        /// <summary>
        /// Write data rows into an existing table (by name or 1-based index),
        /// overwriting the data area and expanding the table downward when more
        /// rows are supplied than currently fit.
        /// </summary>
        public ExcelTableResult WriteTable(
            string sheetName,
            string tableNameOrIndex,
            IReadOnlyList<IReadOnlyList<string>> rows)
        {
            EnsureEditable();
            return ExcelTableOperations.WriteTable(Document, sheetName, tableNameOrIndex, rows);
        }

        /// <summary>
        /// Shift rows down starting at 1-based <paramref name="startRow"/> by
        /// <paramref name="count"/> rows, renumbering cell references. Returns
        /// the number of rows shifted. Formulas are not remapped (M3 scope).
        /// </summary>
        public int InsertRows(string sheetName, int startRow, int count)
        {
            EnsureEditable();
            return ExcelAdvancedOperations.InsertRows(Document, sheetName, startRow, count);
        }

        /// <summary>Add a workbook-scoped defined name for a range on a sheet.</summary>
        public string AddNamedRange(string name, string sheetName, string range)
        {
            EnsureEditable();
            return ExcelAdvancedOperations.AddNamedRange(Document, name, sheetName, range);
        }

        /// <summary>
        /// Add a data validation rule to a range. Supported types: list, whole,
        /// decimal, textLength, date, time, custom.
        /// </summary>
        public void AddDataValidation(string sheetName, string range, string validationType, string formula1, string formula2)
        {
            EnsureEditable();
            ExcelAdvancedOperations.AddDataValidation(Document, sheetName, range, validationType, formula1, formula2);
        }

        /// <summary>Add a data-bar conditional formatting rule to a range.</summary>
        public void AddDataBarConditionalFormatting(string sheetName, string range, string color)
        {
            EnsureEditable();
            ExcelAdvancedOperations.AddDataBarConditionalFormatting(Document, sheetName, range, color);
        }

        /// <summary>
        /// Add a single-series clustered column chart anchored to a worksheet
        /// and return its 1-based chart index on that sheet.
        /// </summary>
        public int AddChart(string sheetName, IReadOnlyList<string> categories, IReadOnlyList<double> values, string seriesName, string title)
        {
            EnsureEditable();
            return ExcelChartOperations.AddChart(Document, sheetName, categories, values, seriesName, title);
        }

        /// <summary>Read chart metadata (type, title, series count) from a worksheet.</summary>
        public IReadOnlyList<ExcelChartInfo> ReadCharts(string sheetName)
        {
            return ExcelChartOperations.ReadCharts(Document, sheetName);
        }

        private void EnsureEditable()
        {
            if (!IsEditable)
                throw new InvalidOperationException("Document was opened read-only and cannot be modified.");
        }

        private void EnsureFullCalcOnLoad()
        {
            var workbookPart = Document.WorkbookPart;
            if (workbookPart == null || workbookPart.Workbook == null)
                return;

            var workbook = workbookPart.Workbook;
            var calcPr = workbook.GetFirstChild<CalculationProperties>();
            if (calcPr == null)
            {
                calcPr = new CalculationProperties { FullCalculationOnLoad = true };
                InsertCalcPrInOrder(workbook, calcPr);
            }
            else
            {
                calcPr.FullCalculationOnLoad = true;
            }
        }

        private static void InsertCalcPrInOrder(Workbook workbook, CalculationProperties calcPr)
        {
            OpenXmlElement anchor = null;
            foreach (var child in workbook.ChildElements)
            {
                if (child is OleSize
                    || child is CustomWorkbookViews
                    || child is PivotCaches
                    || child is WebPublishing
                    || child is FileRecoveryProperties
                    || child is WorkbookExtensionList)
                {
                    anchor = child;
                    break;
                }
            }

            if (anchor != null)
                workbook.InsertBefore(calcPr, anchor);
            else
                workbook.AppendChild(calcPr);
        }

        internal static string GetCellText(Cell cell, SharedStringTable sst)
        {
            if (cell.DataType != null && cell.DataType.Value == CellValues.InlineString)
                return cell.InlineString != null ? cell.InlineString.InnerText : string.Empty;

            var value = cell.CellValue != null ? cell.CellValue.Text ?? string.Empty : string.Empty;

            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString && sst != null)
            {
                int idx;
                if (int.TryParse(value, out idx))
                {
                    var item = sst.Elements<SharedStringItem>().ElementAtOrDefault(idx);
                    return item != null ? item.InnerText : value;
                }
            }

            return value;
        }

        internal static void SetCellText(Cell cell, string text)
        {
            // Write as an inline string to avoid shared string table complexity.
            cell.DataType = CellValues.InlineString;
            cell.CellValue = null;
            cell.InlineString = new InlineString(new Text(text));
        }

        private static List<string> ScanUnresolvedXlsx(string filePath)
        {
            var unresolved = new HashSet<string>(StringComparer.Ordinal);
            using (var doc = SpreadsheetDocument.Open(filePath, false))
            {
                var workbookPart = doc.WorkbookPart;
                if (workbookPart != null)
                {
                    var sstPart = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();
                    var sst = sstPart != null ? sstPart.SharedStringTable : null;

                    foreach (var worksheetPart in workbookPart.WorksheetParts)
                    {
                        var sheetData = worksheetPart.Worksheet != null
                            ? worksheetPart.Worksheet.GetFirstChild<SheetData>()
                            : null;
                        if (sheetData == null)
                            continue;

                        foreach (var row in sheetData.Elements<Row>())
                        {
                            foreach (var cell in row.Elements<Cell>())
                            {
                                var text = GetCellText(cell, sst);
                                foreach (Match match in MergeTextHelper.PlaceholderPattern.Matches(text))
                                    unresolved.Add(match.Groups[1].Value);
                            }
                        }
                    }
                }
            }

            var list = unresolved.ToList();
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
