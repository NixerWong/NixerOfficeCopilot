using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Verification;

namespace OfficeCopilot.Agent.Tests.Verification
{
    [TestClass]
    public sealed class EvidenceLedgerTests
    {
        [TestMethod]
        public void FromConversationMessages_CollectsUserAndCompleteToolResultContent()
        {
            var ledger = EvidenceLedger.FromConversationMessages(new[]
            {
                new BrokerConversationMessage
                {
                    MessageId = "user-1",
                    MessageType = "user",
                    Role = "user",
                    Content = "预算 １，２３４．５０ 元，比例 １２．５％，金额 1.2万。"
                },
                new BrokerConversationMessage
                {
                    MessageId = "tool-1",
                    MessageType = "tool_result",
                    Role = "tool_result",
                    Name = "excel_aggregate",
                    Content = "合计 3,412.75",
                    StructuredContentJson = "{\"total\":3412.75}"
                },
                new BrokerConversationMessage
                {
                    MessageId = "assistant-1",
                    MessageType = "message",
                    Role = "assistant",
                    Content = "未经验证的猜测 99999。"
                },
                new BrokerConversationMessage
                {
                    MessageId = "compact-1",
                    MessageType = "compact_summary",
                    Role = "assistant",
                    Content = "压缩摘要数字 88888。"
                }
            });

            var fullWidthMoney = ledger.Numbers.Single(number => number.Value == 1234.50m);
            Assert.AreEqual(2, fullWidthMoney.DecimalPlaces);
            Assert.AreEqual(EvidenceLedger.EvidenceOrigin.UserMessage, fullWidthMoney.Origin);

            var percentage = ledger.Numbers.Single(number => number.Value == 12.5m);
            Assert.AreEqual(1, percentage.DecimalPlaces);
            Assert.IsTrue(percentage.IsPercentage);

            var tenThousands = ledger.Numbers.Single(number => number.Value == 12000m);
            Assert.AreEqual(1, tenThousands.DecimalPlaces);

            var toolNumber = ledger.Numbers.Single(number => number.Value == 3412.75m);
            Assert.AreEqual(2, toolNumber.DecimalPlaces);
            Assert.AreEqual("excel_aggregate", toolNumber.SourceName);
            Assert.AreEqual("tool-1", toolNumber.MessageId);
            Assert.AreEqual(4, ledger.Numbers.Count);
        }

        [TestMethod]
        public void FromConversationMessages_NormalizesLargeChineseUnitsAndCalculationResults()
        {
            var ledger = EvidenceLedger.FromConversationMessages(new[]
            {
                new BrokerConversationMessage
                {
                    MessageId = "tool-2",
                    MessageType = "tool_result",
                    Role = "tool_result",
                    Name = "runtime.calculate",
                    Content = "result = -5.25; 3.5亿",
                    StructuredContentJson = "{\"operandsSourced\":true}"
                }
            });

            var negative = ledger.Numbers.Single(number => number.Value == -5.25m);
            Assert.AreEqual(2, negative.DecimalPlaces);
            Assert.AreEqual(EvidenceLedger.EvidenceOrigin.CalculationResult, negative.Origin);

            var hundredMillion = ledger.Numbers.Single(number => number.Value == 350000000m);
            Assert.AreEqual(1, hundredMillion.DecimalPlaces);
        }

        [TestMethod]
        public void FromConversationMessages_SkipsUnrepresentableScaledValuesWithoutAborting()
        {
            var ledger = EvidenceLedger.FromConversationMessages(new[]
            {
                new BrokerConversationMessage
                {
                    MessageType = "user",
                    Role = "user",
                    Content = "超范围数值 79228162514264337593543950335 亿，另一个有效值 5。"
                }
            });

            Assert.AreEqual(1, ledger.Numbers.Count);
            Assert.AreEqual(5m, ledger.Numbers[0].Value);
        }
    }
}
