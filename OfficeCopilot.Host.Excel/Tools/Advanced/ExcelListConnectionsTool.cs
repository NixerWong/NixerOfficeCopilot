using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListConnectionsTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_connections",
            DisplayName = "List Connections",
            Description = "Lists the data connections in the current workbook.",
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
                var result = host.ListConnections();
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.list_connections_failed"));
            }
        }

        private static string BuildContent(ExcelConnectionListResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("连接数量：" + result.Connections.Count);

            for (var index = 0; index < result.Connections.Count; index++)
            {
                var item = result.Connections[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(item.Name);
                builder.Append(" [");
                builder.Append(item.Type);
                builder.Append("]");
                if (item.IsPowerQuery)
                {
                    builder.Append(" [PowerQuery]");
                }
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}