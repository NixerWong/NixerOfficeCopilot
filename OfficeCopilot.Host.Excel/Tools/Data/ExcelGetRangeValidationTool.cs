using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelGetRangeValidationTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_get_range_validation",
            DisplayName = "Read Range Data Validation",
            Description = "Reads the data-validation settings of the first cell in the specified range. If no address is provided, falls back to the current selection.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"useSelectionWhenAddressEmpty\":{\"type\":\"boolean\"}"
                + "},"
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
                var argumentsJson = request == null ? null : request.ArgumentsJson;
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelRangeRequest>(argumentsJson);
                arguments = ExcelToolRequestNormalizer.NormalizeRangeRequest(argumentsJson, arguments);

                var result = host.GetRangeValidation(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.get_range_validation_failed"));
            }
        }

        private static string BuildContent(ExcelRangeValidationResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? string.Empty));
            builder.AppendLine("工作表：" + (result.WorksheetName ?? string.Empty));
            builder.AppendLine("区域：" + (result.Address ?? string.Empty));
            builder.AppendLine("存在验证：" + (result.HasValidation ? "是" : "否"));

            if (result.HasValidation)
            {
                builder.AppendLine("类型：" + (result.ValidationType ?? string.Empty));
                builder.AppendLine("运算符：" + (result.ValidationOperator ?? string.Empty));
                builder.AppendLine("Formula1：" + (string.IsNullOrWhiteSpace(result.Formula1) ? "-" : result.Formula1));
                builder.AppendLine("Formula2：" + (string.IsNullOrWhiteSpace(result.Formula2) ? "-" : result.Formula2));
                builder.AppendLine("显示输入提示：" + (result.ShowInputMessage ? "是" : "否"));
                builder.AppendLine("显示错误警告：" + (result.ShowErrorAlert ? "是" : "否"));
            }

            return builder.ToString();
        }
    }
}