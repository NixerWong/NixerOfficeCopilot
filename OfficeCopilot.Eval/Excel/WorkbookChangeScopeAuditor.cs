using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace OfficeCopilot.Eval.Excel
{
    internal sealed class WorkbookChangeScopeAuditResult
    {
        public int ChangedCellCount { get; set; }

        public int OutOfScopeCellCount { get; set; }

        public int ChangedWorksheetCount { get; set; }

        public int OutOfScopeWorksheetCount { get; set; }

        public bool IsWithinScope
        {
            get
            {
                return OutOfScopeCellCount == 0 && OutOfScopeWorksheetCount == 0;
            }
        }
    }

    internal static class WorkbookChangeScopeAuditor
    {
        private const string RelationshipNamespace =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        private static readonly Regex _cellRange = new Regex(
            @"^\$?(?<firstColumn>[A-Za-z]{1,3})\$?(?<firstRow>\d+)(?::\$?(?<lastColumn>[A-Za-z]{1,3})\$?(?<lastRow>\d+))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static void ValidateExpectedScope(IList expectedChangeScope)
        {
            ReadExpectedScope(expectedChangeScope);
        }

        public static WorkbookChangeScopeAuditResult Compare(
            string baselinePath,
            string outputPath,
            IList expectedChangeScope)
        {
            if (string.IsNullOrWhiteSpace(baselinePath))
            {
                throw new ArgumentException("A baseline workbook path is required.", nameof(baselinePath));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("An output workbook path is required.", nameof(outputPath));
            }

            var allowedChanges = ReadExpectedScope(expectedChangeScope);
            var baseline = ReadWorkbook(baselinePath);
            var output = ReadWorkbook(outputPath);
            var result = new WorkbookChangeScopeAuditResult();
            var sheetNames = new HashSet<string>(baseline.Keys, StringComparer.OrdinalIgnoreCase);
            sheetNames.UnionWith(output.Keys);

            foreach (var sheetName in sheetNames)
            {
                SheetSnapshot baselineSheet;
                SheetSnapshot outputSheet;
                var hadBaselineSheet = baseline.TryGetValue(sheetName, out baselineSheet);
                var hasOutputSheet = output.TryGetValue(sheetName, out outputSheet);
                if (!hadBaselineSheet || !hasOutputSheet)
                {
                    result.ChangedWorksheetCount++;
                    var changedSheet = hasOutputSheet ? outputSheet : baselineSheet;
                    result.ChangedCellCount += changedSheet.Cells.Count;
                    if (hadBaselineSheet || !AllowsWorksheetAddition(allowedChanges, sheetName))
                    {
                        result.OutOfScopeWorksheetCount++;
                        result.OutOfScopeCellCount += changedSheet.Cells.Count;
                    }

                    continue;
                }

                var addresses = new HashSet<string>(
                    baselineSheet.Cells.Keys,
                    StringComparer.OrdinalIgnoreCase);
                addresses.UnionWith(outputSheet.Cells.Keys);
                foreach (var address in addresses)
                {
                    CellSnapshot baselineCell;
                    CellSnapshot outputCell;
                    var hadBaselineCell = baselineSheet.Cells.TryGetValue(address, out baselineCell);
                    var hasOutputCell = outputSheet.Cells.TryGetValue(address, out outputCell);
                    if (hadBaselineCell
                        && hasOutputCell
                        && baselineCell.HasSameContent(outputCell))
                    {
                        continue;
                    }

                    result.ChangedCellCount++;
                    if (!AllowsCell(allowedChanges, sheetName, address))
                    {
                        result.OutOfScopeCellCount++;
                    }
                }
            }

            return result;
        }

        private static IList<AllowedChange> ReadExpectedScope(IList expectedChangeScope)
        {
            if (expectedChangeScope == null || expectedChangeScope.Count == 0)
            {
                throw new InvalidDataException(
                    "Manual Eval tasks must declare a non-empty expectedChangeScope.");
            }

            var allowedChanges = new List<AllowedChange>();
            foreach (var item in expectedChangeScope)
            {
                var descriptor = item as IDictionary<string, object>;
                if (descriptor == null)
                {
                    throw new InvalidDataException(
                        "expectedChangeScope entries must be objects.");
                }

                object sheetValue;
                var sheetName = descriptor.TryGetValue("sheet", out sheetValue)
                    ? Convert.ToString(sheetValue, CultureInfo.InvariantCulture)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(sheetName))
                {
                    throw new InvalidDataException(
                        "expectedChangeScope entries must declare a sheet.");
                }

                object wholeSheetValue;
                var wholeSheet = descriptor.TryGetValue("wholeSheet", out wholeSheetValue)
                    && Equals(wholeSheetValue, true);
                if (descriptor.ContainsKey("wholeSheet") && !(wholeSheetValue is bool))
                {
                    throw new InvalidDataException(
                        "expectedChangeScope wholeSheet must be a boolean.");
                }

                object allowWorksheetAdditionValue;
                var allowWorksheetAddition =
                    descriptor.TryGetValue("allowWorksheetAddition", out allowWorksheetAdditionValue)
                    && Equals(allowWorksheetAdditionValue, true);
                if (descriptor.ContainsKey("allowWorksheetAddition")
                    && !(allowWorksheetAdditionValue is bool))
                {
                    throw new InvalidDataException(
                        "expectedChangeScope allowWorksheetAddition must be a boolean.");
                }
                object rangeValue;
                var rangeText = descriptor.TryGetValue("range", out rangeValue)
                    ? Convert.ToString(rangeValue, CultureInfo.InvariantCulture)
                    : string.Empty;
                CellRange range = null;
                var hasRange = !string.IsNullOrWhiteSpace(rangeText);
                if (wholeSheet == hasRange)
                {
                    throw new InvalidDataException(
                        "expectedChangeScope entry for '" + sheetName
                        + "' must specify exactly one of wholeSheet=true or range.");
                }

                if (!wholeSheet && !TryParseRange(rangeText, out range))
                {
                    throw new InvalidDataException(
                        "expectedChangeScope has an invalid cell range for '" + sheetName + "': " + rangeText);
                }

                if (allowWorksheetAddition && !wholeSheet)
                {
                    throw new InvalidDataException(
                        "expectedChangeScope can allow worksheet addition only with wholeSheet=true.");
                }

                allowedChanges.Add(new AllowedChange(
                    sheetName,
                    wholeSheet,
                    allowWorksheetAddition,
                    range));
            }

            return allowedChanges;
        }

        private static bool AllowsWholeSheet(IList<AllowedChange> allowedChanges, string sheetName)
        {
            foreach (var allowedChange in allowedChanges)
            {
                if (allowedChange.WholeSheet
                    && string.Equals(allowedChange.SheetName, sheetName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AllowsWorksheetAddition(
            IList<AllowedChange> allowedChanges,
            string sheetName)
        {
            foreach (var allowedChange in allowedChanges)
            {
                if (allowedChange.AllowWorksheetAddition
                    && string.Equals(allowedChange.SheetName, sheetName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AllowsCell(
            IList<AllowedChange> allowedChanges,
            string sheetName,
            string address)
        {
            int column;
            int row;
            if (!TryParseCellAddress(address, out column, out row))
            {
                throw new InvalidDataException("Workbook contains an invalid cell address: " + address);
            }

            foreach (var allowedChange in allowedChanges)
            {
                if (string.Equals(allowedChange.SheetName, sheetName, StringComparison.OrdinalIgnoreCase)
                    && (allowedChange.WholeSheet || allowedChange.Range.Contains(column, row)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseRange(string value, out CellRange range)
        {
            range = null;
            var match = _cellRange.Match(value ?? string.Empty);
            if (!match.Success)
            {
                return false;
            }

            int firstColumn;
            int firstRow;
            if (!TryParseCellParts(
                match.Groups["firstColumn"].Value,
                match.Groups["firstRow"].Value,
                out firstColumn,
                out firstRow))
            {
                return false;
            }

            int lastColumn;
            int lastRow;
            var hasEnd = match.Groups["lastColumn"].Success;
            if (hasEnd)
            {
                if (!TryParseCellParts(
                    match.Groups["lastColumn"].Value,
                    match.Groups["lastRow"].Value,
                    out lastColumn,
                    out lastRow))
                {
                    return false;
                }
            }
            else
            {
                lastColumn = firstColumn;
                lastRow = firstRow;
            }

            if (lastColumn < firstColumn || lastRow < firstRow)
            {
                return false;
            }

            range = new CellRange(firstColumn, firstRow, lastColumn, lastRow);
            return true;
        }

        private static bool TryParseCellAddress(string value, out int column, out int row)
        {
            var match = _cellRange.Match(value ?? string.Empty);
            if (!match.Success || match.Groups["lastColumn"].Success)
            {
                column = 0;
                row = 0;
                return false;
            }

            return TryParseCellParts(
                match.Groups["firstColumn"].Value,
                match.Groups["firstRow"].Value,
                out column,
                out row);
        }

        private static bool TryParseCellParts(
            string columnText,
            string rowText,
            out int column,
            out int row)
        {
            column = 0;
            row = 0;
            foreach (var character in columnText.ToUpperInvariant())
            {
                column = checked(column * 26 + character - 'A' + 1);
            }

            return int.TryParse(rowText, NumberStyles.None, CultureInfo.InvariantCulture, out row)
                && row > 0;
        }

        private static Dictionary<string, SheetSnapshot> ReadWorkbook(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                var workbook = LoadXml(archive, "xl/workbook.xml");
                var relationships = ReadRelationships(archive);
                var sharedStrings = ReadSharedStrings(archive);
                var sheets = new Dictionary<string, SheetSnapshot>(StringComparer.OrdinalIgnoreCase);
                foreach (XmlNode node in workbook.GetElementsByTagName("*"))
                {
                    var sheetElement = node as XmlElement;
                    if (sheetElement == null || sheetElement.LocalName != "sheet")
                    {
                        continue;
                    }

                    var sheetName = sheetElement.GetAttribute("name");
                    var relationshipId = sheetElement.GetAttribute("id", RelationshipNamespace);
                    string target;
                    if (string.IsNullOrWhiteSpace(sheetName)
                        || !relationships.TryGetValue(relationshipId, out target))
                    {
                        throw new InvalidDataException(
                            "Workbook contains a worksheet without a valid relationship.");
                    }

                    var worksheet = LoadXml(archive, ResolveTarget(target));
                    sheets.Add(sheetName, ReadWorksheet(worksheet, sharedStrings));
                }

                return sheets;
            }
        }

        private static Dictionary<string, string> ReadRelationships(ZipArchive archive)
        {
            var relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XmlNode node in relationships.GetElementsByTagName("*"))
            {
                var relationship = node as XmlElement;
                if (relationship == null || relationship.LocalName != "Relationship")
                {
                    continue;
                }

                result.Add(relationship.GetAttribute("Id"), relationship.GetAttribute("Target"));
            }

            return result;
        }

        private static IList<string> ReadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return new List<string>();
            }

            var sharedStrings = LoadXml(entry);
            var result = new List<string>();
            foreach (XmlNode node in sharedStrings.GetElementsByTagName("*"))
            {
                var sharedString = node as XmlElement;
                if (sharedString != null && sharedString.LocalName == "si")
                {
                    result.Add(sharedString.InnerText);
                }
            }

            return result;
        }

        private static SheetSnapshot ReadWorksheet(XmlDocument worksheet, IList<string> sharedStrings)
        {
            var cells = new Dictionary<string, CellSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlNode node in worksheet.GetElementsByTagName("*"))
            {
                var cell = node as XmlElement;
                if (cell == null || cell.LocalName != "c")
                {
                    continue;
                }

                var address = cell.GetAttribute("r");
                if (string.IsNullOrWhiteSpace(address))
                {
                    throw new InvalidDataException("Worksheet cell is missing its address.");
                }

                var formulaElement = FindChild(cell, "f");
                var valueElement = FindChild(cell, "v");
                var inlineStringElement = FindChild(cell, "is");
                var formula = formulaElement == null ? string.Empty : formulaElement.InnerText;
                var formulaMetadata = formulaElement == null
                    ? string.Empty
                    : string.Join(
                        ";",
                        formulaElement.Attributes
                            .Cast<XmlAttribute>()
                            .OrderBy(attribute => attribute.Name, StringComparer.Ordinal)
                            .Select(attribute => attribute.Name + "=" + attribute.Value));
                var cellType = cell.GetAttribute("t");
                var normalizedCellType = cellType == "s" || cellType == "inlineStr" || cellType == "str"
                    ? "string"
                    : cellType == "n"
                        ? string.Empty
                        : cellType;
                var value = valueElement == null
                    ? string.Empty
                    : valueElement.InnerText;
                if (cellType == "s" && valueElement != null)
                {
                    int index;
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out index)
                        || index < 0
                        || index >= sharedStrings.Count)
                    {
                        throw new InvalidDataException(
                            "Worksheet cell has an invalid shared-string index at " + address + ".");
                    }

                    value = sharedStrings[index];
                }
                else if (cellType == "inlineStr" && inlineStringElement != null)
                {
                    value = inlineStringElement.InnerText;
                }

                var style = cell.GetAttribute("s");
                if (formulaElement == null
                    && string.IsNullOrEmpty(value)
                    && string.IsNullOrEmpty(style))
                {
                    continue;
                }

                cells.Add(address, new CellSnapshot(
                    formulaElement != null,
                    formula,
                    formulaMetadata,
                    formulaElement == null ? value : string.Empty,
                    normalizedCellType,
                    style));
            }

            return new SheetSnapshot(cells);
        }

        private static XmlElement FindChild(XmlElement parent, string localName)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                var element = child as XmlElement;
                if (element != null && element.LocalName == localName)
                {
                    return element;
                }
            }

            return null;
        }

        private static XmlDocument LoadXml(ZipArchive archive, string entryName)
        {
            var entry = archive.GetEntry(entryName);
            if (entry == null)
            {
                throw new InvalidDataException("Workbook is missing package part: " + entryName);
            }

            return LoadXml(entry);
        }

        private static XmlDocument LoadXml(ZipArchiveEntry entry)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = true
            };
            var document = new XmlDocument
            {
                XmlResolver = null
            };
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, settings))
            {
                document.Load(reader);
            }

            return document;
        }

        private static string ResolveTarget(string target)
        {
            var normalizedTarget = (target ?? string.Empty).Replace('\\', '/');
            var segments = new List<string>();
            if (!normalizedTarget.StartsWith("/", StringComparison.Ordinal))
            {
                segments.Add("xl");
            }

            foreach (var segment in normalizedTarget.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count == 0)
                    {
                        throw new InvalidDataException("Workbook relationship target escapes its package.");
                    }

                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    segments.Add(segment);
                }
            }

            return string.Join("/", segments);
        }

        private sealed class AllowedChange
        {
            public AllowedChange(
                string sheetName,
                bool wholeSheet,
                bool allowWorksheetAddition,
                CellRange range)
            {
                SheetName = sheetName;
                WholeSheet = wholeSheet;
                AllowWorksheetAddition = allowWorksheetAddition;
                Range = range;
            }

            public string SheetName { get; private set; }

            public bool WholeSheet { get; private set; }

            public bool AllowWorksheetAddition { get; private set; }

            public CellRange Range { get; private set; }
        }

        private sealed class CellRange
        {
            public CellRange(int firstColumn, int firstRow, int lastColumn, int lastRow)
            {
                FirstColumn = firstColumn;
                FirstRow = firstRow;
                LastColumn = lastColumn;
                LastRow = lastRow;
            }

            private int FirstColumn { get; set; }

            private int FirstRow { get; set; }

            private int LastColumn { get; set; }

            private int LastRow { get; set; }

            public bool Contains(int column, int row)
            {
                return column >= FirstColumn && column <= LastColumn
                    && row >= FirstRow && row <= LastRow;
            }
        }

        private sealed class SheetSnapshot
        {
            public SheetSnapshot(IDictionary<string, CellSnapshot> cells)
            {
                Cells = new Dictionary<string, CellSnapshot>(cells, StringComparer.OrdinalIgnoreCase);
            }

            public IDictionary<string, CellSnapshot> Cells { get; private set; }
        }

        private sealed class CellSnapshot
        {
            public CellSnapshot(
                bool hasFormula,
                string formula,
                string formulaMetadata,
                string value,
                string cellType,
                string style)
            {
                HasFormula = hasFormula;
                Formula = formula;
                FormulaMetadata = formulaMetadata;
                Value = value;
                CellType = cellType;
                Style = style;
            }

            private bool HasFormula { get; set; }

            private string Formula { get; set; }

            private string FormulaMetadata { get; set; }

            private string Value { get; set; }

            private string CellType { get; set; }

            private string Style { get; set; }

            public bool HasSameContent(CellSnapshot other)
            {
                return other != null
                    && HasFormula == other.HasFormula
                    && string.Equals(Formula, other.Formula, StringComparison.Ordinal)
                    && string.Equals(FormulaMetadata, other.FormulaMetadata, StringComparison.Ordinal)
                    && string.Equals(Value, other.Value, StringComparison.Ordinal)
                    && string.Equals(CellType, other.CellType, StringComparison.Ordinal)
                    && string.Equals(Style, other.Style, StringComparison.Ordinal);
            }
        }
    }
}
