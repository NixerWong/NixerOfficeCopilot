using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelVbaScopeSnapshotTests
    {
        [TestMethod]
        public void FindOutOfScopeChanges_ReportsUntargetedStructureAndValueChanges()
        {
            var before = Snapshot(
                Sheet("Summary", "Sheet1", "$A$1:$B$3", "Table1|$A$1:$B$3", "summary"),
                Sheet("Data", "Sheet2", "$A$1:$B$20", "Input|$A$1:$B$20", "before"));
            var after = Snapshot(
                Sheet("Summary", "Sheet1", "$A$1:$B$3", "Table1|$A$1:$B$3", "summary"),
                Sheet("Data", "Sheet2", "$A$1:$C$20", "Input|$A$1:$C$20", "after"));

            var changes = before.FindOutOfScopeChanges(
                after,
                new[] { "Summary" },
                new string[0]);

            Assert.AreEqual(3, changes.Count);
            Assert.IsTrue(changes.Any(change => change.Contains("UsedRange")));
            Assert.IsTrue(changes.Any(change => change.Contains("Excel 表格")));
            Assert.IsTrue(changes.Any(change => change.Contains("单元格值或公式")));
        }

        [TestMethod]
        public void FindOutOfScopeChanges_AllowsOnlyDeclaredNewWorksheet()
        {
            var before = Snapshot(Sheet("Data", "Sheet1", "$A$1:$B$3", string.Empty, "data"));
            var after = Snapshot(
                Sheet("Data", "Sheet1", "$A$1:$B$3", string.Empty, "data"),
                Sheet("Summary", "Sheet2", "$A$1:$B$3", string.Empty, "summary"),
                Sheet("Unexpected", "Sheet3", "$A$1", string.Empty, string.Empty));

            var changes = before.FindOutOfScopeChanges(
                after,
                new[] { "Summary" },
                new[] { "Summary" });

            Assert.AreEqual(1, changes.Count);
            StringAssert.Contains(changes[0], "Unexpected");
        }

        [TestMethod]
        public void FindOutOfScopeChanges_DetectsReplacementEvenWhenDeclaredTargetNameIsUnchanged()
        {
            var before = Snapshot(Sheet("Summary", "Sheet1", "$A$1:$B$3", string.Empty, "before", 1));
            var after = Snapshot(Sheet("Summary", "Sheet9", "$A$1:$B$3", string.Empty, "after", 2));

            var changes = before.FindOutOfScopeChanges(
                after,
                new[] { "Summary" },
                new string[0]);

            Assert.AreEqual(1, changes.Count);
            StringAssert.Contains(changes[0], "已被替换");
            StringAssert.Contains(changes[0], "Sheet1");
            StringAssert.Contains(changes[0], "Sheet9");
        }

        [TestMethod]
        public void FindOutOfScopeChanges_AllowsCodeNameInitializationForDeclaredTarget()
        {
            var before = Snapshot(Sheet("Summary", string.Empty, "$A$1:$B$3", string.Empty, "before", 1));
            var after = Snapshot(Sheet("Summary", "Sheet2", "$A$1:$B$3", string.Empty, "after", 1));

            var changes = before.FindOutOfScopeChanges(
                after,
                new[] { "Summary" },
                new string[0]);

            Assert.AreEqual(0, changes.Count);
        }

        [TestMethod]
        public void FindOutOfScopeChanges_AllowsUntargetedCodeNameInitializationForSameObject()
        {
            var before = Snapshot(Sheet("Data", string.Empty, "$A$1:$B$3", string.Empty, "before", 1));
            var after = Snapshot(Sheet("Data", "Sheet2", "$A$1:$C$3", string.Empty, "after", 1));

            var changes = before.FindOutOfScopeChanges(
                after,
                new string[0],
                new string[0]);

            Assert.AreEqual(2, changes.Count);
            Assert.IsTrue(changes.Any(change => change.Contains("UsedRange")));
            Assert.IsTrue(changes.Any(change => change.Contains("单元格值或公式")));
            Assert.IsFalse(changes.Any(change => change.Contains("已被替换")));
        }

        [TestMethod]
        public void FindOutOfScopeChanges_DetectsUntargetedReplacementWithSameStructureAndEmptyCodeName()
        {
            var before = Snapshot(Sheet("Data", string.Empty, "$A$1:$B$3", string.Empty, "unchanged", 1));
            var after = Snapshot(Sheet("Data", "Sheet2", "$A$1:$B$3", string.Empty, "unchanged", 2));

            var changes = before.FindOutOfScopeChanges(
                after,
                new[] { "Summary" },
                new string[0]);

            Assert.AreEqual(1, changes.Count);
            StringAssert.Contains(changes[0], "已被替换");
            StringAssert.Contains(changes[0], "Data");
        }

        [TestMethod]
        public void FindOutOfScopeChanges_DetectsDeclaredTargetReplacementWithEmptyCodeName()
        {
            var before = Snapshot(Sheet("Summary", string.Empty, "$A$1:$B$3", string.Empty, "unchanged", 1));
            var after = Snapshot(Sheet("Summary", "Sheet2", "$A$1:$B$3", string.Empty, "unchanged", 2));

            var changes = before.FindOutOfScopeChanges(
                after,
                new[] { "Summary" },
                new string[0]);

            Assert.AreEqual(1, changes.Count);
            StringAssert.Contains(changes[0], "已被替换");
            StringAssert.Contains(changes[0], "Summary");
        }

        private static ExcelVbaScopeSnapshot Snapshot(params ExcelVbaWorksheetSnapshot[] sheets)
        {
            return new ExcelVbaScopeSnapshot(
                sheets.ToDictionary(sheet => sheet.Name, System.StringComparer.OrdinalIgnoreCase),
                new List<string>());
        }

        private static ExcelVbaWorksheetSnapshot Sheet(
            string name,
            string codeName,
            string usedRange,
            string listObjectSignature,
            string valueAndFormulaHash,
            long comIdentity = 1)
        {
            return new ExcelVbaWorksheetSnapshot
            {
                Name = name,
                CodeName = codeName,
                ComIdentity = comIdentity,
                UsedRangeAddress = usedRange,
                ListObjectSignatures = string.IsNullOrWhiteSpace(listObjectSignature)
                    ? new List<string>()
                    : new List<string> { listObjectSignature },
                ValueAndFormulaHash = valueAndFormulaHash
            };
        }
    }
}
