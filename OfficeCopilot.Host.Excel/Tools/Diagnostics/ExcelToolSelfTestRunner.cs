using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public enum ExcelToolSelfTestStatus
    {
        Success = 0,
        Failed = 1,
        Skipped = 2
    }

    public sealed class ExcelToolSelfTestItemResult
    {
        public ExcelToolSelfTestItemResult()
        {
            GroupName = string.Empty;
            ToolName = string.Empty;
            DisplayName = string.Empty;
            Message = string.Empty;
        }

        public string GroupName { get; set; }

        public string ToolName { get; set; }

        public string DisplayName { get; set; }

        public ExcelToolSelfTestStatus Status { get; set; }

        public long DurationMilliseconds { get; set; }

        public string Message { get; set; }
    }

    public sealed class ExcelToolSelfTestGroupResult
    {
        public ExcelToolSelfTestGroupResult()
        {
            GroupName = string.Empty;
            Items = new List<ExcelToolSelfTestItemResult>();
        }

        public string GroupName { get; set; }

        public int RegisteredToolCount { get; set; }

        public IList<ExcelToolSelfTestItemResult> Items { get; set; }

        public int SuccessCount
        {
            get { return Items.Count(i => i.Status == ExcelToolSelfTestStatus.Success); }
        }

        public int FailedCount
        {
            get { return Items.Count(i => i.Status == ExcelToolSelfTestStatus.Failed); }
        }

        public int SkippedCount
        {
            get { return Items.Count(i => i.Status == ExcelToolSelfTestStatus.Skipped); }
        }
    }

    public static class ExcelToolSelfTestRunner
    {
        public static IReadOnlyList<ExcelToolSelfTestGroupResult> Run(
            IToolRegistry registry,
            IServiceProvider services,
            string hostApplicationName)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var results = new List<ExcelToolSelfTestGroupResult>();

            results.Add(RunGroup(
                registry,
                services,
                hostApplicationName,
                ExcelToolGroups.Context,
                BuildContextCases()));

            results.Add(RunGroup(
                registry,
                services,
                hostApplicationName,
                ExcelToolGroups.Data,
                BuildDataCases()));

            results.Add(RunGroup(
                registry,
                services,
                hostApplicationName,
                ExcelToolGroups.Advanced,
                BuildAdvancedCases()));

            return results;
        }

        public static string BuildReport(IReadOnlyList<ExcelToolSelfTestGroupResult> results)
        {
            if (results == null || results.Count == 0)
            {
                return "没有可显示的自检结果。";
            }

            var lines = new List<string>();
            lines.Add("Excel 内置工具自检报告");
            lines.Add("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            lines.Add(string.Empty);

            foreach (var group in results)
            {
                lines.Add("分组：" + group.GroupName);
                lines.Add("已注册工具数：" + group.RegisteredToolCount);
                lines.Add("成功：" + group.SuccessCount + "，失败：" + group.FailedCount + "，跳过：" + group.SkippedCount);

                foreach (var item in group.Items)
                {
                    lines.Add(string.Format(
                        "- [{0}] {1} ({2} ms) {3}",
                        GetStatusText(item.Status),
                        item.ToolName,
                        item.DurationMilliseconds,
                        item.Message));
                }

                lines.Add(string.Empty);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static ExcelToolSelfTestGroupResult RunGroup(
            IToolRegistry registry,
            IServiceProvider services,
            string hostApplicationName,
            string groupName,
            IList<ToolSelfTestCase> cases)
        {
            var groupResult = new ExcelToolSelfTestGroupResult
            {
                GroupName = groupName,
                RegisteredToolCount = CountRegisteredTools(registry, groupName)
            };

            for (var i = 0; i < cases.Count; i++)
            {
                groupResult.Items.Add(RunCase(registry, services, hostApplicationName, groupName, cases[i]));
            }

            return groupResult;
        }

        private static ExcelToolSelfTestItemResult RunCase(
            IToolRegistry registry,
            IServiceProvider services,
            string hostApplicationName,
            string groupName,
            ToolSelfTestCase testCase)
        {
            var item = new ExcelToolSelfTestItemResult
            {
                GroupName = groupName,
                ToolName = testCase.ToolName,
                DisplayName = testCase.DisplayName
            };

            if (!string.Equals(hostApplicationName, "Excel", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = ExcelToolSelfTestStatus.Skipped;
                item.Message = "当前宿主不是 Excel。";
                return item;
            }

            var host = services.GetService(typeof(IExcelToolHost)) as IExcelToolHost;
            if (host == null)
            {
                item.Status = ExcelToolSelfTestStatus.Failed;
                item.Message = "未找到 Excel 工具宿主服务。";
                return item;
            }

            var skipReason = testCase.GetSkipReason == null
                ? string.Empty
                : testCase.GetSkipReason(host);

            if (!string.IsNullOrWhiteSpace(skipReason))
            {
                item.Status = ExcelToolSelfTestStatus.Skipped;
                item.Message = skipReason;
                return item;
            }

            IAgentTool tool;
            if (!registry.TryGetTool(testCase.ToolName, out tool) || tool == null)
            {
                item.Status = ExcelToolSelfTestStatus.Failed;
                item.Message = "工具未注册。";
                return item;
            }

            var argumentsJson = testCase.GetArgumentsJson == null
                ? testCase.ArgumentsJson
                : testCase.GetArgumentsJson(host);

            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                argumentsJson = "{}";
            }

            var request = new ToolExecutionRequest
            {
                ToolCallId = Guid.NewGuid().ToString("N"),
                ToolName = testCase.ToolName,
                ArgumentsJson = argumentsJson
            };

            var context = new ToolExecutionContext
            {
                ConversationId = "tool-self-test",
                MessageId = request.ToolCallId,
                HostApplicationName = hostApplicationName ?? string.Empty,
                Services = services
            };

            var watch = Stopwatch.StartNew();

            try
            {

                ToolCallDiagnosticsLogger.Log(
                    context.HostApplicationName,
                    request.ToolName,
                    context,
                    request,
                    "received",
                    "argumentsJson=" + ToolCallDiagnosticsLogger.TruncateForLog(request.ArgumentsJson, 8000));

                var result = tool.ExecuteAsync(context, request).GetAwaiter().GetResult();

                watch.Stop();
                item.DurationMilliseconds = watch.ElapsedMilliseconds;

                ToolCallDiagnosticsLogger.Log(
                    context.HostApplicationName,
                    request.ToolName,
                    context,
                    request,
                    result != null && result.Success ? "completed" : "failed",
                    ToolCallDiagnosticsLogger.BuildResultDetails(result, watch.ElapsedMilliseconds));

                if (result != null && result.Success)
                {
                    item.Status = ExcelToolSelfTestStatus.Success;
                    item.Message = BuildSuccessMessage(result);
                    return item;
                }

                item.Status = ExcelToolSelfTestStatus.Failed;
                item.Message = BuildFailureMessage(result);
                return item;
            }
            catch (Exception ex)
            {
                watch.Stop();
                item.DurationMilliseconds = watch.ElapsedMilliseconds;

                ToolCallDiagnosticsLogger.Log(
                    context.HostApplicationName,
                    request.ToolName,
                    context,
                    request,
                    "failed",
                    ToolCallDiagnosticsLogger.BuildFailureDetails(ex, watch.ElapsedMilliseconds));

                item.Status = ExcelToolSelfTestStatus.Failed;
                item.Message = ex.Message;
                return item;
            }
        }

        private static IList<ToolSelfTestCase> BuildContextCases()
        {
            return new List<ToolSelfTestCase>
            {
                new ToolSelfTestCase("excel_get_active_workbook", "活动工作簿", "{}"),
                new ToolSelfTestCase("excel_get_selection", "当前选区", "{}"),
                new ToolSelfTestCase("excel_list_worksheets", "工作表列表", "{}")
            };
        }

        private static IList<ToolSelfTestCase> BuildDataCases()
        {
            return new List<ToolSelfTestCase>
            {
                new ToolSelfTestCase(
                    "excel_read_range",
                    "读取选区",
                    "{}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_read_worksheet_data",
                    "读取当前工作表已用区域",
                    "{}"),
                new ToolSelfTestCase(
                    "excel_list_named_ranges",
                    "列出名称区域",
                    "{}"),
                new ToolSelfTestCase(
                    "excel_get_named_range_value",
                    "读取首个名称区域",
                    "{}",
                    BuildFirstNamedRangeArgumentsJson,
                    RequireNamedRange),
                new ToolSelfTestCase(
                    "excel_list_tables",
                    "列出表格",
                    "{}"),
                new ToolSelfTestCase(
                    "excel_view_table",
                    "查看首个表格",
                    "{}",
                    BuildFirstTableArgumentsJson,
                    RequireTable),
                new ToolSelfTestCase(
                    "excel_read_table_data",
                    "读取首个表格数据",
                    "{}",
                    BuildFirstTableArgumentsJson,
                    RequireTable),
                new ToolSelfTestCase(
                    "excel_read_range_formulas",
                    "读取选区公式",
                    "{}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_get_range_info",
                    "读取选区信息",
                    "{}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_get_range_hyperlinks",
                    "读取选区超链接",
                    "{}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_get_range_validation",
                    "读取选区数据验证",
                    "{}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_get_range_number_formats",
                    "读取选区数字格式",
                    "{}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_get_range_merge_info",
                    "读取选区合并状态",
                    "{}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_get_range_lock_info",
                    "读取选区锁定状态",
                    "{}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_get_range_style",
                    "读取选区样式",
                    "{}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_find_in_range",
                    "在选区中查找",
                    "{\"searchValue\":\"a\"}",
                    null,
                    RequireSelection),
                new ToolSelfTestCase(
                    "excel_validate_range_formulas",
                    "校验选区公式",
                    "{}",
                    null,
                    RequireSelection)
            };
        }

        private static IList<ToolSelfTestCase> BuildAdvancedCases()
        {
            return new List<ToolSelfTestCase>
            {
                new ToolSelfTestCase(
                    "excel_list_power_queries",
                    "列出 Power Query",
                    "{}"),
                new ToolSelfTestCase(
                    "excel_view_power_query",
                    "查看首个 Power Query",
                    "{}",
                    BuildFirstPowerQueryArgumentsJson,
                    RequirePowerQuery),
                new ToolSelfTestCase(
                    "excel_refresh_power_query",
                    "刷新首个 Power Query",
                    "{}",
                    BuildFirstPowerQueryArgumentsJson,
                    RequirePowerQuery),
                new ToolSelfTestCase(
                    "excel_check_power_query_errors",
                    "检查首个 Power Query 错误",
                    "{}",
                    BuildFirstPowerQueryArgumentsJson,
                    RequirePowerQuery),
                new ToolSelfTestCase(
                    "excel_validate_power_query",
                    "校验 Power Query M 代码",
                    "{}",
                    BuildPowerQueryValidationArgumentsJson,
                    RequireWritableWorkbook),
                new ToolSelfTestCase(
                    "excel_list_vba_modules",
                    "列出 VBA 模块",
                    "{}",
                    null,
                    RequireVbaAccess),
                new ToolSelfTestCase(
                    "excel_view_vba_module",
                    "查看首个 VBA 模块",
                    "{}",
                    BuildFirstVbaModuleArgumentsJson,
                    RequireVbaModule),
                new ToolSelfTestCase(
                    "excel_list_connections",
                    "列出连接",
                    "{}"),
                new ToolSelfTestCase(
                    "excel_view_connection",
                    "查看首个连接",
                    "{}",
                    BuildFirstConnectionArgumentsJson,
                    RequireConnection),
                new ToolSelfTestCase(
                    "excel_list_data_model_tables",
                    "列出数据模型表",
                    "{}"),
                new ToolSelfTestCase(
                    "excel_list_data_model_measures",
                    "列出数据模型度量",
                    "{}"),
                new ToolSelfTestCase(
                    "excel_view_data_model_measure",
                    "查看首个数据模型度量",
                    "{}",
                    BuildFirstMeasureArgumentsJson,
                    RequireDataModelMeasure),
                new ToolSelfTestCase(
                    "excel_list_data_model_relationships",
                    "列出数据模型关系",
                    "{}"),
                new ToolSelfTestCase(
                    "excel_list_data_model_calculated_columns",
                    "列出数据模型计算列",
                    "{}"),
                new ToolSelfTestCase(
                    "excel_view_data_model_calculated_column",
                    "查看首个数据模型计算列",
                    "{}",
                    BuildFirstCalculatedColumnArgumentsJson,
                    RequireCalculatedColumn)
            };
        }

        private static string RequireSelection(IExcelToolHost host)
        {
            var workbookReason = RequireWorkbook(host);
            if (!string.IsNullOrWhiteSpace(workbookReason))
            {
                return workbookReason;
            }

            var selection = host.GetSelectionInfo();
            if (selection == null || !selection.IsAvailable)
            {
                return "当前没有可用选区。";
            }

            return string.Empty;
        }

        private static string RequireNamedRange(IExcelToolHost host)
        {
            var workbookReason = RequireWorkbook(host);
            if (!string.IsNullOrWhiteSpace(workbookReason))
            {
                return workbookReason;
            }

            var result = host.ListNamedRanges();
            if (result == null || result.NamedRanges == null || result.NamedRanges.Count == 0)
            {
                return "当前工作簿没有名称区域。";
            }

            return string.Empty;
        }

        private static string RequireTable(IExcelToolHost host)
        {
            var workbookReason = RequireWorkbook(host);
            if (!string.IsNullOrWhiteSpace(workbookReason))
            {
                return workbookReason;
            }

            var result = host.ListTables();
            if (result == null || result.Tables == null || result.Tables.Count == 0)
            {
                return "当前工作簿没有表格。";
            }

            return string.Empty;
        }

        private static string RequirePowerQuery(IExcelToolHost host)
        {
            var workbookReason = RequireWorkbook(host);
            if (!string.IsNullOrWhiteSpace(workbookReason))
            {
                return workbookReason;
            }

            var result = host.ListPowerQueries();
            if (result == null || result.Queries == null || result.Queries.Count == 0)
            {
                return "当前工作簿没有 Power Query。";
            }

            return string.Empty;
        }

        private static string RequireConnection(IExcelToolHost host)
        {
            var workbookReason = RequireWorkbook(host);
            if (!string.IsNullOrWhiteSpace(workbookReason))
            {
                return workbookReason;
            }

            var result = host.ListConnections();
            if (result == null || result.Connections == null || result.Connections.Count == 0)
            {
                return "当前工作簿没有连接。";
            }

            return string.Empty;
        }

        private static string RequireVbaAccess(IExcelToolHost host)
        {
            var workbookReason = RequireWorkbook(host);
            if (!string.IsNullOrWhiteSpace(workbookReason))
            {
                return workbookReason;
            }

            try
            {
                host.ListVbaModules();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return "当前环境未启用 VBA 工程访问：" + ex.Message;
            }
        }

        private static string RequireVbaModule(IExcelToolHost host)
        {
            var accessReason = RequireVbaAccess(host);
            if (!string.IsNullOrWhiteSpace(accessReason))
            {
                return accessReason;
            }

            var result = host.ListVbaModules();
            if (result == null || result.Modules == null || result.Modules.Count == 0)
            {
                return "当前工作簿没有 VBA 模块。";
            }

            return string.Empty;
        }

        private static string RequireDataModelMeasure(IExcelToolHost host)
        {
            var workbookReason = RequireWorkbook(host);
            if (!string.IsNullOrWhiteSpace(workbookReason))
            {
                return workbookReason;
            }

            var result = host.ListDataModelMeasures();
            if (result == null || result.Measures == null || result.Measures.Count == 0)
            {
                return "当前工作簿没有数据模型度量。";
            }

            return string.Empty;
        }

        private static string RequireCalculatedColumn(IExcelToolHost host)
        {
            var workbookReason = RequireWorkbook(host);
            if (!string.IsNullOrWhiteSpace(workbookReason))
            {
                return workbookReason;
            }

            var result = host.ListDataModelCalculatedColumns();
            if (result == null || result.CalculatedColumns == null || result.CalculatedColumns.Count == 0)
            {
                return "当前工作簿没有数据模型计算列。";
            }

            return string.Empty;
        }

        private static string RequireWritableWorkbook(IExcelToolHost host)
        {
            var workbookReason = RequireWorkbook(host);
            if (!string.IsNullOrWhiteSpace(workbookReason))
            {
                return workbookReason;
            }

            var workbook = host.GetActiveWorkbookInfo();
            if (workbook != null && workbook.IsReadOnly)
            {
                return "当前工作簿为只读，跳过写入型自检。";
            }

            return string.Empty;
        }

        private static string RequireWorkbook(IExcelToolHost host)
        {
            if (host == null)
            {
                return "未找到 Excel 工具宿主服务。";
            }

            var workbook = host.GetActiveWorkbookInfo();
            if (workbook == null || !workbook.IsAvailable)
            {
                return "当前没有活动工作簿。";
            }

            return string.Empty;
        }

        private static string BuildFirstNamedRangeArgumentsJson(IExcelToolHost host)
        {
            var result = host.ListNamedRanges();
            if (result == null || result.NamedRanges == null || result.NamedRanges.Count == 0)
            {
                return "{}";
            }

            var name = result.NamedRanges[0] == null ? string.Empty : result.NamedRanges[0].Name;
            return "{\"name\":\"" + EscapeJsonString(name) + "\"}";
        }

        private static string BuildFirstTableArgumentsJson(IExcelToolHost host)
        {
            var result = host.ListTables();
            if (result == null || result.Tables == null || result.Tables.Count == 0)
            {
                return "{}";
            }

            var tableName = result.Tables[0] == null ? string.Empty : result.Tables[0].Name;
            return "{\"tableName\":\"" + EscapeJsonString(tableName) + "\"}";
        }

        private static string BuildFirstPowerQueryArgumentsJson(IExcelToolHost host)
        {
            var result = host.ListPowerQueries();
            if (result == null || result.Queries == null || result.Queries.Count == 0)
            {
                return "{}";
            }

            var name = result.Queries[0] == null ? string.Empty : result.Queries[0].Name;
            return "{\"name\":\"" + EscapeJsonString(name) + "\"}";
        }

        private static string BuildPowerQueryValidationArgumentsJson(IExcelToolHost host)
        {
            return "{\"mCode\":\"let Source = 1 in Source\"}";
        }

        private static string BuildFirstConnectionArgumentsJson(IExcelToolHost host)
        {
            var result = host.ListConnections();
            if (result == null || result.Connections == null || result.Connections.Count == 0)
            {
                return "{}";
            }

            var connectionName = result.Connections[0] == null ? string.Empty : result.Connections[0].Name;
            return "{\"connectionName\":\"" + EscapeJsonString(connectionName) + "\"}";
        }

        private static string BuildFirstVbaModuleArgumentsJson(IExcelToolHost host)
        {
            var result = host.ListVbaModules();
            if (result == null || result.Modules == null || result.Modules.Count == 0)
            {
                return "{}";
            }

            var moduleName = result.Modules[0] == null ? string.Empty : result.Modules[0].Name;
            return "{\"moduleName\":\"" + EscapeJsonString(moduleName) + "\"}";
        }

        private static string BuildFirstMeasureArgumentsJson(IExcelToolHost host)
        {
            var result = host.ListDataModelMeasures();
            if (result == null || result.Measures == null || result.Measures.Count == 0)
            {
                return "{}";
            }

            var measure = result.Measures[0];
            if (measure == null)
            {
                return "{}";
            }

            return "{"
                + "\"measureName\":\"" + EscapeJsonString(measure.Name) + "\","
                + "\"tableName\":\"" + EscapeJsonString(measure.TableName) + "\""
                + "}";
        }

        private static string BuildFirstCalculatedColumnArgumentsJson(IExcelToolHost host)
        {
            var result = host.ListDataModelCalculatedColumns();
            if (result == null || result.CalculatedColumns == null || result.CalculatedColumns.Count == 0)
            {
                return "{}";
            }

            var column = result.CalculatedColumns[0];
            if (column == null)
            {
                return "{}";
            }

            return "{"
                + "\"tableName\":\"" + EscapeJsonString(column.TableName) + "\","
                + "\"columnName\":\"" + EscapeJsonString(column.Name) + "\""
                + "}";
        }

        private static int CountRegisteredTools(IToolRegistry registry, string groupName)
        {
            var tools = registry.GetTools();
            if (tools == null || tools.Count == 0)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < tools.Count; i++)
            {
                var definition = tools[i] == null ? null : tools[i].Definition;
                if (definition == null || definition.Metadata == null)
                {
                    continue;
                }

                string value;
                if (!definition.Metadata.TryGetValue(ToolMetadataKeys.GroupName, out value))
                {
                    continue;
                }

                if (string.Equals(value, groupName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private static string BuildSuccessMessage(ToolExecutionResult result)
        {
            var content = result == null ? string.Empty : result.Content ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return "执行成功。";
            }

            content = content.Replace("\r", " ").Replace("\n", " ").Trim();
            if (content.Length > 80)
            {
                content = content.Substring(0, 80) + "...";
            }

            return content;
        }

        private static string BuildFailureMessage(ToolExecutionResult result)
        {
            if (result == null)
            {
                return "工具未返回结果。";
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                return result.ErrorMessage;
            }

            if (!string.IsNullOrWhiteSpace(result.Content))
            {
                return result.Content;
            }

            return "工具执行失败。";
        }

        private static string GetStatusText(ExcelToolSelfTestStatus status)
        {
            switch (status)
            {
                case ExcelToolSelfTestStatus.Success:
                    return "成功";
                case ExcelToolSelfTestStatus.Failed:
                    return "失败";
                default:
                    return "跳过";
            }
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private sealed class ToolSelfTestCase
        {
            public ToolSelfTestCase(
                string toolName,
                string displayName,
                string argumentsJson,
                Func<IExcelToolHost, string> getArgumentsJson = null,
                Func<IExcelToolHost, string> getSkipReason = null)
            {
                ToolName = toolName ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
                ArgumentsJson = argumentsJson ?? "{}";
                GetArgumentsJson = getArgumentsJson;
                GetSkipReason = getSkipReason;
            }

            public string ToolName { get; private set; }

            public string DisplayName { get; private set; }

            public string ArgumentsJson { get; private set; }

            public Func<IExcelToolHost, string> GetArgumentsJson { get; private set; }

            public Func<IExcelToolHost, string> GetSkipReason { get; private set; }
        }
    }
}