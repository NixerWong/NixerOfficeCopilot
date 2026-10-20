using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel
{
    public sealed partial class ExcelHostAdapter : IDocumentBackupHostAdapter, IDocumentRestoreHostAdapter
    {
        DocumentRestoreResult IDocumentRestoreHostAdapter.RestoreBackup(DocumentRestoreRequest request)
        {
            request = request ?? new DocumentRestoreRequest();
            var result = RestoreBackup(new ExcelRestoreBackupRequest
            {
                BackupPath = request.BackupPath ?? string.Empty,
                OpenAfterRestore = request.OpenAfterRestore
            });

            return new DocumentRestoreResult
            {
                BackupPath = result.BackupPath ?? string.Empty,
                RestoredPath = result.RestoredPath ?? string.Empty,
                OriginalPath = result.OriginalPath ?? string.Empty,
                Opened = result.Opened,
                Message = result.Message ?? string.Empty
            };
        }
    }
}