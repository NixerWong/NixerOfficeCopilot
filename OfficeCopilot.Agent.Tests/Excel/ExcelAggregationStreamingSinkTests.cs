using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Analysis;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelAggregationStreamingSinkTests
    {
        [TestMethod]
        public void RenderCoverageDoesNotPresentUnavailableCellCountsAsZero()
        {
            var description = ExcelAggregationService.RenderCoverage(
                string.Empty,
                new ExcelCoverageReport
                {
                    TotalRows = 10,
                    ScannedRows = 10,
                    Backend = "com",
                    CellCountsAvailable = false
                });

            StringAssert.Contains(description, "单元格分类计数未采集");
            Assert.IsFalse(description.Contains("非原生数值 0"));
        }

        [TestMethod]
        public void ProfileSinkAccumulatesMetricsAndOnlyRetainsRequestedSampleRows()
        {
            var sink = new ExcelAggregationService.ProfileChunkSink(2, 2);
            sink.Begin(Layout(3));
            sink.Accept(0, new[]
            {
                new object[] { "A", "A" },
                new object[] { 1d, "1" },
                new object[] { null, "note" }
            });
            sink.Accept(2, new[]
            {
                new object[] { "B" },
                new object[] { 2d },
                new object[] { null }
            });
            sink.End();

            Assert.AreEqual(3, sink.Profiles.Count);
            Assert.AreEqual("Amount", sink.Profiles[1].Name);
            Assert.AreEqual(4d, sink.Profiles[1].Sum, 0.0000001d);
            Assert.AreEqual(0, sink.Profiles[1].EmptyCount);
            Assert.AreEqual(2, sink.SampleRows.Count);
            CollectionAssert.AreEqual(new[] { "A", "1", string.Empty }, sink.SampleRows[0]);
            Assert.AreEqual(3, sink.Coverage.ScannedRows);
            Assert.AreEqual(3, sink.Coverage.TotalRows);
        }

        [TestMethod]
        public void ProfileHeaderComparisonReportsColumnOrderWithMultipleColumns()
        {
            var comparison = ExcelAggregationService.CompareProfileHeaders(
                new List<ExcelProfileSourceResult>
                {
                    new ExcelProfileSourceResult
                    {
                        Alias = "jan",
                        Columns =
                        {
                            new ExcelColumnProfile { Name = "Region", InferredType = "text" },
                            new ExcelColumnProfile { Name = "Amount", InferredType = "number" }
                        }
                    },
                    new ExcelProfileSourceResult
                    {
                        Alias = "feb",
                        Columns =
                        {
                            new ExcelColumnProfile { Name = "Amount", InferredType = "number" },
                            new ExcelColumnProfile { Name = "Region", InferredType = "text" }
                        }
                    }
                });

            Assert.IsTrue(comparison.NamesConsistent);
            Assert.IsFalse(comparison.OrderConsistent);
            Assert.IsTrue(comparison.TypesConsistent);
            Assert.AreEqual(1, comparison.Differences.Count);
            StringAssert.Contains(comparison.Differences[0], "列顺序");
        }

        [TestMethod]
        public void ProfileHeaderComparisonReportsMissingExtraColumnsTypesAndInference()
        {
            var comparison = ExcelAggregationService.CompareProfileHeaders(
                new List<ExcelProfileSourceResult>
                {
                    new ExcelProfileSourceResult
                    {
                        Alias = "jan",
                        HeaderInferred = true,
                        Columns =
                        {
                            new ExcelColumnProfile { Name = "Amount", InferredType = "number" },
                            new ExcelColumnProfile { Name = "Region", InferredType = "text" }
                        }
                    },
                    new ExcelProfileSourceResult
                    {
                        Alias = "feb",
                        HeaderInferred = false,
                        Columns =
                        {
                            new ExcelColumnProfile { Name = "Amount", InferredType = "text" },
                            new ExcelColumnProfile { Name = "Store", InferredType = "text" }
                        }
                    }
                });

            Assert.IsFalse(comparison.NamesConsistent);
            Assert.IsFalse(comparison.OrderConsistent);
            Assert.IsFalse(comparison.TypesConsistent);
            Assert.IsFalse(comparison.HeaderInferenceConsistent);
            StringAssert.Contains(string.Join(" ", comparison.Differences), "缺少");
            StringAssert.Contains(string.Join(" ", comparison.Differences), "多出");
            StringAssert.Contains(string.Join(" ", comparison.Differences), "类型");
            StringAssert.Contains(string.Join(" ", comparison.Differences), "表头推断");
        }

        [TestMethod]
        public void ProfileRangeToolSchemaAcceptsMultipleNamedSources()
        {
            var definition = new OfficeCopilot.Host.Excel.Tools.ExcelProfileRangeTool().Definition;

            using (var document = System.Text.Json.JsonDocument.Parse(definition.InputSchemaJson))
            {
                Assert.AreEqual(
                    "array",
                    document.RootElement.GetProperty("properties")
                        .GetProperty("sources").GetProperty("type").GetString());
                Assert.AreEqual(
                    "alias",
                    document.RootElement.GetProperty("properties")
                        .GetProperty("sources").GetProperty("items")
                        .GetProperty("required")[0].GetString());
            }
        }

        [TestMethod]
        public void AggregateSinkCombinesGroupsAcrossChunkBoundaries()
        {
            var request = new ExcelAggregateRequest();
            request.GroupBy.Add("Group");
            request.Measures.Add(new ExcelAggregateMeasure
            {
                Column = "Amount",
                Function = "sum",
                Alias = "Total"
            });
            var result = new ExcelAggregateResult();
            var sink = new ExcelAggregationService.AggregateChunkSink(request, result);
            sink.Begin(Layout(3));
            sink.Accept(0, new[]
            {
                new object[] { "A", "A" },
                new object[] { 1d, 2d },
                new object[] { null, "marker" }
            });
            sink.Accept(2, new[]
            {
                new object[] { "B" },
                new object[] { 3d },
                new object[] { null }
            });
            sink.End();

            Assert.AreEqual(3, result.Coverage.ScannedRows);
            Assert.AreEqual(2, result.GroupCount);
            CollectionAssert.AreEqual(new[] { "A", "3" }, result.Rows[0]);
            CollectionAssert.AreEqual(new[] { "B", "3" }, result.Rows[1]);
        }

        [TestMethod]
        public void LookupIndex_DefaultPolicyRejectsDuplicateKeys()
        {
            var lookup = new ExcelAggregateLookup { Key = "CustomerId" };
            lookup.Columns.Add("Region");
            var sink = new ExcelAggregationService.LookupIndexChunkSink(
                lookup,
                new[] { "Region" },
                "error");
            sink.Begin(LookupLayout(2));

            Assert.ThrowsException<InvalidOperationException>(() => sink.Accept(0, new[]
            {
                new object[] { "C1", "c1" },
                new object[] { "North", "South" }
            }));
        }

        [TestMethod]
        public void LookupIndex_FirstPolicyKeepsFirstValueAndSkipsBlankKeys()
        {
            var lookup = new ExcelAggregateLookup { Key = "CustomerId" };
            lookup.Columns.Add("Region");
            var sink = new ExcelAggregationService.LookupIndexChunkSink(
                lookup,
                new[] { "Region" },
                "first");
            sink.Begin(LookupLayout(3));
            sink.Accept(0, new[]
            {
                new object[] { "C1", "c1", string.Empty },
                new object[] { "North", "South", "Unknown" }
            });
            sink.End();

            Assert.AreEqual(1, sink.DuplicateKeyCount);
            Assert.AreEqual(1, sink.ValuesByKey.Count);
            Assert.AreEqual("North", sink.ValuesByKey["C1"][0]);
        }

        [TestMethod]
        public void LookupRowSchemaCountsUnmatchedKeysAndDropsOnlyForInnerJoin()
        {
            var left = PreparedLookup("left");
            var leftSchema = ExcelAggregationService.CreateAggregateRowSchema(
                new[] { "CustomerId", "Amount" },
                new[] { false, false },
                new[] { left },
                false);
            bool include;
            var leftRow = leftSchema.CreateRow(
                new object[] { "missing", 10d },
                null,
                false,
                out include);

            Assert.IsTrue(include);
            Assert.IsNull(leftRow[2]);
            Assert.AreEqual(1, left.Summary.UnmatchedRowCount);
            Assert.AreEqual(0, left.Summary.DroppedRowCount);

            var inner = PreparedLookup("inner");
            var innerSchema = ExcelAggregationService.CreateAggregateRowSchema(
                new[] { "CustomerId", "Amount" },
                new[] { false, false },
                new[] { inner },
                false);
            innerSchema.CreateRow(
                new object[] { "missing", 10d },
                null,
                false,
                out include);

            Assert.IsFalse(include);
            Assert.AreEqual(1, inner.Summary.UnmatchedRowCount);
            Assert.AreEqual(1, inner.Summary.DroppedRowCount);
        }

        [TestMethod]
        public void ReconcileSinkPreservesWholeRowBlankSemantics()
        {
            var detail = new ExcelAggregationService.ReconcileChunkSink(
                "Group",
                "Amount",
                "detailGroupColumn",
                "detailValueColumn");
            detail.Begin(Layout(3));
            detail.Accept(0, new[]
            {
                new object[] { "A", null, null },
                new object[] { 1d, null, null },
                new object[] { null, "marker", null }
            });
            detail.End();

            Assert.AreEqual(2, detail.Accumulator.RowCount);
            Assert.AreEqual(2, detail.Accumulator.Totals.Count);
            Assert.AreEqual(3, detail.Coverage.ScannedRows);
        }

        private static ExcelColumnReadLayout Layout(int totalRows)
        {
            return new ExcelColumnReadLayout
            {
                Columns = new[] { "Group", "Amount", "Note" },
                SourceColumnIndexes = new[] { 0, 1, 2 },
                ColumnIsDateFormatted = new[] { false, false, false },
                SourceRowCount = totalRows + 1,
                TotalRows = totalRows,
                SourceColumnCount = 3,
                HasHeader = true,
                HeaderInferred = false,
                HeaderInferenceReason = string.Empty
            };
        }

        private static ExcelColumnReadLayout LookupLayout(int totalRows)
        {
            return new ExcelColumnReadLayout
            {
                Columns = new[] { "CustomerId", "Region" },
                ColumnIsDateFormatted = new[] { false, false },
                TotalRows = totalRows
            };
        }

        private static ExcelAggregationService.PreparedAggregateLookup PreparedLookup(string join)
        {
            var summary = new ExcelAggregateLookupSummary { Join = join };
            var lookup = new ExcelAggregateLookup { ForeignKey = "CustomerId" };
            lookup.Columns.Add("Region");
            return new ExcelAggregationService.PreparedAggregateLookup
            {
                Lookup = lookup,
                Join = join,
                Columns = new[] { "Region" },
                ColumnDateFormats = new[] { false },
                KeyDateFormatted = false,
                ValuesByKey = new System.Collections.Generic.Dictionary<string, object[]>(
                    System.StringComparer.OrdinalIgnoreCase)
                {
                    { "C1", new object[] { "North" } }
                },
                Summary = summary
            };
        }
    }
}
