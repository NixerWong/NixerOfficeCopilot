using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelCheckPowerQueryErrorsTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_check_power_query_errors",
            DisplayName = "检查 Power Query 错误",
            Description = "检查指定 Power Query 的连接与错误状态。",
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelPowerQueryErrorCheckRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.CheckPowerQueryErrors(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.check_power_query_errors_failed"));
            }
        }

        private static string BuildContent(ExcelPowerQueryErrorCheckResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("查询：" + result.QueryName);
            builder.AppendLine("仅连接：" + (result.IsConnectionOnly ? "是" : "否"));
            builder.AppendLine("存在错误：" + (result.HasErrors ? "是" : "否"));
            builder.AppendLine("错误分类：" + (string.IsNullOrWhiteSpace(result.ErrorCategory) ? "-" : result.ErrorCategory));
            builder.AppendLine("说明：" + (string.IsNullOrWhiteSpace(result.Message) ? "-" : result.Message));

            if (result.ErrorMessages.Count > 0)
            {
                builder.AppendLine("错误明细：");
                for (var index = 0; index < result.ErrorMessages.Count; index++)
                {
                    builder.Append("- ");
                    builder.AppendLine(result.ErrorMessages[index]);
                }
            }

            return builder.ToString();
        }
    }
}