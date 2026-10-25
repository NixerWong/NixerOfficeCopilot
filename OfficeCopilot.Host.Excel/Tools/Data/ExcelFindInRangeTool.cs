using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelFindInRangeTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_find_in_range",
            DisplayName = "Find in Range",
            Description = "Finds text or formula content in the specified range. If no address is provided, falls back to searching the current selection.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"searchValue\":{\"type\":\"string\"},"
                + "\"matchCase\":{\"type\":\"boolean\"},"
                + "\"matchEntireCell\":{\"type\":\"boolean\"},"
                + "\"searchFormulas\":{\"type\":\"boolean\"},"
                + "\"useSelectionWhenAddressEmpty\":{\"type\":\"boolean\"},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":1}"
                + "},"
                + "\"required\":[\"searchValue\"],"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelRangeFindRequest>(argumentsJson);
                arguments = ExcelToolRequestNormalizer.NormalizeRangeFindRequest(argumentsJson, arguments);

                var result = host.FindInRange(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.find_in_range_failed"));
            }
        }

        private static string BuildContent(ExcelRangeFindResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? string.Empty));
            builder.AppendLine("工作表：" + (result.WorksheetName ?? string.Empty));
            builder.AppendLine("搜索区域：" + (result.Address ?? string.Empty));
            builder.AppendLine("搜索内容：" + (result.SearchValue ?? string.Empty));
            builder.AppendLine("匹配数量：" + result.MatchingCells.Count);
            builder.AppendLine("已截断：" + (result.IsTruncated ? "是" : "否"));

            for (var index = 0; index < result.MatchingCells.Count; index++)
            {
                var match = result.MatchingCells[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(match.Address ?? string.Empty);
                builder.Append(" = ");
                builder.AppendLine(string.IsNullOrWhiteSpace(match.Value) ? "(空)" : match.Value);
            }

            return builder.ToString();
        }
    }
}