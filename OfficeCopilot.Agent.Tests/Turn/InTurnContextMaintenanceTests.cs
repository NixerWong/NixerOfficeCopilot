using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class InTurnContextMaintenanceTests
    {
        [TestMethod]
        public void FoldStaleWordReads_FoldsOlderReadAfterWrite()
        {
            var messages = new List<ModelClientMessage> { User("排版") };
            AddCall(messages, "c1", "word_read_paragraphs", "{}", "1: 旧标题\n2: 旧正文");
            AddCall(messages, "c2", "word_format_paragraphs", "{\"items\":[]}", "Formatted 2/2.");
            AddCall(messages, "c3", "word_read_paragraphs", "{}", "1: 新标题\n2: 新正文");

            var folded = InTurnContextMaintenance.FoldStaleWordReads(messages);

            Assert.AreEqual(1, folded);
            StringAssert.StartsWith(messages[2].Content, InTurnContextMaintenance.StaleReadMarker);
            StringAssert.Contains(messages[2].Content, "段号可能已变化");
            Assert.AreEqual("1: 新标题\n2: 新正文", messages[6].Content);
            Assert.AreEqual(0, InTurnContextMaintenance.FoldStaleWordReads(messages), "already folded results stay folded once");
        }

        [TestMethod]
        public void FoldStaleWordReads_KeepsReadWithoutLaterReadOrWithDifferentArgsAndNoWrite()
        {
            var messages = new List<ModelClientMessage> { User("排版") };
            AddCall(messages, "c1", "word_read_document", "{\"maxCharacters\":100}", "前 100 字");
            AddCall(messages, "c2", "word_read_document", "{\"maxCharacters\":0}", "全文");
            AddCall(messages, "c3", "word_read_outline", "{}", "大纲");
            AddCall(messages, "c4", "word_apply_style", "{}", "ok");

            Assert.AreEqual(0, InTurnContextMaintenance.FoldStaleWordReads(messages));
        }

        [TestMethod]
        public void FoldStaleWordReads_FoldsIdenticalRepeatedRead()
        {
            var messages = new List<ModelClientMessage> { User("看看") };
            AddCall(messages, "c1", "word_read_outline", "{}", "大纲");
            AddCall(messages, "c2", "word_read_outline", "{}", "大纲");

            Assert.AreEqual(1, InTurnContextMaintenance.FoldStaleWordReads(messages));
            Assert.AreEqual("大纲", messages[4].Content);
        }

        [TestMethod]
        public void PreserveInTurnTail_ReattachesToolMessagesAfterRebuild()
        {
            var previous = new List<ModelClientMessage> { Sys("sys"), User("旧问题"), Assistant("旧回答"), User("排版") };
            AddCall(previous, "c1", "word_format_paragraphs", "{}", "Formatted 5/5.");
            var rebuilt = new List<ModelClientMessage> { Sys("sys"), Sys("摘要"), User("排版"), Assistant("中间文本") };

            var result = InTurnContextMaintenance.PreserveInTurnTail(rebuilt, previous);

            Assert.AreEqual(5, result.Count);
            Assert.AreEqual("摘要", result[1].Content);
            Assert.AreEqual("排版", result[2].Content);
            Assert.AreEqual("c1", result[3].ToolCalls[0].Id);
            Assert.AreEqual("tool", result[4].Role);
        }

        [TestMethod]
        public void EstimateTokens_CountsToolCallArguments()
        {
            var messages = new List<ModelClientMessage> { User("12345678") };
            AddCall(messages, "c1", "x", new string('a', 400), "1234");

            Assert.AreEqual((8 + 1 + 400 + 4 + 1) / 4, InTurnContextMaintenance.EstimateTokens(messages));
        }

        private static void AddCall(List<ModelClientMessage> messages, string id, string name, string args, string result)
        {
            var call = new ModelClientMessage { Role = "assistant" };
            call.ToolCalls.Add(new ModelClientToolCall { Id = id, Name = name, ArgumentsJson = args });
            messages.Add(call);
            messages.Add(new ModelClientMessage { Role = "tool", ToolCallId = id, Name = name, Content = result });
        }

        private static ModelClientMessage User(string content) => new ModelClientMessage { Role = "user", Content = content };

        private static ModelClientMessage Assistant(string content) => new ModelClientMessage { Role = "assistant", Content = content };

        private static ModelClientMessage Sys(string content) => new ModelClientMessage { Role = "system", Content = content };
    }
}
