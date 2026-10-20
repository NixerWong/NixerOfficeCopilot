using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;

namespace OfficeCopilot.Agent.Tests.Persistence
{
    [TestClass]
    public class InMemoryConversationStoreTests
    {
        [TestMethod]
        public async Task GetOrCreate_IsIdempotentForSameId()
        {
            var store = new InMemoryConversationStore();

            var first = await store.GetOrCreateAsync(" conversation-1 ", CancellationToken.None);
            var second = await store.GetOrCreateAsync("conversation-1", CancellationToken.None);

            Assert.AreSame(first, second);
            Assert.AreEqual("conversation-1", first.ConversationId);
        }

        [TestMethod]
        public async Task AppendAndList_ProducesTitleAndMessageCount()
        {
            var store = new InMemoryConversationStore();
            await store.AppendMessageAsync("conversation-1", new BrokerConversationMessage
            {
                Role = "user",
                Content = "Create a report"
            }, CancellationToken.None);

            var summaries = await store.ListAsync(10, CancellationToken.None);

            Assert.AreEqual(1, summaries.Count);
            Assert.AreEqual("Create a report", summaries[0].Title);
            Assert.AreEqual(1, summaries[0].MessageCount);
        }

        [TestMethod]
        public async Task SaveAndDelete_UpdatesStoredConversation()
        {
            var store = new InMemoryConversationStore();
            var conversation = await store.GetOrCreateAsync("conversation-1", CancellationToken.None);
            conversation.Metadata["title"] = "Explicit title";
            await store.SaveAsync(conversation, CancellationToken.None);

            var summaries = await store.ListAsync(1, CancellationToken.None);
            var deleted = await store.DeleteAsync("conversation-1", CancellationToken.None);
            var missing = await store.DeleteAsync("conversation-1", CancellationToken.None);

            Assert.AreEqual("Explicit title", summaries[0].Title);
            Assert.IsTrue(deleted);
            Assert.IsFalse(missing);
        }

        [TestMethod]
        public async Task Cancellation_IsHonored()
        {
            var store = new InMemoryConversationStore();
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                    () => store.GetOrCreateAsync("conversation-1", source.Token));
            }
        }
    }
}
