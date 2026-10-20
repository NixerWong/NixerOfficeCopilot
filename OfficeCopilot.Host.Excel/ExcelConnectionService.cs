using System;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using OfficeCopilot.Host.Abstractions;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OfficeCopilot.Host.Excel
{
    internal sealed class ExcelConnectionService : ExcelServiceBase
    {
        public ExcelConnectionService(ExcelInterop.Application application)
            : base(application)
        {
        }

        public ExcelConnectionListResult ListConnections()
        {
            var workbook = GetRequiredWorkbook();
            var result = new ExcelConnectionListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            ExcelInterop.Connections connections = null;
            ExcelInterop.WorkbookConnection connection = null;

            try
            {
                connections = workbook.Connections;
                var count = Convert.ToInt32(connections.Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        connection = connections.Item(index);
                        if (connection == null)
                        {
                            continue;
                        }

                        result.Connections.Add(CreateConnectionInfo(connection));
                    }
                    finally
                    {
                        ReleaseComObject(connection);
                        connection = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(connection);
                ReleaseComObject(connections);
            }

            return result;
        }

        public ExcelConnectionViewResult ViewConnection(ExcelConnectionViewRequest request)
        {
            request = request ?? new ExcelConnectionViewRequest();
            if (string.IsNullOrWhiteSpace(request.ConnectionName))
            {
                throw new InvalidOperationException("连接名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            ExcelInterop.Connections connections = null;
            ExcelInterop.WorkbookConnection connection = null;

            try
            {
                connections = workbook.Connections;
                var count = Convert.ToInt32(connections.Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        connection = connections.Item(index);
                        if (connection == null)
                        {
                            continue;
                        }

                        var name = connection.Name ?? string.Empty;
                        if (!string.Equals(name, request.ConnectionName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        return CreateConnectionViewResult(workbook, connection);
                    }
                    finally
                    {
                        ReleaseComObject(connection);
                        connection = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(connection);
                ReleaseComObject(connections);
            }

            throw new InvalidOperationException("未找到连接：" + request.ConnectionName);
        }

        private static ExcelConnectionInfo CreateConnectionInfo(ExcelInterop.WorkbookConnection connection)
        {
            return new ExcelConnectionInfo
            {
                Name = connection.Name ?? string.Empty,
                Type = MapConnectionType(connection.Type),
                Description = connection.Description ?? string.Empty,
                IsPowerQuery = IsPowerQueryConnection(connection),
                BackgroundQuery = GetConnectionBackgroundQuery(connection),
                RefreshOnFileOpen = GetConnectionRefreshOnFileOpen(connection)
            };
        }

        private static ExcelConnectionViewResult CreateConnectionViewResult(
            ExcelInterop.Workbook workbook,
            ExcelInterop.WorkbookConnection connection)
        {
            return new ExcelConnectionViewResult
            {
                WorkbookName = workbook.Name ?? string.Empty,
                ConnectionName = connection.Name ?? string.Empty,
                Type = MapConnectionType(connection.Type),
                Description = connection.Description ?? string.Empty,
                ConnectionString = GetConnectionString(connection),
                CommandText = GetConnectionCommandText(connection),
                CommandType = GetConnectionCommandType(connection),
                IsPowerQuery = IsPowerQueryConnection(connection),
                BackgroundQuery = GetConnectionBackgroundQuery(connection),
                RefreshOnFileOpen = GetConnectionRefreshOnFileOpen(connection)
            };
        }

        private static bool IsPowerQueryConnection(ExcelInterop.WorkbookConnection connection)
        {
            var name = connection == null ? string.Empty : connection.Name ?? string.Empty;
            return name.StartsWith("Query - ", StringComparison.OrdinalIgnoreCase);
        }

        private static string MapConnectionType(ExcelInterop.XlConnectionType type)
        {
            switch (type)
            {
                case ExcelInterop.XlConnectionType.xlConnectionTypeODBC:
                    return "ODBC";
                case ExcelInterop.XlConnectionType.xlConnectionTypeOLEDB:
                    return "OLEDB";
                case ExcelInterop.XlConnectionType.xlConnectionTypeTEXT:
                    return "Text";
                case ExcelInterop.XlConnectionType.xlConnectionTypeWEB:
                    return "Web";
                case ExcelInterop.XlConnectionType.xlConnectionTypeDATAFEED:
                    return "DataFeed";
                case ExcelInterop.XlConnectionType.xlConnectionTypeMODEL:
                    return "Model";
                case ExcelInterop.XlConnectionType.xlConnectionTypeWORKSHEET:
                    return "Worksheet";
                default:
                    return type.ToString();
            }
        }

        private static bool GetConnectionBackgroundQuery(ExcelInterop.WorkbookConnection connection)
        {
            object innerConnection = null;

            try
            {
                switch (connection.Type)
                {
                    case ExcelInterop.XlConnectionType.xlConnectionTypeOLEDB:
                        innerConnection = connection.OLEDBConnection;
                        return Convert.ToBoolean(((dynamic)innerConnection).BackgroundQuery);

                    case ExcelInterop.XlConnectionType.xlConnectionTypeODBC:
                        innerConnection = connection.ODBCConnection;
                        return Convert.ToBoolean(((dynamic)innerConnection).BackgroundQuery);

                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                ReleaseComObject(innerConnection);
            }
        }

        private static bool GetConnectionRefreshOnFileOpen(ExcelInterop.WorkbookConnection connection)
        {
            object innerConnection = null;

            try
            {
                switch (connection.Type)
                {
                    case ExcelInterop.XlConnectionType.xlConnectionTypeOLEDB:
                        innerConnection = connection.OLEDBConnection;
                        return Convert.ToBoolean(((dynamic)innerConnection).RefreshOnFileOpen);

                    case ExcelInterop.XlConnectionType.xlConnectionTypeODBC:
                        innerConnection = connection.ODBCConnection;
                        return Convert.ToBoolean(((dynamic)innerConnection).RefreshOnFileOpen);

                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                ReleaseComObject(innerConnection);
            }
        }

        private static string GetConnectionCommandType(ExcelInterop.WorkbookConnection connection)
        {
            object innerConnection = null;

            try
            {
                switch (connection.Type)
                {
                    case ExcelInterop.XlConnectionType.xlConnectionTypeOLEDB:
                        innerConnection = connection.OLEDBConnection;
                        return Convert.ToString(((dynamic)innerConnection).CommandType) ?? string.Empty;

                    case ExcelInterop.XlConnectionType.xlConnectionTypeODBC:
                        innerConnection = connection.ODBCConnection;
                        return Convert.ToString(((dynamic)innerConnection).CommandType) ?? string.Empty;

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

        public ExcelPowerQueryRefreshResult RefreshPowerQuery(ExcelPowerQueryRefreshRequest request)
        {
            request = request ?? new ExcelPowerQueryRefreshRequest();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new InvalidOperationException("Power Query 名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            var queryName = request.Name.Trim();
            ExcelInterop.WorkbookQuery query = null;
            ExcelInterop.WorkbookConnection connection = null;

            try
            {
                query = FindWorkbookQuery(workbook, queryName);
                if (query == null)
                {
                    throw new InvalidOperationException("未找到 Power Query：" + queryName);
                }

                var isConnectionOnly = IsQueryConnectionOnly(workbook, queryName);
                var loadedToSheet = GetQueryLoadedWorksheetName(workbook, queryName);

                try
                {
                    query.Refresh();
                }
                catch
                {
                    connection = FindWorkbookConnection(workbook, "Query - " + queryName);
                    if (connection == null)
                    {
                        throw;
                    }

                    connection.Refresh();
                }

                return new ExcelPowerQueryRefreshResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    QueryName = queryName,
                    HasErrors = false,
                    RefreshTime = DateTime.Now,
                    IsConnectionOnly = isConnectionOnly,
                    LoadedToSheet = loadedToSheet
                };
            }
            catch (Exception ex)
            {
                return new ExcelPowerQueryRefreshResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    QueryName = queryName,
                    HasErrors = true,
                    RefreshTime = DateTime.Now,
                    IsConnectionOnly = IsQueryConnectionOnly(workbook, queryName),
                    LoadedToSheet = GetQueryLoadedWorksheetName(workbook, queryName),
                    ErrorMessages = new List<string>
                    {
                        ex.Message
                    }
                };
            }
            finally
            {
                ReleaseComObject(connection);
                ReleaseComObject(query);
            }
        }

        public ExcelPowerQueryErrorCheckResult CheckPowerQueryErrors(ExcelPowerQueryErrorCheckRequest request)
        {
            request = request ?? new ExcelPowerQueryErrorCheckRequest();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new InvalidOperationException("Power Query 名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();
            var queryName = request.Name.Trim();
            ExcelInterop.WorkbookQuery query = null;
            ExcelInterop.WorkbookConnection connection = null;

            try
            {
                query = FindWorkbookQuery(workbook, queryName);
                if (query == null)
                {
                    throw new InvalidOperationException("未找到 Power Query：" + queryName);
                }

                connection = FindWorkbookConnection(workbook, "Query - " + queryName);

                var result = new ExcelPowerQueryErrorCheckResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    QueryName = queryName,
                    IsConnectionOnly = IsQueryConnectionOnly(workbook, queryName),
                    HasErrors = false,
                    Message = "未检测到明显错误。"
                };

                if (connection == null)
                {
                    result.Message = "未找到关联连接，但查询对象存在。";
                    return result;
                }

                var connectionString = GetConnectionString(connection);
                var commandText = GetConnectionCommandText(connection);

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    result.HasErrors = true;
                    result.ErrorCategory = "Connection";
                    result.ErrorMessages.Add("连接字符串为空。");
                }

                if (string.IsNullOrWhiteSpace(commandText))
                {
                    result.Message = "连接已存在，但命令文本为空或无法读取。";
                }

                return result;
            }
            catch (Exception ex)
            {
                return new ExcelPowerQueryErrorCheckResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    QueryName = queryName,
                    IsConnectionOnly = IsQueryConnectionOnly(workbook, queryName),
                    HasErrors = true,
                    ErrorCategory = CategorizePowerQueryError(ex.Message),
                    Message = "检测 Power Query 错误时发生异常。",
                    ErrorMessages = new List<string>
                    {
                        ex.Message
                    }
                };
            }
            finally
            {
                ReleaseComObject(connection);
                ReleaseComObject(query);
            }
        }

        private static ExcelInterop.WorkbookConnection FindWorkbookConnection(ExcelInterop.Workbook workbook, string connectionName)
        {
            ExcelInterop.Connections connections = null;
            ExcelInterop.WorkbookConnection connection = null;

            try
            {
                connections = workbook.Connections;
                var count = Convert.ToInt32(connections.Count);

                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        connection = connections.Item(index);
                        if (connection == null)
                        {
                            continue;
                        }

                        if (string.Equals(connection.Name ?? string.Empty, connectionName, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = connection;
                            connection = null;
                            return result;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(connection);
                        connection = null;
                    }
                }
            }
            finally
            {
                ReleaseComObject(connection);
                ReleaseComObject(connections);
            }

            return null;
        }

        private static string CategorizePowerQueryError(string message)
        {
            var text = message ?? string.Empty;

            if (text.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("权限", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("登录", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Authentication";
            }

            if (text.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("连接", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Connectivity";
            }

            if (text.IndexOf("syntax", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("语法", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Syntax";
            }

            if (text.IndexOf("privacy", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("隐私", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Privacy";
            }

            return "Unknown";
        }

        private string GetQueryLoadedWorksheetName(ExcelInterop.Workbook workbook, string queryName)
        {
            object worksheetsObject = null;
            ExcelInterop.Worksheet worksheet = null;
            object listObjectsObject = null;
            object listObject = null;
            object queryTable = null;
            object workbookConnection = null;
            var connectionName = "Query - " + queryName;

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
                                    return worksheet.Name ?? string.Empty;
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

            return string.Empty;
        }
    }
}
