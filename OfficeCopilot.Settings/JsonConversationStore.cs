using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Settings
{
    public sealed class JsonConversationStore : IConversationStore
    {
        private readonly string _conversationFolder;
        private readonly string _indexFilePath;

        public JsonConversationStore()
            : this(OfficeCopilotStoragePaths.GetConversationsFolder(), OfficeCopilotStoragePaths.GetConversationIndexFilePath())
        {
        }

        public JsonConversationStore(string conversationFolder, string indexFilePath)
        {
            _conversationFolder = conversationFolder;
            _indexFilePath = indexFilePath;
        }

        public ConversationSession LoadBySessionId(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var filePath = Path.Combine(_conversationFolder, sessionId + ".json");
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var session = OfficeCopilotJson.Deserialize<ConversationSession>(json);
            NormalizeConversation(session);
            return session;
        }

        public ConversationSession LoadLatest(string hostApplication, string documentKey)
        {
            var item = LoadIndex()
                .Where(i => string.Equals(i.HostApplication, hostApplication, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(i.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.UpdatedAtUtc)
                .FirstOrDefault();

            return item == null ? null : LoadBySessionId(item.SessionId);
        }

        public IReadOnlyList<ConversationIndexItem> ListByDocument(string hostApplication, string documentKey)
        {
            return LoadIndex()
                .Where(i => string.Equals(i.HostApplication, hostApplication, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(i.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.UpdatedAtUtc)
                .ToList();
        }

        public void Save(ConversationSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            NormalizeConversation(session);

            if (string.IsNullOrWhiteSpace(session.SessionId))
            {
                session.SessionId = Guid.NewGuid().ToString("N");
            }

            if (session.CreatedAtUtc == default(DateTime))
            {
                session.CreatedAtUtc = DateTime.UtcNow;
            }

            session.UpdatedAtUtc = DateTime.UtcNow;

            EnsureDirectory(_conversationFolder);

            var filePath = Path.Combine(_conversationFolder, session.SessionId + ".json");
            File.WriteAllText(filePath, OfficeCopilotJson.Serialize(session), System.Text.Encoding.UTF8);

            var index = LoadIndex();
            var existing = index.FirstOrDefault(i => string.Equals(i.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase));
            var replacement = BuildIndexItem(session);

            if (existing == null)
            {
                index.Add(replacement);
            }
            else
            {
                existing.HostApplication = replacement.HostApplication;
                existing.DocumentKey = replacement.DocumentKey;
                existing.DocumentName = replacement.DocumentName;
                existing.Title = replacement.Title;
                existing.LastMessagePreview = replacement.LastMessagePreview;
                existing.CreatedAtUtc = replacement.CreatedAtUtc;
                existing.UpdatedAtUtc = replacement.UpdatedAtUtc;
                existing.IsPinned = replacement.IsPinned;
                existing.IsArchived = replacement.IsArchived;
            }

            SaveIndex(index);
        }

        public void Delete(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var filePath = Path.Combine(_conversationFolder, sessionId + ".json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            var index = LoadIndex();
            index.RemoveAll(i => string.Equals(i.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            SaveIndex(index);
        }

        private List<ConversationIndexItem> LoadIndex()
        {
            if (!File.Exists(_indexFilePath))
            {
                return new List<ConversationIndexItem>();
            }

            var json = File.ReadAllText(_indexFilePath, System.Text.Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<ConversationIndexItem>();
            }

            var items = OfficeCopilotJson.Deserialize<List<ConversationIndexItem>>(json);
            return items ?? new List<ConversationIndexItem>();
        }

        private void SaveIndex(List<ConversationIndexItem> items)
        {
            EnsureDirectory(Path.GetDirectoryName(_indexFilePath));

            var orderedItems = items
                .OrderByDescending(i => i.UpdatedAtUtc)
                .ToList();

            File.WriteAllText(_indexFilePath, OfficeCopilotJson.Serialize(orderedItems), System.Text.Encoding.UTF8);
        }

        private static ConversationIndexItem BuildIndexItem(ConversationSession session)
        {
            return new ConversationIndexItem
            {
                SessionId = session.SessionId,
                HostApplication = session.HostApplication,
                DocumentKey = session.DocumentKey,
                DocumentName = session.DocumentName,
                Title = string.IsNullOrWhiteSpace(session.Title) ? BuildDefaultTitle(session) : session.Title,
                LastMessagePreview = BuildLastMessagePreview(session),
                CreatedAtUtc = session.CreatedAtUtc,
                UpdatedAtUtc = session.UpdatedAtUtc,
                IsPinned = session.IsPinned,
                IsArchived = session.IsArchived
            };
        }

        private static string BuildDefaultTitle(ConversationSession session)
        {
            var firstUserMessage = session.Messages
                .FirstOrDefault(m => m != null && m.Role == ConversationMessageRole.User && !string.IsNullOrWhiteSpace(m.Content));

            if (firstUserMessage == null)
            {
                return string.IsNullOrWhiteSpace(session.DocumentName) ? "New Session" : session.DocumentName;
            }

            return Truncate(firstUserMessage.Content, 30);
        }

        private static string BuildLastMessagePreview(ConversationSession session)
        {
            var message = session.Messages
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Content))
                .OrderByDescending(m => m.CreatedAtUtc)
                .FirstOrDefault();

            return message == null ? string.Empty : Truncate(message.Content, 80);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "...";
        }

        private static void NormalizeConversation(ConversationSession session)
        {
            if (session == null)
            {
                return;
            }

            if (session.Messages == null)
            {
                session.Messages = new List<ConversationMessage>();
            }

            foreach (var message in session.Messages)
            {
                if (message == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(message.MessageId))
                {
                    message.MessageId = Guid.NewGuid().ToString("N");
                }

                if (message.CreatedAtUtc == default(DateTime))
                {
                    message.CreatedAtUtc = DateTime.UtcNow;
                }
            }
        }

        private static void EnsureDirectory(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
        }
    }
}
