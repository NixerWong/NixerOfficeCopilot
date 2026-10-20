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
            DisplayName = "清理中间表",
            Description = "删除本会话登记且符合指定前缀的中间工作表（默认前缀 _wip_）。"
                + "不会删除用户原有或未登记的同前缀工作表。任务收尾、确认最终结果已经落到正式表之后调用；可先用 dryRun 预演。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"prefix\":{\"type\":\"string\",\"description\":\"要清理的工作表名前缀，默认 _wip_。\"},"
                + "\"dryRun\":{\"type\":\"boolean\",\"description\":\"为 true 时只列出将被删除的表，不做任何改动。\"}"
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
