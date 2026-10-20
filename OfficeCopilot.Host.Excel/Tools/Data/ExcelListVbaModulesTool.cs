using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListVbaModulesTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_vba_modules",
            DisplayName = "列出 VBA 模块",
            Description = "列出当前工作簿中的 VBA 模块、类型、代码行数和过程列表。",
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
                var result = host.ListVbaModules();
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.list_vba_modules_failed"));
            }
        }

        private static string BuildContent(ExcelVbaModuleListResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (string.IsNullOrWhiteSpace(result.WorkbookName) ? "-" : result.WorkbookName));
            builder.AppendLine("模块数量：" + result.Modules.Count);

            for (var index = 0; index < result.Modules.Count; index++)
            {
                var module = result.Modules[index];
                builder.Append(index + 1);
                builder.Append(". ");
                builder.Append(module.Name);
                builder.Append(" [");
                builder.Append(module.Type);
                builder.Append("] ");
                builder.Append(module.LineCount);
                builder.AppendLine(" 行");
            }

            return builder.ToString();
        }
    }
}