using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Context;

namespace OfficeCopilot.Agent.Tests.Context
{
    [TestClass]
    public class TurnSegmenterTests
    {
        [TestMethod]
        public void Segment_StartsNewSegmentAtEachUserPrompt()
        {
            var messages = new List<BrokerConversationMessage>
            {
                Message("system", "setup"),
                Message("user", "first"),
                Message("assistant", "answer"),
                new BrokerConversationMessage { Role = "tool_result", MessageType = "tool_result", Content = "result" },
                Message("user", "second"),
                Message("assistant", "final")
            };

            var segments = TurnSegmenter.Segment(messages, new RoughTokenEstimator());

            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual(1, segments[0].StartIndex);
            Assert.AreEqual(3, segments[0].EndIndex);
            Assert.AreEqual(4, segments[1].StartIndex);
            Assert.AreEqual(5, segments[1].EndIndex);
            Assert.AreEqual(3, segments[0].Messages.Count);
        }

        [TestMethod]
        public void IsUserPrompt_ExcludesToolMessages()
        {
            Assert.IsTrue(TurnSegmenter.IsUserPrompt(Message("user", "prompt")));
            Assert.IsFalse(TurnSegmenter.IsUserPrompt(new BrokerConversationMessage { Role = "user", ToolCallId = "call-1" }));
            Assert.IsTrue(TurnSegmenter.IsToolResult(new BrokerConversationMessage { Role = "tool_result" }));
            Assert.IsTrue(TurnSegmenter.IsToolCall(new BrokerConversationMessage { MessageType = "tool_call" }));
        }

        [TestMethod]
        public void Segment_NullOrEmptyInputReturnsEmptyList()
        {
            Assert.AreEqual(0, TurnSegmenter.Segment(null, new RoughTokenEstimator()).Count);
            Assert.AreEqual(0, TurnSegmenter.Segment(new List<BrokerConversationMessage>(), null).Count);
        }

        private static BrokerConversationMessage Message(string role, string content)
        {
            return new BrokerConversationMessage { Role = role, Content = content };
        }
    }
}
