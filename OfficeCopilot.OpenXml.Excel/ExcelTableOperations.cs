using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace OfficeCopilot.OpenXml.Excel
{
    /// <summary>
    /// ListObject (table) operations for the Excel file engine. Ported from
    /// officecli ExcelHandler.Add.Tables / Query / Set.Tables and reduced to the
    /// explicit-API surface: find/create/read/write a table by name or 1-based
    /// index, always with a header row and the built-in TableStyleMedium2 style.
    /// </summary>
    internal static class ExcelTableOperations
    {
        private sealed class TableLocation
        {
            public WorksheetPart WorksheetPart;
            public TableDefinitionPart TablePart;
            public Table Table;
        }

        private sealed class RangeBounds
        {
            public int StartColumn;
            public int StartRow;
            public int EndColumn;
            public int EndRow;
        }

        internal static ExcelTableData ReadTable(SpreadsheetDocument doc, string sheetName, string tableNameOrIndex)
        {
            var found = FindTable(doc, sheetName, tableNameOrIndex);
            var reference = found.Table.Reference != null ? found.Table.Reference.Value : null;
            if (string.IsNullOrEmpty(reference))
                throw new InvalidOperationException("Table has no reference.");

            var bounds = ParseRangeBounds(reference);
            var hasHeader = found.Table.HeaderRowCount == null || found.Table.HeaderRowCount.Value != 0;

            var headers = new List<string>();
            var columns = found.Table.GetFirstChild<TableColumns>();
            if (columns != null)
            {
                foreach (var column in columns.Elements<TableColumn>())
                    headers.Add(column.Name != null ? column.Name.Value : string.Empty);
            }

            var sheetData = found.WorksheetPart.Worksheet.GetFirstChild<SheetData>();
            var sst = ExcelCellOperations.GetSharedStringTable(doc);
            var rows = new List<IReadOnlyList<string>>();
            int dataStart = hasHeader ? bounds.StartRow + 1 : bounds.StartRow;
            for (int r = dataStart; r <= bounds.EndRow; r++)
            {
                var rowValues = new List<string>();
                for (int c = bounds.StartColumn; c <= bounds.EndColumn; c++)
                {
                    if (sheetData == null)
                    {
                        rowValues.Add(string.Empty);
                        continue;
                    }
                    var cell = ExcelCellOperations.FindCell(sheetData, ExcelCellOperations.CellReference(r, c));
                    rowValues.Add(cell != null ? ExcelDocumentEngine.GetCellText(cell, sst) : string.Empty);
                }
                rows.Add(rowValues);
            }

            return new ExcelTableData
            {
                Name = found.Table.Name != null ? found.Table.Name.Value : string.Empty,
                DisplayName = found.Table.DisplayName != null ? found.Table.DisplayName.Value : string.Empty,
                Reference = reference,
                HasHeader = hasHeader,
                Headers = headers,
                Rows = rows
            };
        }

        internal static ExcelTableResult AddTable(
            SpreadsheetDocument doc,
            string sheetName,
            string startCell,
            IReadOnlyList<string> headers,
            IReadOnlyList<IReadOnlyList<string>> rows,
            string name)
        {
            if (headers == null || headers.Count == 0)
                throw new ArgumentException("headers is required and must contain at least one column.", "headers");

            int startRow, startColumn;
            ExcelCellOperations.ParseCellReference(startCell.ToUpperInvariant(), out startRow, out startColumn);

            int columnCount = headers.Count;
            int rowCount = 1 + (rows != null ? rows.Count : 0);
            int endRow = startRow + rowCount - 1;
            int endColumn = startColumn + columnCount - 1;
            string reference = startCell.ToUpperInvariant() + ":" + ExcelCellOperations.CellReference(endRow, endColumn);

            var worksheetPart = ExcelCellOperations.GetWorksheetPart(doc, sheetName);
            var sheetData = ExcelCellOperations.GetOrCreateSheetData(worksheetPart);

            // Header row: inline string so the visible text matches the
            // tableColumn name exactly (Excel rejects a mismatch).
            for (int c = 0; c < columnCount; c++)
            {
                var cell = ExcelCellOperations.FindOrCreateCell(
                    sheetData, ExcelCellOperations.CellReference(startRow, startColumn + c));
                ExcelDocumentEngine.SetCellText(cell, headers[c] ?? string.Empty);
            }

            // Data rows.
            if (rows != null)
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    var data = rows[r];
                    if (data == null)
                        continue;
                    for (int c = 0; c < columnCount && c < data.Count; c++)
                    {
                        var cell = ExcelCellOperations.FindOrCreateCell(
                            sheetData, ExcelCellOperations.CellReference(startRow + 1 + r, startColumn + c));
                        ExcelCellOperations.SetCellValue(cell, data[c]);
                    }
                }
            }

            var allTables = GetAllTables(doc);
            uint tableId = 1;
            foreach (var existing in allTables)
            {
                if (existing.Id != null && existing.Id.Value >= tableId)
                    tableId = existing.Id.Value + 1;
            }

            var tableName = string.IsNullOrWhiteSpace(name) ? "Table" + tableId : SanitizeTableName(name);
            EnsureTableNameUnique(allTables, tableName);

            var table = new Table
            {
                Id = tableId,
                Name = tableName,
                DisplayName = tableName,
                Reference = reference,
                HeaderRowCount = 1,
                TotalsRowShown = false
            };
            table.AppendChild(new AutoFilter { Reference = reference });

            var tableColumns = new TableColumns { Count = (uint)columnCount };
            for (int i = 0; i < columnCount; i++)
                tableColumns.AppendChild(new TableColumn { Id = (uint)(i + 1), Name = headers[i] ?? ("Column" + (i + 1)) });
            table.AppendChild(tableColumns);

            table.AppendChild(new TableStyleInfo
            {
                Name = "TableStyleMedium2",
                ShowFirstColumn = false,
                ShowLastColumn = false,
                ShowRowStripes = true,
                ShowColumnStripes = false
            });

            var tablePart = worksheetPart.AddNewPart<TableDefinitionPart>();
            tablePart.Table = table;
            tablePart.Table.Save();

            LinkTablePart(worksheetPart, tablePart);
            worksheetPart.Worksheet.Save();

            return new ExcelTableResult
            {
                Name = tableName,
                DisplayName = tableName,
                Reference = reference,
                RowCount = rowCount,
                ColumnCount = columnCount
            };
        }

        internal static ExcelTableResult WriteTable(
            SpreadsheetDocument doc,
            string sheetName,
            string tableNameOrIndex,
            IReadOnlyList<IReadOnlyList<string>> rows)
        {
            if (rows == null)
                throw new ArgumentNullException("rows");

            var found = FindTable(doc, sheetName, tableNameOrIndex);
            var reference = found.Table.Reference != null ? found.Table.Reference.Value : null;
            if (string.IsNullOrEmpty(reference))
                throw new InvalidOperationException("Table has no reference.");

            var bounds = ParseRangeBounds(reference);
            bool hasHeader = found.Table.HeaderRowCount == null || found.Table.HeaderRowCount.Value != 0;
            int dataStart = hasHeader ? bounds.StartRow + 1 : bounds.StartRow;
            int dataEnd = dataStart + rows.Count - 1;

            // Expand the table downward when more rows are written than fit.
            if (dataEnd > bounds.EndRow)
            {
                string newReference = ExcelCellOperations.CellReference(bounds.StartRow, bounds.StartColumn)
                    + ":" + ExcelCellOperations.CellReference(dataEnd, bounds.EndColumn);
                found.Table.Reference = newReference;
                var autoFilter = found.Table.GetFirstChild<AutoFilter>();
                if (autoFilter != null)
                    autoFilter.Reference = newReference;
            }

            var sheetData = ExcelCellOperations.GetOrCreateSheetData(found.WorksheetPart);
            int columnCount = bounds.EndColumn - bounds.StartColumn + 1;
            for (int r = 0; r < rows.Count; r++)
            {
                var data = rows[r];
                if (data == null)
                    continue;
                for (int c = 0; c < columnCount && c < data.Count; c++)
                {
                    var cell = ExcelCellOperations.FindOrCreateCell(
                        sheetData, ExcelCellOperations.CellReference(dataStart + r, bounds.StartColumn + c));
                    ExcelCellOperations.SetCellValue(cell, data[c]);
                }
            }

            found.TablePart.Table.Save();
            found.WorksheetPart.Worksheet.Save();

            return new ExcelTableResult
            {
                Name = found.Table.Name != null ? found.Table.Name.Value : string.Empty,
                DisplayName = found.Table.DisplayName != null ? found.Table.DisplayName.Value : string.Empty,
                Reference = found.Table.Reference != null ? found.Table.Reference.Value : string.Empty,
                RowCount = (dataEnd - bounds.StartRow + 1),
                ColumnCount = columnCount
            };
        }

        private static TableLocation FindTable(SpreadsheetDocument doc, string sheetName, string tableNameOrIndex)
        {
            var worksheetPart = ExcelCellOperations.GetWorksheetPart(doc, sheetName);
            var tableParts = worksheetPart.TableDefinitionParts.ToList();
            if (tableParts.Count == 0)
                throw new ArgumentException(string.Format("Sheet '{0}' has no tables.", sheetName), "tableNameOrIndex");

            int index;
            if (int.TryParse(tableNameOrIndex, out index))
            {
                if (index < 1 || index > tableParts.Count)
                    throw new ArgumentOutOfRangeException("tableNameOrIndex", string.Format(
                        "Table index {0} out of range (1..{1}).", index, tableParts.Count));
                return WrapLocation(worksheetPart, tableParts[index - 1]);
            }

            foreach (var tablePart in tableParts)
            {
                var table = tablePart.Table;
                if (table == null)
                    continue;
                if ((table.Name != null && string.Equals(table.Name.Value, tableNameOrIndex, StringComparison.OrdinalIgnoreCase))
                    || (table.DisplayName != null && string.Equals(table.DisplayName.Value, tableNameOrIndex, StringComparison.OrdinalIgnoreCase)))
                    return WrapLocation(worksheetPart, tablePart);
            }

            throw new ArgumentException(string.Format("Table not found: {0}", tableNameOrIndex), "tableNameOrIndex");
        }

        private static TableLocation WrapLocation(WorksheetPart worksheetPart, TableDefinitionPart tablePart)
        {
            return new TableLocation { WorksheetPart = worksheetPart, TablePart = tablePart, Table = tablePart.Table };
        }

        private static RangeBounds ParseRangeBounds(string reference)
        {
            var parts = reference.Split(':');
            if (parts.Length != 2)
                throw new ArgumentException(string.Format("Invalid range: {0}", reference), "reference");

            int r1, c1, r2, c2;
            ExcelCellOperations.ParseCellReference(parts[0], out r1, out c1);
            ExcelCellOperations.ParseCellReference(parts[1], out r2, out c2);

            return new RangeBounds
            {
                StartColumn = Math.Min(c1, c2),
                EndColumn = Math.Max(c1, c2),
                StartRow = Math.Min(r1, r2),
                EndRow = Math.Max(r1, r2)
            };
        }

        private static List<Table> GetAllTables(SpreadsheetDocument doc)
        {
            var result = new List<Table>();
            var workbookPart = doc.WorkbookPart;
            if (workbookPart == null)
                return result;

            foreach (var worksheetPart in workbookPart.WorksheetParts)
            {
                foreach (var tablePart in worksheetPart.TableDefinitionParts)
                {
                    if (tablePart.Table != null)
                        result.Add(tablePart.Table);
                }
            }
            return result;
        }

        private static void EnsureTableNameUnique(IEnumerable<Table> allTables, string tableName)
        {
            foreach (var table in allTables)
            {
                if ((table.Name != null && string.Equals(table.Name.Value, tableName, StringComparison.OrdinalIgnoreCase))
                    || (table.DisplayName != null && string.Equals(table.DisplayName.Value, tableName, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException(string.Format(
                        "Table name '{0}' already exists in the workbook; choose a different name.", tableName));
            }
        }

        private static string SanitizeTableName(string name)
        {
            var sb = new StringBuilder();
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '\\')
                    sb.Append(ch);
                else if (char.IsWhiteSpace(ch))
                    sb.Append('_');
            }

            var result = sb.ToString();
            if (result.Length == 0)
                return "Table";
            if (char.IsDigit(result[0]))
                result = "_" + result;
            return result;
        }

        private static void LinkTablePart(WorksheetPart worksheetPart, TableDefinitionPart tablePart)
        {
            var worksheet = worksheetPart.Worksheet;
            var tableParts = worksheet.GetFirstChild<TableParts>();
            if (tableParts == null)
            {
                tableParts = new TableParts();
                OpenXmlElement anchor = null;
                foreach (var child in worksheet.ChildElements)
                {
                    if (child is WorksheetExtensionList)
                    {
                        anchor = child;
                        break;
                    }
                }
                if (anchor != null)
                    worksheet.InsertBefore(tableParts, anchor);
                else
                    worksheet.AppendChild(tableParts);
            }

            tableParts.AppendChild(new TablePart { Id = worksheetPart.GetIdOfPart(tablePart) });
            tableParts.Count = (uint)tableParts.Elements<TablePart>().Count();
        }
    }
}
