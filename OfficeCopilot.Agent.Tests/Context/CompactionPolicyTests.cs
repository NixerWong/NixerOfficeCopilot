using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Context;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Context
{
    [TestClass]
    public class CompactionPolicyTests
    {
        [TestMethod]
        public void MicroCompact_EvaluateAndApply_TruncatesOnlyOversizedToolResults()
        {
            var policy = new MicroCompactPolicy(prefixLength: 5, charThreshold: 10);
            var conversation = new BrokerConversationState();
            conversation.Messages.Add(new BrokerConversationMessage { Role = "assistant", Content = "keep" });
            conversation.Messages.Add(new BrokerConversationMessage
            {
                Role = "tool_result",
                MessageType = "tool_result",
                Content = "0123456789ABCDEFGHIJ",
                StructuredContentJson = "metadata"
            });

            var evaluation = policy.Evaluate(conversation);
            var compacted = policy.Apply(conversation);

            Assert.AreEqual(ContextWindowAction.MicroCompact, evaluation.RecommendedAction);
            Assert.AreEqual("01234", compacted.Messages[1].Content);
            Assert.AreEqual("metadata", compacted.Messages[1].StructuredContentJson);
            Assert.AreEqual("keep", compacted.Messages[0].Content);
            Assert.AreEqual("0123456789ABCDEFGHIJ", conversation.Messages[1].Content);
        }

        [TestMethod]
        public void MicroCompact_DoesNotChangeNormalConversation()
        {
            var policy = new MicroCompactPolicy(prefixLength: 5, charThreshold: 10);
            var conversation = new BrokerConversationState();
            conversation.Messages.Add(new BrokerConversationMessage { Role = "assistant", Content = "short" });

            Assert.AreEqual(ContextWindowAction.None, policy.Evaluate(conversation).RecommendedAction);
            Assert.AreSame(conversation, policy.Apply(conversation));
        }

        [TestMethod]
        public void SnipCompact_RemovesInclusiveRangeAndRejectsInvalidRange()
        {
            var policy = new SnipCompactPolicy();
            var conversation = new BrokerConversationState();
            conversation.Messages.Add(new BrokerConversationMessage { Content = "zero" });
            conversation.Messages.Add(new BrokerConversationMessage { Content = "one" });
            conversation.Messages.Add(new BrokerConversationMessage { Content = "two" });
            conversation.Messages.Add(new BrokerConversationMessage { Content = "three" });

            var evaluation = policy.Evaluate(conversation, 1, 2);
            var snipped = policy.Apply(conversation, 1, 2);
            var invalid = policy.Apply(conversation, -1, 2);

            Assert.AreEqual(ContextWindowAction.Snip, evaluation.RecommendedAction);
            Assert.AreEqual(2, snipped.Messages.Count);
            Assert.AreEqual("zero", snipped.Messages[0].Content);
            Assert.AreEqual("three", snipped.Messages[1].Content);
            Assert.AreSame(conversation, invalid);
        }
    }
}
