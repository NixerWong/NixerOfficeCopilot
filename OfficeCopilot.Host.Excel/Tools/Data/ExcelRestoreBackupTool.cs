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
            DisplayName = "Restore from Backup",
            Description = "Restores the specified backup point to a new file named {originalName}_restored_{HHmmss}.xlsx beside the original workbook."
                + " Never overwrites the original file, so the user can compare both versions. Leave backupPath empty to restore the most recent backup point.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            // 只生成新文件、不改动当前工作簿：无需先备份。
            AffectsDocument = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"backupPath\":{\"type\":\"string\",\"description\":\"Full path of the backup file returned by excel_list_backups. Leave empty to use the most recent backup point.\"},"
                + "\"openAfterRestore\":{\"type\":\"boolean\",\"description\":\"Whether to open the restored copy immediately.\"}"
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
