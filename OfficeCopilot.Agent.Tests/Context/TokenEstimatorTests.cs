using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Context;

namespace OfficeCopilot.Agent.Tests.Context
{
    [TestClass]
    public class TokenEstimatorTests
    {
        [TestMethod]
        public void EstimateTokens_UsesFourCharactersPerToken_ForLatinText()
        {
            var estimator = new RoughTokenEstimator();

            Assert.AreEqual(0, estimator.EstimateTokens(string.Empty));
            Assert.AreEqual(0, estimator.EstimateTokens(null));
            Assert.AreEqual(2, estimator.EstimateTokens("12345678"));
            Assert.AreEqual(3, estimator.EstimateTokens("123456789012"));
        }

        [TestMethod]
        public void EstimateTokens_CountsOneTokenPerCjkCharacter()
        {
            var estimator = new RoughTokenEstimator();

            // 6 个汉字 -> 6 token（旧的 len/4 会给出 2，低估 3 倍）
            Assert.AreEqual(6, estimator.EstimateTokens("今天天气不错"));

            // 全角标点同样按宽字符计
            Assert.AreEqual(3, estimator.EstimateTokens("，。！"));

            // 假名与谚文
            Assert.AreEqual(5, estimator.EstimateTokens("こんにちは"));
            Assert.AreEqual(2, estimator.EstimateTokens("한글"));

            // 补充平面（CJK 扩展 B）代理对按一个表意字符计
            Assert.AreEqual(1, estimator.EstimateTokens("\uD840\uDC0B"));
        }

        [TestMethod]
        public void EstimateTokens_MixesCjkAndLatinWeights()
        {
            var estimator = new RoughTokenEstimator();

            // "今天" + " weather " + "不错" = 4 个宽字符 + 9 个窄字符
            // 4 * 1.0 + 9 / 4.0 = 6.25 -> 6
            Assert.AreEqual(6, estimator.EstimateTokens("今天 weather 不错"));
        }

        [TestMethod]
        public void EstimateMessageTokens_IncludesMessageOverheadAndOptionalFields()
        {
            var estimator = new RoughTokenEstimator();
            var message = new BrokerConversationMessage
            {
                Content = "1234",
                Name = "1234",
                StructuredContentJson = "1234"
            };

            Assert.AreEqual(7, estimator.EstimateMessageTokens(message));
            Assert.AreEqual(0, estimator.EstimateMessageTokens(null));
        }

        [TestMethod]
        public void EstimateConversationTokens_SkipsNullMessages()
        {
            var estimator = new RoughTokenEstimator();
            var conversation = new BrokerConversationState();
            conversation.Messages.Add(new BrokerConversationMessage { Content = "1234" });
            conversation.Messages.Add(null);
            conversation.Messages.Add(new BrokerConversationMessage { Content = "12345678" });

            Assert.AreEqual(11, estimator.EstimateConversationTokens(conversation));
            Assert.AreEqual(0, estimator.EstimateConversationTokens(null));
        }
    }
}
