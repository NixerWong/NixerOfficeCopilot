using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Verification.Backends.Managed;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class ManagedCalcBackendTests
    {
        [TestMethod]
        public void CanEvaluateQuerySupportsManagedOperationsWithinReadBudget()
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef { WorksheetName = "Sales" },
                Function = "sum",
                GroupByColumn = "Region"
            };
            query.Columns.Add("Amount");
            query.Filters.Add(new CalcFilter
            {
                Column = "Year",
                Operator = "contains",
                Value = "2025"
            });

            Assert.IsTrue(ManagedCalcBackend.CanEvaluateQuery(query, Cost(100, 3)));
            Assert.IsFalse(ManagedCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
            Assert.IsFalse(ManagedCalcBackend.CanEvaluateQuery(
                query,
                Cost(ExcelColumnReader.MaximumProjectedCellCount + 1, 3)));

            query.Columns.Add("Tax");
            Assert.IsFalse(ManagedCalcBackend.CanEvaluateQuery(query, Cost(100, 4)));
        }

        [TestMethod]
        public void CanEvaluateQuerySupportsEmptyFilterAliasesUsedByAggregateRequests()
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef { WorksheetName = "Sales" },
                Function = "sum"
            };
            query.Columns.Add("Revenue");
            query.Filters.Add(new CalcFilter
            {
                Column = "OrderID",
                Operator = "notEmpty",
                Value = string.Empty
            });

            Assert.IsTrue(ManagedCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Filters[0].Operator = "isEmpty";
            Assert.IsTrue(ManagedCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
        }

        [TestMethod]
        public void ChunkSinkAppliesFiltersAndReturnsGroupedValuesAndCoverage()
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef { TableName = "Sales" },
                Function = "sum",
                GroupByColumn = "Region"
            };
            query.Columns.Add("Amount");
            query.Filters.Add(new CalcFilter
            {
                Column = "Year",
                Operator = "eq",
                Value = "2025"
            });

            var sink = new ManagedCalcBackend.ManagedCalcChunkSink(query);
            sink.Begin(Layout(3, "Amount", "Year", "Region"));
            sink.Accept(
                0,
                new[]
                {
                    new object[] { 10d, 20d, 5d },
                    new object[] { "2025", "2024", "2025" },
                    new object[] { "West", "West", "East" }
                });
            sink.End();

            Assert.AreEqual(10d, sink.Result.Groups["West"], 1e-9);
            Assert.AreEqual(5d, sink.Result.Groups["East"], 1e-9);
            Assert.AreEqual(3, sink.Result.Coverage.TotalRows);
            Assert.AreEqual(3, sink.Result.Coverage.ScannedRows);
            Assert.AreEqual(6, sink.Result.Coverage.NonNumericCount);
            Assert.AreEqual(3, sink.Result.Coverage.TextNumberCount);
            Assert.AreEqual("managed", sink.Result.Coverage.Backend);
            Assert.AreEqual(true, sink.Result.Coverage.CellCountsAvailable);
        }

        [TestMethod]
        public void ChunkSinkCalculatesFilteredErrorCount()
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef { WorksheetName = "Errors" },
                Function = "errorCount"
            };
            query.Columns.Add("Value");
            query.Filters.Add(new CalcFilter
            {
                Column = "Group",
                Operator = "eq",
                Value = "A"
            });

            var sink = new ManagedCalcBackend.ManagedCalcChunkSink(query);
            sink.Begin(Layout(3, "Value", "Group"));
            sink.Accept(
                0,
                new[]
                {
                    new object[] { -2146826246, -2146826252, 2d },
                    new object[] { "A", "B", "A" }
                });
            sink.End();

            Assert.AreEqual(1d, sink.Result.Scalar.Value, 0d);
            Assert.AreEqual(2, sink.Result.Coverage.ErrorCount);
        }

        [TestMethod]
        public void ChunkSinkAveragesNumericValuesAndNumericText()
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "avg"
            };
            query.Columns.Add("Amount");

            var sink = new ManagedCalcBackend.ManagedCalcChunkSink(query);
            sink.Begin(Layout(3, "Amount"));
            sink.Accept(0, new[] { new object[] { 10d, "20", -2146826246 } });
            sink.End();

            Assert.AreEqual(15d, sink.Result.Scalar.Value, 1e-9);
        }

        [TestMethod]
        public void ChunkSinkReturnsNonBlankCountAcrossMultipleColumns()
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "nonBlank"
            };
            query.Columns.Add("Name");
            query.Columns.Add("Amount");

            var sink = new ManagedCalcBackend.ManagedCalcChunkSink(query);
            sink.Begin(Layout(3, "Name", "Amount"));
            sink.Accept(
                0,
                new[]
                {
                    new object[] { "A", null, null },
                    new object[] { 1d, "value", null }
                });
            sink.End();

            Assert.AreEqual(3d, sink.Result.Scalar.Value, 0d);
            Assert.AreEqual(3, sink.Result.Coverage.ScannedRows);
            Assert.AreEqual(3, sink.Result.Coverage.BlankCount);
        }

        [TestMethod]
        public void ChunkSinkReturnsDistinctNonBlankKeys()
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "keySet"
            };
            query.Columns.Add("Customer");

            var sink = new ManagedCalcBackend.ManagedCalcChunkSink(query);
            sink.Begin(Layout(4, "Customer"));
            sink.Accept(0, new[] { new object[] { "A", "A", "B", null } });
            sink.End();

            CollectionAssert.AreEqual(new[] { "A", "B" }, sink.Result.Keys);
        }

        [TestMethod]
        public void SortedSinkIgnoresBlanksAndReturnsUnknownForMixedTypes()
        {
            var sink = new ManagedCalcBackend.SortedCalcChunkSink("ascending");
            sink.Begin(Layout(4, "Amount"));
            sink.Accept(0, new[] { new object[] { 1d, 2d, null, 2d } });
            sink.End();

            Assert.AreEqual(true, sink.Result.IsSorted);
            Assert.AreEqual(1, sink.Result.Coverage.BlankCount);

            var mixedSink = new ManagedCalcBackend.SortedCalcChunkSink("ascending");
            mixedSink.Begin(Layout(2, "Amount"));
            mixedSink.Accept(0, new[] { new object[] { 1d, "text" } });
            mixedSink.End();

            Assert.IsNull(mixedSink.Result.IsSorted);

            var textSink = new ManagedCalcBackend.SortedCalcChunkSink("ascending");
            textSink.Begin(Layout(2, "Name"));
            textSink.Accept(0, new[] { new object[] { "Alpha", "Beta" } });
            textSink.End();

            Assert.IsNull(textSink.Result.IsSorted);
            StringAssert.Contains(textSink.Result.Notes[0], "区域排序语义");
        }

        [TestMethod]
        public void ManagedRowCountReportsStructuralCoverageWithoutCellCounts()
        {
            Assert.IsTrue(ManagedCalcBackend.CanEvaluateQuery(
                new CalcQuery
                {
                    Source = new ExcelSourceRef(),
                    Function = "rowCount"
                },
                Cost(12, 0)));

            var result = ManagedCalcBackend.CreateRowCountResult(12);

            Assert.AreEqual(12d, result.Scalar.Value, 0d);
            Assert.AreEqual(12, result.Coverage.TotalRows);
            Assert.AreEqual(false, result.Coverage.CellCountsAvailable);
        }

        [TestMethod]
        public void ChunkSinkRejectsMissingAndOutOfOrderRows()
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef(),
                Function = "sum"
            };
            query.Columns.Add("Amount");
            var layout = Layout(2, "Amount");

            var incompleteSink = new ManagedCalcBackend.ManagedCalcChunkSink(query);
            incompleteSink.Begin(layout);
            incompleteSink.Accept(0, new[] { new object[] { 1d } });
            Assert.ThrowsException<ExcelCoverageIncompleteException>(() => incompleteSink.End());

            var outOfOrderSink = new ManagedCalcBackend.ManagedCalcChunkSink(query);
            outOfOrderSink.Begin(layout);
            Assert.ThrowsException<System.InvalidOperationException>(
                () => outOfOrderSink.Accept(1, new[] { new object[] { 1d } }));
        }

        private static ExcelColumnReadLayout Layout(int totalRows, params string[] columns)
        {
            return new ExcelColumnReadLayout
            {
                Columns = columns,
                SourceColumnIndexes = new int[columns.Length],
                ColumnIsDateFormatted = new bool[columns.Length],
                SourceRowCount = totalRows,
                TotalRows = totalRows,
                SourceColumnCount = columns.Length
            };
        }

        private static CalcCostEstimate Cost(long rowCount, int columnCount)
        {
            return new CalcCostEstimate
            {
                RowCount = checked((int)rowCount),
                ReadColumnCount = columnCount,
                ProjectedCellCount = rowCount * columnCount
            };
        }
    }
}
