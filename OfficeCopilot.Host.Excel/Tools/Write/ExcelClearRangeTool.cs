using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelClearRangeTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_clear_range",
            DisplayName = "清空区域",
            Description = "清空指定单元格区域中的值和公式，但保留样式与格式；若未提供地址，可回退清空当前选区。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            RequiresConfirmation = true,
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

                var result = host.ClearRange(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.clear_range_failed"));
            }
        }

        private static string BuildContent(ExcelRangeClearResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("工作表：" + (string.IsNullOrWhiteSpace(result.WorksheetName) ? "-" : result.WorksheetName));
            builder.AppendLine("区域：" + (string.IsNullOrWhiteSpace(result.Address) ? "-" : result.Address));
            builder.AppendLine("大小：" + result.RowCount + " 行 × " + result.ColumnCount + " 列");
            builder.Append("已清空单元格：" + result.ClearedCellCount);
            return builder.ToString();
        }
    }
}
