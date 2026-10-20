using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelRestoreBackupTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_restore_backup",
            DisplayName = "从备份还原",
            Description = "把指定备份点还原成一个新文件 {原名}_还原_{时分秒}.xlsx，放在原工作簿旁边。"
                + "绝不覆盖原文件，用户可以对比后自行决定用哪个。留空 backupPath 表示还原最近一个备份点。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            // 只生成新文件、不改动当前工作簿：无需先备份。
            AffectsDocument = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"backupPath\":{\"type\":\"string\",\"description\":\"备份文件完整路径，来自 excel_list_backups。留空表示最近一个备份点。\"},"
                + "\"openAfterRestore\":{\"type\":\"boolean\",\"description\":\"还原后是否直接打开该副本。\"}"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelRestoreBackupRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.RestoreBackup(arguments);

                var builder = new StringBuilder();
                builder.AppendLine(result.Message ?? string.Empty);
                builder.AppendLine("备份源：" + (result.BackupPath ?? "-"));
                builder.AppendLine("还原副本：" + (result.RestoredPath ?? "-"));
                builder.AppendLine("原工作簿：" + (result.OriginalPath ?? "-") + "（未改动）");
                if (result.Opened)
                {
                    builder.AppendLine("已在 Excel 中打开还原副本。");
                }

                return Task.FromResult(CreateSuccess(
                    request,
                    builder.ToString(),
                    ExcelToolJsonSerializer.Serialize(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.restore_backup_failed"));
            }
        }
    }
}
