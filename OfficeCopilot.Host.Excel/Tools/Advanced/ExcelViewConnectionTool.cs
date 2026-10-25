using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelViewConnectionTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_view_connection",
            DisplayName = "View Connection",
            Description = "Shows details for the specified connection.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"connectionName\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"connectionName\"],"
                + "\"additionalProperties\":false"
                + "}"
        };

        public override ToolDefinition Definition
        {
            get { return _definition; }
        }

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetExcelToolHost(context);
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelConnectionViewRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ViewConnection(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.view_connection_failed"));
            }
        }

        private static string BuildContent(ExcelConnectionViewResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("连接：" + result.ConnectionName);
            builder.AppendLine("类型：" + result.Type);
            builder.AppendLine("描述：" + (string.IsNullOrWhiteSpace(result.Description) ? "-" : result.Description));
            builder.AppendLine("Power Query：" + (result.IsPowerQuery ? "是" : "否"));
            builder.AppendLine("后台刷新：" + (result.BackgroundQuery ? "是" : "否"));
            builder.AppendLine("打开时刷新：" + (result.RefreshOnFileOpen ? "是" : "否"));
            builder.AppendLine("连接字符串：" + (string.IsNullOrWhiteSpace(result.ConnectionString) ? "-" : result.ConnectionString));
            builder.AppendLine("命令类型：" + (string.IsNullOrWhiteSpace(result.CommandType) ? "-" : result.CommandType));
            builder.AppendLine("命令文本：");
            builder.Append(result.CommandText ?? string.Empty);
            return builder.ToString();
        }
    }
}