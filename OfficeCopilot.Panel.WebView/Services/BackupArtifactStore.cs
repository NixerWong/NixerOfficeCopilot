using Newtonsoft.Json;
using OfficeCopilot.Host.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OfficeCopilot.Panel.WebView.Services
{
    internal sealed class BackupArtifactRecord
    {
        public BackupArtifactRecord()
        {
            Id = string.Empty;
            HostApplicationName = string.Empty;
            DocumentName = string.Empty;
            OriginalPath = string.Empty;
            BackupPath = string.Empty;
            SessionId = string.Empty;
            ConversationId = string.Empty;
            MessageId = string.Empty;
            ToolName = string.Empty;
            RiskLevel = string.Empty;
        }

        public string Id { get; set; }

        public string HostApplicationName { get; set; }

        public string DocumentName { get; set; }

        public string OriginalPath { get; set; }

        public string BackupPath { get; set; }

        public string SessionId { get; set; }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public string ToolName { get; set; }

        public string RiskLevel { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class BackupArtifactStore : IBackupArtifactStore
    {
        private readonly object _syncRoot;

        public BackupArtifactStore()
        {
            _syncRoot = new object();
        }

        public void Record(DocumentBackupResult result)
        {
            if (result == null || !result.Success || string.IsNullOrWhiteSpace(result.BackupPath))
            {
                return;
            }

            lock (_syncRoot)
            {
                try
                {
                    var path = GetIndexFilePath();
                    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);

                    var records = LoadCore(path);
                    records.Add(new BackupArtifactRecord
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        HostApplicationName = result.HostApplicationName ?? string.Empty,
                        DocumentName = result.DocumentName ?? string.Empty,
                        OriginalPath = result.OriginalPath ?? string.Empty,
                        BackupPath = result.BackupPath ?? string.Empty,
                        SessionId = result.SessionId ?? string.Empty,
                        ConversationId = result.ConversationId ?? string.Empty,
                        MessageId = result.MessageId ?? string.Empty,
                        ToolName = result.ToolName ?? string.Empty,
                        RiskLevel = result.RiskLevel ?? string.Empty,
                        CreatedAtUtc = result.CreatedAtUtc == default(DateTime) ? DateTime.UtcNow : result.CreatedAtUtc
                    });

                    File.WriteAllText(path, JsonConvert.SerializeObject(records, Formatting.Indented), Encoding.UTF8);
                }
                catch
                {
                }
            }
        }

        private static List<BackupArtifactRecord> LoadCore(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new List<BackupArtifactRecord>();
                }

                var json = File.ReadAllText(path, Encoding.UTF8);
                var items = JsonConvert.DeserializeObject<List<BackupArtifactRecord>>(json);
                return items == null
                    ? new List<BackupArtifactRecord>()
                    : items.Where(item => item != null).ToList();
            }
            catch
            {
                return new List<BackupArtifactRecord>();
            }
        }

        private static string GetIndexFilePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "Backups",
                "backups.json");
        }
    }
}
