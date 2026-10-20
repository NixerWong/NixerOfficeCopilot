using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class PriorTurnWordProgressCarryOverTests
    {
        private int _callId;

        [TestMethod]
        public void BuildNote_CarriesAnswersWritesAndBatchResult()
        {
            var messages = new List<BrokerConversationMessage> { User("u1", "按论文格式排版") };
            messages.AddRange(Call("word_read_paragraphs", "{\"startIndex\":1}", "1: 标题"));
            messages.AddRange(Call("runtime.ask_user_question", "{}", "User has answered your questions.\r\n- \"参考文献？\" → \"暂不添加\""));
            messages.AddRange(Call("word_format_paragraphs", "{\"items\":[]}", "Formatted 40/45 paragraphs. Formatting is complete."));
            messages.AddRange(Call("word_select_paragraph", "{\"paragraphIndex\":7}", "ok"));
            messages.AddRange(Call("word_set_paragraph_format", "{\"firstLineIndent\":2}", "ok"));
            messages.AddRange(Call("word_apply_style", "{\"style\":\"Heading 1\"}", "Style not found", success: false));
            messages.Add(Assistant("已达到轮次上限。"));
            messages.Add(User("u2", "继续"));

            var note = PriorTurnWordProgressCarryOver.BuildNote(messages, "u2");

            StringAssert.Contains(note, "暂不添加");
            StringAssert.Contains(note, "不要重复询问");
            StringAssert.Contains(note, "写操作 3 次（成功 2）");
            StringAssert.Contains(note, "Formatted 40/45 paragraphs");
            StringAssert.Contains(note, "word_set_paragraph_format（第 7 段）");
            StringAssert.Contains(note, "✗ word_apply_style");
            StringAssert.Contains(note, "不要从第 1 段开始重新排版");
            Assert.IsFalse(note.Contains("User has answered"));
        }

        [TestMethod]
        public void BuildNote_LooksBackPastTurnWithoutWrites()
        {
            var messages = new List<BrokerConversationMessage> { User("u1", "排版") };
            messages.AddRange(Call("word_format_paragraphs", "{}", "Formatted 10/10 paragraphs."));
            messages.Add(User("u2", "怎么样了"));
            messages.AddRange(Call("word_read_outline", "{}", "outline"));
            messages.Add(User("u3", "继续"));

            var note = PriorTurnWordProgressCarryOver.BuildNote(messages, "u3");

            StringAssert.Contains(note, "Formatted 10/10 paragraphs.");
        }

        [TestMethod]
        public void BuildNote_ReturnsEmptyForReadOnlyOrNonWordTurns()
        {
            var readOnly = new List<BrokerConversationMessage> { User("u1", "读一下") };
            readOnly.AddRange(Call("word_read_document", "{}", "text"));
            readOnly.Add(User("u2", "继续"));
            Assert.AreEqual(string.Empty, PriorTurnWordProgressCarryOver.BuildNote(readOnly, "u2"));

            var excel = new List<BrokerConversationMessage> { User("u1", "汇总") };
            excel.AddRange(Call("runtime.ask_user_question", "{}", "User has answered your questions.\r\n- \"口径\" → \"按月\""));
            excel.AddRange(Call("excel_write_range", "{}", "ok"));
            excel.Add(User("u2", "继续"));
            Assert.AreEqual(string.Empty, PriorTurnWordProgressCarryOver.BuildNote(excel, "u2"));
        }

        [TestMethod]
        public void IsWriteTool_ClassifiesWordTools()
        {
            Assert.IsTrue(PriorTurnWordProgressCarryOver.IsWriteTool("word_format_paragraphs"));
            Assert.IsTrue(PriorTurnWordProgressCarryOver.IsWriteTool("word_insert_text"));
            Assert.IsFalse(PriorTurnWordProgressCarryOver.IsWriteTool("word_select_paragraph"));
            Assert.IsFalse(PriorTurnWordProgressCarryOver.IsWriteTool("word_read_paragraphs"));
            Assert.IsFalse(PriorTurnWordProgressCarryOver.IsWriteTool("excel_write_range"));
        }

        private IEnumerable<BrokerConversationMessage> Call(string name, string args, string result, bool success = true)
        {
            var id = "call_" + (++_callId);
            yield return new BrokerConversationMessage
            {
                MessageType = "tool_call", Role = "tool_call", Name = name, ToolCallId = id, Content = name, StructuredContentJson = args, Success = true
            };
            yield return new BrokerConversationMessage
            {
                MessageType = "tool_result", Role = "tool_result", Name = name, ToolCallId = id, Content = result, Success = success
            };
        }

        private static BrokerConversationMessage User(string id, string content)
        {
            return new BrokerConversationMessage { MessageId = id, MessageType = "user", Role = "user", Content = content };
        }

        private static BrokerConversationMessage Assistant(string content)
        {
            return new BrokerConversationMessage { MessageType = "assistant", Role = "assistant", Content = content, Success = true };
        }
    }
}
