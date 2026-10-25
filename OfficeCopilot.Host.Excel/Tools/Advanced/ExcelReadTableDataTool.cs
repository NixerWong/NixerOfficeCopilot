using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelReadTableDataTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_read_table_data",
            DisplayName = "Read Table Data",
            Description = "Reads the data body of the specified Excel table.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"tableName\":{\"type\":\"string\"},"
                + "\"maxRows\":{\"type\":\"integer\",\"minimum\":1},"
                + "\"maxColumns\":{\"type\":\"integer\",\"minimum\":1}"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelTableDataRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ReadTableData(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.read_table_data_failed"));
            }
        }

        private static string BuildContent(ExcelTableDataResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("表：" + result.TableName);
            builder.AppendLine("工作表：" + result.WorksheetName);
            builder.AppendLine("区域：" + result.RangeAddress);
            builder.AppendLine("大小：" + result.RowCount + " 行 × " + result.ColumnCount + " 列");
            builder.AppendLine("已截断：" + (result.IsTruncated ? "是" : "否"));
            if (result.IsTruncated)
            {
                builder.AppendLine(ExcelReadGuidance.TruncationHint);
            }

            builder.AppendLine("预览：");
            builder.Append(result.Preview ?? string.Empty);
            return builder.ToString();
        }
    }
}