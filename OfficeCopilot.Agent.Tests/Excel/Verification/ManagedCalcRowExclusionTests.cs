using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Verification.Backends.Managed;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class ManagedCalcRowExclusionTests
    {
        [TestMethod]
        public void ManagedBackendAcceptsBoundedExclusionQueries()
        {
            var query = new CalcQuery
            {
                Function = "sum",
                Source = new ExcelSourceRef()
            };
            query.Columns.Add("Amount");
            query.ExcludeRows.Add(12);

            Assert.IsTrue(ManagedCalcBackend.CanEvaluateQuery(
                query,
                Cost(3, 1)));

            query.ExcludeRows.Clear();
            query.ExcludeLabelRows = true;
            Assert.IsTrue(ManagedCalcBackend.CanEvaluateQuery(
                query,
                Cost(3, 4)));
        }

        [TestMethod]
        public void ManagedScalarExcludesExplicitWorksheetRowsButScansFullCoverage()
        {
            var query = new CalcQuery
            {
                Function = "sum",
                Source = new ExcelSourceRef()
            };
            query.Columns.Add("Amount");
            query.ExcludeRows.Add(12);
            var sink = new ManagedCalcBackend.ManagedCalcChunkSink(query);
            sink.Begin(Layout(new[] { "Amount" }, new[] { 0 }));

            sink.Accept(0, new[]
            {
                new object[] { 10d, 500d, 20d }
            });
            sink.End();

            Assert.AreEqual(30d, sink.Result.Scalar.Value, 0d);
            Assert.AreEqual(3, sink.Result.Coverage.TotalRows);
            Assert.AreEqual(3, sink.Result.Coverage.ScannedRows);
            StringAssert.Contains(sink.Result.Notes[0], "排除了 1 行");
        }

        [TestMethod]
        public void ManagedGroupSumCanExcludeSubtotalLabelRowsAcrossAllColumns()
        {
            var query = new CalcQuery
            {
                Function = "sum",
                GroupByColumn = "Department",
                Source = new ExcelSourceRef(),
                ExcludeLabelRows = true
            };
            query.Columns.Add("Amount");
            var sink = new ManagedCalcBackend.ManagedCalcChunkSink(query);
            sink.Begin(Layout(
                new[] { "Amount", "Department", "Description" },
                new[] { 0, 1, 2 }));

            sink.Accept(0, new[]
            {
                new object[] { 10d, 500d, 20d },
                new object[] { "A", "A", "B" },
                new object[] { "detail", "小计", "detail" }
            });
            sink.End();

            Assert.AreEqual(2, sink.Result.Groups.Count);
            Assert.AreEqual(10d, sink.Result.Groups["A"], 0d);
            Assert.AreEqual(20d, sink.Result.Groups["B"], 0d);
            Assert.AreEqual(3, sink.Result.Coverage.ScannedRows);
            StringAssert.Contains(sink.Result.Notes[0], "排除了 1 行");
        }

        [TestMethod]
        public void ManagedBackendRejectsExcludedRowsOutsideTheDataRegion()
        {
            var query = new CalcQuery
            {
                Function = "sum",
                Source = new ExcelSourceRef()
            };
            query.Columns.Add("Amount");
            query.ExcludeRows.Add(10);
            var sink = new ManagedCalcBackend.ManagedCalcChunkSink(query);

            Assert.ThrowsException<InvalidOperationException>(
                () => sink.Begin(Layout(new[] { "Amount" }, new[] { 0 })));
        }

        private static ExcelColumnReadLayout Layout(string[] columns, int[] sourceColumnIndexes)
        {
            var dateFormatted = new bool[columns.Length];
            return new ExcelColumnReadLayout
            {
                Columns = columns,
                SourceColumnIndexes = sourceColumnIndexes,
                ColumnIsDateFormatted = dateFormatted,
                SourceRowCount = 4,
                TotalRows = 3,
                SourceColumnCount = columns.Length,
                SourceFirstRow = 10,
                RawHeaderValues = columns,
                HasHeader = true
            };
        }

        private static CalcCostEstimate Cost(int rowCount, int columnCount)
        {
            return new CalcCostEstimate
            {
                RowCount = rowCount,
                ReadColumnCount = columnCount,
                ProjectedCellCount = (long)rowCount * columnCount
            };
        }
    }
}
