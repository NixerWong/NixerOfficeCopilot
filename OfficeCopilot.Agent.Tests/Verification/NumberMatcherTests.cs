using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Verification;

namespace OfficeCopilot.Agent.Tests.Verification
{
    [TestClass]
    public sealed class NumberMatcherTests
    {
        [TestMethod]
        public void Match_UsesAnswerPrecisionAndRoundsMidpointsAwayFromZero()
        {
            var ledger = Evidence("source 1.2349, midpoint 2.545, negative midpoint -2.545");
            var answer = NumberExtractor.Extract("result 1.23, rounded 2.55, negative -2.55");

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsTrue(report.AllMatched);
            Assert.AreEqual(3, report.Matched.Count);
            Assert.AreEqual(0, report.Unmatched.Count);
            Assert.AreEqual(NumberMatchKind.Direct, report.Matched[0].MatchKind);
            Assert.AreEqual(NumberMatchKind.Direct, report.Matched[1].MatchKind);
            Assert.AreEqual(NumberMatchKind.Direct, report.Matched[2].MatchKind);
        }

        [TestMethod]
        public void Match_ScalesFractionEvidenceForPercentAnswers()
        {
            var ledger = Evidence("share 0.1234");
            var answer = NumberExtractor.Extract("share is 12.3%");

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsTrue(report.AllMatched);
            Assert.AreEqual(NumberMatchKind.PercentageScaled, report.Matched[0].MatchKind);
        }

        [TestMethod]
        public void Match_UsesAlreadyPercentFormattedEvidenceWithoutScalingAgain()
        {
            var ledger = Evidence("share 12.34%");
            var answer = NumberExtractor.Extract("share is 12.3%");

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsTrue(report.AllMatched);
            Assert.AreEqual(NumberMatchKind.Direct, report.Matched[0].MatchKind);
        }

        [TestMethod]
        public void Match_DoesNotTreatBareNumbersAsPercentEvidence()
        {
            var ledger = Evidence("share 12.34");
            var answer = NumberExtractor.Extract("share is 12.3%");

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsFalse(report.AllMatched);
            Assert.AreEqual("12.3%", report.Unmatched.Single().RawText);
        }

        [TestMethod]
        public void Match_DoesNotTreatPercentEvidenceAsBareNumberEvidence()
        {
            var ledger = Evidence("share 12.34%");
            var answer = NumberExtractor.Extract("share is 12.3");

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsFalse(report.AllMatched);
            Assert.AreEqual("12.3", report.Unmatched.Single().RawText);
        }

        [TestMethod]
        public void Match_ComparesNormalizedUnitsAndAllowsAbsoluteValueEvidence()
        {
            var ledger = Evidence("amount 12000, decrease -15");
            var answer = NumberExtractor.Extract("amount is 1.2万; decrease is 15");

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsTrue(report.AllMatched);
            Assert.AreEqual(NumberMatchKind.Direct, report.Matched[0].MatchKind);
            Assert.AreEqual(NumberMatchKind.AbsoluteValue, report.Matched[1].MatchKind);
        }

        [TestMethod]
        public void Match_ReportsNumbersWithoutSupportingEvidence()
        {
            var ledger = Evidence("source 42");
            var answer = NumberExtractor.Extract("answer 99 and 42");

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsFalse(report.AllMatched);
            Assert.AreEqual(1, report.Unmatched.Count);
            Assert.AreEqual("99", report.Unmatched[0].RawText);
            Assert.AreEqual("answer 99 and 42", report.Unmatched[0].Context);
        }

        [TestMethod]
        public void Match_DoesNotTreatSequentialMarkdownRankLabelsAsUnmatchedResults()
        {
            var tableRows = Enumerable.Range(1, 28)
                .Select(rank => "| " + rank + " | " + (rank * 100) + " |");
            var answer = "| 排名 | 月薪合计 |\n|---:|---:|\n"
                + string.Join("\n", tableRows)
                + "\n| — | 40600 |";
            var evidence = ToolEvidence(string.Join(
                ", ",
                Enumerable.Range(1, 28)
                    .Select(rank => (rank * 100).ToString())
                    .Concat(new[] { "40600" })));

            var report = NumberMatcher.Match(
                NumberExtractor.Extract(answer),
                evidence);

            Assert.IsTrue(report.AllMatched);
            Assert.AreEqual(29, report.Matched.Count);
            Assert.AreEqual(0, report.Unmatched.Count);
        }

        [TestMethod]
        public void Match_VerifiesSubtractionFromOperandsInOneTrustedMessage()
        {
            var ledger = ToolEvidence(
                "06月平均月薪为 5534.5641025641；05月平均月薪为 4320.4698795181。");
            const string answerText =
                "差额：5534.5641025641 - 4320.4698795181 = 1214.094223046，约 1,214 元。";

            var report = NumberMatcher.Match(
                NumberExtractor.Extract(answerText),
                ledger,
                answerText);

            Assert.IsTrue(report.AllMatched);
            var derivedMatches = report.Matched
                .Where(match => match.Evidence.Origin == EvidenceLedger.EvidenceOrigin.DerivedCalculation)
                .ToList();
            Assert.AreEqual(2, derivedMatches.Count);
            Assert.IsTrue(derivedMatches.All(match =>
                match.MatchKind == NumberMatchKind.DerivedCalculation));
            Assert.IsTrue(derivedMatches.Any(match => match.Evidence.Value == 1214.094223046m));
        }

        [TestMethod]
        public void Match_DoesNotDeriveSubtractionFromSeparateMessages()
        {
            var ledger = EvidenceLedger.CreateEmpty();
            ledger.RecordToolResult(
                "excel_aggregate",
                "06月平均月薪为 5534.5641025641。",
                string.Empty,
                "june-result");
            ledger.RecordToolResult(
                "excel_aggregate",
                "05月平均月薪为 4320.4698795181。",
                string.Empty,
                "may-result");
            const string answerText =
                "5534.5641025641 - 4320.4698795181 = 1214.094223046";

            var report = NumberMatcher.Match(
                NumberExtractor.Extract(answerText),
                ledger,
                answerText);

            Assert.IsFalse(report.AllMatched);
            Assert.AreEqual("1214.094223046", report.Unmatched.Single().RawText);
        }

        [TestMethod]
        public void Match_DoesNotAcceptIncorrectSubtractionEquation()
        {
            var ledger = ToolEvidence(
                "06月平均月薪为 5534.5641025641；05月平均月薪为 4320.4698795181。");
            const string answerText =
                "5534.5641025641 - 4320.4698795181 = 1215.094223046";

            var report = NumberMatcher.Match(
                NumberExtractor.Extract(answerText),
                ledger,
                answerText);

            Assert.IsFalse(report.AllMatched);
            Assert.AreEqual("1215.094223046", report.Unmatched.Single().RawText);
        }

        [TestMethod]
        public void Match_UsesCellRangeRowSpanForExplicitHeaderInclusiveClaims()
        {
            var ledger = ToolEvidence("输出表：_wip_01_unmatched A1:C151（150 行 × 3 列）");
            var answer = NumberExtractor.Extract(
                "含表头共 151 行",
                ignoreStandaloneIntegersThrough: -1);

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsTrue(report.AllMatched);
            Assert.AreEqual(NumberMatchKind.CellRangeRowSpan, report.Matched[0].MatchKind);
            Assert.AreEqual(151m, report.Matched[0].Evidence.Value);
            Assert.AreEqual(
                EvidenceLedger.EvidenceKind.CellRangeRowSpan,
                report.Matched[0].Evidence.Kind);
        }

        [TestMethod]
        public void Match_DerivesRangeSpanLengthOnlyForExplicitRangeSpanClaims()
        {
            var ledger = ToolEvidence("Selected range: B2:B151");
            var answer = NumberExtractor.Extract(
                "The selected range spans 150 rows",
                ignoreStandaloneIntegersThrough: -1);

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsTrue(report.AllMatched);
            Assert.AreEqual(150m, report.Matched[0].Evidence.Value);
            Assert.AreEqual(NumberMatchKind.CellRangeRowSpan, report.Matched[0].MatchKind);

            var incorrectEndRowClaim = NumberExtractor.Extract(
                "The selected range spans 151 rows",
                ignoreStandaloneIntegersThrough: -1);
            var rejected = NumberMatcher.Match(incorrectEndRowClaim, ledger);
            Assert.IsFalse(rejected.AllMatched);
            Assert.AreEqual("151", rejected.Unmatched[0].RawText);
        }

        [TestMethod]
        public void Match_DoesNotUseRangeRowSpanAsEvidenceForAnUnrelatedNumber()
        {
            var ledger = ToolEvidence("Output table range: A1:C151");
            var answer = NumberExtractor.Extract(
                "Revenue is 151",
                ignoreStandaloneIntegersThrough: -1);

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsFalse(report.AllMatched);
            Assert.AreEqual("151", report.Unmatched[0].RawText);
        }

        [TestMethod]
        public void Match_DoesNotDeriveRangeRowSpansFromUserProvidedAddresses()
        {
            var ledger = Evidence("Please inspect range A1:C151");
            var answer = NumberExtractor.Extract(
                "含表头共 151 行",
                ignoreStandaloneIntegersThrough: -1);

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsFalse(report.AllMatched);
            Assert.AreEqual("151", report.Unmatched[0].RawText);
            Assert.AreEqual(0, ledger.RangeRowSpans.Count);
        }

        [TestMethod]
        public void Match_DoesNotDeriveSpanFromInvalidExcelColumnAddress()
        {
            var ledger = ToolEvidence("Invalid output range: ZZZ1:ZZZ151");
            var answer = NumberExtractor.Extract(
                "The selected range spans 151 rows",
                ignoreStandaloneIntegersThrough: -1);

            var report = NumberMatcher.Match(answer, ledger);

            Assert.IsFalse(report.AllMatched);
            Assert.AreEqual("151", report.Unmatched[0].RawText);
            Assert.AreEqual(0, ledger.RangeRowSpans.Count);
        }

        [TestMethod]
        public void Match_ReplaysTenAnnotatedHistoricalTraceCases()
        {
            var cases = new[]
            {
                new TraceReplayCase(
                    "run-20261005-000617/analysis-department-sales/run-01",
                    "runtime.calculate result: 20.0",
                    "部门占比为 20.0%",
                    new[] { "20.0%" }),
                new TraceReplayCase(
                    "run-20261005-000617/analysis-department-sales/run-02",
                    "runtime.calculate result: 20.0",
                    "实际占比约为 19.98%–20.02%",
                    new[] { "19.98%", "20.02%" }),
                new TraceReplayCase(
                    "run-20261005-000617/trap-text-numbers/run-01",
                    "5000 data rows; blank 250; text numbers 150; errors 50",
                    "原生数值单元格 4550",
                    new[] { "4550" }),
                new TraceReplayCase(
                    "run-20261005-000617/trap-text-numbers/run-02",
                    "Department frequencies: 1000 each; sampled Revenue values 191.28, 296.9",
                    "每 100 行一组，其中有 91 个原生数值",
                    new[] { "100", "91" }),
                new TraceReplayCase(
                    "run-20261005-002713/analysis-large-source-total/run-01",
                    "Sales!A1:L200001; 200000 data rows",
                    "数据行 2–200001，明细行数 200000",
                    new string[0]),
                new TraceReplayCase(
                    "run-20261005-002713/trap-hidden-rows/run-02",
                    "Hidden row scan: 11, 12; 5000 data rows",
                    "SUBTOTAL(103,A2) 可用于忽略隐藏行",
                    new[] { "103" }),
                new TraceReplayCase(
                    "run-20261005-012003/trap-subtotal/run-03",
                    "subtotal row 1501025; detail sum 1499689.86",
                    "两者差额为 1335.14",
                    new[] { "1335.14" }),
                new TraceReplayCase(
                    "run-20261005-031137/trap-hidden-rows/run-01",
                    "5000 data rows; hidden worksheet rows 11, 12",
                    "其余 4999 行均可见",
                    new[] { "4999" }),
                new TraceReplayCase(
                    "run-20261005-031137/trap-hidden-rows/run-03",
                    "5000 data rows; hidden worksheet rows 11, 12",
                    "可见明细为 4999 行，更正为 4998 行",
                    new[] { "4999", "4998" }),
                new TraceReplayCase(
                    "run-20261005-040546/trap-text-numbers/run-01",
                    "native number count 4550; text number count 150",
                    "有效数值单元格总计 4550 + 150 = 4700",
                    new[] { "4700" })
            };

            foreach (var traceCase in cases)
            {
                var report = NumberMatcher.Match(
                    NumberExtractor.Extract(traceCase.Answer, ignoreStandaloneIntegersThrough: -1),
                    ToolEvidence(traceCase.ToolEvidence));

                CollectionAssert.AreEqual(
                    traceCase.ExpectedUnmatched,
                    report.Unmatched.Select(number => number.RawText).ToArray(),
                    traceCase.Trace);
            }
        }

        private static EvidenceLedger Evidence(string text)
        {
            return EvidenceLedger.FromConversationMessages(new[]
            {
                new BrokerConversationMessage
                {
                    MessageType = "user",
                    Role = "user",
                    Content = text
                }
            });
        }

        private static EvidenceLedger ToolEvidence(string text)
        {
            var ledger = EvidenceLedger.CreateEmpty();
            ledger.RecordToolResult("excel_profile_range", text, string.Empty, "trace-evidence");
            return ledger;
        }

        private sealed class TraceReplayCase
        {
            public TraceReplayCase(
                string trace,
                string toolEvidence,
                string answer,
                string[] expectedUnmatched)
            {
                Trace = trace;
                ToolEvidence = toolEvidence;
                Answer = answer;
                ExpectedUnmatched = expectedUnmatched;
            }

            public string Trace { get; private set; }

            public string ToolEvidence { get; private set; }

            public string Answer { get; private set; }

            public string[] ExpectedUnmatched { get; private set; }
        }
    }
}
