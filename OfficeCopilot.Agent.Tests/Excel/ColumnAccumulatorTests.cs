using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Analysis.Accumulators;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ColumnAccumulatorTests
    {
        [TestMethod]
        public void ScalarAccumulatorRetainsMeasureSemanticsAcrossChunkBoundaries()
        {
            var accumulator = new ScalarAccumulator();
            AcceptChunk(accumulator, new object[] { 12d, "12", "bad" });
            AcceptChunk(accumulator, new object[] { -2146826246, true, -5d, "bad", string.Empty, null });

            Assert.AreEqual(7, accumulator.Count);
            Assert.AreEqual(7, accumulator.CountA);
            Assert.AreEqual(3, accumulator.NumericCount);
            Assert.AreEqual(1, accumulator.TextNumberCount);
            Assert.AreEqual(4, accumulator.NonNumericCount);
            Assert.AreEqual(1, accumulator.ErrorCount);
            Assert.AreEqual(2, accumulator.BlankCount);
            Assert.AreEqual(5, accumulator.DistinctCount);
            Assert.AreEqual(19d, accumulator.Sum, 0.0000001d);
            Assert.AreEqual(-5d, accumulator.Min.Value, 0.0000001d);
            Assert.AreEqual(12d, accumulator.Max.Value, 0.0000001d);
            Assert.AreEqual("#N/A", accumulator.TextMin);
            Assert.AreEqual("bad", accumulator.TextMax);
            Assert.AreEqual("12", accumulator.First);
            Assert.AreEqual("bad", accumulator.Last);

            Assert.AreEqual("19", accumulator.Render("sum", false));
            Assert.AreEqual("7", accumulator.Render("count", false));
            Assert.AreEqual("5", accumulator.Render("countdistinct", false));
            Assert.AreEqual("6.3333333333", accumulator.Render("average", false));
            Assert.AreEqual("-5", accumulator.Render("min", false));
            Assert.AreEqual("12", accumulator.Render("max", false));
            Assert.AreEqual("12", accumulator.Render("first", false));
            Assert.AreEqual("bad", accumulator.Render("last", false));
        }

        [TestMethod]
        public void CountOnlyAcceptsRowsWithoutCountingThemAsBlankCells()
        {
            var accumulator = new ScalarAccumulator();
            accumulator.Accept(null, string.Empty, true);
            accumulator.Accept(null, string.Empty, true);

            Assert.AreEqual(2, accumulator.Count);
            Assert.AreEqual(2, accumulator.CountA);
            Assert.AreEqual(0, accumulator.BlankCount);
            Assert.AreEqual(0, accumulator.DistinctCount);
        }

        [TestMethod]
        public void DistinctAccumulatorIgnoresBlanksAndUsesOrdinalTextEquality()
        {
            var accumulator = new DistinctAccumulator();
            accumulator.Accept("A", "A");
            accumulator.Accept("A", "A");
            accumulator.Accept("a", "a");
            accumulator.Accept(string.Empty, string.Empty);
            accumulator.Accept(null, string.Empty);

            Assert.AreEqual(2, accumulator.Count);
        }

        [TestMethod]
        public void ColumnProfileAccumulatorRetainsProfileStatisticsAndTopValueOrdering()
        {
            var snapshot = new ExcelTabularSnapshot(
                new[] { "Value" },
                new List<object[]>
                {
                    new object[] { 3d },
                    new object[] { "3" },
                    new object[] { "bad" },
                    new object[] { -2146826246 },
                    new object[] { null }
                },
                new[] { false });

            var profile = ExcelAggregationEngine.BuildColumnProfiles(snapshot, 10)[0];

            Assert.AreEqual("Value", profile.Name);
            Assert.AreEqual(4, profile.NonEmptyCount);
            Assert.AreEqual(1, profile.EmptyCount);
            Assert.AreEqual(1, profile.ErrorCount);
            Assert.AreEqual(3, profile.DistinctCount);
            Assert.AreEqual("mixed", profile.InferredType);
            Assert.IsTrue(profile.HasNumericStats);
            Assert.AreEqual(6d, profile.Sum, 0.0000001d);
            Assert.AreEqual(3d, profile.Average, 0.0000001d);
            CollectionAssert.AreEqual(
                new[] { "3", "#N/A", "bad" },
                profile.TopValues.Select(value => value.Value).ToArray());
        }

        [TestMethod]
        public void ColumnProfileAccumulatorTracksTextNumberRows()
        {
            var accumulator = new ColumnProfileAccumulator(false);
            accumulator.Accept("12", 8);
            accumulator.Accept(13d, 9);
            accumulator.Accept("not a number", 10);

            Assert.AreEqual(2, accumulator.NumericCount);
            Assert.AreEqual(1, accumulator.TextNumberCount);
            CollectionAssert.AreEqual(new[] { 8 }, accumulator.TextNumberSampleRows.ToArray());
        }

        [TestMethod]
        public void GroupAccumulatorPreservesExistingAggregateOutput()
        {
            var snapshot = new ExcelTabularSnapshot(
                new[] { "Group", "Value" },
                new List<object[]>
                {
                    new object[] { "A", 12d },
                    new object[] { "A", "12" },
                    new object[] { "A", "bad" },
                    new object[] { "B", -5d },
                    new object[] { string.Empty, null }
                },
                new[] { false, false });
            var request = new ExcelAggregateRequest();
            request.GroupBy.Add("Group");
            request.Measures.AddRange(new[]
            {
                Measure("sum", "Sum"),
                Measure("count", "Count"),
                Measure("countDistinct", "Distinct"),
                Measure("average", "Average"),
                Measure("min", "Min"),
                Measure("max", "Max"),
                Measure("first", "First"),
                Measure("last", "Last")
            });

            var result = new ExcelAggregateResult();
            ExcelAggregationEngine.Aggregate(snapshot, request, result);
            var rows = result.Rows.ToDictionary(row => row[0], row => row, StringComparer.Ordinal);

            Assert.AreEqual(5, result.TotalRowCount);
            Assert.AreEqual(1, result.SkippedRowCount);
            Assert.AreEqual(2, result.GroupCount);
            Assert.AreEqual(5, result.Coverage.TotalRows);
            Assert.AreEqual(5, result.Coverage.ScannedRows);
            Assert.AreEqual(6, result.Coverage.NonNumericCount);
            Assert.AreEqual(0, result.Coverage.ErrorCount);
            Assert.AreEqual(2, result.Coverage.BlankCount);
            CollectionAssert.AreEqual(
                new[] { "A", "24", "3", "2", "12", "12", "12", "12", "bad" },
                rows["A"]);
            CollectionAssert.AreEqual(
                new[] { "B", "-5", "1", "1", "-5", "-5", "-5", "-5", "-5" },
                rows["B"]);
        }

        [TestMethod]
        public void AggregateKeepsRowsWithValuesOutsideRequestedMeasureColumns()
        {
            var snapshot = new ExcelTabularSnapshot(
                new[] { "Group", "Value", "Other" },
                new List<object[]>
                {
                    new object[] { null, null, "row marker" }
                },
                new[] { false, false, false });
            var request = new ExcelAggregateRequest();
            request.Measures.Add(new ExcelAggregateMeasure
            {
                Function = "count",
                Alias = "Rows"
            });

            var result = new ExcelAggregateResult();
            ExcelAggregationEngine.Aggregate(snapshot, request, result);

            Assert.AreEqual(0, result.SkippedRowCount);
            Assert.AreEqual(1, result.FilteredRowCount);
            Assert.AreEqual(1, result.GroupCount);
            CollectionAssert.AreEqual(new[] { "1" }, result.Rows[0]);
        }

        [TestMethod]
        public void KeySetAccumulatorMatchesReconcileKeyAndNumericSemantics()
        {
            var accumulator = new KeySetAccumulator();
            accumulator.Accept("North", 2d);
            accumulator.Accept("NORTH", "3");
            accumulator.Accept("South", "not numeric");

            Assert.AreEqual(3, accumulator.RowCount);
            Assert.AreEqual(2, accumulator.Totals.Count);
            Assert.AreEqual(5d, accumulator.Total, 0.0000001d);
            CollectionAssert.AreEqual(new[] { "North", "South" }, accumulator.KeysInOrder);
            Assert.AreEqual(5d, accumulator.Totals["north"], 0.0000001d);
            Assert.AreEqual(0d, accumulator.Totals["SOUTH"], 0.0000001d);
        }

        [TestMethod]
        public void ReconcileUsesKeySetAccumulatorsWithoutChangingComparisonResults()
        {
            var detail = new ExcelTabularSnapshot(
                new[] { "Group", "Value" },
                new List<object[]>
                {
                    new object[] { "North", 2d },
                    new object[] { "north", "3" },
                    new object[] { "South", "not numeric" },
                    new object[] { string.Empty, null }
                },
                new[] { false, false });
            var summary = new ExcelTabularSnapshot(
                new[] { "Group", "Value" },
                new List<object[]>
                {
                    new object[] { "NORTH", 5d },
                    new object[] { "South", 0d },
                    new object[] { "West", 1d }
                },
                new[] { false, false });
            var result = new ExcelReconcileResult();

            ExcelAggregationEngine.Reconcile(
                detail,
                summary,
                new ExcelReconcileRequest
                {
                    DetailGroupColumn = "Group",
                    DetailValueColumn = "Value",
                    SummaryGroupColumn = "Group",
                    SummaryValueColumn = "Value"
                },
                result);

            Assert.AreEqual(3, result.DetailRowCount);
            Assert.AreEqual(2, result.DetailGroupCount);
            Assert.AreEqual(3, result.SummaryRowCount);
            Assert.AreEqual(4, result.DetailCoverage.TotalRows);
            Assert.AreEqual(4, result.DetailCoverage.ScannedRows);
            Assert.AreEqual(1, result.DetailCoverage.TextNumberCount);
            Assert.AreEqual(5, result.DetailCoverage.NonNumericCount);
            Assert.AreEqual(2, result.DetailCoverage.BlankCount);
            Assert.AreEqual(3, result.SummaryCoverage.NonNumericCount);
            Assert.AreEqual(5d, result.DetailTotal, 0.0000001d);
            Assert.AreEqual(6d, result.SummaryTotal, 0.0000001d);
            Assert.IsFalse(result.TotalMatched);
            Assert.IsFalse(result.GroupCountMatched);
            CollectionAssert.AreEqual(new[] { "West" }, result.ExtraInSummary);
            Assert.AreEqual(0, result.MissingInSummary.Count);
            Assert.IsNotNull(result.Verification);
            Assert.IsFalse(result.Verification.Passed);
            Assert.AreEqual(2, result.Verification.AssertionCount);
            Assert.AreEqual(7L, result.Verification.CoveredRows);
            Assert.IsTrue(result.Verification.Details.Diffs.Any(
                difference => difference.Contains("West")));
        }

        private static void AcceptChunk(ScalarAccumulator accumulator, IEnumerable<object> values)
        {
            foreach (var value in values)
            {
                accumulator.Accept(value, ExcelValueSemantics.ToText(value, false), false);
            }
        }

        private static ExcelAggregateMeasure Measure(string function, string alias)
        {
            return new ExcelAggregateMeasure
            {
                Column = "Value",
                Function = function,
                Alias = alias
            };
        }
    }
}
