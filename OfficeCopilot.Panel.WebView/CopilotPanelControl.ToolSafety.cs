using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Panel.WebView.Services;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OfficeCopilot.Panel.WebView
{
    public partial class CopilotPanelControl
    {
        private readonly object _toolConfirmationSyncRoot = new object();
        private readonly Dictionary<string, PendingToolConfirmationRequest> _pendingToolConfirmations =
            new Dictionary<string, PendingToolConfirmationRequest>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sessionApprovedTools =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private IToolConfirmationHandler ResolveConfirmationHandler()
        {
            var confirmationHandler = AgentServices == null ? null : AgentServices.GetService(typeof(IToolConfirmationHandler)) as IToolConfirmationHandler;
            return confirmationHandler ?? new ChatToolConfirmationHandler(this);
        }

        internal bool CanUseInlineToolConfirmation()
        {
            return !IsDisposed
                && IsHandleCreated
                && _webBridge.CanPost;
        }

        internal bool IsToolApprovedForCurrentSession(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            lock (_toolConfirmationSyncRoot)
            {
                return _sessionApprovedTools.Contains(toolName.Trim());
            }
        }

        internal void ApproveToolForCurrentSession(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return;
            }

            lock (_toolConfirmationSyncRoot)
            {
                _sessionApprovedTools.Add(toolName.Trim());
            }
        }

        internal bool IsToolApprovedPermanently(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            EnsureToolConfirmationPoliciesLoaded();

            return _settings.ToolConfirmationPolicies.Any(p =>
                p != null
                && string.Equals(p.ToolName, toolName.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Mode, "allow-always", StringComparison.OrdinalIgnoreCase));
        }

        internal void ApproveToolPermanently(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return;
            }

            EnsureToolConfirmationPoliciesLoaded();

            var normalizedToolName = toolName.Trim();
            var existing = _settings.ToolConfirmationPolicies.FirstOrDefault(p =>
                p != null
                && string.Equals(p.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new ToolConfirmationPolicySettings
                {
                    ToolName = normalizedToolName
                };

                _settings.ToolConfirmationPolicies.Add(existing);
            }

            existing.Mode = "allow-always";
            existing.UpdatedAtUtc = DateTime.UtcNow;
            _settings.UpdatedAtUtc = DateTime.UtcNow;
            SaveSettingsPreservingToolTrustModes();
        }

        internal string RequestToolConfirmationFromWeb(ToolConfirmationRequest request)
        {
            request = request ?? new ToolConfirmationRequest();

            if (!CanUseInlineToolConfirmation() || !InvokeRequired)
            {
                return new WindowsFormsToolConfirmationHandler(this).Confirm(request)
                    ? "allow-once"
                    : "deny";
            }

            // Phase 7C: 统一使用 Phase 7C 的 toolConfirmation WebView 组件 + TCS 模式
            var requestId = request.ToolCallId ?? Guid.NewGuid().ToString("N");
            var tcs = _interactions.Register(requestId, null);

            PostToWeb(new
            {
                type = "toolConfirmation",
                requestId = requestId,
                toolName = request.ToolName ?? string.Empty,
                toolDescription = request.ToolDescription ?? string.Empty,
                operationSummary = request.OperationSummary ?? string.Empty,
                riskLevel = request.RiskLevel ?? "high",
                argumentsSummary = request.ArgumentsSummary ?? string.Empty
            });

            // 同步阻塞等待（HostBridge 要求 Confirm 返回 bool）
            AgentBrokerToolUserInteractionResponse response;
            try
            {
                response = tcs.Task.GetAwaiter().GetResult();
            }
            catch
            {
                response = null;
            }
            finally
            {
                _interactions.Unregister(requestId, true);
                // 通知 JS 清理确认面板
                PostToWeb(new { type = "userInteractionCompleted", requestId = requestId });
            }

            if (response == null || response.Cancelled)
            {
                return "deny";
            }

            // 从 answersJson 解析 decision 级别
            string decision = "allow-once";
            try
            {
                var answers = Newtonsoft.Json.Linq.JObject.Parse(response.AnswersJson ?? "{}");
                decision = answers["decision"]?.ToString() ?? "allow-once";
            }
            catch { }

            // 处理会话/永久授权
            RememberToolConfirmationDecision(request.ToolName, decision);

            return NormalizeToolConfirmationDecision(decision);
        }

        private void HandleToolConfirmationResponse(JObject payload)
        {
            // Phase 7C: 统一委托给 HandleSubmitUserInteraction。
            // 注意 decision 必须原样带进 answers，否则上游无法区分
            // allow-once / allow-session / allow-always，“始终允许”会失效。
            var requestId = payload.Value<string>("requestId") ?? string.Empty;
            var decision = NormalizeToolConfirmationDecision(payload.Value<string>("decision"));
            var approved = !string.Equals(decision, "deny", StringComparison.OrdinalIgnoreCase);
            var answersJson = new JObject
            {
                ["approved"] = approved,
                ["decision"] = decision
            }.ToString(Newtonsoft.Json.Formatting.None);

            HandleSubmitUserInteraction(requestId, answersJson, "{}", !approved);
        }

        /// <summary>
        /// 把工具确认面板返回的授权级别写入会话级 / 永久级授权缓存。
        /// Broker 侧与宿主侧两条确认路径都会调用此方法，确保授权只需给一次。
        /// </summary>
        internal void RememberToolConfirmationDecision(
            string toolName,
            AgentBrokerToolUserInteractionResponse response)
        {
            if (string.IsNullOrWhiteSpace(toolName) || response == null || response.Cancelled)
            {
                return;
            }

            string decision;
            try
            {
                var answers = JObject.Parse(response.AnswersJson ?? "{}");
                decision = answers.Value<string>("decision");
            }
            catch (Exception)
            {
                return;
            }

            RememberToolConfirmationDecision(toolName, decision);
        }

        internal void RememberToolConfirmationDecision(string toolName, string decision)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return;
            }

            var normalized = NormalizeToolConfirmationDecision(decision);

            if (string.Equals(normalized, "allow-always", StringComparison.OrdinalIgnoreCase))
            {
                ApproveToolPermanently(toolName);
                ApproveToolForCurrentSession(toolName);
            }
            else if (string.Equals(normalized, "allow-session", StringComparison.OrdinalIgnoreCase))
            {
                ApproveToolForCurrentSession(toolName);
            }
        }

        private void EnsureToolConfirmationPoliciesLoaded()
        {
            if (_settings == null)
            {
                LoadSettings();
            }

            if (_settings.ToolConfirmationPolicies == null)
            {
                _settings.ToolConfirmationPolicies = new List<ToolConfirmationPolicySettings>();
            }
        }

        private static string NormalizeToolConfirmationDecision(string decision)
        {
            var value = string.IsNullOrWhiteSpace(decision) ? string.Empty : decision.Trim().ToLowerInvariant();

            if (value == "allow-once"
                || value == "allow-session"
                || value == "allow-always")
            {
                return value;
            }

            return "deny";
        }

        private bool ToolRequiresBackup(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName) || ToolRegistry == null)
            {
                return false;
            }

            var definitions = ToolRegistry.GetDefinitions();
            if (definitions == null || definitions.Count == 0)
            {
                return false;
            }

            var definition = definitions.FirstOrDefault(d =>
                d != null
                && string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase));

            return definition != null && definition.RequiresBackup;
        }

        private static string GetBackupPath(ToolExecutionResult result)
        {
            if (result == null || result.Metadata == null)
            {
                return string.Empty;
            }

            string backupPath;
            return result.Metadata.TryGetValue(ToolMetadataKeys.BackupPath, out backupPath)
                ? backupPath ?? string.Empty
                : string.Empty;
        }

        private void OpenBackupArtifacts(JObject payload)
        {
            var backupPaths = ParseBackupPaths(payload)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (backupPaths.Count == 0)
            {
                MessageBox.Show(this, "No configuration backup was found.", "View Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (backupPaths.Count == 1)
            {
                OpenBackupPath(backupPaths[0]);
                return;
            }

            var firstDirectory = Directory.Exists(backupPaths[0])
                ? backupPaths[0]
                : Path.GetDirectoryName(backupPaths[0]) ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(firstDirectory) && Directory.Exists(firstDirectory))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + firstDirectory + "\"")
                {
                    UseShellExecute = true
                });
                return;
            }

            OpenBackupPath(backupPaths[0]);
        }

        private void RestoreBackupArtifact(JObject payload)
        {
            var backupPath = ParseBackupPaths(payload).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)) ?? string.Empty;
            if (backupPath.Length == 0 || !File.Exists(backupPath))
            {
                MessageBox.Show(this, "备份文件不存在，无法还原。", "撤销本次修改", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var host = AgentServices == null
                ? null
                : (AgentServices.GetService(typeof(IDocumentRestoreHostAdapter)) as IDocumentRestoreHostAdapter)
                    ?? (AgentServices.GetService(typeof(ICopilotHostAdapter)) as IDocumentRestoreHostAdapter);
            if (host == null)
            {
                MessageBox.Show(this, "当前宿主不支持从备份还原。", "撤销本次修改", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirmed = MessageBox.Show(
                this,
                "将从下面这个备份点生成一个还原副本：\r\n\r\n" + backupPath
                    + "\r\n\r\n还原副本会保存在原文档旁边（文件名带 _还原_ 后缀；未保存过的文档则放在备份目录），原文件不会被覆盖。是否继续？",
                "撤销本次修改",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            if (confirmed != DialogResult.OK)
            {
                return;
            }

            try
            {
                var result = host.RestoreBackup(new DocumentRestoreRequest
                {
                    BackupPath = backupPath,
                    OpenAfterRestore = true
                });

                MessageBox.Show(
                    this,
                    (result.Message ?? string.Empty) + "\r\n\r\n还原副本：" + (result.RestoredPath ?? string.Empty),
                    "撤销本次修改",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "撤销本次修改", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static List<string> ParseBackupPaths(JObject payload)
        {
            var results = new List<string>();
            if (payload == null)
            {
                return results;
            }

            var singlePath = payload.Value<string>("backupPath") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(singlePath))
            {
                results.Add(singlePath.Trim());
            }

            var array = payload["backupPaths"] as JArray;
            if (array == null)
            {
                return results;
            }

            foreach (var item in array)
            {
                var path = item == null ? string.Empty : item.ToString();
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                path = path.Trim();

                if (results.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                results.Add(path);
            }

            return results;
        }

        private void OpenBackupPath(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
                    {
                        UseShellExecute = true
                    });
                    return;
                }

                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
                    {
                        UseShellExecute = true
                    });
                    return;
                }

                MessageBox.Show(this, "The backup file does not exist.", "View Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to open the backup: " + ex.Message, "View Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private sealed class PendingToolConfirmationRequest
        {
            public PendingToolConfirmationRequest()
            {
                Decision = "deny";
                WaitHandle = new ManualResetEventSlim(false);
            }

            public string Decision { get; set; }

            public ManualResetEventSlim WaitHandle { get; private set; }
        }
    }
}
