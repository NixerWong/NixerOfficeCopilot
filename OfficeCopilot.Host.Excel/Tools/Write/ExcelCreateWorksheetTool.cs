using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelCreateWorksheetTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_create_worksheet",
            DisplayName = "Create Worksheet",
            Description = "Creates a worksheet at the end of the current workbook. Sheets named with the _wip_NN_description pattern are registered as session intermediates and can be used for scratch calculations.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\",\"description\":\"Name of the new worksheet; intermediate sheets must use the _wip_NN_description format.\"}"
                + "},"
                + "\"required\":[\"worksheetName\"],"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelWorksheetCreationRequest>(
                    request == null ? null : request.ArgumentsJson);
                var worksheetName = (arguments.WorksheetName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(worksheetName))
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        "新工作表必须提供名称。",
                        "excel.worksheet_name_required"));
                }

                var isWipWorksheet = ExcelStagingConventions.IsWipSheet(worksheetName);
                var nameViolation = ExcelStagingConventions.GetStagedNameViolation(worksheetName);
                if (isWipWorksheet && nameViolation != null)
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        nameViolation,
                        "excel.wip_sheet_name_invalid"));
                }

                IExcelWipRegistryHostAdapter registryHost = null;
                ExcelWipSheetRegistryResult registrySnapshot = null;
                if (isWipWorksheet)
                {
                    registryHost = host as IExcelWipRegistryHostAdapter;
                    if (registryHost == null)
                    {
                        return Task.FromResult(CreateFailure(
                            request,
                            "本会话中间表登记服务不可用；为避免创建未登记的 scratch 工作表，已阻止操作。",
                            "excel.wip_registry_unavailable"));
                    }

                    registrySnapshot = registryHost.GetRegisteredWipWorksheets(context.ConversationId);
                    if (registrySnapshot == null || !registrySnapshot.Success)
                    {
                        return Task.FromResult(CreateFailure(
                            request,
                            registrySnapshot == null
                                ? "无法读取本会话工作表快照；为避免创建未登记的 scratch 工作表，已阻止操作。"
                                : registrySnapshot.ErrorMessage ?? "无法读取本会话工作表快照；为避免创建未登记的 scratch 工作表，已阻止操作。",
                            registrySnapshot == null
                                ? "excel.wip_registry_snapshot_failed"
                                : registrySnapshot.ErrorCode));
                    }
                }

                var result = host.CreateWorksheet(new ExcelWorksheetCreationRequest
                {
                    WorksheetName = worksheetName
                });
                var content = BuildContent(result);
                if (isWipWorksheet)
                {
                    var registration = registryHost.RefreshWipSheetRegistry(
                        context.ConversationId,
                        true,
                        worksheetName,
                        registrySnapshot.CurrentWorksheetNames);
                    var wasRegistered = registration != null
                        && registration.Success
                        && registration.RegisteredWorksheetNames != null
                        && registration.RegisteredWorksheetNames.Contains(
                            worksheetName,
                            StringComparer.OrdinalIgnoreCase);
                    if (!wasRegistered)
                    {
                        var registrationError = registration == null
                            ? "登记服务未返回结果。"
                            : registration.ErrorMessage ?? "未能确认登记结果。";
                        content += "\n错误：工作表已创建，但未能登记为本会话中间表。"
                            + "请勿继续将其作为 scratch 写入，也不要再次调用建表；当前工作表仍保留。"
                            + " 原因：" + registrationError;
                        return Task.FromResult(CreateFailure(
                            request,
                            "工作表已创建，但未能登记为本会话中间表；请勿按 scratch 继续写入。",
                            string.IsNullOrWhiteSpace(registration == null ? null : registration.ErrorCode)
                                ? "excel.wip_registry_registration_failed"
                                : registration.ErrorCode,
                            content,
                            ExcelToolJsonSerializer.Serialize(result)));
                    }
                }

                return Task.FromResult(CreateSuccess(
                    request,
                    content,
                    ExcelToolJsonSerializer.Serialize(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.create_worksheet_failed"));
            }
        }

        private static string BuildContent(ExcelWorksheetCreationResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("已创建工作表：" + (result == null ? string.Empty : result.WorksheetName));
            if (result != null && !string.IsNullOrWhiteSpace(result.WorkbookName))
            {
                builder.Append("工作簿：" + result.WorkbookName);
            }

            return builder.ToString().TrimEnd();
        }
    }
}
