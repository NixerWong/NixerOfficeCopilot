using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Verification;
using System.Linq;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public class VerificationLedgerTests
    {
        [TestMethod]
        public void AggregateResult_UsesItsOwnPassingVerification()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_aggregate",
                "{\"worksheetName\":\"Sales\",\"measures\":[{\"column\":\"Amount\",\"function\":\"sum\"}]}",
                true,
                CreateResult("A", true, true, false, 2, 125, "Sales"));

            var summary = ledger.GetSummary();

            Assert.IsTrue(summary.HasExcelDataToolCalls);
            Assert.AreEqual("A", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
            Assert.AreEqual(1, summary.VerificationCount);
            Assert.AreEqual(2, summary.AssertionCount);
            Assert.AreEqual(125L, summary.CoveredRows);
            Assert.AreEqual(1, summary.BasisJson.Count);
        }

        [TestMethod]
        public void PassingStandaloneAssertion_IsReportedWithoutARegisteredSessionArtifact()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_assert",
                "{\"basis\":{\"source\":{\"worksheetName\":\"Orders\",\"address\":\"A1:C3001\"},"
                    + "\"headerMode\":\"yes\"},\"assertions\":[{\"kind\":\"scalar\","
                    + "\"source\":{\"worksheetName\":\"Orders\",\"address\":\"A1:C3001\","
                    + "\"column\":\"Amount\",\"function\":\"sum\"},"
                    + "\"target\":{\"worksheetName\":\"S0_FollowUp_Result\","
                    + "\"address\":\"A1:B2\",\"column\":\"Value\",\"function\":\"sum\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 3001, "Orders"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("A", summary.Tier);
            Assert.AreEqual(0, summary.OutputItemCount);
            Assert.AreEqual(0, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
            Assert.AreEqual(1, summary.VerificationCount);
            Assert.AreEqual(1, summary.AssertionCount);
            Assert.AreEqual(3001L, summary.CoveredRows);
        }

        [TestMethod]
        public void FailedStandaloneAssertion_RemainsTierC()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"kind\":\"scalar\","
                    + "\"target\":{\"worksheetName\":\"Output\",\"address\":\"A1:B2\"}}]}",
                true,
                CreateResult("C", false, false, false, 1, 2, "Output"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(0, summary.OutputItemCount);
            Assert.AreEqual(1, summary.VerificationCount);
        }

        [TestMethod]
        public void MatchingSingleCellTextReadback_ReceivesTierB()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\","
                    + "\"value\":\"APPEND_PROBE_20261007\"}",
                true,
                CreateSingleCellWriteResult("test.xlsx", "Monthly", "P1"),
                isWriteTool: true);
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\"}",
                true,
                CreateSingleCellReadback("Monthly", "P1", "APPEND_PROBE_20261007"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("B", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
            Assert.AreEqual(1, summary.VerificationCount);
            StringAssert.Contains(summary.PrimaryReason, "缺少独立复核");
        }

        [TestMethod]
        public void SingleCellTextReadback_WithDifferentValueRemainsTierC()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\",\"value\":\"expected\"}",
                true,
                CreateSingleCellWriteResult("test.xlsx", "Monthly", "P1"),
                isWriteTool: true);
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\"}",
                true,
                CreateSingleCellReadback("Monthly", "P1", "actual"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            StringAssert.Contains(summary.PrimaryReason, "回读值与写入值不一致");
        }

        [TestMethod]
        public void SingleCellTextReadback_ForDifferentAddressDoesNotVerifyWrite()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\",\"value\":\"expected\"}",
                true,
                CreateSingleCellWriteResult("test.xlsx", "Monthly", "P1"),
                isWriteTool: true);
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"Q1\"}",
                true,
                CreateSingleCellReadback("Monthly", "Q1", "expected"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(0, summary.VerificationCount);
            StringAssert.Contains(summary.PrimaryReason, "尚无后续覆盖验证");
        }

        [TestMethod]
        public void SingleCellTextReadback_FromDifferentWorkbookRemainsTierC()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\",\"value\":\"expected\"}",
                true,
                CreateSingleCellWriteResult("test.xlsx", "Monthly", "P1"),
                isWriteTool: true);
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\"}",
                true,
                CreateSingleCellReadback(
                    "Monthly",
                    "P1",
                    "expected",
                    workbook: "other.xlsx"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            StringAssert.Contains(summary.PrimaryReason, "缺少完整范围或值证据");
        }

        [TestMethod]
        public void SingleCellTextReadback_DoesNotVerifyAnEarlierWriteToTheSameCell()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\",\"value\":\"first\"}",
                true,
                CreateSingleCellWriteResult("test.xlsx", "Monthly", "P1"),
                isWriteTool: true);
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\"}",
                true,
                CreateSingleCellReadback("Monthly", "P1", "first"));
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\",\"value\":\"second\"}",
                true,
                CreateSingleCellWriteResult("test.xlsx", "Monthly", "P1"),
                isWriteTool: true);

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(2, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(1, summary.UncoveredOutputItemCount);
        }

        [TestMethod]
        public void SingleCellTextReadback_RejectsTruncatedResult()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\",\"value\":\"expected\"}",
                true,
                CreateSingleCellWriteResult("test.xlsx", "Monthly", "P1"),
                isWriteTool: true);
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\"}",
                true,
                CreateSingleCellReadback("Monthly", "P1", "expected", isTruncated: true));

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            StringAssert.Contains(summary.PrimaryReason, "缺少完整范围或值证据");
        }

        [TestMethod]
        public void FailedSingleCellReadback_SupersedesEarlierPassingReadback()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\",\"value\":\"expected\"}",
                true,
                CreateSingleCellWriteResult("test.xlsx", "Monthly", "P1"),
                isWriteTool: true);
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\"}",
                true,
                CreateSingleCellReadback("Monthly", "P1", "expected"));
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"P1\"}",
                false,
                "{}");

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(0, summary.CoveredOutputItemCount);
            StringAssert.Contains(summary.PrimaryReason, "回读工具未成功");
        }

        [TestMethod]
        public void WriteStage_RequiresVerificationLinkedToThatStage()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Output\",\"address\":\"A1:B2\"}",
                true,
                "{}",
                isWriteTool: true,
                executionStepId: "mutation:first");

            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"Output\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 2, "Output"),
                verificationTargetStepId: "mutation:other");
            Assert.IsFalse(ledger.HasPassingVerificationForStep("mutation:first"));

            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"Output\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 2, "Output"),
                verificationTargetStepId: "mutation:first");

            Assert.IsTrue(ledger.HasPassingVerificationForStep("mutation:first"));
            Assert.AreEqual("A", ledger.GetSummary().Tier);
            Assert.AreEqual(1, ledger.GetSummary().CoveredOutputItemCount);
        }

        [TestMethod]
        public void WipCleanupDryRun_VerifiesTheCreatedWipWorksheetStage()
        {
            const string stepId = "mutation:structural-transform";
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_create_worksheet",
                "{\"worksheetName\":\"_wip_01_restart_probe\"}",
                true,
                "{\"WorkbookName\":\"test.xlsx\",\"WorksheetName\":\"_wip_01_restart_probe\",\"CodeName\":\"\"}",
                isWriteTool: true,
                executionStepId: stepId);
            ledger.RecordToolCall(
                "excel_cleanup_workspace",
                "{\"prefix\":\"_wip_01_restart_probe\",\"dryRun\":true}",
                true,
                "{\"prefix\":\"_wip_01_restart_probe\","
                    + "\"matchedSheets\":[\"_wip_01_restart_probe\"],"
                    + "\"removedSheets\":[],\"dryRun\":true}",
                isWriteTool: false,
                executionStepId: stepId);

            Assert.IsTrue(ledger.HasPassingVerificationForCurrentCall("excel_cleanup_workspace"));
            Assert.IsTrue(ledger.HasPassingVerificationForStep(stepId));
            Assert.IsFalse(ledger.HasExcelDataToolCalls);
        }

        [TestMethod]
        public void WipCleanupDryRun_DoesNotVerifyAnUnrelatedOrMutatingStage()
        {
            const string stepId = "mutation:structural-transform";
            var unrelatedLedger = new VerificationLedger();
            unrelatedLedger.RecordToolCall(
                "excel_create_worksheet",
                "{\"worksheetName\":\"_wip_01_expected\"}",
                true,
                "{\"WorksheetName\":\"_wip_01_expected\"}",
                isWriteTool: true,
                executionStepId: stepId);
            unrelatedLedger.RecordToolCall(
                "excel_cleanup_workspace",
                "{\"prefix\":\"_wip_01_expected\",\"dryRun\":true}",
                true,
                "{\"matchedSheets\":[\"_wip_01_other\"],\"removedSheets\":[],\"dryRun\":true}",
                executionStepId: stepId);
            Assert.IsFalse(unrelatedLedger.HasPassingVerificationForCurrentCall("excel_cleanup_workspace"));
            Assert.IsFalse(unrelatedLedger.HasPassingVerificationForStep(stepId));

            var mutatingLedger = new VerificationLedger();
            mutatingLedger.RecordToolCall(
                "excel_create_worksheet",
                "{\"worksheetName\":\"_wip_01_expected\"}",
                true,
                "{\"WorksheetName\":\"_wip_01_expected\"}",
                isWriteTool: true,
                executionStepId: stepId);
            mutatingLedger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"_wip_01_expected\",\"address\":\"A1\"}",
                true,
                "{}",
                isWriteTool: true,
                executionStepId: stepId);
            mutatingLedger.RecordToolCall(
                "excel_cleanup_workspace",
                "{\"prefix\":\"_wip_01_expected\",\"dryRun\":true}",
                true,
                "{\"matchedSheets\":[\"_wip_01_expected\"],\"removedSheets\":[],\"dryRun\":true}",
                executionStepId: stepId);

            Assert.IsFalse(mutatingLedger.HasPassingVerificationForCurrentCall("excel_cleanup_workspace"));
            Assert.IsFalse(mutatingLedger.HasPassingVerificationForStep(stepId));
        }

        [TestMethod]
        public void WipCleanupReadback_VerifiesRemovedWorksheetsAreAbsent()
        {
            const string stepId = "mutation:structural-transform";
            const string worksheetName = "_wip_01_restart_probe";
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_cleanup_workspace",
                "{\"prefix\":\"" + worksheetName + "\",\"dryRun\":false}",
                true,
                "{\"prefix\":\"" + worksheetName + "\","
                    + "\"matchedSheets\":[\"" + worksheetName + "\"],"
                    + "\"removedSheets\":[\"" + worksheetName + "\"],\"dryRun\":false}",
                isWriteTool: true,
                executionStepId: stepId);
            ledger.RecordToolCall(
                "excel_list_worksheets",
                "{}",
                true,
                "{\"WorkbookName\":\"test.xlsx\","
                    + "\"Worksheets\":[{\"Name\":\"Monthly\",\"Index\":1}],"
                    + "\"TotalWorksheetCount\":1,\"IsTruncated\":false}",
                executionStepId: "verify:structural-transform",
                verificationTargetStepId: stepId);

            Assert.IsTrue(ledger.HasPassingVerificationForCurrentCall("excel_list_worksheets"));
            Assert.IsTrue(ledger.HasPassingVerificationForStep(stepId));
            Assert.IsFalse(ledger.HasExcelDataToolCalls);
        }

        [TestMethod]
        public void WipCleanupReadback_RejectsRemainingOrTruncatedWorksheetLists()
        {
            const string stepId = "mutation:structural-transform";
            const string worksheetName = "_wip_01_restart_probe";

            var remainingLedger = new VerificationLedger();
            remainingLedger.RecordToolCall(
                "excel_cleanup_workspace",
                "{\"prefix\":\"" + worksheetName + "\",\"dryRun\":false}",
                true,
                "{\"matchedSheets\":[\"" + worksheetName + "\"],"
                    + "\"removedSheets\":[\"" + worksheetName + "\"],\"dryRun\":false}",
                isWriteTool: true,
                executionStepId: stepId);
            remainingLedger.RecordToolCall(
                "excel_list_worksheets",
                "{}",
                true,
                "{\"Worksheets\":[{\"Name\":\"Monthly\"},"
                    + "{\"Name\":\"" + worksheetName + "\"}],"
                    + "\"TotalWorksheetCount\":2,\"IsTruncated\":false}",
                executionStepId: "verify:structural-transform",
                verificationTargetStepId: stepId);
            Assert.IsFalse(remainingLedger.HasPassingVerificationForCurrentCall("excel_list_worksheets"));
            Assert.IsFalse(remainingLedger.HasPassingVerificationForStep(stepId));

            var truncatedLedger = new VerificationLedger();
            truncatedLedger.RecordToolCall(
                "excel_cleanup_workspace",
                "{\"prefix\":\"" + worksheetName + "\",\"dryRun\":false}",
                true,
                "{\"matchedSheets\":[\"" + worksheetName + "\"],"
                    + "\"removedSheets\":[\"" + worksheetName + "\"],\"dryRun\":false}",
                isWriteTool: true,
                executionStepId: stepId);
            truncatedLedger.RecordToolCall(
                "excel_list_worksheets",
                "{}",
                true,
                "{\"Worksheets\":[{\"Name\":\"Monthly\"}],"
                    + "\"TotalWorksheetCount\":2,\"IsTruncated\":true}",
                executionStepId: "verify:structural-transform",
                verificationTargetStepId: stepId);
            Assert.IsFalse(truncatedLedger.HasPassingVerificationForCurrentCall("excel_list_worksheets"));
            Assert.IsFalse(truncatedLedger.HasPassingVerificationForStep(stepId));
        }

        [TestMethod]
        public void VbaOutput_UsesDeclaredRangeAndTargetedVerificationForFinalTrust()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_execute_vba",
                "{\"outputSheet\":\"_wip_01_long\",\"declaredWriteTargets\":["
                    + "{\"sheet\":\"_wip_01_long\",\"range\":\"A1:D6001\"}]}",
                true,
                "{}",
                isWriteTool: true,
                executionStepId: "mutation:build");
            ledger.RecordToolCall(
                "excel_execute_vba",
                "{\"outputSheet\":\"Long\",\"declaredWriteTargets\":["
                    + "{\"sheet\":\"Long\",\"range\":\"A1:D6001\"}]}",
                true,
                "{}",
                isWriteTool: true,
                executionStepId: "mutation:copy");
            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":["
                    + "{\"target\":{\"worksheetName\":\"_wip_01_long\","
                    + "\"address\":\"A1:D6001\"}},"
                    + "{\"target\":{\"worksheetName\":\"Long\","
                    + "\"address\":\"A1:D6001\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 6000, "_wip_01_long"),
                verificationTargetStepId: "verification:later");

            var summary = ledger.GetSummary();

            Assert.IsFalse(ledger.HasPassingVerificationForStep("mutation:build"));
            Assert.IsFalse(ledger.HasPassingVerificationForStep("mutation:copy"));
            Assert.AreEqual("A", summary.Tier);
            Assert.AreEqual(2, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
        }

        [TestMethod]
        public void VbaOutput_VerificationForDifferentRangeDoesNotCoverIt()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_execute_vba",
                "{\"outputSheet\":\"_wip_01_long\",\"declaredWriteTargets\":["
                    + "{\"sheet\":\"_wip_01_long\",\"range\":\"A1:D6001\"}]}",
                true,
                "{}",
                isWriteTool: true,
                executionStepId: "mutation:build");
            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"_wip_01_long\","
                    + "\"address\":\"A1:C6001\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 6000, "_wip_01_long"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(0, summary.CoveredOutputItemCount);
            Assert.AreEqual(1, summary.UncoveredOutputItemCount);
        }

        [TestMethod]
        public void WriteStage_AssertionForDifferentAddressDoesNotCoverOutput()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Output\",\"address\":\"A1:B2\"}",
                true,
                "{}",
                isWriteTool: true,
                executionStepId: "mutation:range");
            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"Output\","
                    + "\"address\":\"C1:D2\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 2, "Output"),
                verificationTargetStepId: "mutation:range");

            Assert.IsFalse(ledger.HasPassingVerificationForStep("mutation:range"));
            Assert.AreEqual("C", ledger.GetSummary().Tier);
            Assert.AreEqual(0, ledger.GetSummary().CoveredOutputItemCount);
        }

        [TestMethod]
        public void FormulaValidation_CoversOnlyMatchingWriteStageAndRange()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_set_range_formulas",
                "{\"worksheetName\":\"Output\",\"address\":\"H2:H4\"}",
                true,
                "{}",
                isWriteTool: true,
                executionStepId: "mutation:formulas");
            ledger.RecordToolCall(
                "excel_validate_range_formulas",
                "{\"worksheetName\":\"Output\",\"address\":\"H2:H4\"}",
                true,
                "{\"WorksheetName\":\"Output\",\"Address\":\"H2:H4\","
                    + "\"IsValid\":true,\"FormulaCount\":3,\"ValidCount\":3,"
                    + "\"ErrorCount\":0,\"IsTruncated\":false}",
                verificationTargetStepId: "mutation:formulas");

            Assert.IsTrue(ledger.HasPassingVerificationForStep("mutation:formulas"));
            Assert.AreEqual("B", ledger.GetSummary().Tier);
            Assert.AreEqual(1, ledger.GetSummary().CoveredOutputItemCount);
        }

        [TestMethod]
        public void FormulaValidationForWrongRange_DoesNotCoverWriteStage()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_set_range_formulas",
                "{\"worksheetName\":\"Output\",\"address\":\"H2:H4\"}",
                true,
                "{}",
                isWriteTool: true,
                executionStepId: "mutation:formulas");
            ledger.RecordToolCall(
                "excel_validate_range_formulas",
                "{\"worksheetName\":\"Output\",\"address\":\"H2:H5\"}",
                true,
                "{\"WorksheetName\":\"Output\",\"Address\":\"H2:H5\","
                    + "\"IsValid\":true,\"FormulaCount\":4,\"ValidCount\":4,"
                    + "\"ErrorCount\":0,\"IsTruncated\":false}",
                verificationTargetStepId: "mutation:formulas");

            Assert.IsFalse(ledger.HasPassingVerificationForStep("mutation:formulas"));
            Assert.AreEqual("C", ledger.GetSummary().Tier);
            Assert.AreEqual(0, ledger.GetSummary().CoveredOutputItemCount);
        }

        [TestMethod]
        public void KeySetComparison_IsRegisteredAsAReadOnlyVerifiedArtifact()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_compare_key_sets",
                "{\"target\":{\"tableName\":\"Orders\"},"
                    + "\"source\":{\"tableName\":\"Customers\"}}",
                true,
                CreateResult("A", true, true, false, 0, 40, "Orders"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("A", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
            Assert.AreEqual(1, summary.VerificationCount);
            Assert.AreEqual(0, summary.AssertionCount);
            Assert.IsTrue(summary.HasKeySetDifferenceCalculation);
        }

        [TestMethod]
        public void PassingVerificationCanBeQueriedForCurrentToolCallOnly()
        {
            var ledger = new VerificationLedger();
            const string arguments = "{\"target\":{\"tableName\":\"Orders\"},"
                + "\"source\":{\"tableName\":\"Customers\"}}";
            ledger.RecordToolCall(
                "excel_compare_key_sets",
                arguments,
                true,
                CreateResult("A", true, true, false, 0, 40, "Orders"));

            Assert.IsTrue(ledger.HasPassingVerificationForCurrentCall("excel_compare_key_sets"));

            ledger.RecordToolCall(
                "excel_compare_key_sets",
                arguments,
                true,
                "{}");

            Assert.IsFalse(ledger.HasPassingVerificationForCurrentCall("excel_compare_key_sets"));
        }

        [TestMethod]
        public void CompleteMultiSourceProfile_IsAReadOnlyTierBResult()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_profile_range",
                "{\"sources\":[{\"worksheetName\":\"Jan\",\"address\":\"A1:B3\"},"
                    + "{\"worksheetName\":\"Feb\",\"address\":\"A1:B3\"}],\"headerMode\":\"yes\"}",
                true,
                "{\"topology\":\"multiSource\",\"rowCount\":4,\"isTruncated\":false,"
                    + "\"coverage\":{\"totalRows\":4,\"scannedRows\":4},"
                    + "\"sourceProfiles\":["
                    + "{\"worksheetName\":\"Jan\",\"isTruncated\":false,"
                    + "\"coverage\":{\"totalRows\":2,\"scannedRows\":2}},"
                    + "{\"worksheetName\":\"Feb\",\"isTruncated\":false,"
                    + "\"coverage\":{\"totalRows\":2,\"scannedRows\":2}}],"
                    + "\"headerConsistency\":{\"namesConsistent\":true,\"orderConsistent\":false}}");

            var summary = ledger.GetSummary();

            Assert.IsTrue(ledger.HasPassingVerificationForCurrentCall("excel_profile_range"));
            Assert.AreEqual("B", summary.Tier);
            Assert.AreEqual(0, summary.OutputItemCount);
            Assert.AreEqual(0, summary.CoveredOutputItemCount);
            Assert.AreEqual(4L, summary.CoveredRows);
        }

        [TestMethod]
        public void IncompleteMultiSourceProfile_DoesNotSatisfyReadOnlyVerification()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_profile_range",
                "{\"sources\":[{\"worksheetName\":\"Jan\",\"address\":\"A1:B3\"}],"
                    + "\"headerMode\":\"yes\"}",
                true,
                "{\"topology\":\"multiSource\",\"rowCount\":2,\"isTruncated\":false,"
                    + "\"coverage\":{\"totalRows\":2,\"scannedRows\":2},"
                    + "\"sourceProfiles\":[{\"worksheetName\":\"Jan\",\"isTruncated\":false,"
                    + "\"coverage\":{\"totalRows\":3,\"scannedRows\":2}}]}");

            Assert.IsFalse(ledger.HasPassingVerificationForCurrentCall("excel_profile_range"));
            Assert.AreEqual("C", ledger.GetSummary().Tier);
        }

        [TestMethod]
        public void CompleteUnionResultWithoutIndependentCheck_CanBeReturnedWithTierC()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_aggregate",
                "{\"sources\":[{\"worksheetName\":\"Jan\",\"address\":\"A1:B4\"},"
                    + "{\"worksheetName\":\"Feb\",\"address\":\"A1:B4\"}],"
                    + "\"measures\":[{\"column\":\"Revenue\",\"function\":\"sum\"}]}",
                true,
                CreateUnverifiedAggregateResult(
                    "多源或 lookup 只读结果没有已写入的结果范围可供独立复核；不得调用 excel_assert 为对话结果补造验证，应报告完整扫描结果并保留 C 档标记。"));

            Assert.IsFalse(ledger.HasPassingVerificationForCurrentCall("excel_aggregate"));
            Assert.IsTrue(ledger.HasCompleteReadOnlyResultForCurrentCall("excel_aggregate"));
            Assert.AreEqual("C", ledger.GetSummary().Tier);
        }

        [TestMethod]
        public void PassingLaterUnionAssertion_CoversCompleteUnverifiedAggregate()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_aggregate",
                CreateUnionAggregateArguments(),
                true,
                CreateUnverifiedAggregateResult("等待目标范围进行独立复核。"));
            ledger.RecordToolCall(
                "excel_assert",
                CreateUnionAssertionArguments("Revenue", "sum"),
                true,
                CreateUnionAssertionResult("A", false));
            ledger.RecordToolCall(
                "excel_assert",
                CreateUnionAssertionArguments("Revenue", "sum"),
                true,
                CreateUnionAssertionResult("A", true));

            var summary = ledger.GetSummary();

            Assert.AreEqual("A", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
            Assert.AreEqual(3, summary.VerificationCount);
            Assert.AreEqual(1, summary.AssertionCount);
        }

        [TestMethod]
        public void UnionAssertionWithDifferentSourcesOrMeasure_DoesNotCoverAggregate()
        {
            var differentSourcesLedger = new VerificationLedger();
            differentSourcesLedger.RecordToolCall(
                "excel_aggregate",
                CreateUnionAggregateArguments(),
                true,
                CreateUnverifiedAggregateResult("等待目标范围进行独立复核。"));
            differentSourcesLedger.RecordToolCall(
                "excel_assert",
                CreateUnionAssertionArguments("Revenue", "sum", includeMarch: false),
                true,
                CreateUnionAssertionResult("A", true, includeMarch: false));

            var differentMeasureLedger = new VerificationLedger();
            differentMeasureLedger.RecordToolCall(
                "excel_aggregate",
                CreateUnionAggregateArguments(),
                true,
                CreateUnverifiedAggregateResult("等待目标范围进行独立复核。"));
            differentMeasureLedger.RecordToolCall(
                "excel_assert",
                CreateUnionAssertionArguments("Revenue", "count"),
                true,
                CreateUnionAssertionResult("A", true));

            Assert.AreEqual("C", differentSourcesLedger.GetSummary().Tier);
            Assert.AreEqual("C", differentMeasureLedger.GetSummary().Tier);
        }

        [TestMethod]
        public void FailedAggregateAssertion_IsNotEligibleAsUnverifiedReadOnlyResult()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_aggregate",
                "{\"worksheetName\":\"Sales\",\"address\":\"A1:B4\","
                    + "\"measures\":[{\"column\":\"Revenue\",\"function\":\"sum\"}]}",
                true,
                "{\"isTruncated\":false,\"coverage\":{\"totalRows\":3,\"scannedRows\":3},"
                    + "\"rows\":[[\"100\"]],\"verification\":{\"tier\":\"C\",\"passed\":false,"
                    + "\"assertionCount\":1,\"coveredRows\":3,\"independent\":false,"
                    + "\"sampled\":false,\"producer\":\"managed\",\"notes\":["
                    + "\"验证 target 不受任何计算后端支持\"],"
                    + "\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}}}");

            Assert.IsFalse(ledger.HasCompleteReadOnlyResultForCurrentCall("excel_aggregate"));
        }

        [TestMethod]
        public void CompleteHiddenRowsScan_IsRegisteredAsItsOwnVerifiedArtifact()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_get_range_info",
                "{\"worksheetName\":\"Sales\",\"address\":\"A2:C10\","
                    + "\"includeHiddenRows\":true}",
                true,
                "{\"worksheetName\":\"Sales\",\"hiddenRowsScanComplete\":true,"
                    + "\"hiddenRowsScanned\":9,\"hiddenRowCount\":2,"
                    + "\"hiddenRowNumbers\":[4,7],"
                    + "\"verification\":{\"tier\":\"A\",\"passed\":true,"
                    + "\"assertionCount\":0,\"coveredRows\":9,\"independent\":true,"
                    + "\"sampled\":false,\"producer\":\"managed\","
                    + "\"operation\":\"hiddenRowsScan\",\"notes\":[],"
                    + "\"details\":{\"items\":[],\"diffs\":[],\"diffCount\":0}}}");

            var summary = ledger.GetSummary();

            Assert.AreEqual("A", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(1, summary.VerificationCount);
            Assert.AreEqual(0, summary.AssertionCount);
            Assert.AreEqual(9L, summary.CoveredRows);
            Assert.IsTrue(summary.HasHiddenRowsScan);
        }

        [TestMethod]
        public void HiddenRowsScanWithoutCompleteConsistentPayload_RemainsTierC()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_get_range_info",
                "{\"worksheetName\":\"Sales\",\"address\":\"A2:C10\","
                    + "\"includeHiddenRows\":true}",
                true,
                "{\"worksheetName\":\"Sales\",\"hiddenRowsScanComplete\":false,"
                    + "\"hiddenRowsScanned\":0,\"hiddenRowCount\":0,"
                    + "\"hiddenRowNumbers\":[],"
                    + "\"verification\":{\"tier\":\"A\",\"passed\":true,"
                    + "\"assertionCount\":0,\"coveredRows\":9,\"independent\":true,"
                    + "\"sampled\":false,\"producer\":\"managed\","
                    + "\"operation\":\"hiddenRowsScan\",\"notes\":[],"
                    + "\"details\":{\"items\":[],\"diffs\":[],\"diffCount\":0}}}");

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(0, summary.CoveredOutputItemCount);
        }

        [TestMethod]
        public void RangeInfoWithoutHiddenRowsOptIn_DoesNotCreateAnArtifact()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_get_range_info",
                "{\"worksheetName\":\"Sales\",\"address\":\"A1:C10\"}",
                true,
                "{\"worksheetName\":\"Sales\",\"rowCount\":10}");

            var summary = ledger.GetSummary();

            Assert.IsTrue(summary.HasExcelDataToolCalls);
            Assert.AreEqual(0, summary.OutputItemCount);
            Assert.AreEqual(0, summary.VerificationCount);
        }

        [TestMethod]
        public void FormatWrite_IsCoveredOnlyByMatchingStyleReadback()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_set_range_format",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:N1\",\"bold\":true}",
                true,
                "{}",
                true);
            ledger.RecordToolCall(
                "excel_get_range_style",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:N1\"}",
                true,
                "{\"WorksheetName\":\"Monthly\",\"Address\":\"A1:N1\","
                    + "\"CellCount\":14,\"Bold\":\"true\",\"MixedProperties\":\"\"}");

            var summary = ledger.GetSummary();

            Assert.IsTrue(summary.HasExcelDataToolCalls);
            Assert.AreEqual("B", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
            Assert.AreEqual(1, summary.VerificationCount);
            Assert.AreEqual(1, summary.AssertionCount);
            Assert.AreEqual(1L, summary.CoveredRows);
        }

        [TestMethod]
        public void MatchingFormatReadback_IsPassingEvidenceForCurrentCall()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_set_range_format",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:N1\",\"bold\":true}",
                true,
                "{}",
                isWriteTool: true,
                executionStepId: "mutation:format");
            ledger.RecordToolCall(
                "excel_get_range_style",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:N1\"}",
                true,
                "{\"WorksheetName\":\"Monthly\",\"Address\":\"A1:N1\","
                    + "\"CellCount\":14,\"Bold\":\"true\",\"MixedProperties\":\"\"}",
                executionStepId: "mutation:format");

            Assert.IsTrue(ledger.HasPassingVerificationForCurrentCall("excel_get_range_style"));
        }

        [TestMethod]
        public void FormatWriteWithMismatchedReadback_RemainsUnverified()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_set_range_format",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:N1\",\"bold\":true}",
                true,
                "{}",
                true);
            ledger.RecordToolCall(
                "excel_get_range_style",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:N1\"}",
                true,
                "{\"WorksheetName\":\"Monthly\",\"Address\":\"A1:N1\","
                    + "\"CellCount\":14,\"Bold\":\"false\",\"MixedProperties\":\"\"}");

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(0, summary.CoveredOutputItemCount);
            Assert.AreEqual(1, summary.UncoveredOutputItemCount);
        }

        [TestMethod]
        public void FormatReadbackForDifferentAddress_DoesNotCoverTheWrite()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_set_range_format",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:N1\",\"bold\":true}",
                true,
                "{}",
                true);
            ledger.RecordToolCall(
                "excel_get_range_style",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A2:N2\"}",
                true,
                "{\"WorksheetName\":\"Monthly\",\"Address\":\"A2:N2\","
                    + "\"CellCount\":14,\"Bold\":\"true\",\"MixedProperties\":\"\"}");

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(0, summary.CoveredOutputItemCount);
            Assert.AreEqual(1, summary.UncoveredOutputItemCount);
        }

        [TestMethod]
        public void FormatPropertyWithoutReadableStyleField_DoesNotCountAsVerified()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_set_range_format",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:N1\","
                    + "\"bold\":true,\"wrapText\":true}",
                true,
                "{}",
                true);
            ledger.RecordToolCall(
                "excel_get_range_style",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:N1\"}",
                true,
                "{\"WorksheetName\":\"Monthly\",\"Address\":\"A1:N1\","
                    + "\"CellCount\":14,\"Bold\":\"true\",\"MixedProperties\":\"\"}");

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(0, summary.CoveredOutputItemCount);
            Assert.AreEqual(1, summary.UncoveredOutputItemCount);
        }

        [TestMethod]
        public void FailedAggregateAttempt_DoesNotPoisonLaterVerifiedAggregate()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_aggregate",
                "{\"worksheetName\":\"Orders\",\"measures\":[{\"column\":\"WrongHeader\",\"function\":\"sum\"}]}",
                false,
                "{}");
            ledger.RecordToolCall(
                "excel_aggregate",
                "{\"worksheetName\":\"Orders\",\"measures\":[{\"column\":\"Amount\",\"function\":\"sum\"}]}",
                true,
                CreateResult("A", true, true, false, 1, 1000, "Orders"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("A", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
        }

        [TestMethod]
        public void FailedAggregateWithoutSuccessfulResult_RemainsTierC()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_aggregate",
                "{\"worksheetName\":\"Orders\",\"measures\":[{\"column\":\"WrongHeader\",\"function\":\"sum\"}]}",
                false,
                "{}");

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(0, summary.OutputItemCount);
        }

        [TestMethod]
        public void WriteIsCoveredOnlyByLaterVerificationOfSameWorksheet()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"_wip_01_Result\",\"address\":\"A1:B5\"}",
                true,
                "{}");
            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"_wip_01_Result\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 5, "Source"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("A", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
            Assert.AreEqual(1, summary.VerificationCount);
        }

        [TestMethod]
        public void CoverageSummaryCountsCoveredAndUncoveredOutputItems()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"_wip_01_Covered\"}",
                true,
                "{}");
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"_wip_02_Uncovered\"}",
                true,
                "{}");
            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"_wip_01_Covered\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 5, "Source"));

            var summary = ledger.GetSummary();

            Assert.AreEqual(2, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(1, summary.UncoveredOutputItemCount);
        }

        [TestMethod]
        public void VerificationForDifferentWorksheet_DoesNotCoverWrite()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"_wip_01_Result\"}",
                true,
                "{}");
            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"Sales\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 5, "Sales"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(0, summary.CoveredOutputItemCount);
            Assert.AreEqual(1, summary.UncoveredOutputItemCount);
            StringAssert.Contains(summary.PrimaryReason, "_wip_01_Result");
        }

        [TestMethod]
        public void LatestFailedVerification_DowngradesPreviouslyVerifiedOutput()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_execute_vba",
                "{\"outputSheet\":\"_wip_02_Summary\"}",
                true,
                "{}");
            ledger.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"_wip_02_Summary\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 10, "Source"));
            ledger.RecordToolCall(
                "excel_reconcile",
                "{\"summaryWorksheetName\":\"_wip_02_Summary\"}",
                true,
                CreateResult("C", false, false, false, 1, 10, "Source"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);
            StringAssert.Contains(summary.PrimaryReason, "未通过");
        }

        [TestMethod]
        public void SampledOrNonIndependentEvidence_CannotReceiveTierA()
        {
            var sampledLedger = CreateVbaLedger(sampled: true, independent: true);
            var sameBackendLedger = CreateVbaLedger(sampled: false, independent: false);

            Assert.AreEqual("B", sampledLedger.GetSummary().Tier);
            Assert.AreEqual("B", sameBackendLedger.GetSummary().Tier);
        }

        [TestMethod]
        public void FailedWriteAndMissingEnvelope_RemainTierC()
        {
            var failedWrite = new VerificationLedger();
            failedWrite.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Output\"}",
                false,
                "{}");
            failedWrite.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"Output\"}}]}",
                true,
                CreateResult("A", true, true, false, 1, 1, "Source"));

            var missingEnvelope = new VerificationLedger();
            missingEnvelope.RecordToolCall("excel_aggregate", "{}", true, "{\"rows\":[]}");

            var incompleteEnvelope = new VerificationLedger();
            incompleteEnvelope.RecordToolCall(
                "excel_write_range",
                "{\"worksheetName\":\"Output\"}",
                true,
                "{}");
            incompleteEnvelope.RecordToolCall(
                "excel_assert",
                "{\"assertions\":[{\"target\":{\"worksheetName\":\"Output\"}}]}",
                true,
                "{\"verification\":{\"tier\":\"A\",\"passed\":true}}");

            Assert.AreEqual("C", failedWrite.GetSummary().Tier);
            StringAssert.Contains(failedWrite.GetSummary().PrimaryReason, "未成功");
            Assert.AreEqual("C", missingEnvelope.GetSummary().Tier);
            StringAssert.Contains(missingEnvelope.GetSummary().PrimaryReason, "canonical verification");
            Assert.AreEqual(0, incompleteEnvelope.GetSummary().CoveredOutputItemCount);
            Assert.AreEqual(1, incompleteEnvelope.GetSummary().UncoveredOutputItemCount);
        }

        [TestMethod]
        public void EscalatedWriteBlockedBeforeExecution_IsSupersededByVerifiedRetry()
        {
            const string writeArguments =
                "{\"worksheetName\":\"Results\",\"address\":\"A3:B3\","
                + "\"values\":[[\"EscalationProbe\",\"999\"]]}";
            const string blockedWriteResult =
                "{\"writeEffect\":{\"actualWriteEffect\":\"append\","
                + "\"writeSucceeded\":false,\"targetWorkbookFullName\":\"C:\\\\reports\\\\test.xlsx\"}}";
            const string successfulWriteResult =
                "{\"WorkbookName\":\"test.xlsx\",\"WorksheetName\":\"Results\","
                + "\"Address\":\"A3:B3\",\"writeEffect\":{\"actualWriteEffect\":\"modify\","
                + "\"writeSucceeded\":true,\"targetWorkbookFullName\":\"C:\\\\reports\\\\test.xlsx\"}}";
            const string assertionArguments =
                "{\"basis\":{\"topology\":\"single\",\"headerMode\":\"no\","
                + "\"source\":{\"worksheetName\":\"Results\",\"address\":\"A3:B3\"}},"
                + "\"assertions\":[{\"kind\":\"nonBlank\","
                + "\"target\":{\"worksheetName\":\"Results\",\"address\":\"A3:B3\","
                + "\"columns\":[\"A\",\"B\"]},\"expectedValue\":2}]}";

            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_write_range",
                writeArguments,
                false,
                blockedWriteResult,
                isWriteTool: true,
                errorCode: "WRITE_EFFECT_ESCALATED");
            ledger.RecordToolCall(
                "excel_write_range",
                writeArguments,
                true,
                successfulWriteResult,
                isWriteTool: true);
            ledger.RecordToolCall(
                "excel_assert",
                assertionArguments,
                true,
                CreateResult("A", true, true, false, 1, 2, "Results"));

            var summary = ledger.GetSummary();

            Assert.AreEqual("A", summary.Tier);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual(1, summary.CoveredOutputItemCount);
            Assert.AreEqual(0, summary.UncoveredOutputItemCount);

            var unresolvedEscalation = new VerificationLedger();
            unresolvedEscalation.RecordToolCall(
                "excel_write_range",
                writeArguments,
                false,
                blockedWriteResult,
                isWriteTool: true,
                errorCode: "WRITE_EFFECT_ESCALATED");

            Assert.AreEqual("C", unresolvedEscalation.GetSummary().Tier);
            StringAssert.Contains(unresolvedEscalation.GetSummary().PrimaryReason, "未成功");
        }

        [TestMethod]
        public void OtherExcelDataWrites_AreTrackedAndFormattingRequiresReadback()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_file_write_range",
                "{\"filePath\":\"output.xlsx\"}",
                true,
                "{}",
                isWriteTool: true);

            var summary = ledger.GetSummary();

            Assert.IsTrue(summary.HasExcelDataToolCalls);
            Assert.AreEqual(1, summary.OutputItemCount);
            Assert.AreEqual("C", summary.Tier);
            StringAssert.Contains(summary.PrimaryReason, "目标工作表未明确");

            var formattingLedger = new VerificationLedger();
            formattingLedger.RecordToolCall(
                "excel_set_range_format",
                "{\"worksheetName\":\"Summary\",\"address\":\"A1:B5\"}",
                true,
                "{}",
                isWriteTool: true);

            Assert.IsTrue(formattingLedger.GetSummary().HasExcelDataToolCalls);
            Assert.AreEqual(1, formattingLedger.GetSummary().OutputItemCount);
            Assert.AreEqual("C", formattingLedger.GetSummary().Tier);
        }

        [TestMethod]
        public void SuccessfulDirectReadOnlyResults_AreScopedWithoutBeingReportedAsUnverifiedOutputs()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall("excel_list_worksheets", "{}", true, "{}");
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A2\"}",
                true,
                CreateSingleCellReadback("Monthly", "A2", "S0001"),
                executionStepId: "Inspect current workbook");
            ledger.RecordToolCall("excel_list_backups", "{}", true, "{}");

            var summary = ledger.GetSummary();

            Assert.IsTrue(summary.HasExcelDataToolCalls);
            Assert.AreEqual("B", summary.Tier);
            Assert.AreEqual(0, summary.OutputItemCount);
            Assert.AreEqual(0, summary.VerificationCount);
            StringAssert.Contains(summary.PrimaryReason, "只读数据由 Excel 工具直接返回");
            CollectionAssert.AreEqual(
                new[]
                {
                    "工作表清单",
                    "读取 Monthly!A2（步骤：Inspect current workbook）",
                    "备份清单"
                },
                summary.ScopeDescriptions.ToArray());
        }

        [TestMethod]
        public void FailedDirectReadOnlyResult_RemainsTierC()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A2\"}",
                false,
                "{}");

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            StringAssert.Contains(summary.PrimaryReason, "只读数据读取未成功");
            StringAssert.Contains(summary.ScopeDescriptions.Single(), "Monthly!A2");
        }

        [TestMethod]
        public void TruncatedDirectReadOnlyResult_RemainsTierC()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_read_range",
                "{\"worksheetName\":\"Monthly\",\"address\":\"A1:B500\"}",
                true,
                "{\"IsTruncated\":true}");

            var summary = ledger.GetSummary();

            Assert.AreEqual("C", summary.Tier);
            StringAssert.Contains(summary.PrimaryReason, "被截断");
        }

        [TestMethod]
        public void NonExcelTools_DoNotCreateLedgerActivity()
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall("runtime.get_host_context", "{}", true, "{}");

            var summary = ledger.GetSummary();

            Assert.IsFalse(summary.HasExcelDataToolCalls);
            Assert.AreEqual(string.Empty, summary.Tier);
            Assert.AreEqual(0, summary.OutputItemCount);
        }

        private static VerificationLedger CreateVbaLedger(bool sampled, bool independent)
        {
            var ledger = new VerificationLedger();
            ledger.RecordToolCall(
                "excel_execute_vba",
                "{\"outputSheet\":\"_wip_03_Result\"}",
                true,
                CreateResult("A", true, independent, sampled, 1, 20, "Source"));
            return ledger;
        }

        private static string CreateSingleCellReadback(
            string worksheet,
            string address,
            string value,
            bool isTruncated = false,
            string workbook = "test.xlsx")
        {
            return "{\"WorkbookName\":\"" + workbook + "\","
                + "\"WorksheetName\":\"" + worksheet + "\","
                + "\"Address\":\"" + address + "\","
                + "\"RowCount\":1,\"ColumnCount\":1,"
                + "\"IsTruncated\":" + (isTruncated ? "true" : "false") + ","
                + "\"Rows\":[[\"" + value + "\"]]}";
        }

        private static string CreateSingleCellWriteResult(
            string workbook,
            string worksheet,
            string address)
        {
            return "{\"WorkbookName\":\"" + workbook + "\","
                + "\"WorksheetName\":\"" + worksheet + "\","
                + "\"Address\":\"" + address + "\"}";
        }

        private static string CreateResult(
            string tier,
            bool passed,
            bool independent,
            bool sampled,
            int assertionCount,
            long coveredRows,
            string basisWorksheet)
        {
            return "{\"verification\":{"
                + "\"tier\":\"" + tier + "\","
                + "\"passed\":" + (passed ? "true" : "false") + ","
                + "\"assertionCount\":" + assertionCount + ","
                + "\"coveredRows\":" + coveredRows + ","
                + "\"independent\":" + (independent ? "true" : "false") + ","
                + "\"sampled\":" + (sampled ? "true" : "false") + ","
                + "\"producer\":\"vba\","
                + "\"basis\":{\"source\":{\"worksheetName\":\"" + basisWorksheet + "\"}},"
                + "\"notes\":[],"
                + "\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}"
                + "}}";
        }

        private static string CreateUnverifiedAggregateResult(string reason)
        {
            return "{\"isTruncated\":false,"
                + "\"coverage\":{\"totalRows\":6,\"scannedRows\":6},"
                + "\"rows\":[[\"790\",\"9\"]],"
                + "\"verification\":{\"tier\":\"C\",\"passed\":false,"
                + "\"assertionCount\":0,\"coveredRows\":0,\"independent\":false,"
                + "\"sampled\":false,\"producer\":\"managed\",\"notes\":[\""
                + reason
                + "\"],\"details\":{\"items\":[],\"diffs\":[],\"diffCount\":0}}}";
        }

        private static string CreateUnionAggregateArguments()
        {
            return "{\"sources\":["
                + "{\"worksheetName\":\"Jan\",\"address\":\"A1:B4\",\"alias\":\"Jan\"},"
                + "{\"worksheetName\":\"Feb\",\"address\":\"A1:B4\",\"alias\":\"Feb\"},"
                + "{\"worksheetName\":\"Mar\",\"address\":\"A1:B4\",\"alias\":\"Mar\"}],"
                + "\"headerMode\":\"yes\","
                + "\"measures\":[{\"column\":\"Revenue\",\"function\":\"sum\"}]}";
        }

        private static string CreateUnionAssertionArguments(
            string column,
            string function,
            bool includeMarch = true)
        {
            var sources = includeMarch
                ? "{\"sheet\":\"Jan\",\"range\":\"A1:B4\",\"alias\":\"Jan\"},"
                    + "{\"sheet\":\"Feb\",\"range\":\"A1:B4\",\"alias\":\"Feb\"},"
                    + "{\"sheet\":\"Mar\",\"range\":\"A1:B4\",\"alias\":\"Mar\"}"
                : "{\"sheet\":\"Jan\",\"range\":\"A1:B4\",\"alias\":\"Jan\"},"
                    + "{\"sheet\":\"Feb\",\"range\":\"A1:B4\",\"alias\":\"Feb\"},"
                    + "{\"sheet\":\"Apr\",\"range\":\"A1:B4\",\"alias\":\"Apr\"}";
            return "{\"basis\":{\"topology\":\"union\",\"headerMode\":\"yes\","
                + "\"sources\":[" + sources + "]},"
                + "\"assertions\":[{\"kind\":\"scalar\","
                + "\"target\":{\"worksheetName\":\"_wip_01_union_assert\","
                + "\"address\":\"A1:A2\",\"column\":\"" + column
                + "\",\"function\":\"" + function + "\"},"
                + "\"source\":{\"column\":\"" + column
                + "\",\"function\":\"" + function + "\"},"
                + "\"tolerance\":0.01}]}";
        }

        private static string CreateUnionAssertionResult(
            string tier,
            bool passed,
            bool includeMarch = true)
        {
            var sources = includeMarch
                ? "{\"sheet\":\"Jan\",\"range\":\"A1:B4\",\"alias\":\"Jan\"},"
                    + "{\"sheet\":\"Feb\",\"range\":\"A1:B4\",\"alias\":\"Feb\"},"
                    + "{\"sheet\":\"Mar\",\"range\":\"A1:B4\",\"alias\":\"Mar\"}"
                : "{\"sheet\":\"Jan\",\"range\":\"A1:B4\",\"alias\":\"Jan\"},"
                    + "{\"sheet\":\"Feb\",\"range\":\"A1:B4\",\"alias\":\"Feb\"},"
                    + "{\"sheet\":\"Apr\",\"range\":\"A1:B4\",\"alias\":\"Apr\"}";
            return "{\"verification\":{\"tier\":\"" + tier + "\","
                + "\"passed\":" + (passed ? "true" : "false") + ","
                + "\"assertionCount\":1,\"coveredRows\":10,"
                + "\"independent\":true,\"sampled\":false,\"producer\":\"com\","
                + "\"basis\":{\"topology\":\"union\",\"headerMode\":\"yes\","
                + "\"sources\":[" + sources + "]},"
                + "\"notes\":[],\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}}}";
        }
    }
}
