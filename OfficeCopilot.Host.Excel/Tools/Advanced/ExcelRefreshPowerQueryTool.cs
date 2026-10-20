using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelRefreshPowerQueryTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_refresh_power_query",
            DisplayName = "刷新 Power Query",
            Description = "刷新指定 Power Query 及其底层连接。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"name\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"name\"],"
                + "\"additionalProperties\":false"
                + "}"
        };

        public override ToolDefinition Definition
        {
            get { return _definition; }
        }

        public override Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request)
        {
            try
            {
                var host = GetExcelToolHost(context);
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelPowerQueryRefreshRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.RefreshPowerQuery(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                if (result.HasErrors)
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        result.ErrorMessages.Count > 0 ? result.ErrorMessages[0] : "刷新 Power Query 失败。",
                        "excel.refresh_power_query_failed",
                        content));
                }

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.refresh_power_query_failed"));
            }
        }

        private static string BuildContent(ExcelPowerQueryRefreshResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("查询：" + result.QueryName);
            builder.AppendLine("刷新时间：" + result.RefreshTime.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("仅连接：" + (result.IsConnectionOnly ? "是" : "否"));
            builder.AppendLine("加载工作表：" + (string.IsNullOrWhiteSpace(result.LoadedToSheet) ? "-" : result.LoadedToSheet));
            builder.Append("错误：" + (result.HasErrors ? "有" : "无"));
            return builder.ToString();
        }
    }
}