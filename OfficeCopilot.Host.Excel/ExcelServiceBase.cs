using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel
{
    /// <summary>
    /// Excel 宿主服务的公共基类：持有 Excel Application 引用，并提供跨服务共享的
    /// 工作簿/工作表解析、区域预览构建、查询探测与 COM 释放等辅助方法。
    /// </summary>
    public abstract class ExcelServiceBase
    {
        protected readonly ExcelInterop.Application _application;

        protected ExcelServiceBase(ExcelInterop.Application application)
        {
            _application = application;
        }
        protected static string GetConnectionString(ExcelInterop.WorkbookConnection connection)
        {
            object innerConnection = null;

            try
            {
                switch (connection.Type)
                {
                    case ExcelInterop.XlConnectionType.xlConnectionTypeOLEDB:
                        innerConnection = connection.OLEDBConnection;
                        return Convert.ToString(((dynamic)innerConnection).Connection) ?? string.Empty;

                    case ExcelInterop.XlConnectionType.xlConnectionTypeODBC:
                        innerConnection = connection.ODBCConnection;
                        return Convert.ToString(((dynamic)innerConnection).Connection) ?? string.Empty;

                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                ReleaseComObject(innerConnection);
            }
        }

        protected static string GetConnectionCommandText(ExcelInterop.WorkbookConnection connection)
        {
            object innerConnection = null;

            try
            {
                switch (connection.Type)
                {
                    case ExcelInterop.XlConnectionType.xlConnectionTypeOLEDB:
                        innerConnection = connection.OLEDBConnection;
                        return Convert.ToString(((dynamic)innerConnection).CommandText) ?? string.Empty;

                    case ExcelInterop.XlConnectionType.xlConnectionTypeODBC:
                        innerConnection = connection.ODBCConnection;
                        return Convert.ToString(((dynamic)innerConnection).CommandText) ?? string.Empty;

                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                ReleaseComObject(innerConnection);
            }
        }

        protected ExcelInterop.Workbook GetRequiredWorkbook()
        {
            var workbook = _application != null ? _application.ActiveWorkbook : null;
            if (workbook == null)
            {
                throw new InvalidOperationException("当前没有活动工作簿。");
            }

            return workbook;
        }

        protected ExcelInterop.Worksheet ResolveWorksheet(ExcelInterop.Workbook workbook, string worksheetName)
        {
            if (workbook == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(worksheetName))
            {
                return _application != null ? _application.ActiveSheet as ExcelInterop.Worksheet : null;
            }

            try
            {
                return workbook.Worksheets[worksheetName.Trim()] as ExcelInterop.Worksheet;
            }
            catch
            {
                throw new InvalidOperationException("未找到工作表：" + worksheetName);
            }
        }

        protected static ExcelInterop.WorkbookQuery FindWorkbookQuery(ExcelInterop.Workbook workbook, string queryName)
        {
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

                        if (string.Equals(query.Name ?? string.Empty, queryName, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = query;
                            query = null;
                            return result;
                        }
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

            return null;
        }

        protected bool IsQueryConnectionOnly(ExcelInterop.Workbook workbook, string queryName)
        {
            if (string.IsNullOrWhiteSpace(queryName))
            {
                return true;
            }

            var connectionName = "Query - " + queryName;

            if (IsQueryLoadedToWorksheet(workbook, connectionName))
            {
                return false;
            }

            if (IsQueryLoadedToDataModel(workbook, queryName, connectionName))
            {
                return false;
            }

            return true;
        }

        protected static bool IsQueryLoadedToWorksheet(ExcelInterop.Workbook workbook, string connectionName)
        {
            object worksheetsObject = null;
            ExcelInterop.Worksheet worksheet = null;
            object listObjectsObject = null;
            object listObject = null;
            object queryTable = null;
            object workbookConnection = null;

            try
            {
                worksheetsObject = workbook.Worksheets;
                dynamic worksheets = worksheetsObject;
                var worksheetCount = Convert.ToInt32(worksheets.Count);

                for (var worksheetIndex = 1; worksheetIndex <= worksheetCount; worksheetIndex++)
                {
                    try
                    {
                        worksheet = worksheets.Item(worksheetIndex) as ExcelInterop.Worksheet;
                        if (worksheet == null)
                        {
                            continue;
                        }

                        listObjectsObject = worksheet.ListObjects;
                        dynamic listObjects = listObjectsObject;
                        var listObjectCount = Convert.ToInt32(listObjects.Count);

                        for (var listObjectIndex = 1; listObjectIndex <= listObjectCount; listObjectIndex++)
                        {
                            try
                            {
                                listObject = listObjects.Item(listObjectIndex);
                                dynamic runtimeListObject = listObject;

                                try
                                {
                                    queryTable = runtimeListObject.QueryTable;
                                }
                                catch
                                {
                                    queryTable = null;
                                }

                                if (queryTable == null)
                                {
                                    continue;
                                }

                                workbookConnection = ((dynamic)queryTable).WorkbookConnection;
                                var currentName = Convert.ToString(((dynamic)workbookConnection).Name) ?? string.Empty;
                                if (string.Equals(currentName, connectionName, StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                            }
                            finally
                            {
                                ReleaseComObject(workbookConnection);
                                workbookConnection = null;
                                ReleaseComObject(queryTable);
                                queryTable = null;
                                ReleaseComObject(listObject);
                                listObject = null;
                            }
                        }
                    }
                    finally
                    {
                        ReleaseComObject(listObjectsObject);
                        listObjectsObject = null;
                        ReleaseComObject(worksheet);
                        worksheet = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(workbookConnection);
                ReleaseComObject(queryTable);
                ReleaseComObject(listObject);
                ReleaseComObject(listObjectsObject);
                ReleaseComObject(worksheet);
                ReleaseComObject(worksheetsObject);
            }

            return false;
        }

        protected static bool IsQueryLoadedToDataModel(ExcelInterop.Workbook workbook, string queryName, string connectionName)
        {
            object model = null;
            object modelTables = null;
            object modelTable = null;

            try
            {
                dynamic runtimeWorkbook = workbook;

                try
                {
                    model = runtimeWorkbook.Model;
                }
                catch
                {
                    model = null;
                }

                if (model == null)
                {
                    return false;
                }

                dynamic runtimeModel = model;
                modelTables = runtimeModel.ModelTables;
                dynamic runtimeModelTables = modelTables;
                var count = Convert.ToInt32(runtimeModelTables.Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        modelTable = runtimeModelTables.Item(index);
                        dynamic runtimeModelTable = modelTable;

                        var sourceName = Convert.ToString(runtimeModelTable.SourceName) ?? string.Empty;
                        var tableName = Convert.ToString(runtimeModelTable.Name) ?? string.Empty;

                        if (string.Equals(sourceName, queryName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(sourceName, connectionName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(tableName, queryName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(modelTable);
                        modelTable = null;
                    }
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                ReleaseComObject(modelTable);
                ReleaseComObject(modelTables);
                ReleaseComObject(model);
            }

            return false;
        }

        protected static List<string[]> BuildRangeRows(ExcelInterop.Range range, int maxRows, int maxColumns)
        {
            var rows = new List<string[]>();
            if (range == null)
            {
                return rows;
            }

            var rowCount = Math.Min(Convert.ToInt32(range.Rows.Count), Math.Max(1, maxRows));
            var columnCount = Math.Min(Convert.ToInt32(range.Columns.Count), Math.Max(1, maxColumns));

            for (var row = 1; row <= rowCount; row++)
            {
                var rowValues = new string[columnCount];

                for (var column = 1; column <= columnCount; column++)
                {
                    var cell = range.Cells[row, column] as ExcelInterop.Range;
                    var text = cell != null ? Convert.ToString(cell.Text) : string.Empty;
                    rowValues[column - 1] = text ?? string.Empty;
                }

                rows.Add(rowValues);
            }

            return rows;
        }

        protected static int NormalizePositive(int value, int defaultValue)
        {
            return value > 0 ? value : defaultValue;
        }

        protected static string GetWorksheetName(ExcelInterop.Worksheet worksheet)
        {
            return worksheet == null ? string.Empty : worksheet.Name ?? string.Empty;
        }

        protected static string GetAddress(ExcelInterop.Range range)
        {
            if (range == null)
            {
                return "未选中单元格";
            }

            return range.Address[false, false, ExcelInterop.XlReferenceStyle.xlA1];
        }

        protected static string BuildRangePreview(ExcelInterop.Range range)
        {
            return BuildRangePreview(range, 5, 5);
        }

        protected static string BuildRangePreview(ExcelInterop.Range range, int maxRows, int maxColumns)
        {
            if (range == null)
            {
                return "当前没有可用选区";
            }

            var rowCount = Math.Min(Convert.ToInt32(range.Rows.Count), Math.Max(1, maxRows));
            var columnCount = Math.Min(Convert.ToInt32(range.Columns.Count), Math.Max(1, maxColumns));
            var builder = new StringBuilder();

            for (var row = 1; row <= rowCount; row++)
            {
                for (var column = 1; column <= columnCount; column++)
                {
                    var cell = range.Cells[row, column] as ExcelInterop.Range;
                    var text = cell != null ? Convert.ToString(cell.Text) : string.Empty;

                    if (column > 1)
                    {
                        builder.Append("\t");
                    }

                    builder.Append(string.IsNullOrWhiteSpace(text) ? "(空)" : text);
                }

                if (row < rowCount)
                {
                    builder.AppendLine();
                }
            }

            if (Convert.ToInt32(range.Rows.Count) > rowCount || Convert.ToInt32(range.Columns.Count) > columnCount)
            {
                builder.AppendLine();
                builder.Append("...");
            }

            return builder.ToString();
        }

        protected static string BuildFormulaPreview(string formula)
        {
            var normalized = Regex.Replace(formula ?? string.Empty, @"\s+", " ").Trim();
            if (normalized.Length <= 80)
            {
                return normalized;
            }

            return normalized.Substring(0, 80) + "...";
        }

        protected static bool SafeGetBool(Func<bool> getter, bool defaultValue)
        {
            try
            {
                return getter();
            }
            catch
            {
                return defaultValue;
            }
        }

        protected static double SafeGetDouble(Func<double> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return 0.0;
            }
        }

        protected static void ReleaseComObject(object comObject)
        {
            if (comObject == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.FinalReleaseComObject(comObject);
                }
            }
            catch
            {
            }
        }

        protected static int SafeGetInt32(Func<int> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return 0;
            }
        }

        protected static string NormalizeFormulaText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Trim();
            if (text.StartsWith("=", StringComparison.Ordinal) || text.StartsWith("{=", StringComparison.Ordinal))
            {
                return text;
            }

            return "=" + text;
        }

        protected static string BuildFormulaPreview(ExcelRangeFormulaWriteRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            if (request.Formulas != null && request.Formulas.Count > 0)
            {
                return BuildMatrixPreview(
                    request.Formulas,
                    request.Formulas.Count,
                    GetMatrixColumnCount(request.Formulas));
            }

            return NormalizeFormulaText(request.Formula);
        }

        protected ExcelInterop.Range ResolveWriteRange(
            ExcelInterop.Worksheet worksheet,
            string address,
            bool useSelectionWhenAddressEmpty)
        {
            if (useSelectionWhenAddressEmpty && string.IsNullOrWhiteSpace(address))
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

            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            try
            {
                return worksheet.Range[address.Trim()] as ExcelInterop.Range;
            }
            catch
            {
                throw new InvalidOperationException("区域地址无效：" + address);
            }
        }

        protected static void EnsureWorkbookWritable(ExcelInterop.Workbook workbook)
        {
            if (workbook == null)
            {
                throw new InvalidOperationException("当前没有活动工作簿。");
            }

            if (workbook.ReadOnly)
            {
                throw new InvalidOperationException("当前工作簿为只读状态，无法写入内容。");
            }
        }

        protected static ExcelInterop.Range ResizeRangeForWrite(ExcelInterop.Range range, int rowCount, int columnCount)
        {
            if (range == null)
            {
                return null;
            }

            var currentRows = Convert.ToInt32(range.Rows.Count);
            var currentColumns = Convert.ToInt32(range.Columns.Count);
            if (currentRows == rowCount && currentColumns == columnCount)
            {
                return range;
            }

            if (currentRows != 1 || currentColumns != 1)
            {
                throw new InvalidOperationException("写入二维数据时，目标区域大小必须与数据一致，或提供单个单元格作为起点。");
            }

            return range.Resize[rowCount, columnCount] as ExcelInterop.Range;
        }

        protected static int GetMatrixColumnCount(IList<string[]> values)
        {
            var columnCount = 0;
            if (values == null)
            {
                return columnCount;
            }

            for (var rowIndex = 0; rowIndex < values.Count; rowIndex++)
            {
                var row = values[rowIndex];
                var candidate = row == null ? 0 : row.Length;
                if (candidate > columnCount)
                {
                    columnCount = candidate;
                }
            }

            return columnCount;
        }

        protected static string BuildMatrixPreview(IList<string[]> values, int rowCount, int columnCount)
        {
            var previewRows = Math.Min(rowCount, 3);
            var previewColumns = Math.Min(columnCount, 3);
            var builder = new StringBuilder();

            for (var rowIndex = 0; rowIndex < previewRows; rowIndex++)
            {
                if (rowIndex > 0)
                {
                    builder.Append(" | ");
                }

                var row = values[rowIndex];
                for (var columnIndex = 0; columnIndex < previewColumns; columnIndex++)
                {
                    if (columnIndex > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(row != null && columnIndex < row.Length
                        ? row[columnIndex] ?? string.Empty
                        : string.Empty);
                }
            }

            if (rowCount > previewRows || columnCount > previewColumns)
            {
                builder.Append(" ...");
            }

            return builder.ToString();
        }

        protected static List<string[]> BuildFormulaRows(ExcelInterop.Range range, int maxRows, int maxColumns)
        {
            var rows = new List<string[]>();
            if (range == null)
            {
                return rows;
            }

            var rowCount = Math.Min(Convert.ToInt32(range.Rows.Count), Math.Max(1, maxRows));
            var columnCount = Math.Min(Convert.ToInt32(range.Columns.Count), Math.Max(1, maxColumns));

            for (var row = 1; row <= rowCount; row++)
            {
                var rowValues = new string[columnCount];

                for (var column = 1; column <= columnCount; column++)
                {
                    ExcelInterop.Range cell = null;

                    try
                    {
                        cell = range.Cells[row, column] as ExcelInterop.Range;
                        rowValues[column - 1] = cell != null
                            ? (Convert.ToString(cell.Formula) ?? string.Empty)
                            : string.Empty;
                    }
                    finally
                    {
                        ReleaseComObject(cell);
                    }
                }

                rows.Add(rowValues);
            }

            return rows;
        }
    }
}
