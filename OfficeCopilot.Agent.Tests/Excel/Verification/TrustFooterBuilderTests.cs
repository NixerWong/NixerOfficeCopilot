using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Verification;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public class TrustFooterBuilderTests
    {
        [TestMethod]
        public void Build_ReturnsEmptyWhenNoExcelDataToolsWereUsed()
        {
            var footer = TrustFooterBuilder.Build(new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = false
            });

            Assert.AreEqual(string.Empty, footer);
        }

        [TestMethod]
        public void Build_FormatsTierAAndStructuredBasis()
        {
            var summary = new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = true,
                Tier = "A",
                AssertionCount = 3,
                CoveredRows = 52310,
                BasisJson = new List<string>
                {
                    "{\"source\":{\"worksheetName\":\"销售明细\"},"
                    + "\"filters\":[{\"column\":\"年份\",\"operator\":\"eq\",\"value\":\"2025\"}],"
                    + "\"groupBy\":[\"部门\"],"
                    + "\"measures\":[{\"column\":\"金额\",\"function\":\"sum\"}],"
                    + "\"note\":\"按下单日期统计；不含退货\"}"
                }
            };

            var footer = TrustFooterBuilder.Build(summary);

            StringAssert.Contains(footer, "校验：A级：全量独立复核通过（3 项断言通过，覆盖 52,310 行）");
            StringAssert.Contains(footer, "口径：销售明细");
            StringAssert.Contains(footer, "筛选 年份=2025");
            StringAssert.Contains(footer, "按 部门 汇总 sum(金额)");
            StringAssert.Contains(footer, "不含退货");
        }

        [TestMethod]
        public void Build_DescribesKeySetDifferenceAsACompletedComparisonNotAnEqualityAssertion()
        {
            var footer = TrustFooterBuilder.Build(new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = true,
                Tier = "A",
                HasKeySetDifferenceCalculation = true,
                CoveredRows = 6000
            });

            StringAssert.Contains(
                footer,
                "A级：全量独立复核通过（双向键集合差异已完整计算；覆盖 6,000 行）");
            Assert.IsFalse(footer.Contains("断言通过"));
        }

        [TestMethod]
        public void Build_DescribesHiddenRowsAsACompleteScan()
        {
            var footer = TrustFooterBuilder.Build(new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = true,
                Tier = "A",
                HasHiddenRowsScan = true,
                CoveredRows = 5000
            });

            StringAssert.Contains(
                footer,
                "A级：全量独立复核通过（隐藏行状态已完整扫描；覆盖 5,000 行）");
        }

        [TestMethod]
        public void Build_ExplainsTierBAndTierC()
        {
            var tierB = TrustFooterBuilder.Build(new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = true,
                Tier = "B",
                PrimaryReason = "抽样覆盖"
            });
            var tierC = TrustFooterBuilder.Build(new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = true,
                Tier = "C",
                PrimaryReason = "汇总表尚无后续验证"
            });

            StringAssert.Contains(tierB, "B级：已复核，但仍有局限（抽样覆盖）");
            StringAssert.Contains(tierC, "C级：尚未完成有效验证 —— 汇总表尚无后续验证");
        }

        [TestMethod]
        public void Build_DowngradesUntracedNumbersAndCapsCorrectedAnswersAtTierB()
        {
            var summary = new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = true,
                Tier = "A",
                AssertionCount = 1,
                CoveredRows = 8
            };
            var unresolvedNumbers = NumberExtractor.Extract("总金额是 999。");
            var untraced = TrustFooterBuilder.Build(
                summary,
                unresolvedNumbers);
            var corrected = TrustFooterBuilder.Build(
                summary,
                numberCorrectionSucceeded: true);

            StringAssert.Contains(untraced, "校验：C级：尚未完成有效验证 —— 有部分结论数字未能找到直接证据");
            StringAssert.Contains(untraced, "未能找到直接证据的数字：999");
            StringAssert.Contains(untraced, "本轮自动追溯未能在用户消息或工具结果中匹配到这些数字");
            StringAssert.Contains(corrected, "校验：B级：已复核，但仍有局限（最终回答中的数字经过订正）");
            Assert.IsFalse(corrected.Contains("A级：全量独立复核通过"));
            Assert.AreEqual("C", TrustFooterBuilder.GetEffectiveTier(summary, unresolvedNumbers));
            Assert.AreEqual(
                "B",
                TrustFooterBuilder.GetEffectiveTier(summary, numberCorrectionSucceeded: true));
        }

        [TestMethod]
        public void Build_FormatsDerivedGroupsAndTheirParameters()
        {
            var summary = new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = true,
                Tier = "A",
                BasisJson = new List<string>
                {
                    "{\"source\":{\"worksheetName\":\"员工\"},"
                    + "\"derivedGroupBy\":["
                    + "{\"column\":\"生日\",\"transform\":\"month\",\"alias\":\"出生月\"},"
                    + "{\"column\":\"工资\",\"transform\":\"numberBucket\","
                    + "\"alias\":\"工资区间\",\"size\":1000}],"
                    + "\"measures\":[{\"column\":\"工资\",\"function\":\"sum\"}]}"
                }
            };

            var footer = TrustFooterBuilder.Build(summary);

            StringAssert.Contains(footer, "按 生日→出生月（month）");
            StringAssert.Contains(footer, "工资→工资区间（numberBucket，桶宽 1000）");
            StringAssert.Contains(footer, "sum(工资)");
        }

        [TestMethod]
        public void Build_ShowsAtMostThreeBasesAndCountsTheRest()
        {
            var summary = new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = true,
                Tier = "C",
                BasisJson = new List<string>
                {
                    "{\"source\":{\"worksheetName\":\"Sheet1\"}}",
                    "{\"source\":{\"worksheetName\":\"Sheet2\"}}",
                    "{\"source\":{\"worksheetName\":\"Sheet3\"}}",
                    "{\"source\":{\"worksheetName\":\"Sheet4\"}}"
                }
            };

            var footer = TrustFooterBuilder.Build(summary);

            StringAssert.Contains(footer, "Sheet1");
            StringAssert.Contains(footer, "Sheet2");
            StringAssert.Contains(footer, "Sheet3");
            StringAssert.Contains(footer, "等 1 项");
            Assert.IsFalse(footer.Contains("Sheet4"));
        }

        [TestMethod]
        public void Build_ListsDirectReadScopesAndOmitsVagueBasis()
        {
            var footer = TrustFooterBuilder.Build(new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = true,
                Tier = "B",
                PrimaryReason = "只读数据由 Excel 工具直接返回；未进行独立复核。",
                ScopeDescriptions = new List<string>
                {
                    "工作表清单",
                    "读取 Monthly!A2",
                    "备份清单"
                }
            });

            StringAssert.Contains(footer, "校验：B级：已复核，但仍有局限");
            StringAssert.Contains(
                footer,
                "检查对象：工作表清单；读取 Monthly!A2；备份清单");
            Assert.IsFalse(footer.Contains("口径："));
        }

        [TestMethod]
        public void RemoveModelGeneratedTrustText_RemovesConflictingAndDuplicateTiers()
        {
            const string content =
                "A2 原值为 S0001。\n\n"
                + "### 7. 验证档位 **C 级**：没有登记产出项\n\n"
                + "— 校验：B级：模型自报\n"
                + "口径：模型口径\n\n"
                + "— 校验：B级：模型自报重复\n"
                + "口径：模型口径";

            var cleaned = TrustFooterBuilder.RemoveModelGeneratedTrustText(content);

            Assert.AreEqual("A2 原值为 S0001。", cleaned);
        }

        [TestMethod]
        public void RemoveModelGeneratedTrustText_PreservesNumbersInOrdinaryProse()
        {
            const string content = "单元格 A1 的值是 100。";

            Assert.AreEqual(
                content,
                TrustFooterBuilder.RemoveModelGeneratedTrustText(content));
        }
    }
}
