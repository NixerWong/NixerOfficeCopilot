using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelGetRangeInfoTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_get_range_info",
            DisplayName = "读取区域信息",
            Description = "读取区域的尺寸、位置和数字格式等信息；可按需完整扫描隐藏行及其工作表行号；若未提供地址，可回退读取当前选区。",
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
                + "\"includeHiddenRows\":{\"type\":\"boolean\",\"description\":\"为 true 时逐行完整扫描该区域的隐藏状态并返回工作表行号；最多 100000 行，超出时明确失败且不返回部分结果；默认 false。\"}"
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

                var result = host.GetRangeInfo(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.get_range_info_failed"));
            }
        }

        private static string BuildContent(ExcelRangeInfoResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? string.Empty));
            builder.AppendLine("工作表：" + (result.WorksheetName ?? string.Empty));
            builder.AppendLine("区域：" + (result.Address ?? string.Empty));
            builder.AppendLine("大小：" + result.RowCount + " 行 × " + result.ColumnCount + " 列");
            builder.AppendLine("数字格式：" + (string.IsNullOrWhiteSpace(result.NumberFormat) ? "-" : result.NumberFormat));
            builder.AppendLine("Left：" + result.Left);
            builder.AppendLine("Top：" + result.Top);
            builder.AppendLine("Width：" + result.Width);
            builder.Append("Height：" + result.Height);
            if (result.HiddenRowsScanComplete)
            {
                builder.AppendLine();
                builder.AppendLine(
                    "隐藏行扫描：完整覆盖 " + result.HiddenRowsScanned + " 行");
                builder.AppendLine("隐藏行数：" + result.HiddenRowCount);
                builder.Append("隐藏的工作表行号："
                    + (result.HiddenRowNumbers == null || result.HiddenRowNumbers.Count == 0
                        ? "无"
                        : string.Join(", ", result.HiddenRowNumbers)));
            }
            return builder.ToString();
        }
    }
}