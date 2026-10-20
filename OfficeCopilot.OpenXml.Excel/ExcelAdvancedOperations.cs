using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace OfficeCopilot.OpenXml.Excel
{
    /// <summary>
    /// Advanced (E5) operations for the Excel file engine: row insertion,
    /// defined names, data validation and data-bar conditional formatting.
    /// Ported from officecli ExcelHandler.Add.cs / Add.Cf.cs and reduced to the
    /// explicit-API surface.
    /// </summary>
    internal static class ExcelAdvancedOperations
    {
        // Worksheet children that must appear after dataValidations /
        // conditionalFormatting in schema order. Used to place new containers.
        private static readonly Type[] ConditionalFormattingAnchors = new Type[]
        {
            typeof(DataValidations), typeof(Hyperlinks), typeof(PrintOptions), typeof(PageMargins),
            typeof(PageSetup), typeof(HeaderFooter), typeof(RowBreaks), typeof(ColumnBreaks),
            typeof(CustomProperties), typeof(CellWatches), typeof(IgnoredErrors),
            typeof(DocumentFormat.OpenXml.Spreadsheet.Drawing), typeof(LegacyDrawing),
            typeof(LegacyDrawingHeaderFooter), typeof(Picture), typeof(OleObjects), typeof(Controls),
            typeof(WebPublishItems), typeof(TableParts), typeof(WorksheetExtensionList)
        };

        private static readonly Type[] DataValidationAnchors = new Type[]
        {
            typeof(Hyperlinks), typeof(PrintOptions), typeof(PageMargins),
            typeof(PageSetup), typeof(HeaderFooter), typeof(RowBreaks), typeof(ColumnBreaks),
            typeof(CustomProperties), typeof(CellWatches), typeof(IgnoredErrors),
            typeof(DocumentFormat.OpenXml.Spreadsheet.Drawing), typeof(LegacyDrawing),
            typeof(LegacyDrawingHeaderFooter), typeof(Picture), typeof(OleObjects), typeof(Controls),
            typeof(WebPublishItems), typeof(TableParts), typeof(WorksheetExtensionList)
        };

        private static readonly Type[] WorkbookDefinedNamesAnchors = new Type[]
        {
            typeof(CalculationProperties), typeof(OleSize), typeof(CustomWorkbookViews),
            typeof(PivotCaches), typeof(WebPublishing), typeof(FileRecoveryProperties),
            typeof(WorkbookExtensionList)
        };

        /// <summary>
        /// Shift <paramref name="count"/> rows down starting at 1-based
        /// <paramref name="startRow"/>. Returns the number of rows shifted.
        /// Cell references are renumbered with their rows; formulas and other
        /// range-bearing structures are not remapped (M3 scope).
        /// </summary>
        internal static int InsertRows(SpreadsheetDocument doc, string sheetName, int startRow, int count)
        {
            if (startRow < 1)
                throw new ArgumentOutOfRangeException("startRow", "startRow must be >= 1.");
            if (count < 1)
                throw new ArgumentOutOfRangeException("count", "count must be >= 1.");

            var part = ExcelCellOperations.GetWorksheetPart(doc, sheetName);
            var sheetData = ExcelCellOperations.GetOrCreateSheetData(part);

            var rows = sheetData.Elements<Row>()
                .Where(r => r.RowIndex != null && r.RowIndex.Value >= (uint)startRow)
                .OrderByDescending(r => r.RowIndex.Value)
                .ToList();

            foreach (var row in rows)
            {
                uint oldIndex = row.RowIndex.Value;
                uint newIndex = oldIndex + (uint)count;
                foreach (var cell in row.Elements<Cell>())
                {
                    if (cell.CellReference == null || cell.CellReference.Value == null)
                        continue;
                    int r, c;
                    ExcelCellOperations.ParseCellReference(cell.CellReference.Value, out r, out c);
                    if (r >= startRow)
                        cell.CellReference = ExcelCellOperations.CellReference(r + count, c);
                }
                row.RowIndex = newIndex;
            }

            part.Worksheet.Save();
            return rows.Count;
        }

        /// <summary>
        /// Add a workbook-scoped defined name pointing at a range on a sheet.
        /// Returns the name. Throws when the name already exists.
        /// </summary>
        internal static string AddNamedRange(SpreadsheetDocument doc, string name, string sheetName, string range)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required.", "name");
            if (string.IsNullOrWhiteSpace(range))
                throw new ArgumentException("range is required.", "range");

            var workbookPart = doc.WorkbookPart;
            if (workbookPart == null || workbookPart.Workbook == null)
                throw new InvalidOperationException("Workbook not found.");

            var workbook = workbookPart.Workbook;
            var definedNames = workbook.GetFirstChild<DefinedNames>();
            if (definedNames == null)
            {
                definedNames = new DefinedNames();
                InsertBeforeFirst(workbook, definedNames, WorkbookDefinedNamesAnchors);
            }

            foreach (var existing in definedNames.Elements<DefinedName>())
            {
                if (existing.Name != null && string.Equals(existing.Name.Value, name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(string.Format("Defined name '{0}' already exists.", name));
            }

            // Always quote the sheet name: Excel accepts 'Sheet1'!A1:B2.
            string reference = "'" + sheetName + "'!" + range.ToUpperInvariant();
            definedNames.AppendChild(new DefinedName(reference) { Name = name });
            workbook.Save();
            return name;
        }

        /// <summary>
        /// Add a data validation rule to a range. Supported types: list, whole,
        /// decimal, textLength, date, time, custom.
        /// </summary>
        internal static void AddDataValidation(
            SpreadsheetDocument doc,
            string sheetName,
            string range,
            string validationType,
            string formula1,
            string formula2)
        {
            var part = ExcelCellOperations.GetWorksheetPart(doc, sheetName);
            var worksheet = part.Worksheet;

            var validation = new DataValidation
            {
                Type = ParseValidationType(validationType),
                AllowBlank = true,
                ShowErrorMessage = true,
                ShowInputMessage = false,
                SequenceOfReferences = new ListValue<StringValue>(new[] { new StringValue(range.ToUpperInvariant()) })
            };
            if (formula1 != null)
                validation.Formula1 = new Formula1(formula1);
            if (formula2 != null)
                validation.Formula2 = new Formula2(formula2);

            var container = worksheet.GetFirstChild<DataValidations>();
            if (container == null)
            {
                container = new DataValidations();
                InsertBeforeFirst(worksheet, container, DataValidationAnchors);
            }
            container.AppendChild(validation);
            container.Count = (uint)container.Elements<DataValidation>().Count();
            worksheet.Save();
        }

        /// <summary>
        /// Add a data-bar conditional formatting rule to a range. The color is a
        /// 6-digit RGB hex (optionally #-prefixed or 8-digit ARGB).
        /// </summary>
        internal static void AddDataBarConditionalFormatting(SpreadsheetDocument doc, string sheetName, string range, string color)
        {
            var part = ExcelCellOperations.GetWorksheetPart(doc, sheetName);
            var worksheet = part.Worksheet;

            var rule = new ConditionalFormattingRule
            {
                Type = ConditionalFormatValues.DataBar,
                Priority = NextCfPriority(worksheet)
            };

            var dataBar = new DataBar();
            dataBar.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Min });
            dataBar.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Max });
            dataBar.Append(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = NormalizeArgb(color) });
            rule.Append(dataBar);

            var conditionalFormatting = new ConditionalFormatting(rule)
            {
                SequenceOfReferences = new ListValue<StringValue>(new[] { new StringValue(range.ToUpperInvariant()) })
            };

            InsertBeforeFirst(worksheet, conditionalFormatting, ConditionalFormattingAnchors);
            worksheet.Save();
        }

        private static DataValidationValues ParseValidationType(string validationType)
        {
            switch ((validationType ?? "list").Trim().ToLowerInvariant())
            {
                case "list":
                    return DataValidationValues.List;
                case "whole":
                    return DataValidationValues.Whole;
                case "decimal":
                    return DataValidationValues.Decimal;
                case "textlength":
                case "text_length":
                    return DataValidationValues.TextLength;
                case "date":
                    return DataValidationValues.Date;
                case "time":
                    return DataValidationValues.Time;
                case "custom":
                    return DataValidationValues.Custom;
                default:
                    throw new ArgumentException(string.Format(
                        "Unknown validation type '{0}'. Valid: list, whole, decimal, textLength, date, time, custom.", validationType));
            }
        }

        private static int NextCfPriority(Worksheet worksheet)
        {
            int max = 0;
            foreach (var cf in worksheet.Elements<ConditionalFormatting>())
            {
                foreach (var rule in cf.Elements<ConditionalFormattingRule>())
                {
                    if (rule.Priority != null && rule.Priority.Value > max)
                        max = rule.Priority.Value;
                }
            }
            return max + 1;
        }

        private static string NormalizeArgb(string color)
        {
            var c = (color ?? "638EC6").Trim().TrimStart('#');
            if (c.Length == 6)
                return "FF" + c.ToUpperInvariant();
            if (c.Length == 8)
                return c.ToUpperInvariant();
            return "FF638EC6";
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
