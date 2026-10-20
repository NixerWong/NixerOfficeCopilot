using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Verification;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class ComparatorTests
    {
        [TestMethod]
        public void ScalarComparisonReportsValuesAndAppliesTolerance()
        {
            var plan = Plan("total", "scalar", false);
            plan.ExpectedValue = 10d;
            plan.Tolerance = 0.01d;
            var execution = new VerificationAssertionExecution
            {
                Plan = plan,
                TargetResult = Result(10.005d, 4, 4, "com")
            };

            var report = new Comparator().Compare(new[] { execution });

            Assert.IsTrue(report.Passed);
            Assert.AreEqual("A", report.Tier);
            Assert.AreEqual(1, report.Items.Count);
            Assert.AreEqual(10d, report.Items[0].ExpectedValue.Value, 0d);
            Assert.AreEqual(10.005d, report.Items[0].ActualValue.Value, 0d);
            Assert.AreEqual(0.005d, report.Items[0].Difference.Value, 1e-10d);
            Assert.AreEqual(0.01d, report.Items[0].Tolerance.Value, 0d);
            Assert.AreEqual("com", report.Items[0].TargetBackend);
        }

        [TestMethod]
        public void ScalarSourceComparisonUsesDefaultToleranceAndNonIndependentTier()
        {
            var plan = Plan("total", "scalar", true, false);
            plan.Tolerance = 0d;
            var execution = new VerificationAssertionExecution
            {
                Plan = plan,
                TargetResult = Result(10.5d, 3, 3, "managed"),
                SourceResult = Result(10d, 5, 5, "com")
            };

            var report = new Comparator().Compare(new[] { execution });
            var outcome = report.Items[0];

            Assert.IsFalse(report.Passed);
            Assert.AreEqual("B", report.Tier);
            Assert.AreEqual(0.5d, outcome.Difference.Value, 0d);
            Assert.AreEqual(1e-6d, outcome.Tolerance.Value, 0d);
            Assert.IsFalse(outcome.Independent);
            Assert.AreEqual(1, report.DiffCount);
            StringAssert.Contains(string.Join(" ", report.Diffs), "target=10.5");
            StringAssert.Contains(string.Join(" ", report.Diffs), "expected=10");
        }

        [TestMethod]
        public void GroupMatchComparesEveryKeyAndReportsMissingExtraAndValueDiffs()
        {
            var plan = Plan("departments", "groupMatch", true);
            plan.Tolerance = 0.01d;
            var target = Result(null, 3, 3, "managed");
            target.Groups.Add("A", 10d);
            target.Groups.Add("C", 30d);
            var source = Result(null, 4, 4, "managed");
            source.Groups.Add("A", 10.1d);
            source.Groups.Add("B", 20d);

            var report = new Comparator().Compare(new[]
            {
                new VerificationAssertionExecution
                {
                    Plan = plan,
                    TargetResult = target,
                    SourceResult = source
                }
            });
            var outcome = report.Items[0];

            Assert.IsFalse(report.Passed);
            Assert.AreEqual("A", report.Tier);
            Assert.AreEqual(3, outcome.DiffCount);
            Assert.AreEqual(3, outcome.ComparedCount.Value);
            Assert.AreEqual(3, report.Diffs.Count);
            StringAssert.Contains(string.Join(" ", report.Diffs), "缺失于 target");
            StringAssert.Contains(string.Join(" ", report.Diffs), "多出于 target");
        }

        [TestMethod]
        public void GroupMatchAcceptsDifferencesWithinTolerance()
        {
            var target = Result(null, 2, 2, "managed");
            target.Groups.Add("A", 10.005d);
            var source = Result(null, 2, 2, "managed");
            source.Groups.Add("A", 10d);
            var plan = Plan("department", "groupMatch", true);
            plan.Tolerance = 0.01d;

            var report = new Comparator().Compare(new[]
            {
                new VerificationAssertionExecution
                {
                    Plan = plan,
                    TargetResult = target,
                    SourceResult = source
                }
            });

            Assert.IsTrue(report.Passed);
            Assert.AreEqual(0, report.DiffCount);
            Assert.AreEqual(1, report.Items[0].ComparedCount.Value);
        }

        [TestMethod]
        public void KeySetComparisonUsesExactKeysAndReportsMissingAndExtra()
        {
            var target = Result(null, 3, 3, "managed");
            target.Keys.Add("B");
            target.Keys.Add("c");
            var source = Result(null, 3, 3, "managed");
            source.Keys.Add("A");
            source.Keys.Add("B");

            var report = new Comparator().Compare(new[]
            {
                new VerificationAssertionExecution
                {
                    Plan = Plan("ids", "keySet", true),
                    TargetResult = target,
                    SourceResult = source
                }
            });
            var outcome = report.Items[0];

            Assert.IsFalse(report.Passed);
            Assert.AreEqual(2, outcome.DiffCount);
            Assert.AreEqual(2, outcome.ExpectedCount.Value);
            Assert.AreEqual(2, outcome.ActualCount.Value);
            StringAssert.Contains(string.Join(" ", report.Diffs), "'A' 缺失于 target");
            StringAssert.Contains(string.Join(" ", report.Diffs), "'c' 多出于 target");
        }

        [TestMethod]
        public void KeySetDifferenceReportsBothDirectionsWithBoundedDeterministicSamples()
        {
            var target = Result(null, 6, 6, "managed");
            target.Keys.AddRange(new[] { "E", "C", "shared", "A", "D", "B" });
            var source = Result(null, 3, 3, "managed");
            source.Keys.AddRange(new[] { "G", "shared", "F" });
            var plan = Plan("ids", "keySetDifference", true);
            plan.SampleCount = 3;

            var report = new Comparator().Compare(new[]
            {
                new VerificationAssertionExecution
                {
                    Plan = plan,
                    TargetResult = target,
                    SourceResult = source
                }
            });
            var outcome = report.Items[0];

            Assert.IsTrue(report.Passed);
            Assert.AreEqual("A", report.Tier);
            Assert.AreEqual(0, report.DiffCount);
            Assert.AreEqual(5, outcome.TargetOnlyCount.Value);
            Assert.AreEqual(2, outcome.SourceOnlyCount.Value);
            CollectionAssert.AreEqual(
                new[] { "A", "C", "E" },
                outcome.TargetOnlySamples);
            CollectionAssert.AreEqual(
                new[] { "F", "G" },
                outcome.SourceOnlySamples);
        }

        [TestMethod]
        public void KeySetDifferenceUsesExactCaseAndWhitespaceAndRejectsIncompleteCoverage()
        {
            var exactPlan = Plan("ids", "keySetDifference", true);
            var exactReport = new Comparator().Compare(new[]
            {
                new VerificationAssertionExecution
                {
                    Plan = exactPlan,
                    TargetResult = WithKeys(Result(null, 1, 1, "managed"), "Key"),
                    SourceResult = WithKeys(Result(null, 1, 1, "managed"), "key ")
                }
            });

            Assert.IsTrue(exactReport.Passed);
            Assert.AreEqual(1, exactReport.Items[0].TargetOnlyCount.Value);
            Assert.AreEqual(1, exactReport.Items[0].SourceOnlyCount.Value);

            var incompleteTarget = Result(null, 1, 2, "managed");
            incompleteTarget.Keys.Add("Key");
            var incompleteSource = WithKeys(Result(null, 2, 2, "managed"), "Key");
            var incompleteReport = new Comparator().Compare(new[]
            {
                new VerificationAssertionExecution
                {
                    Plan = Plan("ids", "keySetDifference", true),
                    TargetResult = incompleteTarget,
                    SourceResult = incompleteSource
                }
            });

            Assert.IsFalse(incompleteReport.Passed);
            Assert.AreEqual("C", incompleteReport.Tier);
            Assert.IsNull(incompleteReport.Items[0].TargetOnlyCount);
            Assert.IsNull(incompleteReport.Items[0].SourceOnlyCount);
            StringAssert.Contains(string.Join(" ", incompleteReport.Notes), "覆盖不完整");
        }

        [TestMethod]
        public void KeySetDifferenceSamplesLowerMiddleForEvenSizedSets()
        {
            var target = Result(null, 150, 150, "managed");
            var source = Result(null, 150, 150, "managed");
            for (var key = 2851; key <= 3000; key++)
            {
                target.Keys.Add("C" + key);
            }
            for (var key = 3001; key <= 3150; key++)
            {
                source.Keys.Add("C" + key);
            }

            var plan = Plan("ids", "keySetDifference", true);
            plan.SampleCount = 3;
            var report = new Comparator().Compare(new[]
            {
                new VerificationAssertionExecution
                {
                    Plan = plan,
                    TargetResult = target,
                    SourceResult = source
                }
            });

            Assert.IsTrue(report.Passed);
            CollectionAssert.AreEqual(
                new[] { "C2851", "C2925", "C3000" },
                report.Items[0].TargetOnlySamples);
            CollectionAssert.AreEqual(
                new[] { "C3001", "C3075", "C3150" },
                report.Items[0].SourceOnlySamples);
        }

        [TestMethod]
        public void SortedUnknownAndIncompleteCoverageCannotPass()
        {
            var unknownSorted = new VerificationAssertionExecution
            {
                Plan = Plan("order", "sorted", false),
                TargetResult = Result(null, 2, 2, "managed")
            };
            unknownSorted.TargetResult.IsSorted = null;
            var incomplete = new VerificationAssertionExecution
            {
                Plan = Plan("count", "rowCount", true),
                TargetResult = Result(5d, 4, 5, "com"),
                SourceResult = Result(5d, 5, 5, "com")
            };

            var report = new Comparator().Compare(new[] { unknownSorted, incomplete });

            Assert.IsFalse(report.Passed);
            Assert.AreEqual("C", report.Tier);
            Assert.IsNull(report.Items[0].IsSorted);
            Assert.IsFalse(report.Items[0].Passed);
            Assert.IsFalse(report.Items[1].Passed);
            StringAssert.Contains(string.Join(" ", report.Notes), "无法确定排序");
            StringAssert.Contains(string.Join(" ", report.Notes), "覆盖不完整");
        }

        [TestMethod]
        public void NonBlankComparisonUsesScalarToleranceAndSortedFalseIsReported()
        {
            var nonBlankPlan = Plan("nonblank", "nonBlank", true);
            nonBlankPlan.Tolerance = 1d;
            var nonBlank = new VerificationAssertionExecution
            {
                Plan = nonBlankPlan,
                TargetResult = Result(4d, 4, 4, "managed"),
                SourceResult = Result(5d, 5, 5, "managed")
            };
            var unsortedResult = Result(null, 3, 3, "managed");
            unsortedResult.IsSorted = false;
            var sorted = new VerificationAssertionExecution
            {
                Plan = Plan("order", "sorted", false),
                TargetResult = unsortedResult
            };

            var report = new Comparator().Compare(new[] { nonBlank, sorted });

            Assert.IsFalse(report.Passed);
            Assert.AreEqual("A", report.Tier);
            Assert.IsTrue(report.Items[0].Passed);
            Assert.AreEqual(1d, report.Items[0].Tolerance.Value, 0d);
            Assert.IsFalse(report.Items[1].Passed);
            Assert.AreEqual(false, report.Items[1].IsSorted.Value);
        }

        [TestMethod]
        public void BusyAndTimeoutFailuresAreBothReportedAtTierC()
        {
            var plan = Plan("total", "scalar", true);
            var execution = new VerificationAssertionExecution
            {
                Plan = plan,
                TargetException = new COMException(
                    "Excel busy.",
                    unchecked((int)0x80010001)),
                SourceException = new TimeoutException("deadline elapsed")
            };

            var report = new Comparator().Compare(new[] { execution });

            Assert.IsFalse(report.Passed);
            Assert.AreEqual("C", report.Tier);
            var notes = string.Join(" ", report.Notes);
            StringAssert.Contains(notes, "COM 忙碌");
            StringAssert.Contains(notes, "超时");
        }

        [TestMethod]
        public void DiffDetailsAreCappedButTotalDiffCountIsPreserved()
        {
            var target = Result(null, 25, 25, "managed");
            var source = Result(null, 25, 25, "managed");
            for (var index = 0; index < 25; index++)
            {
                target.Keys.Add("extra-" + index);
            }

            var report = new Comparator().Compare(new[]
            {
                new VerificationAssertionExecution
                {
                    Plan = Plan("keys", "keySet", true),
                    TargetResult = target,
                    SourceResult = source
                }
            });

            Assert.AreEqual(25, report.DiffCount);
            Assert.AreEqual(20, report.Diffs.Count);
            Assert.AreEqual(25, report.Items[0].DiffCount);
            StringAssert.Contains(string.Join(" ", report.Notes), "仅展示前 20 条");
        }

        [TestMethod]
        public void ReportContractRoundTripsOutcomesAndInitializesOmittedCollections()
        {
            var report = new ExcelVerificationReport
            {
                Passed = true,
                Tier = "A"
            };
            report.Items.Add(new ExcelAssertionOutcome
            {
                Id = "sum",
                Kind = "scalar",
                Passed = true,
                Tier = "A",
                ExpectedValue = 5d,
                ActualValue = 5d,
                Difference = 0d,
                Tolerance = 1e-6d,
                Independent = true,
                TargetBackend = "com"
            });
            report.Items.Add(new ExcelAssertionOutcome
            {
                Id = "order",
                Kind = "sorted",
                Passed = true,
                Tier = "A",
                IsSorted = true,
                Independent = true,
                TargetBackend = "managed"
            });

            var copy = RoundTrip(report);

            Assert.IsTrue(copy.Passed);
            Assert.AreEqual("A", copy.Tier);
            Assert.AreEqual(2, copy.Items.Count);
            Assert.AreEqual(5d, copy.Items[0].ActualValue.Value, 0d);
            Assert.IsTrue(copy.Items[0].Independent);
            Assert.AreEqual(true, copy.Items[1].IsSorted.Value);
            Assert.IsNotNull(copy.Diffs);
            Assert.IsNotNull(copy.Notes);

            var omittedCollections = Deserialize("{\"passed\":false,\"tier\":\"C\"}");
            Assert.IsNotNull(omittedCollections.Items);
            Assert.IsNotNull(omittedCollections.Diffs);
            Assert.IsNotNull(omittedCollections.Notes);
        }

        private static VerificationAssertionPlan Plan(
            string id,
            string kind,
            bool hasSource,
            bool independent = true)
        {
            var plan = new VerificationAssertionPlan
            {
                AssertionId = id,
                Kind = kind
            };
            plan.Sides.Add(new PlannedCalcSide
            {
                Side = "target",
                Backend = new StubBackend("com"),
                Independent = independent
            });
            if (hasSource)
            {
                plan.Sides.Add(new PlannedCalcSide
                {
                    Side = "source",
                    Backend = new StubBackend("managed"),
                    Independent = independent
                });
            }

            return plan;
        }

        private static CalcResult Result(
            double? scalar,
            int scannedRows,
            int totalRows,
            string backend)
        {
            return new CalcResult
            {
                Scalar = scalar,
                Coverage = new ExcelCoverageReport
                {
                    TotalRows = totalRows,
                    ScannedRows = scannedRows,
                    Backend = backend
                }
            };
        }

        private static CalcResult WithKeys(CalcResult result, params string[] keys)
        {
            result.Keys.AddRange(keys);
            return result;
        }

        private static ExcelVerificationReport RoundTrip(ExcelVerificationReport report)
        {
            var serializer = new DataContractJsonSerializer(typeof(ExcelVerificationReport));
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, report);
                stream.Position = 0;
                return (ExcelVerificationReport)serializer.ReadObject(stream);
            }
        }

        private static ExcelVerificationReport Deserialize(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(ExcelVerificationReport));
            using (var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (ExcelVerificationReport)serializer.ReadObject(stream);
            }
        }

        private sealed class StubBackend : OfficeCopilot.Host.Excel.Verification.Backends.IExcelCalcBackend
        {
            public StubBackend(string name)
            {
                Name = name;
            }

            public string Name { get; private set; }

            public bool CanEvaluate(CalcQuery query, CalcCostEstimate cost)
            {
                return false;
            }

            public CalcResult Evaluate(CalcQuery query)
            {
                throw new NotSupportedException();
            }
        }
    }
}
