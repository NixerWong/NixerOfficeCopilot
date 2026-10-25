using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class PriorTurnDeckWorkspaceCarryOverTests
    {
        private const string DeckId = "20261018-220144-9uboh2";

        [TestMethod]
        public void BuildNote_UsesLatestPreviewedWorkspaceFromPriorTurn()
        {
            var messages = new List<BrokerConversationMessage>
            {
                User("u1", "做成ppt"),
                ToolResult("ppt_style_preview", "{\"deckId\":\"" + DeckId + "\",\"candidates\":[]}"),
                ToolResult("ppt_deck_validate", "{\"deckId\":\"" + DeckId + "\",\"slideCount\":10,\"title\":\"决策模型接口 授课\"}"),
                ToolResult("ppt_deck_preview", "{\"deckId\":\"" + DeckId + "\",\"slide\":1,\"slideCount\":10,\"title\":\"决策模型接口 授课\"}"),
                Assistant("预览窗口已打开，确认后构建。"),
                User("u2", "继续")
            };

            var note = PriorTurnDeckWorkspaceCarryOver.BuildNote(messages, "u2");

            StringAssert.Contains(note, DeckId);
            StringAssert.Contains(note, "决策模型接口 授课");
            StringAssert.Contains(note, "Slide count: 10");
        }

        [TestMethod]
        public void BuildNote_IgnoresFailedResultsAndInvalidIds()
        {
            var messages = new List<BrokerConversationMessage>
            {
                User("u1", "做成ppt"),
                ToolResult("ppt_deck_preview", "{\"deckId\":\"" + DeckId + "\",\"slideCount\":10,\"title\":\"A\"}"),
                ToolResult("ppt_deck_build", "{\"deckId\":\"20261018-220500-a1b2c3\"}", success: false),
                ToolResult("ppt_deck_get_slides", "{\"deckId\":\"deck-3f8a2c\"}"),
                User("u2", "继续")
            };

            var note = PriorTurnDeckWorkspaceCarryOver.BuildNote(messages, "u2");

            StringAssert.Contains(note, DeckId);
            Assert.IsFalse(note.Contains("a1b2c3"));
            Assert.IsFalse(note.Contains("deck-3f8a2c"));
        }

        [TestMethod]
        public void BuildNote_ReturnsEmptyWithoutDeckResults()
        {
            var messages = new List<BrokerConversationMessage>
            {
                User("u1", "你好"),
                ToolResult("ppt_get_active_presentation", "{\"name\":\"演示文稿1\"}"),
                User("u2", "继续")
            };

            Assert.AreEqual(string.Empty, PriorTurnDeckWorkspaceCarryOver.BuildNote(messages, "u2"));
        }

        private static BrokerConversationMessage User(string id, string content)
        {
            return new BrokerConversationMessage { MessageId = id, MessageType = "user", Role = "user", Content = content };
        }

        private static BrokerConversationMessage Assistant(string content)
        {
            return new BrokerConversationMessage { MessageType = "assistant", Role = "assistant", Content = content, Success = true };
        }

        private static BrokerConversationMessage ToolResult(string name, string json, bool success = true)
        {
            return new BrokerConversationMessage
            {
                MessageType = "tool_result",
                Role = "tool_result",
                Name = name,
                StructuredContentJson = json,
                Success = success
            };
        }
    }
}
