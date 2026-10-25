using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelAnalyzeRangeTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_analyze_range",
            DisplayName = "Analyze Range",
            Description = "Analyzes a specified range using Excel's native calculation engine without writing to any cells. Supports aggregate and lookup_by_extreme operations.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"operation\":{\"type\":\"string\",\"enum\":[\"aggregate\",\"lookup_by_extreme\"]},"
                + "\"function\":{\"type\":\"string\",\"enum\":[\"max\",\"min\",\"average\",\"avg\",\"sum\",\"count\",\"counta\"]},"
                + "\"returnAddress\":{\"type\":\"string\"},"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelAnalyzeRangeRequest>(argumentsJson);
                arguments = ExcelToolRequestNormalizer.NormalizeAnalyzeRangeRequest(argumentsJson, arguments);

                var result = host.AnalyzeRange(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.analyze_range_failed"));
            }
        }

        private static string BuildContent(ExcelAnalyzeRangeResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? string.Empty));
            builder.AppendLine("工作表：" + (result.WorksheetName ?? string.Empty));
            builder.AppendLine("分析区域：" + (result.Address ?? string.Empty));
            builder.AppendLine("操作：" + (result.Operation ?? string.Empty));
            builder.AppendLine("函数：" + (result.Function ?? string.Empty));
            builder.AppendLine("结果：" + (string.IsNullOrWhiteSpace(result.ValueText) ? "(空)" : result.ValueText));
            builder.AppendLine("结果类型：" + (result.ValueType ?? string.Empty));
            builder.AppendLine("执行方式：" + (result.Engine ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(result.MatchAddress))
            {
                builder.AppendLine("匹配单元格：" + result.MatchAddress);
                builder.AppendLine("匹配序号：" + result.MatchIndex.ToString());
            }

            if (!string.IsNullOrWhiteSpace(result.ReturnAddress))
            {
                builder.AppendLine("关联区域：" + result.ReturnAddress);
            }

            if (!string.IsNullOrWhiteSpace(result.MatchedReturnAddress))
            {
                builder.AppendLine("关联结果区域：" + result.MatchedReturnAddress);
            }

            if (result.MatchedReturnValues != null && result.MatchedReturnValues.Count > 0)
            {
                builder.AppendLine("关联结果：" + string.Join(" | ", result.MatchedReturnValues));
            }

            if (string.Equals(result.Operation, "lookup_by_extreme", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine("说明：若存在重复极值，返回首个匹配项。");
            }

            return builder.ToString().Trim();
        }
    }
}
