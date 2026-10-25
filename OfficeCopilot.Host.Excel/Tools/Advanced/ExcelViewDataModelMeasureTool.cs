using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelViewDataModelMeasureTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_view_data_model_measure",
            DisplayName = "View Data Model Measure",
            Description = "Shows the complete DAX formula for the specified Data Model measure.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"measureName\":{\"type\":\"string\"},"
                + "\"tableName\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"measureName\"],"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelDataModelMeasureViewRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ViewDataModelMeasure(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.view_data_model_measure_failed"));
            }
        }

        private static string BuildContent(ExcelDataModelMeasureViewResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("表：" + result.TableName);
            builder.AppendLine("Measure：" + result.MeasureName);
            builder.AppendLine("字符数：" + result.CharacterCount);
            builder.AppendLine("描述：" + (string.IsNullOrWhiteSpace(result.Description) ? "-" : result.Description));
            builder.AppendLine("DAX：");
            builder.Append(result.DaxFormula ?? string.Empty);
            return builder.ToString();
        }
    }
}