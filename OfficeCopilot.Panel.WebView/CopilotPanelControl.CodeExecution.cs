using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Settings;

namespace OfficeCopilot.Panel.WebView
{
    public partial class CopilotPanelControl
    {
        private void PushAssistantAvatarToWeb()
        {
            var hostName = HostAdapter == null ? string.Empty : HostAdapter.HostApplicationName ?? string.Empty;
            PostToWeb(new
            {
                type = "setAssistantAvatar",
                avatarPath = GetAssistantAvatarPath(hostName)
            });
        }

        private static string GetAssistantAvatarPath(string hostName)
        {
            switch (hostName)
            {
                case HostApplicationNames.Excel:
                    return "images/excel-face.png";
                case HostApplicationNames.Word:
                    return "images/word-face.png";
                case HostApplicationNames.PowerPoint:
                    return "images/powerpoint-face.png";
                default:
                    return "images/bot.png";
            }
        }

        private void PushHostCapabilitiesToWeb()
        {
            var vbaHost = HostAdapter as IVbaExecutionHostAdapter;
            var availability = vbaHost != null
                ? vbaHost.GetVbaExecutionAvailability(false)
                : new VbaExecutionAvailability
                {
                    CanExecute = false,
                    Reason = "Current host does not support direct VBA execution."
                };

            var vbaEditorHost = HostAdapter as IVbaEditorHostAdapter;
            var editorAvailability = vbaEditorHost != null
                ? vbaEditorHost.GetVbaEditorAvailability(false)
                : new VbaExecutionAvailability
                {
                    CanExecute = false,
                    Reason = "Current host does not support opening code in VBA editor."
                };

            PostToWeb(new
            {
                type = "setCapabilities",
                capabilities = new
                {
                    canExecuteVba = vbaHost != null,
                    canExecuteVbaNow = availability.CanExecute,
                    canExecuteVbaReason = availability.Reason ?? string.Empty,
                    canOpenInVbaEditor = vbaEditorHost != null,
                    canOpenInVbaEditorNow = editorAvailability.CanExecute,
                    canOpenInVbaEditorReason = editorAvailability.Reason ?? string.Empty
                }
            });
        }

        private Task HandleOpenCodeInVbaEditorAsync(JObject payload)
        {
            var requestId = payload == null ? string.Empty : payload.Value<string>("requestId") ?? string.Empty;
            var language = payload == null ? string.Empty : payload.Value<string>("language") ?? string.Empty;
            var code = payload == null ? string.Empty : payload.Value<string>("code") ?? string.Empty;

            if (!IsRunnableVbaLanguage(language))
            {
                PostOpenInVbaEditorResult(requestId, false, "Only VBA code can be opened in VBA editor.", string.Empty);
                return Task.CompletedTask;
            }

            var vbaEditorHost = HostAdapter as IVbaEditorHostAdapter;
            if (vbaEditorHost == null)
            {
                PostOpenInVbaEditorResult(requestId, false, "Current host does not support opening code in VBA editor.", string.Empty);
                return Task.CompletedTask;
            }

            var availability = vbaEditorHost.GetVbaEditorAvailability(true);
            if (!availability.CanExecute)
            {
                PostOpenInVbaEditorResult(
                    requestId,
                    false,
                    string.IsNullOrWhiteSpace(availability.Reason)
                        ? "Current host does not support opening code in VBA editor."
                        : availability.Reason,
                    string.Empty);
                return Task.CompletedTask;
            }

            try
            {
                VbaEditorOpenResult result = null;

                SafeUiInvoke(() =>
                {
                    result = vbaEditorHost.OpenInVbaEditor(new VbaEditorOpenRequest
                    {
                        Code = code
                    });
                });

                result = result ?? new VbaEditorOpenResult
                {
                    Success = false,
                    Message = "Failed to open code in VBA editor.",
                    ModuleName = string.Empty
                };

                PostOpenInVbaEditorResult(
                    requestId,
                    result.Success,
                    result.Message,
                    result.ModuleName);
            }
            catch (Exception ex)
            {
                PostOpenInVbaEditorResult(requestId, false, ex.Message, string.Empty);
            }

            return Task.CompletedTask;
        }

        private Task HandleExecuteCodeBlockAsync(JObject payload)
        {
            var requestId = payload == null ? string.Empty : payload.Value<string>("requestId") ?? string.Empty;
            var messageId = payload == null ? string.Empty : payload.Value<string>("messageId") ?? string.Empty;
            var language = payload == null ? string.Empty : payload.Value<string>("language") ?? string.Empty;
            var code = payload == null ? string.Empty : payload.Value<string>("code") ?? string.Empty;
            var codeBlockIndex = payload == null ? -1 : payload.Value<int?>("codeBlockIndex") ?? -1;

            if (!IsRunnableVbaLanguage(language))
            {
                PostCodeExecutionResult(requestId, false, "Only VBA code blocks can be executed.", string.Empty);
                return Task.CompletedTask;
            }

            var vbaHost = HostAdapter as IVbaExecutionHostAdapter;
            if (vbaHost == null)
            {
                PostCodeExecutionResult(requestId, false, "Current host does not support direct VBA execution.", string.Empty);
                return Task.CompletedTask;
            }

            var availability = vbaHost.GetVbaExecutionAvailability(true);
            if (!availability.CanExecute)
            {
                PostCodeExecutionResult(
                    requestId,
                    false,
                    string.IsNullOrWhiteSpace(availability.Reason)
                        ? "Current host does not support direct VBA execution."
                        : availability.Reason,
                    string.Empty);
                return Task.CompletedTask;
            }
            var syntaxResult = vbaHost.ValidateVbaSyntax(code);
            if (!syntaxResult.IsValid)
            {
                PostCodeExecutionResult(
                    requestId,
                    false,
                    syntaxResult.Message ?? "VBA syntax validation failed.",
                    string.Empty);
                return Task.CompletedTask;
            }
            DocumentBackupResult backupResult = null;

            try
            {
                PostCodeExecutionBackupState(requestId, "pending", string.Empty);

                backupResult = CreateCodeExecutionBackup(messageId);

                if (backupResult == null || !backupResult.Success)
                {
                    PostCodeExecutionBackupState(requestId, "failed", string.Empty);
                    PostCodeExecutionResult(
                        requestId,
                        false,
                        backupResult == null || string.IsNullOrWhiteSpace(backupResult.ErrorMessage)
                            ? "Failed to create pre-execution backup."
                            : backupResult.ErrorMessage,
                        string.Empty);
                    return Task.CompletedTask;
                }

                PostCodeExecutionBackupState(requestId, "completed", backupResult.BackupPath ?? string.Empty);
                RecordCodeExecutionBackup(messageId, codeBlockIndex, language, backupResult.BackupPath ?? string.Empty);
            }
            catch (Exception ex)
            {
                PostCodeExecutionBackupState(requestId, "failed", string.Empty);
                PostCodeExecutionResult(requestId, false, ex.Message, string.Empty);
                return Task.CompletedTask;
            }

            try
            {
                VbaExecutionResult result = null;

                SafeUiInvoke(() =>
                {
                    result = vbaHost.ExecuteVba(new VbaExecutionRequest
                    {
                        Code = code,
                        RemoveModuleAfterRun = true
                    });
                });

                result = result ?? new VbaExecutionResult
                {
                    Success = false,
                    Message = "VBA execution returned no result."
                };

                PostCodeExecutionResult(
                    requestId,
                    result.Success,
                    result.Message,
                    backupResult == null ? string.Empty : backupResult.BackupPath ?? string.Empty);
            }
            catch (Exception ex)
            {
                PostCodeExecutionResult(
                    requestId,
                    false,
                    ex.Message,
                    backupResult == null ? string.Empty : backupResult.BackupPath ?? string.Empty);
            }

            return Task.CompletedTask;
        }

        private DocumentBackupResult CreateCodeExecutionBackup(string messageId)
        {
            var backupAdapter = HostAdapter as IDocumentBackupHostAdapter;
            if (backupAdapter == null)
            {
                throw new InvalidOperationException("Current host does not provide document backup capability.");
            }

            DocumentBackupResult backupResult = null;

            SafeUiInvoke(() =>
            {
                backupResult = backupAdapter.CreateBackup(new DocumentBackupRequest
                {
                    SessionId = _currentSession == null ? string.Empty : _currentSession.Id.ToString("D"),
                    ConversationId = _currentSession == null ? string.Empty : _currentSession.Id.ToString("D"),
                    MessageId = messageId ?? string.Empty,
                    ToolName = "executeCodeBlock",
                    OperationDescription = "Direct VBA code execution",
                    UserIntent = SummarizeUserIntent(_lastUserMessageText),
                    RiskLevel = "high"
                });
            });

            backupResult = backupResult ?? new DocumentBackupResult
            {
                Success = false,
                ErrorMessage = "Failed to create pre-execution backup."
            };

            if (backupResult.Success)
            {
                var backupStore = AgentServices == null
                    ? null
                    : AgentServices.GetService(typeof(IBackupArtifactStore)) as IBackupArtifactStore;

                if (backupStore != null)
                {
                    backupStore.Record(backupResult);
                }
            }

            return backupResult;
        }

        private void RecordCodeExecutionBackup(string messageId, int codeBlockIndex, string language, string backupPath)
        {
            if (string.IsNullOrWhiteSpace(messageId)
                || codeBlockIndex < 0
                || string.IsNullOrWhiteSpace(backupPath))
            {
                return;
            }

            EnsureCurrentSession();
            if (_currentSession == null || _currentSession.Messages == null)
            {
                return;
            }

            var assistantMessage = _currentSession.Messages
                .LastOrDefault(m => string.Equals(m.Id, messageId, StringComparison.OrdinalIgnoreCase));

            if (assistantMessage == null)
            {
                return;
            }

            if (assistantMessage.CodeExecutions == null)
            {
                assistantMessage.CodeExecutions = new System.Collections.Generic.List<Models.ChatCodeExecutionState>();
            }

            var item = assistantMessage.CodeExecutions
                .FirstOrDefault(c => c != null && c.CodeBlockIndex == codeBlockIndex);

            if (item == null)
            {
                assistantMessage.CodeExecutions.Add(new Models.ChatCodeExecutionState
                {
                    CodeBlockIndex = codeBlockIndex,
                    Language = language ?? string.Empty,
                    BackupPath = backupPath,
                    CreatedAt = DateTime.Now
                });
            }
            else
            {
                item.Language = language ?? string.Empty;
                item.BackupPath = backupPath;
            }

            _currentSession.UpdatedAt = DateTime.Now;
        }

        private void PostCodeExecutionBackupState(string requestId, string state, string backupPath)
        {
            PostToWeb(new
            {
                type = "codeExecutionBackupState",
                requestId = requestId ?? string.Empty,
                state = state ?? string.Empty,
                backupPath = backupPath ?? string.Empty
            });
        }

        private void PostCodeExecutionResult(string requestId, bool success, string message, string backupPath)
        {
            PostToWeb(new
            {
                type = "codeExecutionResult",
                requestId = requestId,
                success = success,
                message = message ?? string.Empty,
                backupPath = backupPath ?? string.Empty
            });
        }

        private void PostOpenInVbaEditorResult(string requestId, bool success, string message, string moduleName)
        {
            PostToWeb(new
            {
                type = "openInVbaEditorResult",
                requestId = requestId ?? string.Empty,
                success = success,
                message = message ?? string.Empty,
                moduleName = moduleName ?? string.Empty
            });
        }

        private static bool IsRunnableVbaLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return false;
            }

            var normalized = language.Trim().ToLowerInvariant();
            return normalized == "vba"
                || normalized == "vb"
                || normalized == "visual-basic"
                || normalized == "visualbasic";
        }
    }
}