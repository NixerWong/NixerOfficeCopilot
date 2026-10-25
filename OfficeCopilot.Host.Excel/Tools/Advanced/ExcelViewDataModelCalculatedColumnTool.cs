using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelViewDataModelCalculatedColumnTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_view_data_model_calculated_column",
            DisplayName = "View Data Model Calculated Column",
            Description = "Shows the complete DAX formula for the specified Data Model calculated column.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"tableName\":{\"type\":\"string\"},"
                + "\"columnName\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"tableName\",\"columnName\"],"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelDataModelCalculatedColumnViewRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ViewDataModelCalculatedColumn(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.view_data_model_calculated_column_failed"));
            }
        }

        private static string BuildContent(ExcelDataModelCalculatedColumnViewResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("表：" + result.TableName);
            builder.AppendLine("列：" + result.ColumnName);
            builder.AppendLine("数据类型：" + (string.IsNullOrWhiteSpace(result.DataType) ? "-" : result.DataType));
            builder.AppendLine("描述：" + (string.IsNullOrWhiteSpace(result.Description) ? "-" : result.Description));
            builder.AppendLine("字符数：" + result.CharacterCount);
            builder.AppendLine("DAX：");
            builder.Append(result.DaxFormula ?? string.Empty);
            return builder.ToString();
        }
    }
}