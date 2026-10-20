using System;
using System.Collections.Generic;

namespace OfficeCopilot.Host.Abstractions
{
    public sealed class HostContextSnapshot
    {
        public string HostApplicationName { get; set; }

        public string DocumentName { get; set; }

        public string WorksheetName { get; set; }

        public string SelectionAddress { get; set; }

        public string SelectionPreview { get; set; }

        public string Summary { get; set; }

        public string WorkbookMapSummary { get; set; }

        public ExcelWorksheetListResult WorkbookMap { get; set; }
    }

    public interface ICopilotHostAdapter
    {
        string HostApplicationName { get; }

        HostContextSnapshot GetContextSnapshot();

        void InsertText(string text);
    }

    public sealed class VbaExecutionRequest
    {
        public string Code { get; set; }

        public string ModuleName { get; set; }

        public string EntryPoint { get; set; }

        public bool RemoveModuleAfterRun { get; set; } = true;

        /// <summary>执行看门狗秒数；≤ 0 表示不设限。超时后向 Excel 发送中断信号。</summary>
        public int TimeoutSeconds { get; set; }

        /// <summary>由宿主确认策略设置，启用仅写中间表的运行时保护。</summary>
        public bool StagedOnly { get; set; }

        /// <summary>由 HostBridge 静态校验后传入；临时保护除这些声明目标外的工作表。</summary>
        public IList<string> ProtectionAllowedWorksheetNames { get; set; }
    }

    public sealed class VbaExecutionResult
    {
        public VbaExecutionResult()
        {
            LogEntries = new List<string>();
        }

        public bool Success { get; set; }

        public string Message { get; set; }

        public string ModuleName { get; set; }

        public string EntryPoint { get; set; }

        /// <summary>被执行代码通过注入的 NixerLog 过程回传的结构化日志。</summary>
        public List<string> LogEntries { get; set; }

        public long ElapsedMilliseconds { get; set; }

        /// <summary>本次执行是否因看门狗超时而被中断。</summary>
        public bool TimedOut { get; set; }

        public string ErrorCode { get; set; }
    }

    public sealed class VbaExecutionAvailability
    {
        public bool CanExecute { get; set; }

        public string Reason { get; set; }
    }

    public interface IVbaExecutionHostAdapter
    {
        bool CanExecuteVba { get; }

        VbaExecutionAvailability GetVbaExecutionAvailability(bool verifyWriteAccess);

        VbaExecutionResult ExecuteVba(VbaExecutionRequest request);

        VbaSyntaxValidationResult ValidateVbaSyntax(string code);
    }

    public interface IStagedWriteProtectionHostAdapter
    {
        VbaExecutionAvailability GetStagedWriteProtectionAvailability();
    }

    public interface IVbaDeclaredWriteProtectionHostAdapter
    {
        VbaExecutionAvailability GetDeclaredWriteProtectionAvailability(
            IList<string> allowedWorksheetNames);
    }

    public sealed class VbaSyntaxValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
    }

    public sealed class VbaEditorOpenRequest
    {
        public string Code { get; set; }

        public string ModuleName { get; set; }
    }

    public sealed class VbaEditorOpenResult
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public string ModuleName { get; set; }
    }

    public interface IVbaEditorHostAdapter
    {
        bool CanOpenInVbaEditor { get; }

        VbaExecutionAvailability GetVbaEditorAvailability(bool verifyWriteAccess);

        VbaEditorOpenResult OpenInVbaEditor(VbaEditorOpenRequest request);
    }

    public sealed class DocumentBackupRequest
    {
        public DocumentBackupRequest()
        {
            SessionId = string.Empty;
            ConversationId = string.Empty;
            MessageId = string.Empty;
            ToolName = string.Empty;
            OperationDescription = string.Empty;
            UserIntent = string.Empty;
            RiskLevel = "high";
        }

        public string SessionId { get; set; }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public string ToolName { get; set; }

        public string OperationDescription { get; set; }

        /// <summary>触发本次备份的用户原话摘要，是用户日后认出备份点的唯一线索。</summary>
        public string UserIntent { get; set; }

        public string RiskLevel { get; set; }
    }

    public sealed class DocumentBackupResult
    {
        public DocumentBackupResult()
        {
            HostApplicationName = string.Empty;
            DocumentName = string.Empty;
            OriginalPath = string.Empty;
            BackupPath = string.Empty;
            ErrorMessage = string.Empty;
            SessionId = string.Empty;
            ConversationId = string.Empty;
            MessageId = string.Empty;
            ToolName = string.Empty;
            RiskLevel = string.Empty;
            OperationDescription = string.Empty;
            UserIntent = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CreatedAtUtc = DateTime.UtcNow;
        }

        public bool Success { get; set; }

        public string HostApplicationName { get; set; }

        public string DocumentName { get; set; }

        public string OriginalPath { get; set; }

        public string BackupPath { get; set; }

        public string ErrorMessage { get; set; }

        public string SessionId { get; set; }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public string ToolName { get; set; }

        public string RiskLevel { get; set; }

        public string OperationDescription { get; set; }

        public string UserIntent { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }

    public interface IDocumentBackupHostAdapter
    {
        DocumentBackupResult CreateBackup(DocumentBackupRequest request);
    }

    public sealed class DocumentBackupEntry
    {
        public DocumentBackupEntry()
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

        public string BackupPath { get; set; }

        public string CreatedAtLocal { get; set; }

        public string ToolName { get; set; }

        public string RiskLevel { get; set; }

        public string UserIntent { get; set; }

        public string OperationDescription { get; set; }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public bool Exists { get; set; }
    }

    public sealed class DocumentBackupListResult
    {
        public DocumentBackupListResult()
        {
            DocumentName = string.Empty;
            OriginalPath = string.Empty;
            BackupFolder = string.Empty;
            Entries = new List<DocumentBackupEntry>();
        }

        public string DocumentName { get; set; }

        /// <summary>原文档路径；未保存过的文档为空。</summary>
        public string OriginalPath { get; set; }

        public string BackupFolder { get; set; }

        public List<DocumentBackupEntry> Entries { get; set; }
    }

    public sealed class DocumentRestoreRequest
    {
        public DocumentRestoreRequest()
        {
            BackupPath = string.Empty;
        }

        /// <summary>留空表示还原最近一个备份点。</summary>
        public string BackupPath { get; set; }

        public bool OpenAfterRestore { get; set; }
    }

    public sealed class DocumentRestoreResult
    {
        public DocumentRestoreResult()
        {
            BackupPath = string.Empty;
            RestoredPath = string.Empty;
            OriginalPath = string.Empty;
            Message = string.Empty;
        }

        public string BackupPath { get; set; }

        /// <summary>还原副本路径。还原永远另存为新文件，绝不覆盖原文档。</summary>
        public string RestoredPath { get; set; }

        public string OriginalPath { get; set; }

        public bool Opened { get; set; }

        public string Message { get; set; }
    }

    /// <summary>宿主无关的备份还原契约，供面板"撤销本次修改"按钮使用。</summary>
    public interface IDocumentRestoreHostAdapter
    {
        DocumentRestoreResult RestoreBackup(DocumentRestoreRequest request);
    }

    public interface IBackupArtifactStore
    {
        void Record(DocumentBackupResult result);
    }

    public sealed class ToolConfirmationRequest
    {
        public ToolConfirmationRequest()
        {
            ToolName = string.Empty;
            ToolCallId = string.Empty;
            ToolDisplayName = string.Empty;
            ToolDescription = string.Empty;
            OperationSummary = string.Empty;
            ArgumentsSummary = string.Empty;
            RiskLevel = "high";
            DocumentName = string.Empty;
            BackupPath = string.Empty;
            SessionId = string.Empty;
            ConversationId = string.Empty;
            MessageId = string.Empty;
        }

        public string ToolName { get; set; }

        public string ToolCallId { get; set; }

        public string ToolDisplayName { get; set; }

        public string ToolDescription { get; set; }

        public string OperationSummary { get; set; }

        public string ArgumentsSummary { get; set; }

        public string RiskLevel { get; set; }

        public string DocumentName { get; set; }

        public string BackupPath { get; set; }

        public string SessionId { get; set; }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }
    }

    public interface IToolConfirmationHandler
    {
        bool Confirm(ToolConfirmationRequest request);
    }
}
