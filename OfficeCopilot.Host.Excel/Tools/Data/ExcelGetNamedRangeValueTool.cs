using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelGetNamedRangeValueTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_get_named_range_value",
            DisplayName = "Read Named Range Value",
            Description = "Reads the current value, type, and reference information for the specified named range.",
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelNamedRangeValueRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.GetNamedRangeValue(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.get_named_range_value_failed"));
            }
        }

        private static string BuildContent(ExcelNamedRangeValueResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("名称：" + (string.IsNullOrWhiteSpace(result.Name) ? "-" : result.Name));
            builder.AppendLine("作用域：" + (string.IsNullOrWhiteSpace(result.Scope) ? "-" : result.Scope));
            builder.AppendLine("引用：" + (string.IsNullOrWhiteSpace(result.RefersTo) ? "-" : result.RefersTo));
            builder.AppendLine("类型：" + (string.IsNullOrWhiteSpace(result.ValueType) ? "-" : result.ValueType));
            builder.Append("值：" + (string.IsNullOrWhiteSpace(result.ValueText) ? "-" : result.ValueText));
            return builder.ToString();
        }
    }
}