using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    /// <summary>
    /// 清理分步执行留下的中间过程表（默认 _wip_ 前缀）。
    /// </summary>
    public sealed class ExcelCleanupWorkspaceTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_cleanup_workspace",
            DisplayName = "Clean Up Intermediate Tables",
            Description = "Deletes intermediate worksheets registered during this session that match the specified prefix (default: _wip_)."
                + " Never deletes pre-existing or unregistered worksheets with the same prefix. Call at the end of a task after confirming that final results are in the production table; use dryRun to preview first.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"prefix\":{\"type\":\"string\",\"description\":\"Worksheet-name prefix to clean up (default: _wip_).\"},"
                + "\"dryRun\":{\"type\":\"boolean\",\"description\":\"When true, only lists worksheets that would be deleted and makes no changes.\"}"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelCleanupWorkspaceRequest>(argumentsJson)
                    ?? new ExcelCleanupWorkspaceRequest();
                var registryHost = host as IExcelWipRegistryHostAdapter;
                if (registryHost == null)
                {
                    throw new InvalidOperationException("无法验证本会话登记的中间表；为避免删除用户工作表，已阻止清理。");
                }

                var registry = registryHost.GetRegisteredWipWorksheets(context.ConversationId);
                if (registry == null || !registry.Success)
                {
                    throw new InvalidOperationException(registry == null
                        ? "读取本会话中间表登记失败；为避免删除用户工作表，已阻止清理。"
                        : registry.ErrorMessage ?? "读取本会话中间表登记失败；为避免删除用户工作表，已阻止清理。");
                }

                arguments.RegisteredWorksheetNames = registry.RegisteredWorksheetNames;

                var result = host.CleanupWorkspace(arguments);
                var content = BuildContent(result);
                var refresh = registryHost.RefreshWipSheetRegistry(
                    context.ConversationId,
                    false,
                    null,
                    null);
                if (refresh == null || !refresh.Success)
                {
                    content += "\n警告：已执行清理，但中间表登记状态未能同步；"
                        + (refresh == null || string.IsNullOrWhiteSpace(refresh.ErrorMessage)
                            ? "下一轮将重新核对工作表。"
                            : refresh.ErrorMessage);
                }

                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);
                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.cleanup_workspace_failed"));
            }
        }

        private static string BuildContent(ExcelCleanupWorkspaceResult result)
        {
            if (result == null)
            {
                return "清理结果为空。";
            }

            var builder = new StringBuilder();
            builder.AppendLine("前缀：" + result.Prefix);
            builder.AppendLine("说明：" + result.Message);

            if (result.MatchedSheets.Count > 0)
            {
                builder.AppendLine("匹配到的表：" + string.Join("、", result.MatchedSheets));
            }

            if (result.RemovedSheets.Count > 0)
            {
                builder.AppendLine("已删除：" + string.Join("、", result.RemovedSheets));
            }

            return builder.ToString().TrimEnd();
        }
    }
}
