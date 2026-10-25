using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelValidateRangeFormulasTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_validate_range_formulas",
            DisplayName = "Validate Range Formulas",
            Description = "Validates formulas in the specified range and returns errors and warnings. If no address is provided, falls back to the current selection.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"useSelectionWhenAddressEmpty\":{\"type\":\"boolean\"},"
                + "\"maxRows\":{\"type\":\"integer\",\"minimum\":1},"
                + "\"maxColumns\":{\"type\":\"integer\",\"minimum\":1}"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelRangeFormulaValidationRequest>(argumentsJson);
                arguments = ExcelToolRequestNormalizer.NormalizeRangeFormulaValidationRequest(argumentsJson, arguments);

                var result = host.ValidateRangeFormulas(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                if (!result.IsValid)
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        result.Errors.Count > 0 ? result.Errors[0].Message : "检测到公式错误",
                        "excel.validate_range_formulas_failed",
                        content));
                }

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.validate_range_formulas_failed"));
            }
        }

        private static string BuildContent(ExcelRangeFormulaValidationResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? string.Empty));
            builder.AppendLine("工作表：" + (result.WorksheetName ?? string.Empty));
            builder.AppendLine("区域：" + (result.Address ?? string.Empty));
            builder.AppendLine("有效：" + (result.IsValid ? "是" : "否"));
            builder.AppendLine("公式数量：" + result.FormulaCount);
            builder.AppendLine("有效数量：" + result.ValidCount);
            builder.AppendLine("错误数量：" + result.ErrorCount);
            builder.AppendLine("警告数量：" + result.Warnings.Count);
            builder.Append("已截断：" + (result.IsTruncated ? "是" : "否"));
            return builder.ToString();
        }
    }
}