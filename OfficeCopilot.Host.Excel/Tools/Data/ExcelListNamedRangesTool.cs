using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListNamedRangesTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_named_ranges",
            DisplayName = "列出命名区域",
            Description = "列出当前工作簿中的命名区域、作用域、引用公式和值摘要。",
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

        public override Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request)
        {
            try
            {
                var host = GetExcelToolHost(context);
                var result = host.ListNamedRanges();
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.list_named_ranges_failed"));
            }
        }

        private static string BuildContent(ExcelNamedRangeListResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("命名区域数量：" + result.NamedRanges.Count);

            for (var index = 0; index < result.NamedRanges.Count; index++)
            {
                var item = result.NamedRanges[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(item.Name);
                builder.Append(" [");
                builder.Append(string.IsNullOrWhiteSpace(item.Scope) ? "Workbook" : item.Scope);
                builder.Append("] = ");
                builder.AppendLine(string.IsNullOrWhiteSpace(item.ValueText) ? "-" : item.ValueText);
            }

            return builder.ToString();
        }
    }
}