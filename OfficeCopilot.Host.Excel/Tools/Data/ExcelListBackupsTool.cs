using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelListBackupsTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_list_backups",
            DisplayName = "List Backups",
            Description = "Lists automatic backup points for the current workbook, including time, triggering tool, and a summary of user intent."
                + " When the user asks to undo the last operation or restore the previous state, use this tool first to locate a backup point.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"maxCount\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Maximum number of backup points to return (default: 20), ordered newest first.\"}"
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
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelListBackupsRequest>(
                    request == null ? null : request.ArgumentsJson);

                var result = host.ListBackups(arguments);
                return Task.FromResult(CreateSuccess(
                    request,
                    BuildContent(result),
                    ExcelToolJsonSerializer.Serialize(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(request, ex, "excel.list_backups_failed"));
            }
        }

        private static string BuildContent(ExcelListBackupsResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? "-"));
            builder.AppendLine("备份目录：" + (string.IsNullOrWhiteSpace(result.BackupFolder) ? "-" : result.BackupFolder));

            if (result.Entries.Count == 0)
            {
                builder.AppendLine("没有找到备份点。工作簿未保存到磁盘，或尚未执行过需要备份的写操作。");
                return builder.ToString();
            }

            builder.AppendLine("备份点（新→旧）：");
            foreach (var entry in result.Entries)
            {
                builder.Append("- ").Append(entry.CreatedAtLocal)
                    .Append("  [").Append(entry.ToolName).Append("]")
                    .Append(entry.Exists ? string.Empty : "（文件已丢失）")
                    .AppendLine();
                if (!string.IsNullOrWhiteSpace(entry.UserIntent))
                {
                    builder.AppendLine("  意图：" + entry.UserIntent);
                }

                builder.AppendLine("  路径：" + entry.BackupPath);
            }

            return builder.ToString();
        }
    }
}
