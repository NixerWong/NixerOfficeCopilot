using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelViewVbaModuleTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_view_vba_module",
            DisplayName = "View VBA Module",
            Description = "Shows the complete code for the specified VBA module.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"moduleName\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"moduleName\"],"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelVbaModuleViewRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ViewVbaModule(arguments);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.view_vba_module_failed"));
            }
        }

        private static string BuildContent(ExcelVbaModuleViewResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("模块：" + (string.IsNullOrWhiteSpace(result.ModuleName) ? "-" : result.ModuleName));
            builder.AppendLine("类型：" + (string.IsNullOrWhiteSpace(result.ModuleType) ? "-" : result.ModuleType));
            builder.AppendLine("行数：" + result.LineCount);
            builder.AppendLine("代码：");
            builder.Append(result.Code ?? string.Empty);
            return builder.ToString();
        }
    }
}