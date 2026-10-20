using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelGetActiveWorkbookTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_get_active_workbook",
            DisplayName = "获取当前工作簿",
            Description = "返回当前 Excel 活动工作簿、当前工作表和只读状态。",
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
                var workbook = host.GetActiveWorkbookInfo();

                if (workbook == null || !workbook.IsAvailable)
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        "当前没有活动工作簿。",
                        "excel.no_active_workbook"));
                }

                var content = BuildContent(workbook);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(workbook);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.get_active_workbook_failed"));
            }
        }

        private static string BuildContent(ExcelWorkbookInfo workbook)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + workbook.Name);
            builder.AppendLine("完整路径：" + (string.IsNullOrWhiteSpace(workbook.FullName) ? "-" : workbook.FullName));
            builder.AppendLine("当前工作表：" + (string.IsNullOrWhiteSpace(workbook.WorksheetName) ? "-" : workbook.WorksheetName));
            builder.Append("只读：" + (workbook.IsReadOnly ? "是" : "否"));
            return builder.ToString();
        }
    }
}