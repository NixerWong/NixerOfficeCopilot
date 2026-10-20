using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class FakeCalcBackendTests
    {
        [TestMethod]
        public void EvaluateAppliesFiltersAndSumsMultipleColumnsWithCoverage()
        {
            var backend = new FakeCalcBackend(
                new[] { "Amount", "Tax", "Region" },
                new object[,]
                {
                    { 10d, 1d, "North" },
                    { 5d, 0.5d, "South" },
                    { "2", 0d, "North" },
                    { -2146826246, 1d, "North" },
                    { null, null, null }
                });
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef { WorksheetName = "Data" },
                Function = "sum"
            };
            query.Columns.Add("Amount");
            query.Columns.Add("Tax");
            query.Filters.Add(new CalcFilter
            {
                Column = "Region",
                Operator = "eq",
                Value = "north"
            });

            var result = backend.Evaluate(query);

            Assert.AreEqual(14d, result.Scalar.Value, 1e-9);
            Assert.AreEqual(5, result.Coverage.TotalRows);
            Assert.AreEqual(5, result.Coverage.ScannedRows);
            Assert.AreEqual("fake", result.Coverage.Backend);
            Assert.AreEqual(true, result.Coverage.CellCountsAvailable);
            Assert.AreEqual(6, result.Coverage.NonNumericCount);
            Assert.AreEqual(1, result.Coverage.TextNumberCount);
            Assert.AreEqual(1, result.Coverage.ErrorCount);
            Assert.AreEqual(3, result.Coverage.BlankCount);
        }

        [TestMethod]
        public void EvaluateSupportsGroupingAndNumericRangeFilters()
        {
            var backend = new FakeCalcBackend(
                new[] { "Amount", "Region" },
                new object[,]
                {
                    { 10d, "North" },
                    { 20d, "North" },
                    { 30d, "South" },
                    { 5d, "North" }
                });
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef { TableName = "Sales" },
                Function = "sum",
                GroupByColumn = "Region"
            };
            query.Columns.Add("Amount");
            query.Filters.Add(new CalcFilter
            {
                Column = "Amount",
                Operator = "gt",
                Value = "9"
            });

            var result = backend.Evaluate(query);

            Assert.AreEqual(30d, result.Groups["North"], 1e-9);
            Assert.AreEqual(30d, result.Groups["South"], 1e-9);
        }

        [TestMethod]
        public void CanEvaluateValidatesFixtureShapeAndEstimate()
        {
            var backend = new FakeCalcBackend(
                new[] { "Amount", "Region" },
                new object[,] { { 1d, "North" }, { 2d, "South" } });
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "sum"
            };
            query.Columns.Add("Amount");
            query.Filters.Add(new CalcFilter
            {
                Column = "Region",
                Operator = "notBlank",
                Value = string.Empty
            });

            Assert.IsTrue(backend.CanEvaluate(query, Cost(2, 2)));
            Assert.IsFalse(backend.CanEvaluate(query, Cost(3, 2)));
            Assert.IsFalse(backend.CanEvaluate(query, Cost(2, 1)));
            Assert.IsFalse(backend.CanEvaluate(query, new CalcCostEstimate
            {
                RowCount = 2,
                ReadColumnCount = 2,
                ProjectedCellCount = 3
            }));

            query.Filters[0].Operator = "contains";
            Assert.IsTrue(backend.CanEvaluate(query, Cost(2, 2)));
            query.Filters[0].Operator = "unsupported";
            Assert.IsFalse(backend.CanEvaluate(query, Cost(2, 2)));
        }

        [TestMethod]
        public void EvaluatePreservesInjectedNotesAndPropagatesInjectedBusyFailure()
        {
            var busy = new COMException("Excel is busy.", unchecked((int)0x80010001));
            var backend = new FakeCalcBackend(
                new[] { "Amount" },
                new object[,] { { 4d } },
                new[] { "fixture note" },
                busy);
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "sum"
            };
            query.Columns.Add("Amount");

            var exception = Assert.ThrowsException<COMException>(
                () => backend.Evaluate(query));

            Assert.AreSame(busy, exception);
            Assert.AreEqual(unchecked((int)0x80010001), exception.ErrorCode);
        }

        [TestMethod]
        public void EvaluateReturnsInjectedNotesOnSuccessfulResults()
        {
            var backend = new FakeCalcBackend(
                new[] { "Amount" },
                new object[,] { { 4d } },
                new[] { "fixture note" });
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "count"
            };
            query.Columns.Add("Amount");

            var result = backend.Evaluate(query);

            Assert.AreEqual(1, result.Notes.Count);
            Assert.AreEqual("fixture note", result.Notes[0]);
            Assert.AreEqual(1d, result.Scalar.Value);
        }

        [TestMethod]
        public void EvaluateSupportsVerificationResultShapes()
        {
            var backend = new FakeCalcBackend(
                new[] { "Id", "Amount", "Name" },
                new object[,]
                {
                    { "A", 1d, "Alpha" },
                    { "B", 2d, "Beta" },
                    { "A", null, "Gamma" },
                    { null, 4d, "Delta" }
                });
            var keySetQuery = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "keySet"
            };
            keySetQuery.Columns.Add("Id");
            Assert.IsTrue(backend.CanEvaluate(keySetQuery, Cost(4, 1)));
            CollectionAssert.AreEqual(new[] { "A", "B" }, backend.Evaluate(keySetQuery).Keys);

            var nonBlankQuery = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "nonBlank"
            };
            nonBlankQuery.Columns.Add("Amount");
            nonBlankQuery.Columns.Add("Name");
            Assert.IsTrue(backend.CanEvaluate(nonBlankQuery, Cost(4, 2)));
            Assert.AreEqual(7d, backend.Evaluate(nonBlankQuery).Scalar.Value, 0d);

            var sortedQuery = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "sorted",
                SortDirection = "ascending"
            };
            sortedQuery.Columns.Add("Amount");
            Assert.IsTrue(backend.CanEvaluate(sortedQuery, Cost(4, 1)));
            Assert.AreEqual(true, backend.Evaluate(sortedQuery).IsSorted);

            var rowCountQuery = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "rowCount"
            };
            Assert.IsTrue(backend.CanEvaluate(rowCountQuery, Cost(4, 0)));
            var rowCountResult = backend.Evaluate(rowCountQuery);
            Assert.AreEqual(4d, rowCountResult.Scalar.Value, 0d);
            Assert.IsFalse(rowCountResult.Coverage.CellCountsAvailable);
        }

        private static CalcCostEstimate Cost(int rowCount, int readColumnCount)
        {
            return new CalcCostEstimate
            {
                RowCount = rowCount,
                ReadColumnCount = readColumnCount,
                ProjectedCellCount = (long)rowCount * readColumnCount
            };
        }
    }
}
