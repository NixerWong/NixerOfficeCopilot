using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelReadWorksheetDataTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_read_worksheet_data",
            DisplayName = "读取工作表数据",
            Description = "读取指定工作表中的区域数据；未提供地址时默认读取 UsedRange。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"maxRows\":{\"type\":\"integer\",\"minimum\":1},"
                + "\"maxColumns\":{\"type\":\"integer\",\"minimum\":1},"
                + "\"useUsedRangeWhenAddressEmpty\":{\"type\":\"boolean\",\"default\":true}"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelWorksheetDataRequest>(argumentsJson);
                arguments = ExcelToolRequestNormalizer.NormalizeWorksheetDataRequest(argumentsJson, arguments);

                var result = host.ReadWorksheetData(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.read_worksheet_data_failed"));
            }
        }

        private static string BuildContent(ExcelWorksheetDataResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("工作表：" + (string.IsNullOrWhiteSpace(result.WorksheetName) ? "-" : result.WorksheetName));
            builder.AppendLine("区域：" + (string.IsNullOrWhiteSpace(result.Address) ? "-" : result.Address));
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