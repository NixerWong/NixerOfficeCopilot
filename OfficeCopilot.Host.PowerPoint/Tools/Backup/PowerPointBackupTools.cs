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
            DisplayName = "列出备份",
            Description = "列出当前演示文稿的备份点（新 → 旧）：备份文件路径、时间、触发工具、用户意图。"
                + "每次回复里第一次修改演示文稿前都会自动备份一次。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"maxCount\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100,\"description\":\"最多返回条数，默认 20。\"}"
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
            DisplayName = "从备份还原",
            Description = "把指定备份点还原成新文件 {原名}_还原_{时分秒}.pptx：已保存的演示文稿放在原文件旁边，"
                + "未保存的放在备份目录。绝不覆盖原文件。留空 backupPath 表示最近一个备份点。",
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
                + "\"backupPath\":{\"type\":\"string\",\"description\":\"备份文件完整路径，来自 ppt_list_backups。留空表示最近一个备份点。\"},"
                + "\"openAfterRestore\":{\"type\":\"boolean\",\"description\":\"还原后是否直接打开该副本，默认 true。\"}"
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
