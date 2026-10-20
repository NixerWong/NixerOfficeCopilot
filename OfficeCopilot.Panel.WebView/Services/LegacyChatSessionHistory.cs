using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Panel.WebView.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OfficeCopilot.Panel.WebView.Services
{
    internal static class LegacyChatSessionHistory
    {
        private const string LegacyFolderName = "NixerOfficeCopilot";
        private const string LegacySessionsFolderName = "ChatSessions";
        private const string LegacySessionsFileName = "sessions.json";

        public static string DefaultFilePath
        {
            get
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return string.IsNullOrWhiteSpace(localAppData)
                    ? string.Empty
                    : Path.Combine(localAppData, LegacyFolderName, LegacySessionsFolderName, LegacySessionsFileName);
            }
        }

        internal static List<ChatSessionState> Load(string filePath, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new List<ChatSessionState>();
            }

            try
            {
                var sessions = JsonConvert.DeserializeObject<List<ChatSessionState>>(File.ReadAllText(filePath));
                if (sessions == null)
                {
                    return new List<ChatSessionState>();
                }

                var validSessions = new List<ChatSessionState>();
                var skippedCount = 0;
                foreach (var session in sessions)
                {
                    if (session == null || session.Id == Guid.Empty)
                    {
                        skippedCount++;
                        continue;
                    }

                    session.Messages = session.Messages ?? new List<ChatMessageState>();
                    validSessions.Add(session);
                }

                if (skippedCount > 0)
                {
                    logger?.LogWarning(
                        "旧版会话文件中有 {SkippedCount} 条无效记录，已跳过。FilePath={FilePath}",
                        skippedCount,
                        filePath);
                }

                return validSessions;
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "读取旧版会话文件失败；原文件未修改。FilePath={FilePath}", filePath);
            }
            catch (IOException ex)
            {
                logger?.LogWarning(ex, "读取旧版会话文件失败；原文件未修改。FilePath={FilePath}", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger?.LogWarning(ex, "读取旧版会话文件失败；原文件未修改。FilePath={FilePath}", filePath);
            }

            return new List<ChatSessionState>();
        }

        internal static List<ChatSessionState> Merge(
            IEnumerable<ChatSessionState> brokerSessions,
            IEnumerable<ChatSessionState> legacySessions)
        {
            var merged = new List<ChatSessionState>();
            var sessionsById = new Dictionary<Guid, ChatSessionState>();

            AddSessions(merged, sessionsById, brokerSessions, mergeLegacyDetails: false);
            AddSessions(merged, sessionsById, legacySessions, mergeLegacyDetails: true);

            return merged
                .OrderByDescending(session => session.UpdatedAt)
                .ToList();
        }

        internal static bool Remove(string filePath, Guid sessionId, ILogger logger)
        {
            if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            string temporaryPath = null;
            try
            {
                var document = JArray.Parse(File.ReadAllText(filePath));
                var removedCount = 0;

                foreach (var item in document.ToList())
                {
                    var record = item as JObject;
                    var idToken = record == null
                        ? null
                        : record.GetValue("Id", StringComparison.OrdinalIgnoreCase);

                    Guid recordId;
                    if (idToken != null && Guid.TryParse(idToken.ToString(), out recordId) && recordId == sessionId)
                    {
                        item.Remove();
                        removedCount++;
                    }
                }

                if (removedCount == 0)
                {
                    return false;
                }

                temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryPath, document.ToString(Formatting.Indented), new UTF8Encoding(false));
                File.Replace(temporaryPath, filePath, null);
                temporaryPath = null;
                return true;
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "删除旧版会话失败；原文件未修改。FilePath={FilePath}", filePath);
            }
            catch (IOException ex)
            {
                logger?.LogWarning(ex, "删除旧版会话失败；原文件未修改。FilePath={FilePath}", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger?.LogWarning(ex, "删除旧版会话失败；原文件未修改。FilePath={FilePath}", filePath);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException ex)
                    {
                        logger?.LogWarning(ex, "清理旧版会话临时文件失败。FilePath={FilePath}", temporaryPath);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        logger?.LogWarning(ex, "清理旧版会话临时文件失败。FilePath={FilePath}", temporaryPath);
                    }
                }
            }

            return false;
        }

        private static void AddSessions(
            IList<ChatSessionState> merged,
            IDictionary<Guid, ChatSessionState> sessionsById,
            IEnumerable<ChatSessionState> sessions,
            bool mergeLegacyDetails)
        {
            if (sessions == null)
            {
                return;
            }

            foreach (var session in sessions)
            {
                if (session == null || session.Id == Guid.Empty)
                {
                    continue;
                }

                ChatSessionState existing;
                if (sessionsById.TryGetValue(session.Id, out existing))
                {
                    if (mergeLegacyDetails)
                    {
                        MergeLegacyDetails(existing, session);
                    }

                    continue;
                }

                session.Messages = session.Messages ?? new List<ChatMessageState>();
                sessionsById.Add(session.Id, session);
                merged.Add(session);
            }
        }

        private static void MergeLegacyDetails(ChatSessionState target, ChatSessionState legacy)
        {
            if (target.Messages == null || target.Messages.Count == 0)
            {
                target.Messages = legacy.Messages == null
                    ? new List<ChatMessageState>()
                    : legacy.Messages;
            }

            if (target.ModelProviderId == null)
            {
                target.ModelProviderId = legacy.ModelProviderId;
            }

            if (string.IsNullOrWhiteSpace(target.Title)
                || string.Equals(target.Title, ChatSessionFactory.DefaultNewSessionTitle, StringComparison.Ordinal))
            {
                target.Title = legacy.Title;
            }

            if (legacy.CreatedAt != default(DateTime)
                && (target.CreatedAt == default(DateTime) || legacy.CreatedAt < target.CreatedAt))
            {
                target.CreatedAt = legacy.CreatedAt;
            }

            if (legacy.UpdatedAt > target.UpdatedAt)
            {
                target.UpdatedAt = legacy.UpdatedAt;
            }
        }
    }
}
