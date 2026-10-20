using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Tools;

namespace OfficeCopilot.Host.Excel
{
    public sealed partial class ExcelHostAdapter : ExcelServiceBase, ICopilotHostAdapter, IVbaExecutionHostAdapter, IStagedWriteProtectionHostAdapter, IVbaDeclaredWriteProtectionHostAdapter, IVbaEditorHostAdapter, IExcelToolHost, IExcelWriteEffectHostAdapter, IExcelWipRegistryHostAdapter
    {
        private static readonly ExcelWorksheetMapCache _workbookMapCache =
            new ExcelWorksheetMapCache();

        public ExcelHostAdapter(ExcelInterop.Application application)
            : base(application)
        {
            InitializeServices(application);
        }

        public string HostApplicationName
        {
            get { return "Excel"; }
        }

        public HostContextSnapshot GetContextSnapshot()
        {
            var workbookInfo = GetActiveWorkbookInfo();
            var selectionInfo = GetSelectionInfo();
            var workbookMapSummary = string.Empty;
            ExcelWorksheetListResult workbookMap = null;
            if (workbookInfo.IsAvailable)
            {
                try
                {
                    InvalidateWorkbookMapCache(
                        string.IsNullOrWhiteSpace(workbookInfo.FullName)
                            ? workbookInfo.Name
                            : workbookInfo.FullName);
                    workbookMap = ListWorksheets();
                    workbookMapSummary = BuildWorkbookMapSummary(workbookMap);
                }
                catch (Exception ex)
                {
                    workbookMapSummary = "工作簿地图读取失败：" + ex.Message;
                }
            }

            var snapshot = new HostContextSnapshot
            {
                HostApplicationName = HostApplicationName,
                DocumentName = workbookInfo.IsAvailable ? workbookInfo.Name : "未打开工作簿",
                WorksheetName = !string.IsNullOrWhiteSpace(selectionInfo.WorksheetName)
                    ? selectionInfo.WorksheetName
                    : (workbookInfo.IsAvailable ? workbookInfo.WorksheetName : "未激活工作表"),
                SelectionAddress = selectionInfo.IsAvailable ? selectionInfo.Address : "未选中单元格",
                SelectionPreview = selectionInfo.IsAvailable ? selectionInfo.Preview : "当前没有可用选区",
                WorkbookMapSummary = workbookMapSummary,
                WorkbookMap = workbookMap
            };

            snapshot.Summary = string.Format(
                "当前活动工作簿：{0}；当前工作表：{1}；当前选区：{2}",
                snapshot.DocumentName,
                snapshot.WorksheetName,
                snapshot.SelectionAddress ?? "-");

            return snapshot;
        }

        public ExcelWorkbookInfo GetActiveWorkbookInfo()
        {
            var workbook = _application != null ? _application.ActiveWorkbook : null;
            var worksheet = _application != null ? _application.ActiveSheet as ExcelInterop.Worksheet : null;

            return new ExcelWorkbookInfo
            {
                IsAvailable = workbook != null,
                Name = workbook != null ? workbook.Name : string.Empty,
                FullName = workbook != null ? workbook.FullName : string.Empty,
                WorksheetName = worksheet != null ? worksheet.Name : string.Empty,
                IsReadOnly = workbook != null && workbook.ReadOnly
            };
        }

        public ExcelSelectionInfo GetSelectionInfo()
        {
            var workbook = _application != null ? _application.ActiveWorkbook : null;
            var activeWorksheet = _application != null ? _application.ActiveSheet as ExcelInterop.Worksheet : null;
            var selectedRange = _application != null ? _application.Selection as ExcelInterop.Range : null;
            var selectionWorksheet = selectedRange != null ? selectedRange.Worksheet as ExcelInterop.Worksheet : activeWorksheet;

            return new ExcelSelectionInfo
            {
                IsAvailable = selectedRange != null,
                WorkbookName = workbook != null ? workbook.Name : string.Empty,
                WorksheetName = GetWorksheetName(selectionWorksheet),
                Address = selectedRange != null ? GetAddress(selectedRange) : string.Empty,
                RowCount = selectedRange != null ? Convert.ToInt32(selectedRange.Rows.Count) : 0,
                ColumnCount = selectedRange != null ? Convert.ToInt32(selectedRange.Columns.Count) : 0,
                Preview = selectedRange != null ? BuildRangePreview(selectedRange, 5, 5) : string.Empty
            };
        }

        public ExcelRangeReadResult ReadRange(ExcelRangeReadRequest request)
        {
            request = request ?? new ExcelRangeReadRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveReadRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的单元格区域。");
            }

            var maxRows = NormalizePositive(request.MaxRows, 20);
            var maxColumns = NormalizePositive(request.MaxColumns, 10);
            var totalRows = Convert.ToInt32(range.Rows.Count);
            var totalColumns = Convert.ToInt32(range.Columns.Count);

            return new ExcelRangeReadResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = GetWorksheetName(worksheet),
                Address = GetAddress(range),
                RowCount = totalRows,
                ColumnCount = totalColumns,
                IsTruncated = totalRows > maxRows || totalColumns > maxColumns,
                Preview = BuildRangePreview(range, maxRows, maxColumns),
                Rows = BuildRangeRows(range, maxRows, maxColumns)
            };
        }

        public ExcelWorksheetListResult ListWorksheets()
        {
            const int maximumWorksheetDetails = 50;
            var workbook = GetRequiredWorkbook();
            var workbookKey = GetWorkbookMapCacheKey(workbook);
            ExcelWorksheetListResult cachedResult;
            if (_workbookMapCache.TryGet(workbookKey, out cachedResult))
            {
                return cachedResult;
            }

            var result = new ExcelWorksheetListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            object worksheetsObject = null;
            ExcelInterop.Worksheet worksheet = null;

            try
            {
                worksheetsObject = workbook.Worksheets;
                dynamic worksheets = worksheetsObject;
                var count = Convert.ToInt32(worksheets.Count);
                result.TotalWorksheetCount = count;
                result.IsTruncated = count > maximumWorksheetDetails;

                for (var index = 1; index <= Math.Min(count, maximumWorksheetDetails); index++)
                {
                    object usedRangeObject = null;
                    var worksheetName = string.Empty;
                    var isVisible = false;
                    try
                    {
                        worksheet = worksheets.Item(index) as ExcelInterop.Worksheet;
                        if (worksheet == null)
                        {
                            continue;
                        }

                        worksheetName = worksheet.Name ?? string.Empty;
                        isVisible = GetWorksheetVisible(worksheet);
                        usedRangeObject = worksheet.UsedRange;
                        var usedRange = usedRangeObject as ExcelInterop.Range;
                        var usedRangeAddress = usedRange == null ? string.Empty : GetAddress(usedRange);
                        var usedRangeRows = 0;
                        var usedRangeColumns = 0;
                        if (usedRange != null
                            && !TryGetUsedRangeDimensions(
                                usedRangeAddress,
                                out usedRangeRows,
                                out usedRangeColumns))
                        {
                            usedRangeRows = Convert.ToInt32(usedRange.Rows.Count);
                            usedRangeColumns = Convert.ToInt32(usedRange.Columns.Count);
                        }
                        var hasFormulas = usedRange != null && HasFormulas(usedRange);
                        var headerColumns = BuildHeaderColumns(usedRange, 12);
                        result.Worksheets.Add(new ExcelWorksheetInfo
                        {
                            Name = worksheetName,
                            CodeName = worksheet.CodeName ?? string.Empty,
                            Index = index,
                            IsVisible = isVisible,
                            UsedRangeAddress = usedRangeAddress,
                            UsedRangeRows = usedRangeRows,
                            UsedRangeColumns = usedRangeColumns,
                            HeaderSignature = BuildHeaderSignature(headerColumns),
                            HeaderColumns = headerColumns,
                            HasFormulas = hasFormulas,
                            IsProtected = worksheet.ProtectContents,
                            RoleGuess = GuessWorksheetRole(worksheet.Name, usedRangeRows, hasFormulas)
                        });
                    }
                    catch (Exception ex)
                    {
                        result.Worksheets.Add(new ExcelWorksheetInfo
                        {
                            Name = worksheetName,
                            Index = index,
                            IsVisible = isVisible,
                            InspectionError = ex.Message,
                            RoleGuess = "unknown"
                        });
                    }
                    finally
                    {
                        ReleaseComObject(usedRangeObject);
                        ReleaseComObject(worksheet);
                        worksheet = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(worksheetsObject);
            }

            ExcelWorkbookMapInference.Populate(result);
            _workbookMapCache.Store(workbookKey, result);
            return result;
        }

        public static void InvalidateWorkbookMapCache(string workbookKey)
        {
            _workbookMapCache.Invalidate(workbookKey);
        }

        public static void InvalidateWorkbookMapCache(ExcelInterop.Workbook workbook)
        {
            _workbookMapCache.Invalidate(GetWorkbookMapCacheKey(workbook));
        }

        public static void ClearWorkbookMapCache()
        {
            _workbookMapCache.Clear();
        }

        private static string GetWorkbookMapCacheKey(ExcelInterop.Workbook workbook)
        {
            var fullName = workbook == null ? string.Empty : workbook.FullName;
            return string.IsNullOrWhiteSpace(fullName)
                ? (workbook == null ? string.Empty : workbook.Name ?? string.Empty)
                : fullName;
        }

        public ExcelWorksheetCreationResult CreateWorksheet(ExcelWorksheetCreationRequest request)
        {
            var worksheetName = request == null ? string.Empty : (request.WorksheetName ?? string.Empty).Trim();
            ValidateWorksheetName(worksheetName);

            ExcelInterop.Workbook workbook = null;
            ExcelInterop.Sheets worksheets = null;
            ExcelInterop.Worksheet lastWorksheet = null;
            ExcelInterop.Worksheet createdWorksheet = null;
            try
            {
                workbook = GetRequiredWorkbook();
                EnsureWorkbookWritable(workbook);
                if (CaptureWorksheetCodeNames(workbook).ContainsKey(worksheetName))
                {
                    throw new InvalidOperationException("工作表已存在：" + worksheetName);
                }

                worksheets = workbook.Worksheets;
                lastWorksheet = worksheets.Item[worksheets.Count] as ExcelInterop.Worksheet;
                if (lastWorksheet == null)
                {
                    throw new InvalidOperationException("无法确定新工作表的插入位置。");
                }

                createdWorksheet = worksheets.Add(After: lastWorksheet) as ExcelInterop.Worksheet;
                if (createdWorksheet == null)
                {
                    throw new InvalidOperationException("Excel 未能创建工作表。");
                }

                createdWorksheet.Name = worksheetName;
                InvalidateWorkbookMapCache(workbook);
                return new ExcelWorksheetCreationResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    WorksheetName = GetWorksheetName(createdWorksheet),
                    CodeName = createdWorksheet.CodeName ?? string.Empty
                };
            }
            catch (Exception createException)
            {
                if (createdWorksheet != null)
                {
                    try
                    {
                        var displayAlerts = _application.DisplayAlerts;
                        try
                        {
                            _application.DisplayAlerts = false;
                            createdWorksheet.Delete();
                        }
                        finally
                        {
                            _application.DisplayAlerts = displayAlerts;
                        }
                    }
                    catch (Exception rollbackException)
                    {
                        throw new InvalidOperationException(
                            "新工作表创建未完成，且无法回滚残留工作表。",
                            new AggregateException(createException, rollbackException));
                    }
                }

                throw;
            }
            finally
            {
                ReleaseComObject(createdWorksheet);
                ReleaseComObject(lastWorksheet);
                ReleaseComObject(worksheets);
                ReleaseComObject(workbook);
            }
        }

        private static void ValidateWorksheetName(string worksheetName)
        {
            if (string.IsNullOrWhiteSpace(worksheetName))
            {
                throw new ArgumentException("工作表名不能为空。", nameof(worksheetName));
            }

            if (worksheetName.Length > 31
                || worksheetName.IndexOfAny(new[] { ':', '\\', '/', '?', '*', '[', ']' }) >= 0
                || worksheetName[0] == '\''
                || worksheetName[worksheetName.Length - 1] == '\'')
            {
                throw new ArgumentException("工作表名不符合 Excel 命名规则：" + worksheetName, nameof(worksheetName));
            }
        }

        private static string BuildWorkbookMapSummary(ExcelWorksheetListResult worksheetList)
        {
            if (worksheetList == null)
            {
                return string.Empty;
            }

            const int maximumSummaryCharacters = 4000;
            var builder = new StringBuilder();
            builder.Append("工作表 ")
                .Append(worksheetList.TotalWorksheetCount)
                .Append(" 张");
            if (worksheetList.IsTruncated)
            {
                builder.Append("（仅展示前 ").Append(worksheetList.Worksheets.Count).Append(" 张）");
            }

            builder.AppendLine("：");
            if (worksheetList.WorksheetGroups != null && worksheetList.WorksheetGroups.Count > 0)
            {
                if (!AppendWorkbookMapSummaryLine(
                    builder,
                    "表头相似候选组（列名集合相似度≥80%；不保证列顺序或类型兼容）：",
                    maximumSummaryCharacters))
                {
                    return builder.ToString().TrimEnd();
                }

                foreach (var group in worksheetList.WorksheetGroups.Take(8))
                {
                    var groupLine = "- " + group.GroupId + " "
                        + FormatWorksheetNames(group.WorksheetNames, 6)
                        + "；最低相似度 "
                        + group.MinimumHeaderSimilarity.ToString("P0", CultureInfo.InvariantCulture);
                    if (!AppendWorkbookMapSummaryLine(builder, groupLine, maximumSummaryCharacters))
                    {
                        return builder.ToString().TrimEnd();
                    }
                }
            }

            if (worksheetList.SharedKeyCandidates != null && worksheetList.SharedKeyCandidates.Count > 0)
            {
                if (!AppendWorkbookMapSummaryLine(
                    builder,
                    "跨表共享列候选（唯一性未验证，使用前需画像确认）：",
                    maximumSummaryCharacters))
                {
                    return builder.ToString().TrimEnd();
                }

                foreach (var candidate in worksheetList.SharedKeyCandidates.Take(8))
                {
                    var candidateLine = "- " + candidate.ColumnName + "："
                        + FormatWorksheetNames(candidate.WorksheetNames, 6);
                    if (!AppendWorkbookMapSummaryLine(builder, candidateLine, maximumSummaryCharacters))
                    {
                        return builder.ToString().TrimEnd();
                    }
                }
            }

            foreach (var worksheet in worksheetList.Worksheets)
            {
                if (worksheet == null)
                {
                    continue;
                }

                var line = new StringBuilder();
                line.Append("- ")
                    .Append(worksheet.Name ?? string.Empty)
                    .Append(worksheet.IsVisible ? " [可见]" : " [隐藏]");
                if (!string.IsNullOrWhiteSpace(worksheet.CodeName))
                {
                    line.Append(" (CodeName=").Append(worksheet.CodeName).Append(")");
                }

                line.Append("; UsedRange=")
                    .Append(string.IsNullOrWhiteSpace(worksheet.UsedRangeAddress) ? "-" : worksheet.UsedRangeAddress)
                    .Append(" (")
                    .Append(worksheet.UsedRangeRows)
                    .Append("行×")
                    .Append(worksheet.UsedRangeColumns)
                    .Append("列)")
                    .Append("; 角色=")
                    .Append(worksheet.RoleGuess ?? "other")
                    .Append("; 公式=")
                    .Append(worksheet.HasFormulas ? "有" : "无");

                if (!string.IsNullOrWhiteSpace(worksheet.HeaderSignature))
                {
                    line.Append("; 表头=").Append(worksheet.HeaderSignature);
                }

                if (worksheet.IsProtected)
                {
                    line.Append("; 已保护");
                }

                if (!string.IsNullOrWhiteSpace(worksheet.InspectionError))
                {
                    line.Append("; 结构读取失败=").Append(worksheet.InspectionError);
                }

                if (!AppendWorkbookMapSummaryLine(
                    builder,
                    line.ToString(),
                    maximumSummaryCharacters))
                {
                    break;
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static bool AppendWorkbookMapSummaryLine(
            StringBuilder builder,
            string line,
            int maximumCharacters)
        {
            if (builder.Length + line.Length + Environment.NewLine.Length > maximumCharacters)
            {
                builder.AppendLine("- 工作簿地图摘要已截断；请调用 excel_list_worksheets 查看完整结构。");
                return false;
            }

            builder.AppendLine(line);
            return true;
        }

        private static string FormatWorksheetNames(
            IList<string> worksheetNames,
            int maximumNames)
        {
            if (worksheetNames == null || worksheetNames.Count == 0)
            {
                return "-";
            }

            var names = worksheetNames.Take(maximumNames).ToList();
            if (worksheetNames.Count > names.Count)
            {
                names.Add("等" + worksheetNames.Count.ToString(CultureInfo.InvariantCulture) + "张");
            }

            return string.Join("、", names);
        }

        private static bool HasFormulas(ExcelInterop.Range range)
        {
            var value = range.HasFormula;
            return value == null
                || Convert.IsDBNull(value)
                || Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        private static bool TryGetUsedRangeDimensions(
            string address,
            out int rowCount,
            out int columnCount)
        {
            rowCount = 0;
            columnCount = 0;
            var match = Regex.Match(
                address ?? string.Empty,
                @"^\$?([A-Za-z]+)\$?([0-9]+)(?::\$?([A-Za-z]+)\$?([0-9]+))?$",
                RegexOptions.CultureInvariant);
            if (!match.Success
                || !int.TryParse(
                    match.Groups[2].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var firstRow)
                || !TryGetColumnIndex(match.Groups[1].Value, out var firstColumn))
            {
                return false;
            }

            var lastRow = firstRow;
            var lastColumn = firstColumn;
            if (match.Groups[4].Success
                && (!int.TryParse(
                        match.Groups[4].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out lastRow)
                    || !TryGetColumnIndex(match.Groups[3].Value, out lastColumn)))
            {
                return false;
            }

            if (lastRow < firstRow || lastColumn < firstColumn)
            {
                return false;
            }

            rowCount = lastRow - firstRow + 1;
            columnCount = lastColumn - firstColumn + 1;
            return true;
        }

        private static bool TryGetColumnIndex(string letters, out int columnIndex)
        {
            columnIndex = 0;
            if (string.IsNullOrWhiteSpace(letters))
            {
                return false;
            }

            foreach (var letter in letters)
            {
                var normalized = char.ToUpperInvariant(letter);
                if (normalized < 'A' || normalized > 'Z')
                {
                    return false;
                }

                columnIndex = (columnIndex * 26) + normalized - 'A' + 1;
            }

            return true;
        }

        private static List<string> BuildHeaderColumns(
            ExcelInterop.Range usedRange,
            int maximumColumns)
        {
            if (usedRange == null || maximumColumns <= 0)
            {
                return new List<string>();
            }

            object headerRangeObject = null;
            try
            {
                dynamic rows = usedRange.Rows;
                headerRangeObject = rows.Item(1);
                var headerRange = headerRangeObject as ExcelInterop.Range;
                if (headerRange == null)
                {
                    return new List<string>();
                }

                var headers = new List<string>();
                var values = headerRange.Value2;
                if (values is object[,] cells)
                {
                    var firstColumn = cells.GetLowerBound(1);
                    var lastColumn = Math.Min(
                        cells.GetUpperBound(1),
                        firstColumn + maximumColumns - 1);
                    for (var column = firstColumn; column <= lastColumn; column++)
                    {
                        headers.Add(NormalizeHeader(cells.GetValue(cells.GetLowerBound(0), column)));
                    }
                }
                else
                {
                    headers.Add(NormalizeHeader(values));
                }

                return headers;
            }
            finally
            {
                ReleaseComObject(headerRangeObject);
            }
        }

        private static string BuildHeaderSignature(IList<string> headers)
        {
            return headers != null && headers.Any(header => !string.IsNullOrWhiteSpace(header))
                ? string.Join(" | ", headers)
                : string.Empty;
        }

        private static string NormalizeHeader(object value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(
                text.Normalize(NormalizationForm.FormKC).Trim(),
                @"\s+",
                " ");
            if (normalized.Length > 60)
            {
                normalized = normalized.Substring(0, 57) + "...";
            }

            return normalized;
        }

        private static string GuessWorksheetRole(string worksheetName, int usedRangeRows, bool hasFormulas)
        {
            var name = (worksheetName ?? string.Empty).Trim();
            if (ExcelStagingConventions.IsWipSheet(name))
            {
                return "wip";
            }

            if (ContainsAny(name, "汇总", "summary", "report", "报表", "total"))
            {
                return "summary";
            }

            return usedRangeRows > 1 || hasFormulas ? "data" : "other";
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            return !string.IsNullOrWhiteSpace(value)
                && terms != null
                && Array.Exists(terms, term => !string.IsNullOrWhiteSpace(term)
                    && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public ExcelWorksheetDataResult ReadWorksheetData(ExcelWorksheetDataRequest request)
        {
            request = request ?? new ExcelWorksheetDataRequest();

            var workbook = GetRequiredWorkbook();
            var worksheet = ResolveWorksheet(workbook, request.WorksheetName);
            var range = ResolveWorksheetDataRange(worksheet, request);
            if (range == null)
            {
                throw new InvalidOperationException("未找到可读取的工作表区域。");
            }

            var maxRows = NormalizePositive(request.MaxRows, 50);
            var maxColumns = NormalizePositive(request.MaxColumns, 20);
            var totalRows = Convert.ToInt32(range.Rows.Count);
            var totalColumns = Convert.ToInt32(range.Columns.Count);

            return new ExcelWorksheetDataResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                WorksheetName = GetWorksheetName(worksheet),
                Address = GetAddress(range),
                RowCount = totalRows,
                ColumnCount = totalColumns,
                IsTruncated = totalRows > maxRows || totalColumns > maxColumns,
                Preview = BuildRangePreview(range, maxRows, maxColumns),
                Headers = BuildHeaderRow(range, maxColumns),
                Rows = BuildRangeRows(range, maxRows, maxColumns)
            };
        }

        public ExcelNamedRangeListResult ListNamedRanges()
        {
            var workbook = GetRequiredWorkbook();
            var result = new ExcelNamedRangeListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            object namesObject = null;
            ExcelInterop.Name nameObject = null;

            try
            {
                namesObject = workbook.Names;
                dynamic names = namesObject;
                var count = Convert.ToInt32(names.Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        nameObject = names.Item(index) as ExcelInterop.Name;
                        if (nameObject == null)
                        {
                            continue;
                        }

                        result.NamedRanges.Add(new ExcelNamedRangeInfo
                        {
                            Name = SafeGetString(() => nameObject.Name),
                            Scope = GetNameScope(nameObject),
                            RefersTo = SafeGetString(() => nameObject.RefersTo),
                            ValueText = GetNamedRangeValueText(nameObject),
                            ValueType = GetNamedRangeValueType(nameObject),
                            IsVisible = SafeGetBool(() => nameObject.Visible, true)
                        });
                    }
                    finally
                    {
                        ReleaseComObject(nameObject);
                        nameObject = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(namesObject);
            }

            return result;
        }

        public ExcelNamedRangeValueResult GetNamedRangeValue(ExcelNamedRangeValueRequest request)
        {
            request = request ?? new ExcelNamedRangeValueRequest();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new InvalidOperationException("命名区域名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            ExcelInterop.Name nameObject = null;

            try
            {
                nameObject = FindNamedRange(workbook, request.Name.Trim());
                if (nameObject == null)
                {
                    throw new InvalidOperationException("未找到命名区域：" + request.Name);
                }

                return new ExcelNamedRangeValueResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    Name = SafeGetString(() => nameObject.Name),
                    Scope = GetNameScope(nameObject),
                    RefersTo = SafeGetString(() => nameObject.RefersTo),
                    ValueText = GetNamedRangeValueText(nameObject),
                    ValueType = GetNamedRangeValueType(nameObject),
                    IsVisible = SafeGetBool(() => nameObject.Visible, true)
                };
            }
            finally
            {
                ReleaseComObject(nameObject);
            }
        }

        public ExcelPowerQueryListResult ListPowerQueries()
        {
            var workbook = GetRequiredWorkbook();
            var result = new ExcelPowerQueryListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            ExcelInterop.Queries queries = null;
            ExcelInterop.WorkbookQuery query = null;

            try
            {
                queries = workbook.Queries;
                var count = Convert.ToInt32(queries.Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        query = queries.Item(index);
                        if (query == null)
                        {
                            continue;
                        }

                        var name = query.Name ?? string.Empty;
                        var formula = query.Formula ?? string.Empty;

                        result.Queries.Add(new ExcelPowerQueryInfo
                        {
                            Name = name,
                            Formula = formula,
                            FormulaPreview = BuildFormulaPreview(formula),
                            IsConnectionOnly = IsQueryConnectionOnly(workbook, name)
                        });
                    }
                    finally
                    {
                        ReleaseComObject(query);
                        query = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(queries);
            }

            return result;
        }

        public ExcelPowerQueryViewResult ViewPowerQuery(ExcelPowerQueryViewRequest request)
        {
            request = request ?? new ExcelPowerQueryViewRequest();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new InvalidOperationException("Power Query 名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            ExcelInterop.WorkbookQuery query = null;

            try
            {
                query = FindWorkbookQuery(workbook, request.Name.Trim());
                if (query == null)
                {
                    throw new InvalidOperationException("未找到 Power Query：" + request.Name);
                }

                var formula = query.Formula ?? string.Empty;
                return new ExcelPowerQueryViewResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    Name = query.Name ?? string.Empty,
                    Formula = formula,
                    CharacterCount = formula.Length,
                    IsConnectionOnly = IsQueryConnectionOnly(workbook, query.Name ?? string.Empty)
                };
            }
            finally
            {
                ReleaseComObject(query);
            }
        }

        public void InsertText(string text)
        {
            if (_application == null)
            {
                return;
            }

            var targetCell = _application.ActiveCell as ExcelInterop.Range;
            if (targetCell == null)
            {
                var selection = _application.Selection as ExcelInterop.Range;
                targetCell = selection != null ? selection.Cells[1, 1] as ExcelInterop.Range : null;
            }

            if (targetCell != null)
            {
                targetCell.Value2 = text;
            }
        }

        private ExcelInterop.Range ResolveReadRange(ExcelInterop.Worksheet worksheet, ExcelRangeReadRequest request)
        {
            if (request.UseSelectionWhenAddressEmpty && string.IsNullOrWhiteSpace(request.Address))
            {
                var selection = _application != null ? _application.Selection as ExcelInterop.Range : null;
                if (selection != null)
                {
                    return selection;
                }
            }

            if (worksheet == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.Address))
            {
                return null;
            }

            try
            {
                return worksheet.Range[request.Address.Trim()] as ExcelInterop.Range;
            }
            catch
            {
                throw new InvalidOperationException("区域地址无效：" + request.Address);
            }
        }

        private ExcelInterop.Range ResolveWorksheetDataRange(ExcelInterop.Worksheet worksheet, ExcelWorksheetDataRequest request)
        {
            if (worksheet == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(request.Address))
            {
                try
                {
                    return worksheet.Range[request.Address.Trim()] as ExcelInterop.Range;
                }
                catch
                {
                    throw new InvalidOperationException("区域地址无效：" + request.Address);
                }
            }

            if (!request.UseUsedRangeWhenAddressEmpty)
            {
                return null;
            }

            try
            {
                return worksheet.UsedRange;
            }
            catch
            {
                throw new InvalidOperationException("无法获取工作表已使用区域。");
            }
        }

        private static ExcelInterop.Name FindNamedRange(ExcelInterop.Workbook workbook, string targetName)
        {
            object namesObject = null;
            ExcelInterop.Name nameObject = null;

            try
            {
                namesObject = workbook.Names;
                dynamic names = namesObject;
                var count = Convert.ToInt32(names.Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        nameObject = names.Item(index) as ExcelInterop.Name;
                        if (nameObject == null)
                        {
                            continue;
                        }

                        var currentName = SafeGetString(() => nameObject.Name);
                        if (string.Equals(currentName, targetName, StringComparison.OrdinalIgnoreCase)
                            || currentName.EndsWith("!" + targetName, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = nameObject;
                            nameObject = null;
                            return result;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(nameObject);
                        nameObject = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(namesObject);
            }

            return null;
        }

        private static List<string> BuildHeaderRow(ExcelInterop.Range range, int maxColumns)
        {
            var headers = new List<string>();
            if (range == null)
            {
                return headers;
            }

            var totalRows = Convert.ToInt32(range.Rows.Count);
            var totalColumns = Math.Min(Convert.ToInt32(range.Columns.Count), Math.Max(1, maxColumns));
            if (totalRows <= 0)
            {
                return headers;
            }

            for (var column = 1; column <= totalColumns; column++)
            {
                var cell = range.Cells[1, column] as ExcelInterop.Range;
                headers.Add(cell != null ? (Convert.ToString(cell.Text) ?? string.Empty) : string.Empty);
            }

            return headers;
        }

        private static bool GetWorksheetVisible(ExcelInterop.Worksheet worksheet)
        {
            if (worksheet == null)
            {
                return false;
            }

            try
            {
                return Convert.ToInt32(worksheet.Visible) == (int)ExcelInterop.XlSheetVisibility.xlSheetVisible;
            }
            catch
            {
                return true;
            }
        }

        private static string GetNameScope(ExcelInterop.Name nameObject)
        {
            object parent = null;

            try
            {
                parent = nameObject.Parent;
                var worksheet = parent as ExcelInterop.Worksheet;
                if (worksheet != null)
                {
                    return worksheet.Name ?? string.Empty;
                }

                return "Workbook";
            }
            catch
            {
                return "Workbook";
            }
            finally
            {
                ReleaseComObject(parent);
            }
        }

        private static string GetNamedRangeValueText(ExcelInterop.Name nameObject)
        {
            ExcelInterop.Range range = null;

            try
            {
                try
                {
                    range = nameObject.RefersToRange as ExcelInterop.Range;
                }
                catch
                {
                    range = null;
                }

                if (range != null)
                {
                    if (Convert.ToInt32(range.Rows.Count) == 1 && Convert.ToInt32(range.Columns.Count) == 1)
                    {
                        return Convert.ToString(range.Text) ?? string.Empty;
                    }

                    return BuildRangePreview(range, 5, 5);
                }
            }
            finally
            {
                ReleaseComObject(range);
            }

            var refersTo = SafeGetString(() => nameObject.RefersTo);
            if (refersTo.StartsWith("=", StringComparison.Ordinal))
            {
                refersTo = refersTo.Substring(1);
            }

            return refersTo;
        }

        private static string GetNamedRangeValueType(ExcelInterop.Name nameObject)
        {
            ExcelInterop.Range range = null;

            try
            {
                try
                {
                    range = nameObject.RefersToRange as ExcelInterop.Range;
                }
                catch
                {
                    range = null;
                }

                if (range != null)
                {
                    if (Convert.ToInt32(range.Rows.Count) != 1 || Convert.ToInt32(range.Columns.Count) != 1)
                    {
                        return "range";
                    }

                    var value = range.Value2;
                    return GetValueTypeName(value);
                }
            }
            finally
            {
                ReleaseComObject(range);
            }

            var refersTo = SafeGetString(() => nameObject.RefersTo);
            if (string.IsNullOrWhiteSpace(refersTo))
            {
                return "empty";
            }

            return "formula";
        }

        private static string GetValueTypeName(object value)
        {
            if (value == null)
            {
                return "empty";
            }

            if (value is string)
            {
                return "string";
            }

            if (value is bool)
            {
                return "boolean";
            }

            if (value is DateTime)
            {
                return "date";
            }

            if (value is double || value is float || value is decimal
                || value is byte || value is short || value is int || value is long)
            {
                return "number";
            }

            return value.GetType().Name;
        }

        private static string SafeGetString(Func<string> getter)
        {
            try
            {
                return getter() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

    }
}
