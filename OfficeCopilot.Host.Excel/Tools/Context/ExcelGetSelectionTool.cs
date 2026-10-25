using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelGetSelectionTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_get_selection",
            DisplayName = "Get Selection",
            Description = "Returns the active Excel selection's worksheet, address, dimensions, and preview.",
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
                var selection = host.GetSelectionInfo();

                if (selection == null || !selection.IsAvailable)
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        "当前没有可用选区。",
                        "excel.no_selection"));
                }

                var content = BuildContent(selection);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(selection);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.get_selection_failed"));
            }
        }

        private static string BuildContent(ExcelSelectionInfo selection)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(selection.WorkbookName) ? "-" : selection.WorkbookName));
            builder.AppendLine("工作表：" + (string.IsNullOrWhiteSpace(selection.WorksheetName) ? "-" : selection.WorksheetName));
            builder.AppendLine("选区：" + (string.IsNullOrWhiteSpace(selection.Address) ? "-" : selection.Address));
            builder.AppendLine("大小：" + selection.RowCount + " 行 × " + selection.ColumnCount + " 列");
            builder.AppendLine("预览：");
            builder.Append(selection.Preview ?? string.Empty);
            return builder.ToString();
        }
    }
}