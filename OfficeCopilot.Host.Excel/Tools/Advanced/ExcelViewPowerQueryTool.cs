using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelViewPowerQueryTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_view_power_query",
            DisplayName = "查看 Power Query",
            Description = "查看指定 Power Query 的完整 M 代码。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelPowerQueryViewRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ViewPowerQuery(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.view_power_query_failed"));
            }
        }

        private static string BuildContent(ExcelPowerQueryViewResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("名称：" + (string.IsNullOrWhiteSpace(result.Name) ? "-" : result.Name));
            builder.AppendLine("模式：" + (result.IsConnectionOnly ? "仅连接" : "已加载"));
            builder.AppendLine("字符数：" + result.CharacterCount);
            builder.AppendLine("M 代码：");
            builder.Append(result.Formula ?? string.Empty);
            return builder.ToString();
        }
    }
}