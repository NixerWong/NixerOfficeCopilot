using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListDataModelRelationshipsTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_data_model_relationships",
            DisplayName = "List Data Model Relationships",
            Description = "Lists the relationships in the current workbook's Data Model.",
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
                var result = host.ListDataModelRelationships();
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.list_data_model_relationships_failed"));
            }
        }

        private static string BuildContent(ExcelDataModelRelationshipListResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + result.WorkbookName);
            builder.AppendLine("关系数量：" + result.Relationships.Count);

            for (var index = 0; index < result.Relationships.Count; index++)
            {
                var item = result.Relationships[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(item.FromTable);
                builder.Append("[");
                builder.Append(item.FromColumn);
                builder.Append("] -> ");
                builder.Append(item.ToTable);
                builder.Append("[");
                builder.Append(item.ToColumn);
                builder.Append("] ");
                builder.AppendLine(item.IsActive ? "[Active]" : "[Inactive]");
            }

            return builder.ToString();
        }
    }
}