using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Panel.WebView.Models;
using OfficeCopilot.Panel.WebView.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Panel
{
    [TestClass]
    public class LegacyChatSessionHistoryTests
    {
        private string _directory;
        private string _filePath;

        [TestInitialize]
        public void Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "occ-legacy-sessions-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _filePath = Path.Combine(_directory, "sessions.json");
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [TestMethod]
        public async Task LoadRecentAsync_DoesNotShowLegacySessionsWhenBrokerIsUnavailable()
        {
            var legacy = CreateSession(Guid.NewGuid(), "Earlier chat", "legacy message");
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(new[] { legacy }));

            var repository = new BrokerSessionRepository(() => null, null);
            var loaded = await repository.LoadRecentAsync("Excel", 200, CancellationToken.None);

            Assert.IsNull(loaded);
        }

        [TestMethod]
        public void Merge_DeduplicatesBrokerSessionsAndKeepsLegacyMessages()
        {
            var id = Guid.NewGuid();
            var brokerSession = CreateSession(id, ChatSessionFactory.DefaultNewSessionTitle, null);
            brokerSession.Messages.Clear();
            var legacySession = CreateSession(id, "Original title", "original message");
            var anotherLegacySession = CreateSession(Guid.NewGuid(), "Older chat", "older message");

            var merged = LegacyChatSessionHistory.Merge(
                new[] { brokerSession },
                new[] { legacySession, anotherLegacySession });

            Assert.AreEqual(2, merged.Count);
            var mergedBrokerSession = merged.Single(session => session.Id == id);
            Assert.AreEqual("Original title", mergedBrokerSession.Title);
            Assert.AreEqual("original message", mergedBrokerSession.Messages.Single().Content);
        }

        [TestMethod]
        public void Remove_DeletesOnlySelectedLegacyEntryAndPreservesOtherFields()
        {
            var removedId = Guid.NewGuid();
            var retainedId = Guid.NewGuid();
            File.WriteAllText(
                _filePath,
                "[{\"Id\":\"" + removedId.ToString("D") + "\",\"Title\":\"remove\"},"
                + "{\"Id\":\"" + retainedId.ToString("D") + "\",\"Title\":\"keep\",\"LegacyField\":\"preserve\"}]");

            var removed = LegacyChatSessionHistory.Remove(_filePath, removedId, null);

            Assert.IsTrue(removed);
            var remaining = JArray.Parse(File.ReadAllText(_filePath));
            Assert.AreEqual(1, remaining.Count);
            Assert.AreEqual(retainedId.ToString("D"), remaining[0]["Id"].ToString());
            Assert.AreEqual("preserve", remaining[0]["LegacyField"].ToString());
        }

        private static ChatSessionState CreateSession(Guid id, string title, string message)
        {
            return new ChatSessionState
            {
                Id = id,
                Title = title,
                CreatedAt = DateTime.Now.AddMinutes(-2),
                UpdatedAt = DateTime.Now.AddMinutes(-1),
                Messages = string.IsNullOrEmpty(message)
                    ? new List<ChatMessageState>()
                    : new List<ChatMessageState>
                    {
                        new ChatMessageState
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Role = "user",
                            Content = message,
                            CreatedAt = DateTime.Now.AddMinutes(-2)
                        }
                    }
            };
        }
    }
}
