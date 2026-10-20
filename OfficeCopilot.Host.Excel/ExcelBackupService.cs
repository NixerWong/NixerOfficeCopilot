using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Diagnostics.Logging;
using OfficeCopilot.Host.Excel.Tools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel
{
    internal sealed class ExcelBackupService : ExcelServiceBase
    {
        private const string IndexFileName = "index.jsonl";

        public ExcelBackupService(ExcelInterop.Application application)
            : base(application)
        {
        }

        public DocumentBackupResult CreateBackup(DocumentBackupRequest request)
        {
            request = request ?? new DocumentBackupRequest();

            try
            {
                var workbook = GetRequiredWorkbook();
                var documentName = workbook.Name ?? string.Empty;
                var originalPath = workbook.FullName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(originalPath))
                {
                    var msg = "无法创建备份：当前工作簿尚未保存到磁盘。请先按 Ctrl+S 保存工作簿后重试，或改用只读分析工具。";
                    BackupLog.Write($"[backup] FAIL: {msg}");
                    return CreateBackupFailure(request, msg, documentName);
                }

                var backupPath = BuildBackupPath("Excel", originalPath, documentName, request, ".xlsx");
                BackupLog.Write($"[backup] Creating backup: {backupPath} (len={backupPath.Length})");
                var dir = Path.GetDirectoryName(backupPath) ?? string.Empty;
                Directory.CreateDirectory(dir);
                BackupLog.Write($"[backup] Directory ensured: {dir}");
                workbook.SaveCopyAs(backupPath);
                BackupLog.Write($"[backup] OK: {backupPath}");

                var result = new DocumentBackupResult
                {
                    Success = true,
                    HostApplicationName = "Excel",
                    DocumentName = documentName,
                    OriginalPath = originalPath,
                    BackupPath = backupPath,
                    SessionId = request.SessionId ?? string.Empty,
                    ConversationId = request.ConversationId ?? string.Empty,
                    MessageId = request.MessageId ?? string.Empty,
                    ToolName = request.ToolName ?? string.Empty,
                    RiskLevel = request.RiskLevel ?? string.Empty,
                    OperationDescription = request.OperationDescription ?? string.Empty,
                    UserIntent = request.UserIntent ?? string.Empty,
                    CreatedAtUtc = DateTime.UtcNow
                };

                AppendIndexEntry(dir, result);
                return result;
            }
            catch (Exception ex)
            {
                BackupLog.Write($"[backup] FAIL exception: {ex.GetType().Name}: {ex.Message}");
                return CreateBackupFailure(request, ex.Message);
            }
        }

        private static DocumentBackupResult CreateBackupFailure(
            DocumentBackupRequest request,
            string errorMessage,
            string documentName = null,
            string originalPath = null)
        {
            return new DocumentBackupResult
            {
                Success = false,
                HostApplicationName = "Excel",
                DocumentName = documentName ?? string.Empty,
                OriginalPath = originalPath ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty,
                SessionId = request == null ? string.Empty : request.SessionId ?? string.Empty,
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                ToolName = request == null ? string.Empty : request.ToolName ?? string.Empty,
                RiskLevel = request == null ? string.Empty : request.RiskLevel ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        public ExcelListBackupsResult ListBackups(ExcelListBackupsRequest request)
        {
            request = request ?? new ExcelListBackupsRequest();

            var workbook = GetRequiredWorkbook();
            var documentName = workbook.Name ?? string.Empty;
            var originalPath = workbook.FullName ?? string.Empty;

            var result = new ExcelListBackupsResult
            {
                WorkbookName = documentName,
                OriginalPath = originalPath
            };

            if (string.IsNullOrWhiteSpace(originalPath))
            {
                return result;
            }

            var folder = BuildBackupFolder("Excel", originalPath, documentName);
            result.BackupFolder = folder;

            var maxCount = request.MaxCount > 0 ? request.MaxCount : 20;
            var entries = ReadIndexEntries(folder);
            for (var i = entries.Count - 1; i >= 0 && result.Entries.Count < maxCount; i--)
            {
                result.Entries.Add(entries[i]);
            }

            return result;
        }

        public ExcelRestoreBackupResult RestoreBackup(ExcelRestoreBackupRequest request)
        {
            request = request ?? new ExcelRestoreBackupRequest();

            var workbook = GetRequiredWorkbook();
            var documentName = workbook.Name ?? string.Empty;
            var originalPath = workbook.FullName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                throw new InvalidOperationException("当前工作簿尚未保存到磁盘，没有可用的备份点。");
            }

            var backupPath = (request.BackupPath ?? string.Empty).Trim();
            if (backupPath.Length == 0)
            {
                var folder = BuildBackupFolder("Excel", originalPath, documentName);
                var entries = ReadIndexEntries(folder);
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    if (File.Exists(entries[i].BackupPath))
                    {
                        backupPath = entries[i].BackupPath;
                        break;
                    }
                }
            }

            if (backupPath.Length == 0 || !File.Exists(backupPath))
            {
                throw new InvalidOperationException("未找到可用的备份文件：" + (backupPath.Length == 0 ? "(无备份点)" : backupPath));
            }

            // 还原不覆盖原文件：还原动作本身不应该再制造一次不可逆操作。
            var directory = Path.GetDirectoryName(originalPath) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(originalPath);
            var extension = Path.GetExtension(originalPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".xlsx";
            }

            var restoredPath = Path.Combine(
                directory,
                baseName + "_还原_" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) + extension);

            var suffix = 1;
            while (File.Exists(restoredPath))
            {
                restoredPath = Path.Combine(
                    directory,
                    baseName + "_还原_" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture)
                        + "_" + suffix.ToString(CultureInfo.InvariantCulture) + extension);
                suffix++;
            }

            File.Copy(backupPath, restoredPath);
            BackupLog.Write($"[restore] {backupPath} -> {restoredPath}");

            var result = new ExcelRestoreBackupResult
            {
                BackupPath = backupPath,
                RestoredPath = restoredPath,
                OriginalPath = originalPath,
                Message = "已从备份生成还原副本，原工作簿未被修改。"
            };

            if (request.OpenAfterRestore)
            {
                try
                {
                    _application.Workbooks.Open(restoredPath);
                    result.Opened = true;
                }
                catch (Exception ex)
                {
                    result.Message += " 但自动打开失败：" + ex.Message;
                }
            }

            return result;
        }

        [DataContract]
        internal sealed class BackupIndexRecord
        {
            [DataMember(Name = "backupPath", Order = 1)]
            public string BackupPath { get; set; }

            [DataMember(Name = "createdAtUtc", Order = 2)]
            public string CreatedAtUtc { get; set; }

            [DataMember(Name = "toolName", Order = 3)]
            public string ToolName { get; set; }

            [DataMember(Name = "riskLevel", Order = 4)]
            public string RiskLevel { get; set; }

            [DataMember(Name = "userIntent", Order = 5)]
            public string UserIntent { get; set; }

            [DataMember(Name = "operationDescription", Order = 6)]
            public string OperationDescription { get; set; }

            [DataMember(Name = "conversationId", Order = 7)]
            public string ConversationId { get; set; }

            [DataMember(Name = "messageId", Order = 8)]
            public string MessageId { get; set; }
        }

        private static void AppendIndexEntry(string folder, DocumentBackupResult result)
        {
            try
            {
                var record = new BackupIndexRecord
                {
                    BackupPath = result.BackupPath ?? string.Empty,
                    CreatedAtUtc = result.CreatedAtUtc.ToString("o", CultureInfo.InvariantCulture),
                    ToolName = result.ToolName ?? string.Empty,
                    RiskLevel = result.RiskLevel ?? string.Empty,
                    UserIntent = result.UserIntent ?? string.Empty,
                    OperationDescription = result.OperationDescription ?? string.Empty,
                    ConversationId = result.ConversationId ?? string.Empty,
                    MessageId = result.MessageId ?? string.Empty
                };

                var line = ExcelToolJsonSerializer.Serialize(record);
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                File.AppendAllText(Path.Combine(folder, IndexFileName), line + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                // 索引只影响可读性，写失败不能拖垮已经成功的备份。
                BackupLog.Write($"[backup] index append failed: {ex.Message}");
            }
        }

        private static List<ExcelBackupEntry> ReadIndexEntries(string folder)
        {
            var entries = new List<ExcelBackupEntry>();
            var indexPath = Path.Combine(folder ?? string.Empty, IndexFileName);
            if (!File.Exists(indexPath))
            {
                return entries;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(indexPath, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                BackupLog.Write($"[backup] index read failed: {ex.Message}");
                return entries;
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                BackupIndexRecord record;
                try
                {
                    record = ExcelToolJsonSerializer.Deserialize<BackupIndexRecord>(line);
                }
                catch
                {
                    continue;
                }

                DateTime createdAtUtc;
                var createdAtLocal = DateTime.TryParse(
                    record.CreatedAtUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out createdAtUtc)
                    ? createdAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : (record.CreatedAtUtc ?? string.Empty);

                entries.Add(new ExcelBackupEntry
                {
                    BackupPath = record.BackupPath ?? string.Empty,
                    CreatedAtLocal = createdAtLocal,
                    ToolName = record.ToolName ?? string.Empty,
                    RiskLevel = record.RiskLevel ?? string.Empty,
                    UserIntent = record.UserIntent ?? string.Empty,
                    OperationDescription = record.OperationDescription ?? string.Empty,
                    ConversationId = record.ConversationId ?? string.Empty,
                    MessageId = record.MessageId ?? string.Empty,
                    Exists = !string.IsNullOrWhiteSpace(record.BackupPath) && File.Exists(record.BackupPath)
                });
            }

            return entries;
        }

        private static string BuildBackupFolder(string hostApplicationName, string originalPath, string documentName)
        {
            var backupsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "Backups",
                string.IsNullOrWhiteSpace(hostApplicationName) ? "Unknown" : hostApplicationName);

            var docBase = SanitizeFileName(Path.GetFileNameWithoutExtension(
                string.IsNullOrWhiteSpace(documentName) ? "workbook" : documentName));
            if (docBase.Length > 16)
            {
                docBase = docBase.Substring(0, 16);
            }

            var pathHash4 = ComputeShortHash(
                string.IsNullOrWhiteSpace(originalPath) ? documentName : originalPath, 4);

            return Path.Combine(backupsRoot, docBase + "_" + pathHash4);
        }

        private static string BuildBackupPath(
            string hostApplicationName,
            string originalPath,
            string documentName,
            DocumentBackupRequest request,
            string fallbackExtension)
        {
            var folder = BuildBackupFolder(hostApplicationName, originalPath, documentName);

            var extension = Path.GetExtension(originalPath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = fallbackExtension;

            // 文件名：时间_HHmmss → 短且不重复
            var fileName = DateTime.Now.ToString("HHmmss") + extension;

            return Path.Combine(folder, fileName);
        }

        /// <summary>取 SHA256 前 <paramref name="length"/> 位小写十六进制。</summary>
        private static string ComputeShortHash(string value, int length)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(length);
                for (var i = 0; i < length && i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static string ComputeStableHash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                var isInvalid = false;
                for (var j = 0; j < invalidChars.Length; j++)
                {
                    if (invalidChars[j] == current)
                    {
                        isInvalid = true;
                        break;
                    }
                }

                builder.Append(isInvalid ? '_' : current);
            }

            return builder.ToString().Trim('_');
        }
    }

    internal static class BackupLog
    {
        public static void Write(string message)
        {
            OfficeCopilotLog.Write("Host.Excel.Backup", message);
        }
    }
}
