using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Agent.Tests.Excel
{
    /// <summary>P4-2 中间表命名规范 + P3-2 金丝雀代码重定向。</summary>
    [TestClass]
    public class ExcelStagingConventionsTests
    {
        [TestMethod]
        public void StagedName_NonWipSheetsAreNotConstrained()
        {
            // 正式表命名是用户的事，只有 _wip_ 前缀才受我们的序号规范约束。
            Assert.IsNull(ExcelStagingConventions.GetStagedNameViolation("季度报表"));
            Assert.IsNull(ExcelStagingConventions.GetStagedNameViolation(""));
            Assert.IsNull(ExcelStagingConventions.GetStagedNameViolation(null));
        }

        [TestMethod]
        public void StagedName_AcceptsTwoDigitSequenceWithDescription()
        {
            Assert.IsNull(ExcelStagingConventions.GetStagedNameViolation("_wip_01_清洗"));
            Assert.IsNull(ExcelStagingConventions.GetStagedNameViolation("_wip_12_汇总"));
        }

        [TestMethod]
        public void StagedName_RejectsMissingSequenceOrDescription()
        {
            Assert.IsNotNull(ExcelStagingConventions.GetStagedNameViolation("_wip_汇总"));
            Assert.IsNotNull(ExcelStagingConventions.GetStagedNameViolation("_wip_1_汇总"));
            Assert.IsNotNull(ExcelStagingConventions.GetStagedNameViolation("_wip_01_"));
        }

        [TestMethod]
        public void StagedName_ProbeSheetsAreExemptFromOwnRule()
        {
            // 金丝雀自己用的两张表不参与命名校验，否则会被自己的规则拦下。
            Assert.IsNull(ExcelStagingConventions.GetStagedNameViolation(ExcelStagingConventions.ProbeSourceSheetName));
            Assert.IsNull(ExcelStagingConventions.GetStagedNameViolation(ExcelStagingConventions.ProbeOutputSheetName));
        }

        [TestMethod]
        public void Cleanup_RequiresRegisteredWipSheetAndMatchingPrefix()
        {
            var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "_wip_01_clean",
                "Report"
            };

            Assert.IsTrue(ExcelStagingConventions.IsRegisteredWipWorksheetForCleanup(
                "_WIP_01_CLEAN",
                "_wip_",
                registered));
            Assert.IsFalse(ExcelStagingConventions.IsRegisteredWipWorksheetForCleanup(
                "_wip_user_data",
                "_wip_",
                registered));
            Assert.IsFalse(ExcelStagingConventions.IsRegisteredWipWorksheetForCleanup(
                "Report",
                "_wip_",
                registered));
            Assert.IsFalse(ExcelStagingConventions.IsRegisteredWipWorksheetForCleanup(
                "_wip_01_clean",
                "_tmp_",
                registered));
        }

        [TestMethod]
        public void Registry_ResolvesOnlyTheExpectedNewWipWorksheet()
        {
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "_wip_01_clean", string.Empty },
                { "_wip_02_unrelated", "Sheet4" }
            };
            var beforeOperation = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Source"
            };
            var atTurnStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Source"
            };

            string failureReason;
            Assert.IsTrue(ExcelStagingConventions.TryGetCreatedWipWorksheet(
                "_wip_01_clean",
                current,
                beforeOperation,
                atTurnStart,
                out failureReason),
                failureReason);
            Assert.IsTrue(current.ContainsKey("_wip_02_unrelated"));
        }

        [TestMethod]
        public void Registry_RejectsExistingOrUnverifiableWipWorksheets()
        {
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "_wip_01_clean", "Sheet3" }
            };
            var beforeOperation = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "_wip_01_clean"
            };
            var atTurnStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string failureReason;
            Assert.IsFalse(ExcelStagingConventions.TryGetCreatedWipWorksheet(
                "_wip_01_clean",
                current,
                beforeOperation,
                atTurnStart,
                out failureReason));

            beforeOperation.Clear();
            atTurnStart.Add("_wip_01_clean");
            Assert.IsFalse(ExcelStagingConventions.TryGetCreatedWipWorksheet(
                "_wip_01_clean",
                current,
                beforeOperation,
                atTurnStart,
                out failureReason));
        }

        [TestMethod]
        public void RegistryIdentityMarkerIsGuidBasedAndStable()
        {
            var marker = ExcelStagingConventions.CreateWipWorksheetIdentityMarker();

            string registryIdentity;
            Assert.IsTrue(ExcelStagingConventions.TryFormatWipWorksheetIdentity(
                marker,
                out registryIdentity));
            StringAssert.Contains(registryIdentity, "wip-identity:");
            Assert.IsTrue(ExcelStagingConventions.IsWipWorksheetRegistryIdentity(registryIdentity));
            Assert.IsFalse(ExcelStagingConventions.TryFormatWipWorksheetIdentity(
                "Sheet3",
                out registryIdentity));
            Assert.IsFalse(ExcelStagingConventions.IsWipWorksheetRegistryIdentity("Sheet3"));
        }

        [TestMethod]
        public void Probe_RedirectsSourceAndOutputLiterals()
        {
            const string code =
                "Sub Run()\r\n"
                + "  Set src = ThisWorkbook.Worksheets(\"销售明细\")\r\n"
                + "  Set dst = ThisWorkbook.Worksheets(\"_wip_01_汇总\")\r\n"
                + "End Sub";

            string probeCode, failure;
            var ok = ExcelStagingConventions.TryRedirectToProbe(code, "销售明细", "_wip_01_汇总", out probeCode, out failure);

            Assert.IsTrue(ok, failure);
            StringAssert.Contains(probeCode, "\"" + ExcelStagingConventions.ProbeSourceSheetName + "\"");
            StringAssert.Contains(probeCode, "\"" + ExcelStagingConventions.ProbeOutputSheetName + "\"");
            Assert.IsFalse(probeCode.Contains("销售明细"));
        }

        [TestMethod]
        public void Probe_RejectsCodeWithoutQuotedSourceLiteral()
        {
            // 用 ActiveSheet 的代码在样本上跑等于在原表上跑，必须拒绝而不是静默跳过金丝雀。
            const string code = "Sub Run()\r\n  ActiveSheet.Cells(1, 1).Value = 1\r\nEnd Sub";

            string probeCode, failure;
            var ok = ExcelStagingConventions.TryRedirectToProbe(code, "销售明细", "_wip_01_汇总", out probeCode, out failure);

            Assert.IsFalse(ok);
            Assert.IsNull(probeCode);
            StringAssert.Contains(failure, "销售明细");
        }
    }
}
