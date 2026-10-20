using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Excel.Analysis;
using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelCoverageReportTests
    {
        [TestMethod]
        public void BuildCoverageReportCountsEveryScannedCellOnce()
        {
            var snapshot = new ExcelTabularSnapshot(
                new[] { "A", "B" },
                new List<object[]>
                {
                    new object[] { 42d, "7.5" },
                    new object[] { "invalid", -2146826246 },
                    new object[] { null, true },
                    new object[] { string.Empty, 0d }
                },
                new[] { false, false });

            var report = ExcelAggregationEngine.BuildCoverageReport(snapshot, 4);

            Assert.AreEqual(4, report.TotalRows);
            Assert.AreEqual(4, report.ScannedRows);
            Assert.AreEqual(4, report.NonNumericCount);
            Assert.AreEqual(1, report.TextNumberCount);
            Assert.AreEqual(1, report.ErrorCount);
            Assert.AreEqual(2, report.BlankCount);
            Assert.AreEqual("managed", report.Backend);
            Assert.AreEqual(true, report.CellCountsAvailable);
        }

        [TestMethod]
        public void BuildCoverageReportRejectsIncompleteRowCoverage()
        {
            var snapshot = new ExcelTabularSnapshot(
                new[] { "A" },
                new List<object[]> { new object[] { 1d } },
                new[] { false });

            var exception = Assert.ThrowsException<ExcelCoverageIncompleteException>(
                () => ExcelAggregationEngine.BuildCoverageReport(snapshot, 2));

            StringAssert.Contains(exception.Message, "已扫描 1 / 2");
            Assert.AreEqual("excel.incomplete_coverage", ExcelCoverageIncompleteException.ErrorCode);
        }
    }
}
