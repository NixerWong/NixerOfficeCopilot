using Newtonsoft.Json;
using OfficeCopilot.Host.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OfficeCopilot.Host.PowerPoint
{
    /// <summary>
    /// 备份目录布局、index.jsonl 读写与还原路径计算。与 Excel 方案一致：
    /// <c>%LOCALAPPDATA%\NixerOfficeCopilot\Backups\PowerPoint\{文档名前16位}_{路径哈希4位}\HHmmss.pptx</c>。
    /// 不依赖 COM，便于单元测试。
    /// </summary>
    public sealed class PowerPointBackupStore
    {
        public const string IndexFileName = "index.jsonl";
        public const string HostFolderName = "PowerPoint";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string _backupsRoot;

        public PowerPointBackupStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "Backups",
                HostFolderName))
        {
        }

        public PowerPointBackupStore(string backupsRoot)
        {
            if (string.IsNullOrWhiteSpace(backupsRoot))
            {
                throw new ArgumentException("backupsRoot is required.", nameof(backupsRoot));
            }

            _backupsRoot = backupsRoot;
        }

        public string BackupsRoot
        {
            get { return _backupsRoot; }
        }

        /// <summary>
        /// 备份目录。已保存文档按完整路径哈希；未保存文档（"演示文稿1"）按文档名哈希，
        /// 同一会话里同名未保存文档共享目录，这是可以接受的。
        /// </summary>
        public string GetFolder(string originalPath, string documentName)
        {
            var docBase = SanitizeFileName(Path.GetFileNameWithoutExtension(
                string.IsNullOrWhiteSpace(documentName) ? "presentation" : documentName));
            if (docBase.Length == 0)
            {
                docBase = "presentation";
            }

            if (docBase.Length > 16)
            {
                docBase = docBase.Substring(0, 16);
            }

            var key = string.IsNullOrWhiteSpace(originalPath) ? "unsaved:" + (documentName ?? string.Empty) : originalPath;
            return Path.Combine(_backupsRoot, docBase + "_" + ComputeShortHash(key, 4));
        }

        public string GetNewBackupPath(string originalPath, string documentName, DateTime now)
        {
            var folder = GetFolder(originalPath, documentName);
            var extension = GetExtension(originalPath);
            var stamp = now.ToString("HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(folder, stamp + extension);
            var suffix = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, stamp + "_" + suffix.ToString(CultureInfo.InvariantCulture) + extension);
                suffix++;
            }

            return path;
        }

        /// <summary>
        /// 还原副本路径：已保存文档放在原文件旁边；未保存文档放在备份目录。永不覆盖已有文件。
        /// </summary>
        public string GetRestoredPath(string originalPath, string documentName, string backupPath, DateTime now)
        {
            string directory;
            string baseName;
            string extension;
            if (!string.IsNullOrWhiteSpace(originalPath) && Path.IsPathRooted(originalPath))
            {
                directory = Path.GetDirectoryName(originalPath) ?? string.Empty;
                baseName = Path.GetFileNameWithoutExtension(originalPath);
                extension = GetExtension(originalPath);
            }
            else
            {
                directory = Path.GetDirectoryName(backupPath ?? string.Empty);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    directory = GetFolder(originalPath, documentName);
                }

                baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(
                    string.IsNullOrWhiteSpace(documentName) ? "presentation" : documentName));
                if (baseName.Length == 0)
                {
                    baseName = "presentation";
                }

                extension = GetExtension(backupPath);
            }

            var stamp = now.ToString("HHmmss", CultureInfo.InvariantCulture);
            var restoredPath = Path.Combine(directory, baseName + "_还原_" + stamp + extension);
            var suffix = 1;
            while (File.Exists(restoredPath))
            {
                restoredPath = Path.Combine(
                    directory,
                    baseName + "_还原_" + stamp + "_" + suffix.ToString(CultureInfo.InvariantCulture) + extension);
                suffix++;
            }

            return restoredPath;
        }

        public void AppendIndex(string folder, DocumentBackupResult result)
        {
            if (string.IsNullOrWhiteSpace(folder) || result == null)
            {
                return;
            }

            var record = new IndexRecord
            {
                BackupPath = result.BackupPath ?? string.Empty,
                CreatedAtUtc = result.CreatedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ToolName = result.ToolName ?? string.Empty,
                RiskLevel = result.RiskLevel ?? string.Empty,
                UserIntent = result.UserIntent ?? string.Empty,
                OperationDescription = result.OperationDescription ?? string.Empty,
                ConversationId = result.ConversationId ?? string.Empty,
                MessageId = result.MessageId ?? string.Empty
            };

            Directory.CreateDirectory(folder);
            var line = JsonConvert.SerializeObject(record, Formatting.None);
            File.AppendAllText(Path.Combine(folder, IndexFileName), line + "\n", Utf8NoBom);
        }

        /// <summary>按写入顺序返回索引条目（旧 → 新）。损坏行跳过。</summary>
        public List<DocumentBackupEntry> ReadIndex(string folder)
        {
            var entries = new List<DocumentBackupEntry>();
            var indexPath = Path.Combine(folder ?? string.Empty, IndexFileName);
            if (!File.Exists(indexPath))
            {
                return entries;
            }

            foreach (var line in File.ReadAllLines(indexPath, Utf8NoBom))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                IndexRecord record;
                try
                {
                    record = JsonConvert.DeserializeObject<IndexRecord>(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (record == null)
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

                entries.Add(new DocumentBackupEntry
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

        public string FindLatestExistingBackup(string folder)
        {
            var entries = ReadIndex(folder);
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Exists)
                {
                    return entries[i].BackupPath;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 只允许还原本宿主备份根目录下的文件，防止面板/模型传入任意路径被复制到文档目录。
        /// </summary>
        public bool IsUnderBackupsRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string full;
            string root;
            try
            {
                full = Path.GetFullPath(path);
                root = Path.GetFullPath(_backupsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            }
            catch (Exception)
            {
                return false;
            }

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetExtension(string path)
        {
            var extension = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetExtension(path);
            return string.IsNullOrWhiteSpace(extension) ? ".pptx" : extension;
        }

        internal static string ComputeShortHash(string value, int length)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(length * 2);
                for (var i = 0; i < length && i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        internal static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return builder.ToString().Trim('_', ' ', '.');
        }

        private sealed class IndexRecord
        {
            [JsonProperty("backupPath", Order = 1)]
            public string BackupPath { get; set; }

            [JsonProperty("createdAtUtc", Order = 2)]
            public string CreatedAtUtc { get; set; }

            [JsonProperty("toolName", Order = 3)]
            public string ToolName { get; set; }

            [JsonProperty("riskLevel", Order = 4)]
            public string RiskLevel { get; set; }

            [JsonProperty("userIntent", Order = 5)]
            public string UserIntent { get; set; }

            [JsonProperty("operationDescription", Order = 6)]
            public string OperationDescription { get; set; }

            [JsonProperty("conversationId", Order = 7)]
            public string ConversationId { get; set; }

            [JsonProperty("messageId", Order = 8)]
            public string MessageId { get; set; }
        }
    }
}
