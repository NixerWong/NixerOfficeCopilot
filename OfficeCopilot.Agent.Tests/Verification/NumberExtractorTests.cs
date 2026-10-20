using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Verification;

namespace OfficeCopilot.Agent.Tests.Verification
{
    [TestClass]
    public sealed class NumberExtractorTests
    {
        [TestMethod]
        public void Extract_NormalizesSupportedNumberFormsAndRetainsRawPrecision()
        {
            const string answer = "总额 1,234.56；减少 -5；占比 12.3%；约 1.2万与 3.5亿；全角 １２３．４５。增长率为12.5%。";

            var numbers = NumberExtractor.Extract(answer);

            Assert.AreEqual(7, numbers.Count, string.Join(", ", numbers.Select(number => number.RawText)));
            AssertNumber(numbers, "1,234.56", 1234.56m, 2, false);
            AssertNumber(numbers, "-5", -5m, 0, false);
            AssertNumber(numbers, "12.3%", 12.3m, 1, true);
            AssertNumber(numbers, "1.2万", 12000m, 1, false);
            AssertNumber(numbers, "3.5亿", 350000000m, 1, false);
            AssertNumber(numbers, "１２３．４５", 123.45m, 2, false);
            AssertNumber(numbers, "12.5%", 12.5m, 1, true);
        }

        [TestMethod]
        public void Extract_ExcludesCodeAddressesDatesTimesListsAndWorksheetNames()
        {
            const string answer =
                "单元格 A1、$B$2、A1:H100；日期 2025-01-01、2025年3月、3月和时间 14:30。"
                + "\n1. 列表编号\n12) 第二个列表"
                + "\n行内代码 `765.4`，代码块：\n```text\n98765\n```\n"
                + "工作表 Report 2024 的结果为 321.5。";

            var numbers = NumberExtractor.Extract(answer, new[] { "Report 2024" });

            Assert.AreEqual(1, numbers.Count);
            Assert.AreEqual("321.5", numbers[0].RawText);
            Assert.AreEqual(321.5m, numbers[0].Value);
        }

        [TestMethod]
        public void Extract_ExcludesWorksheetRowRangesButKeepsRowCounts()
        {
            const string answer =
                "数据行 2–200001；data rows 2-200001；第 3–9 行；明细行数 200000。";

            var numbers = NumberExtractor.Extract(answer, ignoreStandaloneIntegersThrough: -1);

            CollectionAssert.AreEqual(
                new[] { "200000" },
                numbers.Select(number => number.RawText).ToArray());
        }

        [TestMethod]
        public void Extract_ExcludesRuntimeContextUsageNotice()
        {
            const string answer =
                "销售额为 123.45。\n\n[上下文用量已达 192585/200000 tokens (96%)，已停止生成。请新开会话或 /compact 继续。]";

            var numbers = NumberExtractor.Extract(answer, ignoreStandaloneIntegersThrough: -1);

            CollectionAssert.AreEqual(
                new[] { "123.45" },
                numbers.Select(number => number.RawText).ToArray());
        }

        [TestMethod]
        public void Extract_IgnoresSequentialMarkdownRankColumnButKeepsTableValues()
        {
            var tableRows = Enumerable.Range(1, 28)
                .Select(rank => "| " + rank + " | " + (rank * 100) + " |");
            var answer = "| 排名 | 月薪合计 |\n|---:|---:|\n"
                + string.Join("\n", tableRows)
                + "\n| — | 合计 |";

            var numbers = NumberExtractor.Extract(
                answer,
                ignoreStandaloneIntegersThrough: -1);

            CollectionAssert.AreEqual(
                Enumerable.Range(1, 28)
                    .Select(rank => (rank * 100).ToString())
                    .ToArray(),
                numbers.Select(number => number.RawText).ToArray());
        }

        [TestMethod]
        public void Extract_KeepsNonSequentialValuesInMarkdownRankColumn()
        {
            const string answer =
                "| 排名 | 月薪合计 |\n"
                + "|---:|---:|\n"
                + "| 22 | 100 |\n"
                + "| 25 | 200 |\n"
                + "| 28 | 300 |";

            var numbers = NumberExtractor.Extract(
                answer,
                ignoreStandaloneIntegersThrough: -1);

            CollectionAssert.AreEqual(
                new[] { "22", "100", "25", "200", "28", "300" },
                numbers.Select(number => number.RawText).ToArray());
        }

        [TestMethod]
        public void Extract_IgnoresConfigurableStandaloneSmallIntegersOnly()
        {
            const string answer = "数字：0, 1, 5, 10, 11；精确值 10.0；变化 -5；占比 5%；规模 1.2万。";

            var defaultNumbers = NumberExtractor.Extract(answer);
            var thresholdFive = NumberExtractor.Extract(answer, ignoreStandaloneIntegersThrough: 5);
            var withoutFiltering = NumberExtractor.Extract(answer, ignoreStandaloneIntegersThrough: -1);

            CollectionAssert.AreEqual(
                new[] { "11", "10.0", "-5", "5%", "1.2万" },
                defaultNumbers.Select(number => number.RawText).ToArray());
            CollectionAssert.AreEqual(
                new[] { "10", "11", "10.0", "-5", "5%", "1.2万" },
                thresholdFive.Select(number => number.RawText).ToArray());
            Assert.AreEqual(9, withoutFiltering.Count);
        }

        [TestMethod]
        public void Extract_ProvidesSourceIndexesAndNearbyContext()
        {
            const string answer = "增长率达到 12.5%，高于预期。";

            var number = NumberExtractor.Extract(answer).Single();

            Assert.AreEqual(answer.IndexOf("12.5%"), number.StartIndex);
            Assert.AreEqual("12.5%".Length, number.Length);
            StringAssert.Contains(number.Context, "增长率达到 12.5%");
        }

        private static void AssertNumber(
            System.Collections.Generic.IReadOnlyList<ExtractedNumber> numbers,
            string rawText,
            decimal value,
            int decimalPlaces,
            bool isPercentage)
        {
            var number = numbers.Single(candidate => candidate.RawText == rawText);
            Assert.AreEqual(value, number.Value, rawText);
            Assert.AreEqual(decimalPlaces, number.DecimalPlaces, rawText);
            Assert.AreEqual(isPercentage, number.IsPercentage, rawText);
        }
    }
}
