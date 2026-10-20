using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace OfficeCopilot.Documents.Markdown
{
    public sealed class SpreadsheetMarkdownConverter : ICancellableDocumentMarkdownConverter
    {
        public bool CanConvert(string extension)
        {
            return string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);
        }

        public MarkdownDocument Convert(string path, MarkdownConversionOptions options)
        {
            return Convert(path, options, CancellationToken.None);
        }

        public MarkdownDocument Convert(string path, MarkdownConversionOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A workbook path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The workbook does not exist.", path);
            }

            options = options ?? new MarkdownConversionOptions();
            var fileInfo = new FileInfo(path);
            if (options.MaxFileBytes > 0 && fileInfo.Length > options.MaxFileBytes)
            {
                throw new InvalidDataException("The workbook exceeds the configured file-size limit.");
            }
            OpenXmlPackageSizeGuard.EnsureWithinLimit(path, options.MaxUncompressedPackageBytes, cancellationToken);

            var result = new MarkdownDocument { SourceFormat = Path.GetExtension(path).TrimStart('.').ToLowerInvariant() };
            var outline = new List<string>();

            using (var document = SpreadsheetDocument.Open(path, false))
            {
                var workbookPart = document.WorkbookPart;
                if (workbookPart == null || workbookPart.Workbook == null || workbookPart.Workbook.Sheets == null)
                {
                    throw new InvalidDataException("The workbook does not contain a readable workbook part.");
                }

                var sharedStrings = workbookPart.SharedStringTablePart == null
                    ? new List<string>()
                    : workbookPart.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>()
                        .Select(item => item.InnerText ?? string.Empty)
                        .ToList();
                var styles = workbookPart.WorkbookStylesPart == null ? null : workbookPart.WorkbookStylesPart.Stylesheet;
                var date1904 = workbookPart.Workbook.WorkbookProperties != null
                    && workbookPart.Workbook.WorkbookProperties.Date1904 != null
                    && workbookPart.Workbook.WorkbookProperties.Date1904.Value;

                var sectionIndex = 1;
                var hiddenSheets = new List<string>();
                var selectedWorksheetFound = string.IsNullOrWhiteSpace(options.WorksheetName);
                foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sheetName = sheet.Name == null ? "Sheet" + sectionIndex.ToString(CultureInfo.InvariantCulture) : sheet.Name.Value;
                    var isHidden = sheet.State != null && sheet.State.Value != SheetStateValues.Visible;
                    if (isHidden && !options.IncludeHiddenSheets)
                    {
                        hiddenSheets.Add(sheetName);
                        continue;
                    }

                    var currentSectionIndex = sectionIndex++;
                    if (!string.IsNullOrWhiteSpace(options.WorksheetName)
                        && !string.Equals(sheetName, options.WorksheetName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    selectedWorksheetFound = true;

                    var worksheetPart = workbookPart.GetPartById(sheet.Id.Value) as WorksheetPart;
                    if (worksheetPart == null)
                    {
                        result.Warnings.Add("Worksheet '" + sheetName + "' could not be opened and was skipped.");
                        continue;
                    }

                    var markdown = ConvertWorksheet(worksheetPart, sheetName, currentSectionIndex, sharedStrings, styles, date1904, options, cancellationToken);
                    result.Sections.Add(new MarkdownSection
                    {
                        Index = currentSectionIndex,
                        Title = sheetName,
                        Markdown = markdown
                    });
                    outline.Add(sheetName);
                }

                if (!selectedWorksheetFound)
                {
                    throw new ArgumentException("The requested worksheet was not found or is hidden.", nameof(options.WorksheetName));
                }

                if (hiddenSheets.Count > 0)
                {
                    result.Warnings.Add("Hidden worksheets omitted: " + string.Join(", ", hiddenSheets));
                }
            }

            result.Outline = outline.Count == 0
                ? "Workbook contains no visible worksheets."
                : "Worksheets: " + string.Join("; ", outline);

            // Truncate per worksheet so section indexes and titles stay valid for sectionIndex/range fetches.
            var remainingCharacters = Math.Max(0, options.MaxTotalChars - result.Outline.Length);
            for (var index = 0; index < result.Sections.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var section = result.Sections[index];
                var sectionMarkdown = section.Markdown ?? string.Empty;
                if (sectionMarkdown.Length <= remainingCharacters)
                {
                    remainingCharacters -= sectionMarkdown.Length;
                    continue;
                }

                section.Markdown = sectionMarkdown.Substring(0, remainingCharacters).TrimEnd()
                    + "\n\n[Content truncated at the configured character limit.]";
                result.Truncated = true;
                var omitted = new List<string>();
                for (var omittedIndex = index + 1; omittedIndex < result.Sections.Count; omittedIndex++)
                {
                    var omittedSection = result.Sections[omittedIndex];
                    omittedSection.Markdown = "## Sheet: " + EscapeMarkdown(omittedSection.Title)
                        + "\n\n[Worksheet content omitted at the character limit; request an A1 range with sectionIndex "
                        + omittedSection.Index.ToString(CultureInfo.InvariantCulture) + ".]";
                    omitted.Add(omittedSection.Title);
                }

                if (omitted.Count > 0)
                {
                    result.Warnings.Add("Worksheet content omitted after the character limit: " + string.Join(", ", omitted));
                }
                break;
            }

            return result;
        }

        private static string ConvertWorksheet(
            WorksheetPart worksheetPart,
            string sheetName,
            int sectionIndex,
            IList<string> sharedStrings,
            Stylesheet styles,
            bool date1904,
            MarkdownConversionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var builder = new StringBuilder();
            var worksheet = worksheetPart == null ? null : worksheetPart.Worksheet;
            var selectedRange = ParseCellRange(options.CellRange);
            var firstColumn = selectedRange == null ? 1 : selectedRange.StartColumn;
            var mergedRanges = worksheet == null
                ? new List<string>()
                : worksheet.Elements<MergeCells>()
                    .SelectMany(mergeCells => mergeCells.Elements<MergeCell>())
                    .Where(mergeCell => mergeCell.Reference != null)
                    .Select(mergeCell => mergeCell.Reference.Value)
                    .Where(reference => selectedRange == null || RangesIntersect(selectedRange, ParseCellRange(reference)))
                    .ToList();
            var populatedRegions = selectedRange == null ? FindPopulatedRegions(worksheet, cancellationToken) : new List<CellRange>();
            var sheetData = worksheet == null ? null : worksheet.GetFirstChild<SheetData>();
            var rows = new List<Row>();
            if (sheetData != null && selectedRange == null)
            {
                foreach (var row in sheetData.Elements<Row>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row.Elements<Cell>().Any(HasCellContent))
                    {
                        rows.Add(row);
                        if (rows.Count > options.MaxSheetRows)
                        {
                            break;
                        }
                    }
                }
            }
            else if (sheetData != null)
            {
                var selectedRows = new Dictionary<int, Row>();
                foreach (var row in sheetData.Elements<Row>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row.RowIndex != null
                        && row.RowIndex.Value >= selectedRange.StartRow
                        && row.RowIndex.Value <= selectedRange.EndRow)
                    {
                        selectedRows[(int)row.RowIndex.Value] = row;
                    }
                }
                var rowCount = selectedRange.EndRow - selectedRange.StartRow + 1;
                var visibleRowCount = Math.Min(rowCount, Math.Max(0, options.MaxSheetRows) + 1);
                for (var rowIndex = 0; rowIndex < visibleRowCount; rowIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Row row;
                    var requestedRow = selectedRange.StartRow + rowIndex;
                    rows.Add(selectedRows.TryGetValue(requestedRow, out row) ? row : new Row { RowIndex = (uint)requestedRow });
                }
            }

            var rowsTruncated = selectedRange == null
                ? rows.Count > options.MaxSheetRows
                : selectedRange.EndRow - selectedRange.StartRow + 1 > options.MaxSheetRows;
            if (rowsTruncated)
            {
                rows = rows.Take(Math.Max(0, options.MaxSheetRows)).ToList();
            }

            var maxColumn = selectedRange == null ? GetDimensionColumn(worksheet) : selectedRange.EndColumn;
            if (selectedRange == null && maxColumn == 0)
            {
                maxColumn = rows.SelectMany(row => row.Elements<Cell>())
                    .Select(cell => GetColumnIndex(cell.CellReference == null ? null : cell.CellReference.Value))
                    .DefaultIfEmpty(0)
                    .Max();
            }
            maxColumn = selectedRange == null
                ? Math.Min(Math.Max(maxColumn, 1), options.MaxSheetColumns)
                : Math.Min(maxColumn, firstColumn + options.MaxSheetColumns - 1);

            builder.Append("## Sheet: ");
            builder.Append(EscapeMarkdown(sheetName));
            if (selectedRange != null)
            {
                builder.Append(" (");
                builder.Append(options.CellRange.Replace("$", string.Empty).ToUpperInvariant());
                builder.Append(')');
            }
            builder.AppendLine();
            builder.AppendLine();

            if (mergedRanges.Count > 0)
            {
                builder.Append("> Merged ranges: ");
                builder.AppendLine(string.Join(", ", mergedRanges));
                builder.AppendLine();
            }

            if (populatedRegions.Count > 1)
            {
                builder.Append("> Detected populated regions: ");
                builder.AppendLine(string.Join("; ", populatedRegions.Select(FormatCellRange)));
                builder.AppendLine();
            }

            if (selectedRange == null)
            {
                AppendChartMarkdown(builder, worksheetPart, options);
            }

            if (rows.Count == 0)
            {
                builder.AppendLine("_(No populated cells)_");
                return builder.ToString().TrimEnd();
            }

            var headerCells = new List<string> { "Row" };
            for (var column = firstColumn; column <= maxColumn; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                headerCells.Add(GetColumnName(column));
            }
            MarkdownWriter.AppendRow(builder, headerCells, cancellationToken);
            MarkdownWriter.AppendSeparator(builder, headerCells.Count, new[] { 0 }, cancellationToken);

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rowValues = new List<string>
                {
                    row.RowIndex == null ? string.Empty : row.RowIndex.Value.ToString(CultureInfo.InvariantCulture)
                };

                var values = new Dictionary<int, string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var column = GetColumnIndex(cell.CellReference == null ? null : cell.CellReference.Value);
                    if (column >= firstColumn && column <= maxColumn)
                    {
                        values[column] = GetCellText(cell, sharedStrings, styles, date1904, options.IncludeFormulas);
                    }
                }

                for (var column = firstColumn; column <= maxColumn; column++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string value;
                    values.TryGetValue(column, out value);
                    rowValues.Add(value ?? string.Empty);
                }
                MarkdownWriter.AppendRow(builder, rowValues, cancellationToken);
            }

            if (rowsTruncated)
            {
                builder.AppendLine();
                if (selectedRange != null)
                {
                    builder.AppendLine("> Requested range truncated to the configured row limit.");
                }
                else
                {
                    builder.Append("> Showing the first ");
                    builder.Append(options.MaxSheetRows.ToString(CultureInfo.InvariantCulture));
                    builder.AppendLine(" populated rows; additional rows were omitted.");
                    AppendColumnStatistics(builder, worksheet, sharedStrings, styles, maxColumn, cancellationToken);
                }
            }

            if (selectedRange != null && selectedRange.EndColumn > maxColumn)
            {
                builder.AppendLine();
                builder.AppendLine("> Requested range truncated to the configured column limit.");
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendChartMarkdown(StringBuilder builder, WorksheetPart worksheetPart, MarkdownConversionOptions options)
        {
            var drawingsPart = worksheetPart == null ? null : worksheetPart.DrawingsPart;
            ChartMarkdownRenderer.Append(builder, drawingsPart == null ? null : drawingsPart.ChartParts, options.MaxSheetRows);
        }

        private static void AppendColumnStatistics(
            StringBuilder builder,
            Worksheet worksheet,
            IList<string> sharedStrings,
            Stylesheet styles,
            int maxColumn,
            CancellationToken cancellationToken)
        {
            var statistics = Enumerable.Range(0, maxColumn).Select(_ => new ColumnStatistics()).ToArray();
            var sheetData = worksheet == null ? null : worksheet.GetFirstChild<SheetData>();
            if (sheetData == null)
            {
                return;
            }

            foreach (var row in sheetData.Elements<Row>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var cell in row.Elements<Cell>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var column = GetColumnIndex(cell.CellReference == null ? null : cell.CellReference.Value);
                    if (column < 1 || column > maxColumn || !HasCellContent(cell))
                    {
                        continue;
                    }

                    statistics[column - 1].Add(cell, sharedStrings, styles);
                }
            }

            if (!statistics.Any(item => item.NonEmptyCount > 0))
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("**Column statistics (all populated rows)**");
            builder.AppendLine("| Column | Type | Non-empty | Min | Max | Sum |");
            builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
            for (var index = 0; index < statistics.Length; index++)
            {
                var item = statistics[index];
                if (item.NonEmptyCount == 0)
                {
                    continue;
                }

                builder.Append("| ").Append(GetColumnName(index + 1)).Append(" | ")
                    .Append(item.GetTypeName()).Append(" | ")
                    .Append(item.NonEmptyCount.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(item.HasNumericValues ? FormatStatistic(item.Minimum) : string.Empty).Append(" | ")
                    .Append(item.HasNumericValues ? FormatStatistic(item.Maximum) : string.Empty).Append(" | ")
                    .Append(item.HasNumericValues ? FormatStatistic(item.Sum) : string.Empty).AppendLine(" |");
            }
        }

        private static string FormatStatistic(double value)
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }

        private sealed class ColumnStatistics
        {
            public int NonEmptyCount { get; private set; }
            public bool HasNumericValues { get; private set; }
            public double Minimum { get; private set; }
            public double Maximum { get; private set; }
            public double Sum { get; private set; }

            private int _numberCount;
            private int _dateCount;
            private int _textCount;
            private int _booleanCount;
            private int _formulaOnlyCount;

            public void Add(Cell cell, IList<string> sharedStrings, Stylesheet styles)
            {
                NonEmptyCount++;
                var value = GetCellRawValue(cell);
                if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString
                    && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex)
                    && sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count)
                {
                    value = sharedStrings[sharedStringIndex];
                }
                else if (cell.DataType != null && cell.DataType.Value == CellValues.InlineString)
                {
                    value = cell.InlineString == null ? string.Empty : cell.InlineString.InnerText;
                }

                if (cell.DataType != null && cell.DataType.Value == CellValues.Boolean)
                {
                    _booleanCount++;
                    return;
                }

                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && (cell.DataType == null || cell.DataType.Value == CellValues.Number))
                {
                    var styleIndex = cell.StyleIndex == null ? 0 : (int)cell.StyleIndex.Value;
                    var cellFormat = styles == null || styles.CellFormats == null
                        ? null
                        : styles.CellFormats.Elements<CellFormat>().ElementAtOrDefault(styleIndex);
                    var numberFormatId = cellFormat == null || cellFormat.NumberFormatId == null
                        ? 0
                        : (uint)cellFormat.NumberFormatId.Value;
                    var formatCode = GetNumberFormatCode(styles, numberFormatId);
                    if (IsDateFormat(numberFormatId, formatCode))
                    {
                        _dateCount++;
                        return;
                    }

                    _numberCount++;
                    if (!HasNumericValues)
                    {
                        Minimum = Maximum = number;
                        HasNumericValues = true;
                    }
                    else
                    {
                        Minimum = Math.Min(Minimum, number);
                        Maximum = Math.Max(Maximum, number);
                    }
                    Sum += number;
                    return;
                }

                if (string.IsNullOrWhiteSpace(value) && cell.CellFormula != null)
                {
                    _formulaOnlyCount++;
                }
                else
                {
                    _textCount++;
                }
            }

            public string GetTypeName()
            {
                var types = new List<string>();
                if (_numberCount > 0) types.Add("Number");
                if (_dateCount > 0) types.Add("Date");
                if (_textCount > 0) types.Add("Text");
                if (_booleanCount > 0) types.Add("Boolean");
                if (_formulaOnlyCount > 0) types.Add("Formula");
                return types.Count > 1 ? "Mixed" : types[0];
            }
        }

        private static string GetCellText(Cell cell, IList<string> sharedStrings, Stylesheet styles, bool date1904, bool includeFormula)
        {
            var value = GetCellRawValue(cell);
            if (cell.DataType != null)
            {
                if (cell.DataType.Value == CellValues.SharedString
                    && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex)
                    && sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count)
                {
                    value = sharedStrings[sharedStringIndex];
                }
                else if (cell.DataType.Value == CellValues.InlineString)
                {
                    value = cell.InlineString == null ? string.Empty : cell.InlineString.InnerText;
                }
                else if (cell.DataType.Value == CellValues.Boolean)
                {
                    value = value == "1" ? "TRUE" : "FALSE";
                }
            }
            else
            {
                value = FormatNumericCell(cell, value, styles, date1904);
            }

            var formula = cell.CellFormula == null ? string.Empty : cell.CellFormula.Text;
            if (includeFormula && !string.IsNullOrWhiteSpace(formula))
            {
                value = string.IsNullOrWhiteSpace(value) ? "=" + formula : value + " (=" + formula + ")";
            }

            return value ?? string.Empty;
        }

        private static string FormatNumericCell(Cell cell, string value, Stylesheet styles, bool date1904)
        {
            if (string.IsNullOrWhiteSpace(value)
                || cell.DataType != null && cell.DataType.Value != CellValues.Number
                || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return value;
            }

            var styleIndex = cell.StyleIndex == null ? 0 : (int)cell.StyleIndex.Value;
            var cellFormats = styles == null ? null : styles.CellFormats;
            var cellFormat = cellFormats == null
                ? null
                : cellFormats.Elements<CellFormat>().ElementAtOrDefault(styleIndex);
            var numberFormatId = cellFormat == null || cellFormat.NumberFormatId == null
                ? 0
                : (uint)cellFormat.NumberFormatId.Value;
            var formatCode = GetNumberFormatCode(styles, numberFormatId);

            if (IsDateFormat(numberFormatId, formatCode))
            {
                return FormatDateCell(number, numberFormatId, formatCode, date1904);
            }

            var numericFormat = NormalizeNumericFormat(SelectFormatSection(formatCode, number));
            if (string.IsNullOrWhiteSpace(numericFormat))
            {
                return value;
            }

            try
            {
                return number.ToString(numericFormat, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return value;
            }
        }

        private static string GetNumberFormatCode(Stylesheet styles, uint numberFormatId)
        {
            var builtIn = GetBuiltInNumberFormat(numberFormatId);
            if (builtIn != null)
            {
                return builtIn;
            }

            if (styles == null || styles.NumberingFormats == null)
            {
                return null;
            }

            var format = styles.NumberingFormats.Elements<NumberingFormat>()
                .FirstOrDefault(item => item.NumberFormatId != null && item.NumberFormatId.Value == numberFormatId);
            return format == null || format.FormatCode == null ? null : format.FormatCode.Value;
        }

        private static string GetBuiltInNumberFormat(uint numberFormatId)
        {
            switch (numberFormatId)
            {
                case 0: return "General";
                case 1: return "0";
                case 2: return "0.00";
                case 3: return "#,##0";
                case 4: return "#,##0.00";
                case 5: return "$#,##0;($#,##0)";
                case 6: return "$#,##0;($#,##0)";
                case 7: return "$#,##0.00;($#,##0.00)";
                case 8: return "$#,##0.00;($#,##0.00)";
                case 9: return "0%";
                case 10: return "0.00%";
                case 11: return "0.00E+00";
                case 14: return "yyyy-MM-dd";
                case 15: return "d-MMM-yy";
                case 16: return "d-MMM";
                case 17: return "MMM-yy";
                case 18: return "h:mm tt";
                case 19: return "h:mm:ss tt";
                case 20: return "H:mm";
                case 21: return "H:mm:ss";
                case 22: return "yyyy-MM-dd H:mm";
                case 37: return "#,##0;(#,##0)";
                case 38: return "#,##0;(#,##0)";
                case 39: return "#,##0.00;(#,##0.00)";
                case 40: return "#,##0.00;(#,##0.00)";
                case 45: return "mm:ss";
                case 46: return "[h]:mm:ss";
                case 47: return "mm:ss.0";
                case 48: return "##0.0E+0";
                case 49: return "@";
                default: return null;
            }
        }

        private static bool IsDateFormat(uint numberFormatId, string formatCode)
        {
            if (numberFormatId >= 14 && numberFormatId <= 22 || numberFormatId >= 45 && numberFormatId <= 47)
            {
                return true;
            }

            var code = StripFormatLiteralsAndDirectives(formatCode).ToLowerInvariant();
            return code.IndexOf('y') >= 0
                || code.IndexOf('d') >= 0
                || code.IndexOf('h') >= 0
                || code.IndexOf('s') >= 0
                || code.IndexOf('m') >= 0 && code.IndexOf('/') >= 0;
        }

        private static string FormatDateCell(double serial, uint numberFormatId, string formatCode, bool date1904)
        {
            if (numberFormatId >= 45 && numberFormatId <= 47)
            {
                return FormatElapsedTime(serial, numberFormatId);
            }

            var pattern = GetDateTimePattern(numberFormatId, SelectFormatSection(formatCode, serial));
            try
            {
                if (!date1904 && Math.Floor(serial) == 60)
                {
                    return "1900-02-29" + (serial % 1 == 0 ? string.Empty : " " + TimeSpan.FromDays(serial % 1).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
                }

                var adjustedSerial = !date1904 && serial >= 60 ? serial - 1 : serial;
                var epoch = date1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 31);
                return epoch.AddDays(adjustedSerial).ToString(pattern, CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return serial.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string GetDateTimePattern(uint numberFormatId, string formatCode)
        {
            if (numberFormatId >= 14 && numberFormatId <= 22)
            {
                return GetBuiltInNumberFormat(numberFormatId);
            }

            var code = StripFormatDirectives(formatCode);
            var hasAmPm = code.IndexOf("AM/PM", StringComparison.OrdinalIgnoreCase) >= 0;
            var lower = code.ToLowerInvariant();
            var hasDate = lower.IndexOf('y') >= 0 || lower.IndexOf('d') >= 0;
            var hasTime = lower.IndexOf('h') >= 0 || lower.IndexOf('s') >= 0 || hasAmPm;
            var builder = new StringBuilder();

            for (var index = 0; index < code.Length;)
            {
                if (code[index] == '"')
                {
                    var end = code.IndexOf('"', index + 1);
                    if (end < 0)
                    {
                        builder.Append(code.Substring(index));
                        break;
                    }
                    builder.Append(code.Substring(index, end - index + 1));
                    index = end + 1;
                    continue;
                }

                if (string.Compare(code, index, "AM/PM", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    builder.Append("tt");
                    index += 5;
                    continue;
                }

                var character = code[index];
                if (character == 'm' || character == 'M')
                {
                    var previousHour = lower.LastIndexOf('h', index);
                    var previousDate = Math.Max(lower.LastIndexOf('y', index), lower.LastIndexOf('d', index));
                    var isMinute = hasTime && (!hasDate || previousHour > previousDate);
                    builder.Append(isMinute ? 'm' : 'M');
                }
                else if (character == 'h' || character == 'H')
                {
                    builder.Append(hasAmPm ? 'h' : 'H');
                }
                else
                {
                    builder.Append(character);
                }
                index++;
            }

            var pattern = builder.ToString();
            return string.IsNullOrWhiteSpace(pattern) ? (hasTime && !hasDate ? "H:mm:ss" : "yyyy-MM-dd") : pattern;
        }

        private static string FormatElapsedTime(double serial, uint numberFormatId)
        {
            var elapsed = TimeSpan.FromDays(Math.Abs(serial));
            var hours = (long)Math.Floor(elapsed.TotalHours);
            var prefix = serial < 0 ? "-" : string.Empty;
            if (numberFormatId == 45)
            {
                return prefix + elapsed.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture);
            }
            if (numberFormatId == 46)
            {
                return prefix + hours.ToString(CultureInfo.InvariantCulture) + ":" + elapsed.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture);
            }

            var tenths = elapsed.Milliseconds / 100;
            return prefix + elapsed.Minutes.ToString("00", CultureInfo.InvariantCulture) + elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture) + "." + tenths.ToString(CultureInfo.InvariantCulture);
        }

        private static string SelectFormatSection(string formatCode, double value)
        {
            if (string.IsNullOrWhiteSpace(formatCode))
            {
                return formatCode;
            }

            var sections = new List<string>();
            var builder = new StringBuilder();
            var inQuotes = false;
            var inBrackets = false;
            var escaped = false;
            foreach (var character in formatCode)
            {
                if (escaped)
                {
                    builder.Append(character);
                    escaped = false;
                }
                else if (character == '\\')
                {
                    builder.Append(character);
                    escaped = true;
                }
                else if (character == '"')
                {
                    inQuotes = !inQuotes;
                    builder.Append(character);
                }
                else if (!inQuotes && character == '[')
                {
                    inBrackets = true;
                    builder.Append(character);
                }
                else if (!inQuotes && character == ']')
                {
                    inBrackets = false;
                    builder.Append(character);
                }
                else if (!inQuotes && !inBrackets && character == ';')
                {
                    sections.Add(builder.ToString());
                    builder.Clear();
                }
                else
                {
                    builder.Append(character);
                }
            }
            sections.Add(builder.ToString());

            if (value < 0 && sections.Count > 1)
            {
                return sections[1];
            }
            if (value == 0 && sections.Count > 2)
            {
                return sections[2];
            }
            return sections[0];
        }

        private static string StripFormatLiteralsAndDirectives(string formatCode)
        {
            var builder = new StringBuilder();
            var inQuotes = false;
            var inBrackets = false;
            var escaped = false;
            foreach (var character in formatCode ?? string.Empty)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && character == '[')
                {
                    inBrackets = true;
                }
                else if (!inQuotes && character == ']')
                {
                    inBrackets = false;
                }
                else if (!inQuotes && !inBrackets)
                {
                    builder.Append(character);
                }
            }
            return builder.ToString();
        }

        private static string StripFormatDirectives(string formatCode)
        {
            var builder = new StringBuilder();
            var inBrackets = false;
            var inQuotes = false;
            var escaped = false;
            foreach (var character in formatCode ?? string.Empty)
            {
                if (escaped)
                {
                    builder.Append('\\').Append(character);
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inQuotes = !inQuotes;
                    builder.Append(character);
                }
                else if (!inQuotes && character == '[')
                {
                    inBrackets = true;
                }
                else if (!inQuotes && character == ']')
                {
                    inBrackets = false;
                }
                else if (!inBrackets)
                {
                    builder.Append(character);
                }
            }
            if (escaped)
            {
                builder.Append('\\');
            }
            return builder.ToString();
        }

        private static string NormalizeNumericFormat(string formatCode)
        {
            if (string.IsNullOrWhiteSpace(formatCode) || string.Equals(formatCode, "General", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var builder = new StringBuilder();
            var inQuotes = false;
            var inBrackets = false;
            for (var index = 0; index < formatCode.Length; index++)
            {
                var character = formatCode[index];
                if (character == '"')
                {
                    inQuotes = !inQuotes;
                    builder.Append(character);
                }
                else if (!inQuotes && character == '[')
                {
                    inBrackets = true;
                }
                else if (!inQuotes && character == ']')
                {
                    inBrackets = false;
                }
                else if (inBrackets)
                {
                    continue;
                }
                else if (!inQuotes && (character == '_' || character == '*'))
                {
                    index++;
                }
                else
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private static string GetCellRawValue(Cell cell)
        {
            if (cell == null)
            {
                return string.Empty;
            }

            if (cell.DataType != null && cell.DataType.Value == CellValues.InlineString)
            {
                return cell.InlineString == null ? string.Empty : cell.InlineString.InnerText;
            }

            return cell.CellValue == null ? string.Empty : cell.CellValue.Text;
        }

        private static bool HasCellContent(Cell cell)
        {
            return !string.IsNullOrWhiteSpace(GetCellRawValue(cell))
                || cell.CellFormula != null && !string.IsNullOrWhiteSpace(cell.CellFormula.Text);
        }

        private static string EscapeMarkdown(string value)
        {
            return MarkdownWriter.Escape(value);
        }

        private static int GetDimensionColumn(Worksheet worksheet)
        {
            var reference = worksheet == null || worksheet.SheetDimension == null || worksheet.SheetDimension.Reference == null
                ? string.Empty
                : worksheet.SheetDimension.Reference.Value;
            if (string.IsNullOrWhiteSpace(reference))
            {
                return 0;
            }

            var endpoint = reference.Split(':').Last();
            return GetColumnIndex(endpoint);
        }

        private static int GetColumnIndex(string cellReference)
        {
            if (string.IsNullOrWhiteSpace(cellReference))
            {
                return 0;
            }

            var column = 0;
            foreach (var character in cellReference)
            {
                if (character < 'A' || character > 'Z')
                {
                    if (character < 'a' || character > 'z')
                    {
                        break;
                    }
                    column = column * 26 + character - 'a' + 1;
                }
                else
                {
                    column = column * 26 + character - 'A' + 1;
                }
            }
            return column;
        }

        private static CellRange ParseCellRange(string range)
        {
            if (string.IsNullOrWhiteSpace(range))
            {
                return null;
            }

            var endpoints = range.Replace("$", string.Empty).Trim().Split(':');
            if (endpoints.Length < 1 || endpoints.Length > 2)
            {
                throw new ArgumentException("Cell range must be a single A1 address or an A1:A1 range.", nameof(range));
            }

            int startRow;
            int startColumn;
            int endRow;
            int endColumn;
            if (!TryParseCellAddress(endpoints[0], out startRow, out startColumn)
                || !TryParseCellAddress(endpoints.Length == 1 ? endpoints[0] : endpoints[1], out endRow, out endColumn)
                || endRow < startRow
                || endColumn < startColumn)
            {
                throw new ArgumentException("Cell range must contain valid, ordered A1 addresses.", nameof(range));
            }

            return new CellRange(startRow, startColumn, endRow, endColumn);
        }

        private static bool TryParseCellAddress(string address, out int row, out int column)
        {
            row = 0;
            column = 0;
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            var splitIndex = 0;
            while (splitIndex < address.Length && char.IsLetter(address[splitIndex]))
            {
                splitIndex++;
            }

            return splitIndex > 0
                && splitIndex < address.Length
                && int.TryParse(address.Substring(splitIndex), NumberStyles.None, CultureInfo.InvariantCulture, out row)
                && row > 0
                && row <= 1048576
                && (column = GetColumnIndex(address.Substring(0, splitIndex))) > 0
                && column <= 16384;
        }

        private static bool RangesIntersect(CellRange left, CellRange right)
        {
            return right != null
                && left.StartRow <= right.EndRow && right.StartRow <= left.EndRow
                && left.StartColumn <= right.EndColumn && right.StartColumn <= left.EndColumn;
        }

        private static List<CellRange> FindPopulatedRegions(Worksheet worksheet, CancellationToken cancellationToken)
        {
            var sheetData = worksheet == null ? null : worksheet.GetFirstChild<SheetData>();
            if (sheetData == null)
            {
                return new List<CellRange>();
            }

            var populatedCells = new List<CellPosition>();
            foreach (var row in sheetData.Elements<Row>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var cell in row.Elements<Cell>().Where(HasCellContent))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int rowIndex;
                    int columnIndex;
                    if (TryParseCellAddress(cell.CellReference == null ? null : cell.CellReference.Value, out rowIndex, out columnIndex))
                    {
                        populatedCells.Add(new CellPosition(rowIndex, columnIndex));
                    }
                }
            }

            var regions = new List<CellRange>();
            var currentRowGroup = new List<CellPosition>();
            foreach (var cell in populatedCells.OrderBy(item => item.Row).ThenBy(item => item.Column))
            {
                if (currentRowGroup.Count > 0 && cell.Row > currentRowGroup[currentRowGroup.Count - 1].Row + 1)
                {
                    AddColumnRegions(currentRowGroup, regions);
                    currentRowGroup.Clear();
                }
                currentRowGroup.Add(cell);
            }
            AddColumnRegions(currentRowGroup, regions);
            return regions;
        }

        private static void AddColumnRegions(IList<CellPosition> rowGroup, IList<CellRange> regions)
        {
            if (rowGroup == null || rowGroup.Count == 0)
            {
                return;
            }

            var startRow = rowGroup[0].Row;
            var endRow = rowGroup[rowGroup.Count - 1].Row;
            var orderedCells = rowGroup.OrderBy(item => item.Column).ThenBy(item => item.Row).ToList();
            var startColumn = orderedCells[0].Column;
            var endColumn = startColumn;
            foreach (var cell in orderedCells.Skip(1))
            {
                if (cell.Column > endColumn + 1)
                {
                    regions.Add(new CellRange(startRow, startColumn, endRow, endColumn));
                    startColumn = cell.Column;
                }
                endColumn = Math.Max(endColumn, cell.Column);
            }
            regions.Add(new CellRange(startRow, startColumn, endRow, endColumn));
        }

        private static string FormatCellRange(CellRange range)
        {
            var start = GetColumnName(range.StartColumn) + range.StartRow.ToString(CultureInfo.InvariantCulture);
            if (range.StartRow == range.EndRow && range.StartColumn == range.EndColumn)
            {
                return start;
            }

            return start + ":" + GetColumnName(range.EndColumn) + range.EndRow.ToString(CultureInfo.InvariantCulture);
        }

        private sealed class CellPosition
        {
            public CellPosition(int row, int column)
            {
                Row = row;
                Column = column;
            }

            public int Row { get; private set; }
            public int Column { get; private set; }
        }

        private sealed class CellRange
        {
            public CellRange(int startRow, int startColumn, int endRow, int endColumn)
            {
                StartRow = startRow;
                StartColumn = startColumn;
                EndRow = endRow;
                EndColumn = endColumn;
            }

            public int StartRow { get; private set; }
            public int StartColumn { get; private set; }
            public int EndRow { get; private set; }
            public int EndColumn { get; private set; }
        }

        private static string GetColumnName(int column)
        {
            var name = string.Empty;
            while (column > 0)
            {
                column--;
                name = (char)('A' + column % 26) + name;
                column /= 26;
            }
            return name;
        }
    }
}
