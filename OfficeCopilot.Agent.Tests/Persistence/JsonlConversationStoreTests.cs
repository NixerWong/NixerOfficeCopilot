using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Persistence.Jsonl;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.Agent.Runtime.Planning;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.Agent.Tests.Harness;
using OfficeCopilot.HostBridge.Client;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.Persistence
{
    [TestClass]
    public class JsonlConversationStoreTests
    {
        private string _root;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "occ-jsonl-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 临时目录清理失败不应让用例变红。
            }
        }

        // useIndex:false —— 索引是性能优化，单测只验证 JSONL 文件本身的读写契约。
        private JsonlConversationStore CreateStore()
        {
            return new JsonlConversationStore(_root, useIndex: false);
        }

        private string SessionFilePath(string conversationId)
        {
            return Directory
                .EnumerateFiles(Path.Combine(_root, "sessions"), conversationId + ".jsonl", SearchOption.AllDirectories)
                .Single();
        }

        [TestMethod]
        public async Task GetOrCreate_CreatesHeadRecordAndIsIdempotent()
        {
            var store = CreateStore();

            var first = await store.GetOrCreateAsync("conv-1", CancellationToken.None);
            var second = await store.GetOrCreateAsync(" conv-1 ", CancellationToken.None);

            Assert.AreEqual("conv-1", first.ConversationId);
            Assert.AreEqual("conv-1", second.ConversationId);
            Assert.AreEqual(0, second.Messages.Count);

            var lines = File.ReadAllLines(SessionFilePath("conv-1"));
            Assert.AreEqual(1, lines.Length);
            StringAssert.Contains(lines[0], "\"head\"");
        }

        [TestMethod]
        public async Task GetOrCreate_GeneratesIdWhenBlank()
        {
            var store = CreateStore();

            var conversation = await store.GetOrCreateAsync("  ", CancellationToken.None);

            Assert.IsFalse(string.IsNullOrWhiteSpace(conversation.ConversationId));
        }

        [TestMethod]
        public async Task AppendMessage_RoundTripsThroughDisk()
        {
            var store = CreateStore();
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage
            {
                MessageId = "m1",
                Role = "user",
                Content = "生成一份销售报表"
            }, CancellationToken.None);
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage
            {
                MessageId = "m2",
                Role = "assistant",
                Content = "好的"
            }, CancellationToken.None);

            var reloaded = await CreateStore().GetOrCreateAsync("conv-1", CancellationToken.None);

            Assert.AreEqual(2, reloaded.Messages.Count);
            Assert.AreEqual("m1", reloaded.Messages[0].MessageId);
            Assert.AreEqual("生成一份销售报表", reloaded.Messages[0].Content);
            Assert.AreEqual("assistant", reloaded.Messages[1].Role);
        }

        [TestMethod]
        public async Task AppendMessage_PreservesToolCallFields()
        {
            var store = CreateStore();
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage
            {
                Role = "tool_result",
                MessageType = "tool_result",
                ToolCallId = "call-7",
                Name = "excel.read_range",
                Content = "A1:B2",
                StructuredContentJson = "{\"rows\":2}"
            }, CancellationToken.None);

            var message = (await CreateStore().GetOrCreateAsync("conv-1", CancellationToken.None)).Messages.Single();

            Assert.AreEqual("call-7", message.ToolCallId);
            Assert.AreEqual("excel.read_range", message.Name);
            Assert.AreEqual("tool_result", message.MessageType);
            Assert.AreEqual("{\"rows\":2}", message.StructuredContentJson);
        }

        [TestMethod]
        public async Task AppendMessage_IgnoresNullMessage()
        {
            var store = CreateStore();
            await store.GetOrCreateAsync("conv-1", CancellationToken.None);

            await store.AppendMessageAsync("conv-1", null, CancellationToken.None);

            Assert.AreEqual(0, (await CreateStore().GetOrCreateAsync("conv-1", CancellationToken.None)).Messages.Count);
        }

        [TestMethod]
        public async Task AppendMessage_WritesOneLinePerMessage()
        {
            var store = CreateStore();
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage
            {
                Role = "user",
                Content = "line one\nline two"
            }, CancellationToken.None);

            var lines = File.ReadAllLines(SessionFilePath("conv-1"));

            Assert.AreEqual(2, lines.Length, "head + 1 msg；内嵌换行必须被 JSON 转义，不能破坏行分隔");
        }

        [TestMethod]
        public async Task Save_RewritesFileAtomicallyAndDropsNullMessages()
        {
            var store = CreateStore();
            var conversation = await store.GetOrCreateAsync("conv-1", CancellationToken.None);
            conversation.Messages.Add(new BrokerConversationMessage { Role = "user", Content = "a" });
            conversation.Messages.Add(null);
            conversation.Messages.Add(new BrokerConversationMessage { Role = "assistant", Content = "b" });
            conversation.Metadata["hostApplicationName"] = "Excel";

            await store.SaveAsync(conversation, CancellationToken.None);

            var path = SessionFilePath("conv-1");
            Assert.IsFalse(File.Exists(path + ".tmp"), "临时文件应已改名删除");

            var reloaded = await CreateStore().GetOrCreateAsync("conv-1", CancellationToken.None);
            Assert.AreEqual(2, reloaded.Messages.Count);
            Assert.AreEqual("Excel", reloaded.Metadata["hostApplicationName"]);
        }

        [TestMethod]
        public async Task ExecuteTurn_CancellationPersistsUserAndCancellationMessages()
        {
            var store = CreateStore();
            using (var cancellation = new CancellationTokenSource())
            {
                var contextReader = new CancellingHostContextReader(cancellation);
                var options = new AgentRuntimeOptions();
                var modelClient = new NullModelClient();
                var executor = new BrokerTurnExecutor(
                    options,
                    new BrokerEventDispatcher(),
                    contextReader,
                    new NullHostBridgeClient(),
                    new FakeHostToolInvoker(),
                    modelClient);
                var runtime = new AgentRuntimeService(options, contextReader, store, modelClient, executor, null);
                var request = new AgentBrokerTurnRequest
                {
                    ConversationId = "cancelled-conversation",
                    MessageId = "cancelled-message",
                    UserInput = "先保存这条用户输入"
                };

                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                    runtime.ExecuteTurnAsync(request, null, cancellation.Token));
            }

            var persisted = await CreateStore().GetOrCreateAsync("cancelled-conversation", CancellationToken.None);
            Assert.AreEqual(3, persisted.Messages.Count);
            Assert.AreEqual("先保存这条用户输入", persisted.Messages[0].Content);
            Assert.IsFalse(persisted.Messages[1].Success);
            StringAssert.Contains(persisted.Messages[1].Content, "本回合已中止");
            Assert.AreEqual("turn_interrupted", persisted.Messages[2].MessageType);

            var modelMessages = DefaultContextBuilder.BuildModelMessages(
                new AgentBrokerTurnRequest { MessageId = "follow-up", UserInput = "继续" },
                persisted,
                new HostContextSnapshotDto(),
                new RuntimeSkillPlan());
            Assert.IsTrue(modelMessages.Any(message => message.Role == "system" && message.Content.Contains("turn_interrupted:")));
        }

        [TestMethod]
        public async Task Save_IgnoresNullOrBlankConversation()
        {
            var store = CreateStore();

            await store.SaveAsync(null, CancellationToken.None);
            await store.SaveAsync(new BrokerConversationState { ConversationId = "  " }, CancellationToken.None);

            Assert.IsFalse(Directory.Exists(Path.Combine(_root, "sessions"))
                && Directory.EnumerateFiles(Path.Combine(_root, "sessions"), "*.jsonl", SearchOption.AllDirectories).Any());
        }

        // ---- 损坏行容错：单行坏掉不能让整个会话读不出来 ----

        [TestMethod]
        public async Task Load_SkipsCorruptLinesAndKeepsValidMessages()
        {
            var store = CreateStore();
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "good-1" }, CancellationToken.None);
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "assistant", Content = "good-2" }, CancellationToken.None);

            var path = SessionFilePath("conv-1");
            var lines = File.ReadAllLines(path).ToList();
            lines.Insert(2, "{ this is not valid json");
            lines.Add("]]]truncated");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));

            var reloaded = await CreateStore().GetOrCreateAsync("conv-1", CancellationToken.None);

            Assert.AreEqual(2, reloaded.Messages.Count);
            Assert.AreEqual("good-1", reloaded.Messages[0].Content);
            Assert.AreEqual("good-2", reloaded.Messages[1].Content);
        }

        [TestMethod]
        public async Task Load_SkipsBlankLines()
        {
            var store = CreateStore();
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "good" }, CancellationToken.None);

            var path = SessionFilePath("conv-1");
            var lines = File.ReadAllLines(path).ToList();
            lines.Insert(1, string.Empty);
            lines.Insert(2, "   ");
            File.WriteAllLines(path, lines, new UTF8Encoding(false));

            Assert.AreEqual(1, (await CreateStore().GetOrCreateAsync("conv-1", CancellationToken.None)).Messages.Count);
        }

        [TestMethod]
        public async Task Load_SurvivesMissingHeadRecord()
        {
            var store = CreateStore();
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "good" }, CancellationToken.None);

            var path = SessionFilePath("conv-1");
            File.WriteAllLines(path, File.ReadAllLines(path).Skip(1), new UTF8Encoding(false));

            var reloaded = await CreateStore().GetOrCreateAsync("conv-1", CancellationToken.None);

            Assert.AreEqual("conv-1", reloaded.ConversationId);
            Assert.AreEqual(1, reloaded.Messages.Count);
        }

        [TestMethod]
        public async Task Load_ReturnsEmptyConversationForEmptyFile()
        {
            var store = CreateStore();
            await store.GetOrCreateAsync("conv-1", CancellationToken.None);
            File.WriteAllText(SessionFilePath("conv-1"), string.Empty, new UTF8Encoding(false));

            var reloaded = await CreateStore().GetOrCreateAsync("conv-1", CancellationToken.None);

            Assert.AreEqual("conv-1", reloaded.ConversationId);
            Assert.AreEqual(0, reloaded.Messages.Count);
        }

        // ---- List / Delete ----

        [TestMethod]
        public async Task List_ReturnsEmptyWhenNothingPersisted()
        {
            var summaries = await CreateStore().ListAsync(10, CancellationToken.None);

            Assert.AreEqual(0, summaries.Count);
        }

        [TestMethod]
        public async Task List_ProducesTitleFromFirstUserMessage()
        {
            var store = CreateStore();
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage
            {
                Role = "user",
                Content = "统计各区域销售额"
            }, CancellationToken.None);

            var summaries = await CreateStore().ListAsync(10, CancellationToken.None);

            Assert.AreEqual(1, summaries.Count);
            Assert.AreEqual("conv-1", summaries[0].ConversationId);
            StringAssert.Contains(summaries[0].Title, "统计各区域销售额");
            Assert.AreEqual(1, summaries[0].MessageCount);
        }

        [TestMethod]
        public async Task List_HonoursTakeLimit()
        {
            var store = CreateStore();
            for (var i = 0; i < 4; i++)
            {
                await store.AppendMessageAsync(
                    "conv-" + i,
                    new BrokerConversationMessage { Role = "user", Content = "q" + i },
                    CancellationToken.None);
            }

            var summaries = await CreateStore().ListAsync(2, CancellationToken.None);

            Assert.AreEqual(2, summaries.Count);
        }

        [TestMethod]
        public async Task Delete_RemovesFileAndReportsWhetherItExisted()
        {
            var store = CreateStore();
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "q" }, CancellationToken.None);

            Assert.IsTrue(await store.DeleteAsync("conv-1", CancellationToken.None));
            Assert.IsFalse(await store.DeleteAsync("conv-1", CancellationToken.None));
            Assert.IsFalse(await store.DeleteAsync("never-existed", CancellationToken.None));
        }

        [TestMethod]
        public async Task Delete_LeavesOtherConversationsIntact()
        {
            var store = CreateStore();
            await store.AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "one" }, CancellationToken.None);
            await store.AppendMessageAsync("conv-2", new BrokerConversationMessage { Role = "user", Content = "two" }, CancellationToken.None);

            await store.DeleteAsync("conv-1", CancellationToken.None);

            var reloaded = await CreateStore().GetOrCreateAsync("conv-2", CancellationToken.None);
            Assert.AreEqual("two", reloaded.Messages.Single().Content);
        }

        // ---- 宿主作用域隔离 ----

        private JsonlConversationStore CreateScopedStore(string hostScope)
        {
            return new JsonlConversationStore(_root, hostScope);
        }

        [TestMethod]
        public async Task ScopedStores_IsolateConversationsWithTheSameId()
        {
            await CreateScopedStore("Word").AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "word-q" }, CancellationToken.None);
            await CreateScopedStore("Excel").AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "excel-q" }, CancellationToken.None);

            var word = await CreateScopedStore("Word").GetOrCreateAsync("conv-1", CancellationToken.None);
            var excel = await CreateScopedStore("Excel").GetOrCreateAsync("conv-1", CancellationToken.None);

            Assert.AreEqual("word-q", word.Messages.Single().Content);
            Assert.AreEqual("excel-q", excel.Messages.Single().Content);
        }

        [TestMethod]
        public async Task ScopedList_TakeOneReturnsOnlyThatHostsConversation()
        {
            await CreateScopedStore("Word").AppendMessageAsync("conv-word", new BrokerConversationMessage { Role = "user", Content = "w" }, CancellationToken.None);
            await CreateScopedStore("Excel").AppendMessageAsync("conv-excel", new BrokerConversationMessage { Role = "user", Content = "e" }, CancellationToken.None);

            var summaries = await CreateScopedStore("Word").ListAsync(1, CancellationToken.None);

            Assert.AreEqual(1, summaries.Count);
            Assert.AreEqual("conv-word", summaries[0].ConversationId);
        }

        [TestMethod]
        public async Task ScopedDelete_DoesNotRemoveSameIdInAnotherScope()
        {
            await CreateScopedStore("Word").AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "word-q" }, CancellationToken.None);
            await CreateScopedStore("Excel").AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "excel-q" }, CancellationToken.None);

            Assert.IsTrue(await CreateScopedStore("Word").DeleteAsync("conv-1", CancellationToken.None));

            var excel = await CreateScopedStore("Excel").GetOrCreateAsync("conv-1", CancellationToken.None);
            Assert.AreEqual("excel-q", excel.Messages.Single().Content);
        }

        [TestMethod]
        public async Task ScopedStore_WritesOnlyUnderItsScopeDirectory()
        {
            await CreateScopedStore("PowerPoint").AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "q" }, CancellationToken.None);

            var scopedFiles = Directory.EnumerateFiles(
                Path.Combine(_root, "PowerPoint", "sessions"), "conv-1.jsonl", SearchOption.AllDirectories);
            Assert.AreEqual(1, scopedFiles.Count());
            Assert.IsFalse(Directory.Exists(Path.Combine(_root, "sessions")));
        }

        [TestMethod]
        public void ScopedStore_RejectsInvalidScope()
        {
            Assert.ThrowsException<ArgumentException>(() => new JsonlConversationStore(_root, "..\\evil"));
        }

        [TestMethod]
        public void ConversationScope_ResolvesHostNamesAndFallsBackToUnscoped()
        {
            Assert.AreEqual("Word", ConversationScope.ResolveHostScope(" Word "));
            Assert.AreEqual(ConversationScope.Unscoped, ConversationScope.ResolveHostScope(null));
            Assert.AreEqual(ConversationScope.Unscoped, ConversationScope.ResolveHostScope("   "));
            Assert.AreEqual(ConversationScope.Unscoped, ConversationScope.ResolveHostScope("..\\Word"));
        }

        // ---- 索引对账 ----

        [TestMethod]
        public void IsIndexableConversationFile_ExcludesCompactSnapshotsAndIndexFile()
        {
            Assert.IsTrue(SqliteConversationIndex.IsIndexableConversationFile(@"C:\s\2024\01\conv-1.jsonl"));
            Assert.IsFalse(SqliteConversationIndex.IsIndexableConversationFile(@"C:\s\2024\01\conv-1.compact.jsonl"));
            Assert.IsFalse(SqliteConversationIndex.IsIndexableConversationFile(@"C:\s\2024\01\CONV-1.COMPACT.JSONL"));
            Assert.IsFalse(SqliteConversationIndex.IsIndexableConversationFile(@"C:\s\index.sqlite"));
            Assert.IsFalse(SqliteConversationIndex.IsIndexableConversationFile(null));
        }

        // ---- 路径编码 ----

        [TestMethod]
        public void EncodeConversationFileName_ReplacesInvalidCharacters()
        {
            var encoded = JsonlPaths.EncodeConversationFileName("a/b:c*d");

            Assert.IsFalse(encoded.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0);
            Assert.IsTrue(encoded.EndsWith(".jsonl", StringComparison.Ordinal));
        }

        [TestMethod]
        public void EncodeConversationFileName_FallsBackForBlankId()
        {
            Assert.AreEqual("unnamed.jsonl", JsonlPaths.EncodeConversationFileName("  "));
        }

        [TestMethod]
        public async Task ExistingConversation_IsReusedAcrossDateFolders()
        {
            var yesterday = DateTime.UtcNow.AddDays(-1);
            var pastStore = new JsonlConversationStore(new JsonlPaths(_root, () => yesterday), useIndex: false);
            await pastStore.AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "old" }, CancellationToken.None);

            var todayStore = CreateStore();
            await todayStore.AppendMessageAsync("conv-1", new BrokerConversationMessage { Role = "user", Content = "new" }, CancellationToken.None);

            var files = Directory.GetFiles(Path.Combine(_root, "sessions"), "conv-1.jsonl", SearchOption.AllDirectories);
            Assert.AreEqual(1, files.Length, "同一会话跨日应沿用首次创建的日期目录");

            var reloaded = await CreateStore().GetOrCreateAsync("conv-1", CancellationToken.None);
            Assert.AreEqual(2, reloaded.Messages.Count);
        }

        [TestMethod]
        public void GetOrCreate_ThrowsWhenCancelled()
        {
            var store = CreateStore();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsException<OperationCanceledException>(
                    () => store.GetOrCreateAsync("conv-1", cts.Token).GetAwaiter().GetResult());
            }
        }

        private sealed class CancellingHostContextReader : IHostContextReader
        {
            private readonly CancellationTokenSource _cancellation;

            public CancellingHostContextReader(CancellationTokenSource cancellation)
            {
                _cancellation = cancellation;
            }

            public Task<HostContextSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken)
            {
                _cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new HostContextSnapshotDto());
            }
        }
    }
}
