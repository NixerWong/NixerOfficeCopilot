using System;
using System.Collections.Generic;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel
{
    internal sealed class ExcelTableService : ExcelServiceBase
    {
        public ExcelTableService(ExcelInterop.Application application)
            : base(application)
        {
        }

        public ExcelTableListResult ListTables()
        {
            var workbook = GetRequiredWorkbook();
            var result = new ExcelTableListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            object worksheetsObject = null;
            ExcelInterop.Worksheet worksheet = null;
            object listObjectsObject = null;
            ExcelInterop.ListObject listObject = null;

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
                        var tableCount = Convert.ToInt32(listObjects.Count);

                        for (var tableIndex = 1; tableIndex <= tableCount; tableIndex++)
                        {
                            try
                            {
                                listObject = listObjects.Item(tableIndex) as ExcelInterop.ListObject;
                                if (listObject == null)
                                {
                                    continue;
                                }

                                result.Tables.Add(CreateTableInfo(worksheet, listObject));
                            }
                            finally
                            {
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
                ReleaseComObject(listObject);
                ReleaseComObject(listObjectsObject);
                ReleaseComObject(worksheet);
                ReleaseComObject(worksheetsObject);
            }

            return result;
        }

        public ExcelTableViewResult ViewTable(ExcelTableViewRequest request)
        {
            request = request ?? new ExcelTableViewRequest();
            if (string.IsNullOrWhiteSpace(request.TableName))
            {
                throw new InvalidOperationException("表名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            object worksheetsObject = null;
            ExcelInterop.Worksheet worksheet = null;
            object listObjectsObject = null;
            ExcelInterop.ListObject listObject = null;

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
                        var tableCount = Convert.ToInt32(listObjects.Count);

                        for (var tableIndex = 1; tableIndex <= tableCount; tableIndex++)
                        {
                            try
                            {
                                listObject = listObjects.Item(tableIndex) as ExcelInterop.ListObject;
                                if (listObject == null)
                                {
                                    continue;
                                }

                                var currentName = listObject.Name ?? string.Empty;
                                if (!string.Equals(currentName, request.TableName.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                return new ExcelTableViewResult
                                {
                                    WorkbookName = workbook.Name ?? string.Empty,
                                    Table = CreateTableInfo(worksheet, listObject)
                                };
                            }
                            finally
                            {
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
                ReleaseComObject(listObject);
                ReleaseComObject(listObjectsObject);
                ReleaseComObject(worksheet);
                ReleaseComObject(worksheetsObject);
            }

            throw new InvalidOperationException("未找到表：" + request.TableName);
        }

        public ExcelTableDataResult ReadTableData(ExcelTableDataRequest request)
        {
            request = request ?? new ExcelTableDataRequest();
            if (string.IsNullOrWhiteSpace(request.TableName))
            {
                throw new InvalidOperationException("表名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            object worksheetsObject = null;
            ExcelInterop.Worksheet worksheet = null;
            object listObjectsObject = null;
            ExcelInterop.ListObject listObject = null;
            ExcelInterop.Range dataBodyRange = null;
            ExcelInterop.Range fullRange = null;

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
                        var tableCount = Convert.ToInt32(listObjects.Count);

                        for (var tableIndex = 1; tableIndex <= tableCount; tableIndex++)
                        {
                            try
                            {
                                listObject = listObjects.Item(tableIndex) as ExcelInterop.ListObject;
                                if (listObject == null)
                                {
                                    continue;
                                }

                                var currentName = listObject.Name ?? string.Empty;
                                if (!string.Equals(currentName, request.TableName.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                fullRange = listObject.Range;
                                dataBodyRange = listObject.DataBodyRange;
                                var rowCount = GetTableRowCount(listObject);
                                var columnCount = GetTableColumnCount(listObject);
                                var maxRows = NormalizePositive(request.MaxRows, 100);
                                var maxColumns = NormalizePositive(request.MaxColumns, 30);
                                var hasData = dataBodyRange != null;

                                return new ExcelTableDataResult
                                {
                                    WorkbookName = workbook.Name ?? string.Empty,
                                    TableName = listObject.Name ?? string.Empty,
                                    WorksheetName = worksheet.Name ?? string.Empty,
                                    RangeAddress = fullRange != null ? GetAddress(fullRange) : string.Empty,
                                    Headers = BuildTableColumnNames(listObject),
                                    Rows = hasData ? BuildRangeRows(dataBodyRange, maxRows, maxColumns) : new List<string[]>(),
                                    RowCount = rowCount,
                                    ColumnCount = columnCount,
                                    IsTruncated = rowCount > maxRows || columnCount > maxColumns,
                                    Preview = hasData ? BuildRangePreview(dataBodyRange, Math.Min(maxRows, 10), Math.Min(maxColumns, 10)) : string.Empty
                                };
                            }
                            finally
                            {
                                ReleaseComObject(dataBodyRange);
                                dataBodyRange = null;
                                ReleaseComObject(fullRange);
                                fullRange = null;
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
                ReleaseComObject(dataBodyRange);
                ReleaseComObject(fullRange);
                ReleaseComObject(listObject);
                ReleaseComObject(listObjectsObject);
                ReleaseComObject(worksheet);
                ReleaseComObject(worksheetsObject);
            }

            throw new InvalidOperationException("未找到表：" + request.TableName);
        }

        private static ExcelTableInfo CreateTableInfo(ExcelInterop.Worksheet worksheet, ExcelInterop.ListObject listObject)
        {
            ExcelInterop.Range range = null;

            try
            {
                range = listObject.Range;

                return new ExcelTableInfo
                {
                    Name = listObject.Name ?? string.Empty,
                    WorksheetName = worksheet == null ? string.Empty : worksheet.Name ?? string.Empty,
                    RangeAddress = range != null ? GetAddress(range) : string.Empty,
                    HasHeaders = SafeGetBool(() => listObject.ShowHeaders, true),
                    ShowTotals = SafeGetBool(() => listObject.ShowTotals, false),
                    TableStyle = GetTableStyleName(listObject),
                    RowCount = GetTableRowCount(listObject),
                    ColumnCount = GetTableColumnCount(listObject),
                    Columns = BuildTableColumnNames(listObject)
                };
            }
            finally
            {
                ReleaseComObject(range);
            }
        }

        private static string GetTableStyleName(ExcelInterop.ListObject listObject)
        {
            try
            {
                return Convert.ToString(listObject.TableStyle) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int GetTableRowCount(ExcelInterop.ListObject listObject)
        {
            ExcelInterop.Range dataBodyRange = null;

            try
            {
                dataBodyRange = listObject.DataBodyRange;
                return dataBodyRange == null ? 0 : Convert.ToInt32(dataBodyRange.Rows.Count);
            }
            catch
            {
                return 0;
            }
            finally
            {
                ReleaseComObject(dataBodyRange);
            }
        }

        private static int GetTableColumnCount(ExcelInterop.ListObject listObject)
        {
            object listColumnsObject = null;

            try
            {
                listColumnsObject = listObject.ListColumns;
                return Convert.ToInt32(((dynamic)listColumnsObject).Count);
            }
            catch
            {
                return 0;
            }
            finally
            {
                ReleaseComObject(listColumnsObject);
            }
        }

        private static List<string> BuildTableColumnNames(ExcelInterop.ListObject listObject)
        {
            var columns = new List<string>();
            object listColumnsObject = null;
            object listColumnObject = null;

            try
            {
                listColumnsObject = listObject.ListColumns;
                dynamic listColumns = listColumnsObject;
                var count = Convert.ToInt32(listColumns.Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        listColumnObject = listColumns.Item(index);
                        var name = Convert.ToString(((dynamic)listColumnObject).Name) ?? string.Empty;
                        columns.Add(name);
                    }
                    finally
                    {
                        ReleaseComObject(listColumnObject);
                        listColumnObject = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(listColumnObject);
                ReleaseComObject(listColumnsObject);
            }

            return columns;
        }
    }
}
