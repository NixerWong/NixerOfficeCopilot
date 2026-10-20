using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelColumnReaderTests
    {
        [TestMethod]
        public void ResolveColumnsUsesHeadersPreservesOrderAndDeduplicates()
        {
            var columns = ExcelColumnReader.ResolveColumns(
                new[] { "Name", "Amount", "Amount_2" },
                new[] { "amount", "A", "Amount" });

            Assert.AreEqual(2, columns.Length);
            Assert.AreEqual("Amount", columns[0].Name);
            Assert.AreEqual(1, columns[0].SourceColumnIndex);
            Assert.AreEqual("Name", columns[1].Name);
            Assert.AreEqual(0, columns[1].SourceColumnIndex);
        }

        [TestMethod]
        public void IncludeAllColumnsPreservesSourceOrderForWholeRowSemantics()
        {
            var columns = ExcelColumnReader.ResolveColumns(
                new[] { "Name", "Amount", "Note" },
                new ExcelColumnReadRequest
                {
                    Columns = new[] { "Amount" },
                    IncludeAllColumns = true
                });

            CollectionAssert.AreEqual(
                new[] { 0, 1, 2 },
                columns.Select(column => column.SourceColumnIndex).ToArray());
        }

        [TestMethod]
        public void ResolveColumnsReportsAvailableNamesWhenColumnIsMissing()
        {
            var exception = Assert.ThrowsException<InvalidOperationException>(
                () => ExcelColumnReader.ResolveColumns(
                    new[] { "Name", "Amount" },
                    new[] { "Missing" }));

            StringAssert.Contains(exception.Message, "Missing");
            StringAssert.Contains(exception.Message, "Name、Amount");
        }

        [TestMethod]
        public void CalculateRowsPerChunkRespectsCellBudgetAndAlwaysMakesProgress()
        {
            Assert.AreEqual(50000, ExcelColumnReader.CalculateRowsPerChunk(200000, 4));
            Assert.AreEqual(1, ExcelColumnReader.CalculateRowsPerChunk(200000, 200001));
            Assert.AreEqual(int.MaxValue, ExcelColumnReader.CalculateRowsPerChunk(200000, 0));
        }

        [TestMethod]
        public void StreamChunksCoversRowsExactlyOnceForDifferentChunkSizes()
        {
            foreach (var rowsPerChunk in new[] { 1, 7, 1000 })
            {
                var observedRows = new List<int>();
                ExcelColumnReader.StreamChunks(
                    25,
                    rowsPerChunk,
                    (firstRowIndex, rowCount) =>
                    {
                        var values = new object[rowCount];
                        for (var index = 0; index < rowCount; index++)
                        {
                            values[index] = firstRowIndex + index;
                        }

                        return new[] { values };
                    },
                    new RecordingSink(observedRows));

                CollectionAssert.AreEqual(
                    Enumerable.Range(0, 25).ToArray(),
                    observedRows.ToArray());
            }
        }

        [TestMethod]
        public void ProjectedCellLimitAllowsBoundaryAndRejectsOverflow()
        {
            ExcelColumnReader.EnsureWithinCellLimit(50000000, 1);

            var exception = Assert.ThrowsException<ExcelSourceTooLargeException>(
                () => ExcelColumnReader.EnsureWithinCellLimit(25000001, 2));

            StringAssert.Contains(exception.Message, "50,000,002");
            Assert.AreEqual("excel.source_too_large", ExcelSourceTooLargeException.ErrorCode);
        }

        [TestMethod]
        public void ConvertToColumnsPreservesRowAndColumnOrdering()
        {
            var matrix = new object[,]
            {
                { 1d, "a", true },
                { 2d, "b", false }
            };

            var columns = ExcelColumnReader.ConvertToColumns(matrix, 2, 3);

            CollectionAssert.AreEqual(new object[] { 1d, 2d }, columns[0]);
            CollectionAssert.AreEqual(new object[] { "a", "b" }, columns[1]);
            CollectionAssert.AreEqual(new object[] { true, false }, columns[2]);
        }

        [TestMethod]
        public void ConvertToColumnsRejectsUnexpectedMatrixShapeAndMultiCellScalar()
        {
            Assert.ThrowsException<InvalidOperationException>(
                () => ExcelColumnReader.ConvertToColumns(new object[,] { { 1d } }, 2, 1));
            Assert.ThrowsException<InvalidOperationException>(
                () => ExcelColumnReader.ConvertToColumns(1d, 2, 1));
            Assert.AreEqual(1d, ExcelColumnReader.ConvertToColumns(1d, 1, 1)[0][0]);
        }

        [TestMethod]
        public void ExcelToolFailureMappingPreservesSourceTooLargeErrorCode()
        {
            var result = new SourceTooLargeMappingTool().MapFailure(
                new ExcelSourceTooLargeException(50000001, ExcelColumnReader.MaximumProjectedCellCount));

            Assert.AreEqual(ExcelSourceTooLargeException.ErrorCode, result.ErrorCode);
            StringAssert.Contains(result.ErrorMessage, "超过单次托管计算上限");
        }

        private sealed class SourceTooLargeMappingTool : ExcelToolBase
        {
            private readonly ToolDefinition _definition = new ToolDefinition
            {
                Name = "excel_test_source_limit",
                IsReadOnly = true
            };

            public override ToolDefinition Definition
            {
                get { return _definition; }
            }

            public override Task<ToolExecutionResult> ExecuteAsync(
                ToolExecutionContext context,
                ToolExecutionRequest request)
            {
                return Task.FromResult<ToolExecutionResult>(null);
            }

            public ToolExecutionResult MapFailure(Exception exception)
            {
                return CreateFailure(
                    new ToolExecutionRequest { ToolCallId = "source-limit-1" },
                    exception,
                    "excel.aggregate_failed");
            }
        }

        private sealed class RecordingSink : IColumnChunkSink
        {
            private readonly IList<int> _observedRows;

            public RecordingSink(IList<int> observedRows)
            {
                _observedRows = observedRows;
            }

            public void Begin(ExcelColumnReadLayout layout)
            {
            }

            public void Accept(int firstRowIndex, object[][] columns)
            {
                Assert.AreEqual(1, columns.Length);
                foreach (var value in columns[0])
                {
                    _observedRows.Add(Convert.ToInt32(value));
                }
            }

            public void End()
            {
            }
        }
    }
}
