using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace OfficeCopilot.OpenXml.Excel
{
    /// <summary>
    /// Shared cell/range plumbing for the Excel file engine. Ported from
    /// officecli ExcelHandler's cell helpers (ParseCellReference,
    /// ColumnNameToIndex, FindOrCreateRow/Cell) and reduced to the explicit-API
    /// surface: no path/selector DSL, just numeric rows/columns.
    /// </summary>
    internal static class ExcelCellOperations
    {
        private static readonly Regex CellRefPattern =
            new Regex(@"^([A-Za-z]+)(\d+)$", RegexOptions.Compiled);

        /// <summary>1-based column index from a column name (A=1, XFD=16384).</summary>
        internal static int ColumnNameToIndex(string col)
        {
            int result = 0;
            foreach (var c in col.ToUpperInvariant())
                result = result * 26 + (c - 'A' + 1);
            return result;
        }

        /// <summary>Column name from a 1-based column index (1=A, 16384=XFD).</summary>
        internal static string IndexToColumnName(int index)
        {
            var result = string.Empty;
            while (index > 0)
            {
                index--;
                result = (char)('A' + index % 26) + result;
                index /= 26;
            }
            return result;
        }

        internal static string CellReference(int row, int column)
        {
            ValidateRow(row);
            ValidateColumn(column);
            return IndexToColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse an A1-style reference ("B7") into 1-based row and column.
        /// Throws ArgumentException for malformed or out-of-range references.
        /// </summary>
        internal static void ParseCellReference(string cellRef, out int row, out int column)
        {
            if (cellRef == null)
                throw new ArgumentNullException("cellRef");

            var match = CellRefPattern.Match(cellRef);
            if (!match.Success)
                throw new ArgumentException(string.Format("Invalid cell reference: '{0}'. Expected format like 'A1'.", cellRef), "cellRef");

            if (!int.TryParse(match.Groups[2].Value, out row) || row < 1 || row > 1048576)
                throw new ArgumentException(string.Format("Invalid row in cell reference: '{0}'. Valid range: 1-1048576.", cellRef), "cellRef");

            column = ColumnNameToIndex(match.Groups[1].Value.ToUpperInvariant());
            if (column < 1 || column > 16384)
                throw new ArgumentException(string.Format("Invalid column in cell reference: '{0}'. Valid range: A-XFD.", cellRef), "cellRef");
        }

        private static void ValidateRow(int row)
        {
            if (row < 1 || row > 1048576)
                throw new ArgumentOutOfRangeException("row", string.Format(
                    "Row {0} is out of range. Valid range: 1-1048576.", row));
        }

        private static void ValidateColumn(int column)
        {
            if (column < 1 || column > 16384)
                throw new ArgumentOutOfRangeException("column", string.Format(
                    "Column {0} is out of range. Valid range: A-XFD (1-16384).", column));
        }

        internal static WorksheetPart GetWorksheetPart(SpreadsheetDocument doc, string sheetName)
        {
            if (doc == null)
                throw new ArgumentNullException("doc");
            if (string.IsNullOrWhiteSpace(sheetName))
                throw new ArgumentException("sheetName is required.", "sheetName");

            var workbookPart = doc.WorkbookPart;
            if (workbookPart == null || workbookPart.Workbook == null || workbookPart.Workbook.Sheets == null)
                throw new InvalidOperationException("Workbook has no sheets.");

            var sheet = workbookPart.Workbook.Sheets.Elements<Sheet>()
                .FirstOrDefault(s => s.Name != null && string.Equals(s.Name.Value, sheetName, StringComparison.OrdinalIgnoreCase));
            if (sheet == null)
                throw new ArgumentException(string.Format("Sheet not found: {0}", sheetName), "sheetName");

            if (sheet.Id == null || sheet.Id.Value == null)
                throw new InvalidOperationException(string.Format("Sheet '{0}' has no relationship id.", sheetName));

            var part = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
            if (part == null || part.Worksheet == null)
                throw new InvalidOperationException(string.Format("Worksheet part missing for sheet '{0}'.", sheetName));
            return part;
        }

        internal static SheetData GetOrCreateSheetData(WorksheetPart part)
        {
            var sheetData = part.Worksheet.GetFirstChild<SheetData>();
            if (sheetData == null)
            {
                sheetData = new SheetData();
                part.Worksheet.AppendChild(sheetData);
            }
            return sheetData;
        }

        internal static SharedStringTable GetSharedStringTable(SpreadsheetDocument doc)
        {
            var workbookPart = doc.WorkbookPart;
            if (workbookPart == null)
                return null;
            var sstPart = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();
            return sstPart != null ? sstPart.SharedStringTable : null;
        }

        /// <summary>Find an existing row by 1-based index, or null when absent.</summary>
        private static Row FindRow(SheetData sheetData, uint rowIndex)
        {
            foreach (var row in sheetData.Elements<Row>())
            {
                if (row.RowIndex != null && row.RowIndex.Value == rowIndex)
                    return row;
            }
            return null;
        }

        /// <summary>Find or create a row, inserting new rows in ascending index order.</summary>
        internal static Row FindOrCreateRow(SheetData sheetData, uint rowIndex)
        {
            var row = FindRow(sheetData, rowIndex);
            if (row != null)
                return row;

            row = new Row { RowIndex = rowIndex };
            Row after = null;
            foreach (var existing in sheetData.Elements<Row>())
            {
                if (existing.RowIndex != null && existing.RowIndex.Value < rowIndex)
                    after = existing;
                else
                    break;
            }

            if (after != null)
                after.InsertAfterSelf(row);
            else
                sheetData.InsertAt(row, 0);
            return row;
        }

        /// <summary>Find an existing cell by A1 reference, or null when absent.</summary>
        internal static Cell FindCell(SheetData sheetData, string cellRef)
        {
            foreach (var row in sheetData.Elements<Row>())
            {
                foreach (var cell in row.Elements<Cell>())
                {
                    if (cell.CellReference != null && string.Equals(cell.CellReference.Value, cellRef, StringComparison.OrdinalIgnoreCase))
                        return cell;
                }
            }
            return null;
        }

        /// <summary>Find or create a cell, inserting new cells in ascending column order.</summary>
        internal static Cell FindOrCreateCell(SheetData sheetData, string cellRef)
        {
            var match = CellRefPattern.Match(cellRef);
            if (!match.Success)
                throw new ArgumentException(string.Format("Invalid cell reference: '{0}'.", cellRef), "cellRef");

            uint rowIndex;
            if (!uint.TryParse(match.Groups[2].Value, out rowIndex) || rowIndex < 1 || rowIndex > 1048576)
                throw new ArgumentException(string.Format("Invalid row in cell reference: '{0}'.", cellRef), "cellRef");

            var column = ColumnNameToIndex(match.Groups[1].Value.ToUpperInvariant());
            var row = FindOrCreateRow(sheetData, rowIndex);

            var cell = row.Elements<Cell>().FirstOrDefault(c =>
                c.CellReference != null && string.Equals(c.CellReference.Value, cellRef, StringComparison.OrdinalIgnoreCase));
            if (cell != null)
                return cell;

            cell = new Cell { CellReference = cellRef.ToUpperInvariant() };
            Cell after = null;
            foreach (var existing in row.Elements<Cell>())
            {
                var existingRef = existing.CellReference != null ? existing.CellReference.Value : null;
                var existingCol = string.IsNullOrEmpty(existingRef)
                    ? int.MaxValue
                    : ColumnNameToIndex(CellRefPattern.Match(existingRef).Groups[1].Value.ToUpperInvariant());
                if (existingCol < column)
                    after = existing;
                else
                    break;
            }

            if (after != null)
                after.InsertAfterSelf(cell);
            else
                row.InsertAt(cell, 0);
            return cell;
        }

        /// <summary>
        /// Write a value to a cell. Numeric-looking input is stored as a number
        /// (no <c>t</c> attribute); everything else becomes an inline string.
        /// Clears any existing formula so a literal write never leaves a stale
        /// formula behind.
        /// </summary>
        internal static void SetCellValue(Cell cell, string value)
        {
            cell.CellFormula = null;
            cell.RemoveAllChildren<InlineString>();

            double number;
            if (value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                cell.CellValue = new CellValue(number.ToString(CultureInfo.InvariantCulture));
                cell.DataType = null;
            }
            else
            {
                cell.CellValue = null;
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text(value ?? string.Empty));
            }
        }
    }
}
