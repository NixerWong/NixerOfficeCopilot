using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelListBackupsRequest
    {
        public ExcelListBackupsRequest()
        {
            MaxCount = 20;
        }

        [DataMember(Name = "maxCount", Order = 1)]
        public int MaxCount { get; set; }
    }

    [DataContract]
    public sealed class ExcelBackupEntry
    {
        public ExcelBackupEntry()
        {
            BackupPath = string.Empty;
            CreatedAtLocal = string.Empty;
            ToolName = string.Empty;
            RiskLevel = string.Empty;
            UserIntent = string.Empty;
            OperationDescription = string.Empty;
            ConversationId = string.Empty;
            MessageId = string.Empty;
        }

        [DataMember(Name = "backupPath", Order = 1)]
        public string BackupPath { get; set; }

        [DataMember(Name = "createdAtLocal", Order = 2)]
        public string CreatedAtLocal { get; set; }

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

        [DataMember(Name = "exists", Order = 9)]
        public bool Exists { get; set; }
    }

    [DataContract]
    public sealed class ExcelListBackupsResult
    {
        public ExcelListBackupsResult()
        {
            WorkbookName = string.Empty;
            OriginalPath = string.Empty;
            BackupFolder = string.Empty;
            Entries = new List<ExcelBackupEntry>();
        }

        [DataMember(Name = "workbookName", Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Name = "originalPath", Order = 2)]
        public string OriginalPath { get; set; }

        [DataMember(Name = "backupFolder", Order = 3)]
        public string BackupFolder { get; set; }

        [DataMember(Name = "entries", Order = 4)]
        public List<ExcelBackupEntry> Entries { get; set; }
    }

    [DataContract]
    public sealed class ExcelRestoreBackupRequest
    {
        public ExcelRestoreBackupRequest()
        {
            BackupPath = string.Empty;
        }

        /// <summary>留空表示还原最近一个备份点。</summary>
        [DataMember(Name = "backupPath", Order = 1)]
        public string BackupPath { get; set; }

        [DataMember(Name = "openAfterRestore", Order = 2)]
        public bool OpenAfterRestore { get; set; }
    }

    [DataContract]
    public sealed class ExcelRestoreBackupResult
    {
        public ExcelRestoreBackupResult()
        {
            BackupPath = string.Empty;
            RestoredPath = string.Empty;
            OriginalPath = string.Empty;
            Message = string.Empty;
        }

        [DataMember(Name = "backupPath", Order = 1)]
        public string BackupPath { get; set; }

        /// <summary>还原产物的路径。还原永远另存为新文件，绝不覆盖原工作簿。</summary>
        [DataMember(Name = "restoredPath", Order = 2)]
        public string RestoredPath { get; set; }

        [DataMember(Name = "originalPath", Order = 3)]
        public string OriginalPath { get; set; }

        [DataMember(Name = "opened", Order = 4)]
        public bool Opened { get; set; }

        [DataMember(Name = "message", Order = 5)]
        public string Message { get; set; }
    }
}
