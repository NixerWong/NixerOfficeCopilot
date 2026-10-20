using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelViewTableTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_view_table",
            DisplayName = "查看表",
            Description = "查看指定 Excel 表的结构信息。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"tableName\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"tableName\"],"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelTableViewRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ViewTable(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.view_table_failed"));
            }
        }

        private static string BuildContent(ExcelTableViewResult result)
        {
            var table = result.Table ?? new ExcelTableInfo();
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("表：" + table.Name);
            builder.AppendLine("工作表：" + table.WorksheetName);
            builder.AppendLine("区域：" + table.RangeAddress);
            builder.AppendLine("大小：" + table.RowCount + " 行 × " + table.ColumnCount + " 列");
            builder.AppendLine("显示表头：" + (table.HasHeaders ? "是" : "否"));
            builder.AppendLine("显示汇总行：" + (table.ShowTotals ? "是" : "否"));
            builder.AppendLine("样式：" + (string.IsNullOrWhiteSpace(table.TableStyle) ? "-" : table.TableStyle));
            builder.AppendLine("列：");
            for (var index = 0; index < table.Columns.Count; index++)
            {
                builder.Append("- ");
                builder.AppendLine(table.Columns[index]);
            }

            return builder.ToString();
        }
    }
}