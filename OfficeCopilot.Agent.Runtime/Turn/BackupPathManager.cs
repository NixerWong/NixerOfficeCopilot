using System;
using System.Collections.Generic;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 备份路径管理器：处理工具调用的备份路径协商与通知替换。
    /// 从 <see cref="BrokerTurnExecutor"/> 迁出（阶段 7B），原 5 个 private static 方法。
    /// </summary>
    internal static class BackupPathManager
    {
        public const string MetadataKey = "backup-path";

        /// <summary>判断工具调用是否需要备份。</summary>
        public static bool ToolRequiresBackup(
            PlannedToolCall call,
            IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog)
        {
            if (call == null || toolCatalog == null || string.IsNullOrWhiteSpace(call.ToolName))
            {
                return false;
            }

            HostBridgeToolDescriptor definition;
            return toolCatalog.TryGetValue(call.ToolName, out definition)
                && definition != null
                && definition.RequiresBackup;
        }

        /// <summary>从请求的 Metadata 中读取当前备份路径。</summary>
        public static string GetRequestBackupPath(AgentBrokerTurnRequest request)
        {
            if (request == null || request.Metadata == null)
            {
                return string.Empty;
            }

            string backupPath;
            return request.Metadata.TryGetValue(MetadataKey, out backupPath)
                && !string.IsNullOrWhiteSpace(backupPath)
                ? backupPath.Trim()
                : string.Empty;
        }

        /// <summary>将备份路径写入请求的 Metadata（仅在尚未设置时）。</summary>
        public static void SetRequestBackupPath(AgentBrokerTurnRequest request, string backupPath)
        {
            if (request == null || string.IsNullOrWhiteSpace(backupPath))
            {
                return;
            }

            if (request.Metadata == null)
            {
                request.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            string existingBackupPath;
            if (request.Metadata.TryGetValue(MetadataKey, out existingBackupPath)
                && !string.IsNullOrWhiteSpace(existingBackupPath))
            {
                return;
            }

            request.Metadata[MetadataKey] = backupPath.Trim();
        }

        /// <summary>替换内容中的备份路径通知（source → target）。</summary>
        public static string ReplaceBackupNoticePath(string content, string sourceBackupPath, string targetBackupPath)
        {
            var value = content ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)
                || string.IsNullOrWhiteSpace(sourceBackupPath)
                || string.IsNullOrWhiteSpace(targetBackupPath))
            {
                return value;
            }

            var sourceNotice = "Backup saved to: " + sourceBackupPath.Trim();
            var targetNotice = "Backup saved to: " + targetBackupPath.Trim();

            return value.IndexOf(sourceNotice, StringComparison.OrdinalIgnoreCase) >= 0
                ? value.Replace(sourceNotice, targetNotice)
                : value;
        }

        /// <summary>从工具执行结果中提取备份路径。</summary>
        public static string GetResultBackupPath(HostToolInvocationResult result)
        {
            if (result == null || result.Metadata == null)
            {
                return string.Empty;
            }

            string backupPath;
            return result.Metadata.TryGetValue(MetadataKey, out backupPath)
                && !string.IsNullOrWhiteSpace(backupPath)
                ? backupPath.Trim()
                : string.Empty;
        }
    }
}
