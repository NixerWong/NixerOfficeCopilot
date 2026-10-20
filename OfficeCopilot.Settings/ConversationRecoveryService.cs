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
    public sealed class ConversationRecoveryService
    {
        private readonly IConversationStore _conversationStore;

        public ConversationRecoveryService(IConversationStore conversationStore)
        {
            _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        }

        public ConversationSession LoadLatestForDocument(string hostApplication, string documentPath, string documentName)
        {
            var documentKey = DocumentKeyBuilder.Build(hostApplication, documentPath, documentName);
            return _conversationStore.LoadLatest(hostApplication, documentKey);
        }

        public void SaveForDocument(string hostApplication, string documentPath, string documentName, ConversationSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            session.HostApplication = hostApplication;
            session.DocumentName = documentName;
            session.DocumentKey = DocumentKeyBuilder.Build(hostApplication, documentPath, documentName);

            _conversationStore.Save(session);
        }
    }
}
