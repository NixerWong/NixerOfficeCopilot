using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    public sealed class PowerPointListBackupsTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.ListBackups,
            DisplayName = "List Backups",
            Description = "Lists backup points for the current presentation, newest first, including backup path, time, triggering tool, and user intent."
                + " A backup is automatically created before the first presentation modification in each response.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"maxCount\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100,\"description\":\"Maximum number of entries to return (default: 20).\"}"
                + "},"
                + "\"additionalProperties\":false"
                + "}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var maxCount = ParseArguments(request).Value<int?>("maxCount") ?? 20;
                var result = GetHost(context).ListBackups(Math.Max(1, Math.Min(100, maxCount)));

                var sb = new StringBuilder();
                sb.AppendLine("演示文稿：" + result.DocumentName
                    + (string.IsNullOrEmpty(result.OriginalPath) ? "（未保存）" : "（" + result.OriginalPath + "）"));
                sb.AppendLine("备份目录：" + result.BackupFolder);
                if (result.Entries.Count == 0)
                {
                    sb.Append("暂无备份点。");
                }

                foreach (var entry in result.Entries)
                {
                    sb.Append("- ").Append(entry.CreatedAtLocal)
                        .Append(' ').Append(entry.ToolName);
                    if (!string.IsNullOrEmpty(entry.UserIntent)) sb.Append(" 「").Append(entry.UserIntent).Append('」');
                    if (!entry.Exists) sb.Append(" (文件已不存在)");
                    sb.AppendLine().Append("  ").AppendLine(entry.BackupPath);
                }

                return Task.FromResult(Success(request, sb.ToString().TrimEnd(), ToJson(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.list_backups_failed"));
            }
        }
    }

    public sealed class PowerPointRestoreBackupTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.RestoreBackup,
            DisplayName = "Restore from Backup",
            Description = "Restores the specified backup point to a new file named {originalName}_restored_{HHmmss}.pptx. Saved presentations are restored beside the original file;"
                + " unsaved presentations are restored in the backup directory. Never overwrites the original file. Leave backupPath empty to use the most recent backup point.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = false,
            // 只生成新文件、不改动当前演示文稿：无需先备份。
            AffectsDocument = false,
            RequiresConfirmation = true,
            RiskLevel = "medium",
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"backupPath\":{\"type\":\"string\",\"description\":\"Full path of the backup file returned by ppt_list_backups. Leave empty to use the most recent backup point.\"},"
                + "\"openAfterRestore\":{\"type\":\"boolean\",\"description\":\"Whether to open the restored copy immediately (default: true).\"}"
                + "},"
                + "\"additionalProperties\":false"
                + "}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var args = ParseArguments(request);
                var result = GetHost(context).RestoreBackup(new DocumentRestoreRequest
                {
                    BackupPath = args.Value<string>("backupPath") ?? string.Empty,
                    OpenAfterRestore = args.Value<bool?>("openAfterRestore") ?? true
                });

                var sb = new StringBuilder();
                sb.AppendLine(result.Message);
                sb.AppendLine("备份源：" + result.BackupPath);
                sb.AppendLine("还原副本：" + result.RestoredPath);
                if (!string.IsNullOrEmpty(result.OriginalPath))
                    sb.AppendLine("原演示文稿：" + result.OriginalPath + "（未改动）");
                if (result.Opened)
                    sb.Append("已在 PowerPoint 中打开还原副本。");

                return Task.FromResult(Success(request, sb.ToString().TrimEnd(), ToJson(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.restore_backup_failed"));
            }
        }
    }
}
