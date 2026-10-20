using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.Agent.Runtime.Verification;
using OfficeCopilot.Agent.Tests.Harness;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class CalculateToolTests
    {
        [TestMethod]
        public async Task Dispatcher_RegistersAndExecutesCalculateAsReadOnlyRuntimeTool()
        {
            var hostInvoker = new FakeHostToolInvoker();
            var dispatcher = new DefaultToolDispatcher(hostInvoker);
            var catalog = DefaultToolDispatcher.BuildToolCatalog(new HostBridgeCapabilities());
            var descriptor = catalog[CalculateTool.ToolName];
            var evidenceLedger = EvidenceLedger.FromConversationMessages(new[]
            {
                new BrokerConversationMessage
                {
                    MessageType = "user",
                    Role = "user",
                    Content = "明细值为 125。"
                }
            });
            dispatcher.CurrentEvidenceLedger = evidenceLedger;

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1",
                CalculateTool.ToolName,
                "{\"expression\":\"125 * 2\"}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsTrue(descriptor.IsReadOnly);
            Assert.IsFalse(descriptor.RequiresConfirmation);
            Assert.IsTrue(outcome.Success);
            Assert.AreEqual("计算结果：250", outcome.Content);
            Assert.AreEqual(0, hostInvoker.Invocations.Count);
            using (var document = JsonDocument.Parse(outcome.StructuredContentJson))
            {
                Assert.IsTrue(document.RootElement.GetProperty("operandsSourced").GetBoolean());
                Assert.AreEqual(250m, document.RootElement.GetProperty("value").GetDecimal());
            }

            evidenceLedger.RecordToolResult(
                outcome.ToolName,
                outcome.Content,
                outcome.StructuredContentJson,
                outcome.ToolCallId);
            Assert.IsTrue(evidenceLedger.ContainsOperand(250m));
        }

        [TestMethod]
        public void Calculate_HandlesPrecedenceFunctionsAndExcelStyleRounding()
        {
            var ledger = Evidence("原值 2.545。");
            var tool = new CalculateTool();

            var precedence = tool.Execute("precedence", CalculateTool.ToolName, "{\"expression\":\"2 + 3 * (4 - 1)\"}", ledger);
            var functions = tool.Execute(
                "functions",
                CalculateTool.ToolName,
                "{\"expression\":\"sum(1, 2, 3) + avg(4, 8) + round(2.545, 2)\"}",
                ledger);

            Assert.IsTrue(precedence.Success);
            Assert.AreEqual("计算结果：11", precedence.Content);
            Assert.IsTrue(functions.Success);
            Assert.AreEqual("计算结果：14.55", functions.Content);
            using (var document = JsonDocument.Parse(functions.StructuredContentJson))
            {
                Assert.IsTrue(document.RootElement.GetProperty("operandsSourced").GetBoolean());
            }
        }

        [TestMethod]
        public void Calculate_UnverifiedOperandsReturnResultButCannotBecomeEvidence()
        {
            var ledger = Evidence("源数据 100。");
            var outcome = new CalculateTool().Execute(
                "unsourced",
                CalculateTool.ToolName,
                "{\"expression\":\"100 + 150.5\"}",
                ledger);

            Assert.IsTrue(outcome.Success);
            StringAssert.Contains(outcome.Content, "操作数未溯源：150.5");
            using (var document = JsonDocument.Parse(outcome.StructuredContentJson))
            {
                Assert.IsFalse(document.RootElement.GetProperty("operandsSourced").GetBoolean());
                Assert.AreEqual(
                    150.5m,
                    document.RootElement.GetProperty("unsourcedOperands")[0].GetDecimal());
            }

            ledger.RecordToolResult(
                outcome.ToolName,
                outcome.Content,
                outcome.StructuredContentJson,
                outcome.ToolCallId);
            Assert.IsFalse(ledger.ContainsOperand(250.5m));
        }

        [TestMethod]
        public void Calculate_AcceptsPercentageEvidenceAsFractionOperand()
        {
            var ledger = Evidence("增长率为 12.5%。");
            var outcome = new CalculateTool().Execute(
                "percentage",
                CalculateTool.ToolName,
                "{\"expression\":\"0.125 * 100\"}",
                ledger);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual("计算结果：12.500%", outcome.Content);
            using (var document = JsonDocument.Parse(outcome.StructuredContentJson))
            {
                Assert.IsTrue(document.RootElement.GetProperty("operandsSourced").GetBoolean());
                Assert.IsTrue(document.RootElement.GetProperty("isPercentage").GetBoolean());
            }
        }

        [TestMethod]
        public void Calculate_TagsRatioScaledByHundredAndPreservesPercentageDimension()
        {
            var ledger = Evidence("部门销售额 300240.000000001，总额 1500300。");
            var outcome = new CalculateTool().Execute(
                "ratio",
                CalculateTool.ToolName,
                "{\"expression\":\"round(300240.000000001 / 1500300 * 100, 1)\"}",
                ledger);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual("计算结果：20.0%", outcome.Content);
            using (var document = JsonDocument.Parse(outcome.StructuredContentJson))
            {
                Assert.IsTrue(document.RootElement.GetProperty("operandsSourced").GetBoolean());
                Assert.IsTrue(document.RootElement.GetProperty("isPercentage").GetBoolean());
            }

            ledger.RecordToolResult(
                outcome.ToolName,
                outcome.Content,
                outcome.StructuredContentJson,
                outcome.ToolCallId);

            var percentageAnswer = NumberMatcher.Match(
                NumberExtractor.Extract("占比 20.0%"),
                ledger);
            var bareNumberAnswer = NumberMatcher.Match(
                NumberExtractor.Extract("数值 20.0"),
                ledger);

            Assert.IsTrue(percentageAnswer.AllMatched);
            Assert.IsFalse(bareNumberAnswer.AllMatched);
        }

        [TestMethod]
        public void Calculate_ReportsInvalidInputDivisionByZeroAndOverflow()
        {
            var tool = new CalculateTool();

            var invalidArguments = tool.Execute(
                "arguments",
                CalculateTool.ToolName,
                "{\"expression\":12}",
                EvidenceLedger.CreateEmpty());
            var extraArguments = tool.Execute(
                "extra",
                CalculateTool.ToolName,
                "{\"expression\":\"1 + 2\",\"other\":\"not allowed\"}",
                EvidenceLedger.CreateEmpty());
            var invalidExpression = tool.Execute(
                "syntax",
                CalculateTool.ToolName,
                "{\"expression\":\"2 + * 3\"}",
                EvidenceLedger.CreateEmpty());
            var divisionByZero = tool.Execute(
                "divide",
                CalculateTool.ToolName,
                "{\"expression\":\"1 / 0\"}",
                EvidenceLedger.CreateEmpty());
            var overflow = tool.Execute(
                "overflow",
                CalculateTool.ToolName,
                "{\"expression\":\"79228162514264337593543950335 + 1\"}",
                EvidenceLedger.CreateEmpty());

            Assert.AreEqual("runtime.calculate.invalid_arguments", invalidArguments.ErrorCode);
            Assert.AreEqual("runtime.calculate.invalid_arguments", extraArguments.ErrorCode);
            Assert.AreEqual("runtime.calculate.invalid_expression", invalidExpression.ErrorCode);
            Assert.AreEqual("runtime.calculate.division_by_zero", divisionByZero.ErrorCode);
            Assert.AreEqual("runtime.calculate.overflow", overflow.ErrorCode);
            Assert.IsFalse(invalidArguments.Success);
            Assert.IsFalse(extraArguments.Success);
            Assert.IsFalse(invalidExpression.Success);
            Assert.IsFalse(divisionByZero.Success);
            Assert.IsFalse(overflow.Success);
        }

        private static EvidenceLedger Evidence(string userMessage)
        {
            return EvidenceLedger.FromConversationMessages(new[]
            {
                new BrokerConversationMessage
                {
                    MessageType = "user",
                    Role = "user",
                    Content = userMessage
                }
            });
        }
    }
}
