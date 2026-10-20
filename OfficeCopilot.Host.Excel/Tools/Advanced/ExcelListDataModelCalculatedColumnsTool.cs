using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListDataModelCalculatedColumnsTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_data_model_calculated_columns",
            DisplayName = "列出数据模型计算列",
            Description = "列出当前工作簿 Data Model 中的计算列。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
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
                var result = host.ListDataModelCalculatedColumns();
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.list_data_model_calculated_columns_failed"));
            }
        }

        private static string BuildContent(ExcelDataModelCalculatedColumnListResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("计算列数量：" + result.CalculatedColumns.Count);

            for (var index = 0; index < result.CalculatedColumns.Count; index++)
            {
                var item = result.CalculatedColumns[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(item.TableName);
                builder.Append("[");
                builder.Append(item.Name);
                builder.Append("] ");
                builder.AppendLine(string.IsNullOrWhiteSpace(item.FormulaPreview) ? "-" : item.FormulaPreview);
            }

            return builder.ToString();
        }
    }
}