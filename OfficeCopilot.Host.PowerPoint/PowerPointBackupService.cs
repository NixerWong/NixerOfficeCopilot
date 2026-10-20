using OfficeCopilot.Host.Abstractions;
using System;
using System.IO;
using PowerPointApp = Microsoft.Office.Interop.PowerPoint.Application;
using PowerPointInterop = Microsoft.Office.Interop.PowerPoint;

namespace OfficeCopilot.Host.PowerPoint
{
    /// <summary>
    /// PowerPoint 文档备份与还原。用 <c>Presentation.SaveCopyAs</c> 取快照：
    /// 不修改原文档的保存状态，未保存过的演示文稿同样可以备份。
    /// 还原永远另存为 <c>_还原_</c> 副本，不覆盖原文件。
    /// 所有方法都应在 <see cref="PowerPointComDispatcher"/> 内调用。
    /// </summary>
    internal sealed class PowerPointBackupService
    {
        private readonly PowerPointApp _application;
        private readonly PowerPointBackupStore _store;

        public PowerPointBackupService(PowerPointApp application, PowerPointBackupStore store)
        {
            _application = application;
            _store = store ?? new PowerPointBackupStore();
        }

        public DocumentBackupResult CreateBackup(DocumentBackupRequest request)
        {
            request = request ?? new DocumentBackupRequest();
            string documentName = null;
            string originalPath = null;
            try
            {
                var presentation = GetRequiredPresentation();
                documentName = presentation.Name ?? string.Empty;
                originalPath = GetSavedPath(presentation);

                var backupPath = _store.GetNewBackupPath(originalPath, documentName, DateTime.Now);
                var folder = Path.GetDirectoryName(backupPath) ?? string.Empty;
                Directory.CreateDirectory(folder);

                // 晚绑定调用：SaveCopyAs 的可选参数类型 MsoTriState 位于 office.dll。
                ComLate.Call(presentation, "SaveCopyAs", backupPath);

                if (!File.Exists(backupPath))
                {
                    return CreateFailure(request, "SaveCopyAs 未生成备份文件：" + backupPath, documentName, originalPath);
                }

                var result = new DocumentBackupResult
                {
                    Success = true,
                    HostApplicationName = "PowerPoint",
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

                if (string.IsNullOrEmpty(originalPath))
                {
                    result.Metadata["unsaved"] = "true";
                }

                try
                {
                    _store.AppendIndex(folder, result);
                }
                catch (Exception)
                {
                    // 索引只影响可读性，写失败不能拖垮已经成功的备份。
                }

                return result;
            }
            catch (Exception ex)
            {
                return CreateFailure(request, ex.Message, documentName, originalPath);
            }
        }

        public DocumentBackupListResult ListBackups(int maxCount)
        {
            var presentation = GetRequiredPresentation();
            var documentName = presentation.Name ?? string.Empty;
            var originalPath = GetSavedPath(presentation);
            var folder = _store.GetFolder(originalPath, documentName);

            var result = new DocumentBackupListResult
            {
                DocumentName = documentName,
                OriginalPath = originalPath,
                BackupFolder = folder
            };

            var limit = maxCount > 0 ? maxCount : 20;
            var entries = _store.ReadIndex(folder);
            for (var i = entries.Count - 1; i >= 0 && result.Entries.Count < limit; i--)
            {
                result.Entries.Add(entries[i]);
            }

            return result;
        }

        public DocumentRestoreResult RestoreBackup(DocumentRestoreRequest request)
        {
            request = request ?? new DocumentRestoreRequest();

            var presentation = TryGetActivePresentation();
            var documentName = presentation == null ? string.Empty : presentation.Name ?? string.Empty;
            var originalPath = presentation == null ? string.Empty : GetSavedPath(presentation);

            var backupPath = (request.BackupPath ?? string.Empty).Trim();
            if (backupPath.Length == 0)
            {
                if (presentation == null)
                {
                    throw new InvalidOperationException("当前没有打开的演示文稿，无法确定要还原的备份点。");
                }

                backupPath = _store.FindLatestExistingBackup(_store.GetFolder(originalPath, documentName));
            }

            if (backupPath.Length == 0 || !File.Exists(backupPath))
            {
                throw new InvalidOperationException("未找到可用的备份文件：" + (backupPath.Length == 0 ? "(无备份点)" : backupPath));
            }

            if (!_store.IsUnderBackupsRoot(backupPath))
            {
                throw new InvalidOperationException("只能从 PowerPoint 备份目录还原：" + backupPath);
            }

            // 面板按钮给出的备份可能不属于当前活动文档：此时把还原副本放在备份目录里。
            var belongsToActive = presentation != null
                && string.Equals(
                    Path.GetDirectoryName(backupPath) ?? string.Empty,
                    _store.GetFolder(originalPath, documentName),
                    StringComparison.OrdinalIgnoreCase);
            if (!belongsToActive)
            {
                originalPath = string.Empty;
                documentName = Path.GetFileName(Path.GetDirectoryName(backupPath) ?? string.Empty);
            }

            var restoredPath = _store.GetRestoredPath(originalPath, documentName, backupPath, DateTime.Now);
            Directory.CreateDirectory(Path.GetDirectoryName(restoredPath) ?? string.Empty);
            File.Copy(backupPath, restoredPath);

            var result = new DocumentRestoreResult
            {
                BackupPath = backupPath,
                RestoredPath = restoredPath,
                OriginalPath = originalPath,
                Message = string.IsNullOrEmpty(originalPath)
                    ? "已从备份生成还原副本（保存在备份目录），原演示文稿未被修改。"
                    : "已从备份生成还原副本，原演示文稿未被修改。"
            };

            if (request.OpenAfterRestore && _application != null)
            {
                try
                {
                    ComLate.Call(_application.Presentations, "Open", restoredPath);
                    result.Opened = true;
                }
                catch (Exception ex)
                {
                    result.Message += " 但自动打开失败：" + ex.Message;
                }
            }

            return result;
        }

        private PowerPointInterop.Presentation GetRequiredPresentation()
        {
            var presentation = TryGetActivePresentation();
            if (presentation == null)
            {
                throw new InvalidOperationException("当前没有打开的演示文稿。");
            }

            return presentation;
        }

        private PowerPointInterop.Presentation TryGetActivePresentation()
        {
            if (_application == null)
            {
                return null;
            }

            try
            {
                return _application.Presentations.Count == 0 ? null : _application.ActivePresentation;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        /// <summary>已保存文档返回完整路径；未保存文档（Path 为空）返回空串。</summary>
        internal static string GetSavedPath(PowerPointInterop.Presentation presentation)
        {
            try
            {
                return string.IsNullOrWhiteSpace(presentation.Path) ? string.Empty : presentation.FullName ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static DocumentBackupResult CreateFailure(
            DocumentBackupRequest request,
            string errorMessage,
            string documentName,
            string originalPath)
        {
            return new DocumentBackupResult
            {
                Success = false,
                HostApplicationName = "PowerPoint",
                DocumentName = documentName ?? string.Empty,
                OriginalPath = originalPath ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty,
                SessionId = request.SessionId ?? string.Empty,
                ConversationId = request.ConversationId ?? string.Empty,
                MessageId = request.MessageId ?? string.Empty,
                ToolName = request.ToolName ?? string.Empty,
                RiskLevel = request.RiskLevel ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
        }
    }
}
