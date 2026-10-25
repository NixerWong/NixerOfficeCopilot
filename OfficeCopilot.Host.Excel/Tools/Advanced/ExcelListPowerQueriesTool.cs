using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListPowerQueriesTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_power_queries",
            DisplayName = "List Power Queries",
            Description = "Lists Power Query names, formula previews, and load-mode summaries in the current workbook.",
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
                var result = host.ListPowerQueries();
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.list_power_queries_failed"));
            }
        }

        private static string BuildContent(ExcelPowerQueryListResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("Power Query 数量：" + result.Queries.Count);

            for (var index = 0; index < result.Queries.Count; index++)
            {
                var query = result.Queries[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(query.Name);
                builder.Append(" [");
                builder.Append(query.IsConnectionOnly ? "仅连接" : "已加载");
                builder.Append("] ");
                builder.AppendLine(string.IsNullOrWhiteSpace(query.FormulaPreview) ? "-" : query.FormulaPreview);
            }

            return builder.ToString();
        }
    }
}