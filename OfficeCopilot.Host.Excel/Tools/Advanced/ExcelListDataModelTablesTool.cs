using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListDataModelTablesTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_data_model_tables",
            DisplayName = "List Data Model Tables",
            Description = "Lists the tables in the current workbook's Data Model.",
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
                var result = host.ListDataModelTables();
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.list_data_model_tables_failed"));
            }
        }

        private static string BuildContent(ExcelDataModelTableListResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("数据模型表数量：" + result.Tables.Count);

            for (var index = 0; index < result.Tables.Count; index++)
            {
                var item = result.Tables[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(item.Name);
                builder.Append(" [");
                builder.Append(string.IsNullOrWhiteSpace(item.SourceName) ? "-" : item.SourceName);
                builder.Append("] ");
                builder.Append(item.RecordCount);
                builder.AppendLine(" 行");
            }

            return builder.ToString();
        }
    }
}