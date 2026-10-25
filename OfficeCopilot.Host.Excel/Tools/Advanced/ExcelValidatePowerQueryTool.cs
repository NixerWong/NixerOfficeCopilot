using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelValidatePowerQueryTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_validate_power_query",
            DisplayName = "Validate Power Query",
            Description = "Validates M-code syntax using a temporary query object.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"mCode\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"mCode\"],"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelPowerQueryValidationRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ValidatePowerQuery(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                if (!result.IsValid)
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        result.ValidationErrors.Count > 0 ? result.ValidationErrors[0] : "Power Query 校验失败。",
                        "excel.validate_power_query_failed",
                        content));
                }

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.validate_power_query_failed"));
            }
        }

        private static string BuildContent(ExcelPowerQueryValidationResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("语法有效：" + (result.IsValid ? "是" : "否"));

            if (result.ValidationErrors.Count > 0)
            {
                builder.AppendLine("错误：");
                for (var index = 0; index < result.ValidationErrors.Count; index++)
                {
                    builder.Append("- ");
                    builder.AppendLine(result.ValidationErrors[index]);
                }
            }

            return builder.ToString();
        }
    }
}