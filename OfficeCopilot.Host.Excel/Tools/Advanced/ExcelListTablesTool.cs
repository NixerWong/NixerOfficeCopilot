using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListTablesTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_tables",
            DisplayName = "列出表",
            Description = "列出当前工作簿中的 Excel 表。",
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
                var result = host.ListTables();
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.list_tables_failed"));
            }
        }

        private static string BuildContent(ExcelTableListResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("表数量：" + result.Tables.Count);

            for (var index = 0; index < result.Tables.Count; index++)
            {
                var table = result.Tables[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(table.Name);
                builder.Append(" [");
                builder.Append(table.WorksheetName);
                builder.Append("] ");
                builder.Append(table.RangeAddress);
                builder.Append(" ");
                builder.Append(table.RowCount);
                builder.Append("×");
                builder.Append(table.ColumnCount);
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}