using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public class ExcelWriteEffectEvaluatorTests
    {
        [TestMethod]
        public void RegisteredWipTarget_IsScratch()
        {
            AssertEffect(ToolWriteEffectLevels.Scratch, new ExcelWriteEffectInput
            {
                IsRegisteredWipSheet = true,
                IntersectsBaselineUsedRange = true,
                NonEmptyCellCountInIntersection = 12
            });
        }

        [TestMethod]
        public void NewlyCreatedFormalSheet_IsAppend()
        {
            AssertEffect(ToolWriteEffectLevels.Append, new ExcelWriteEffectInput
            {
                IsNewFormalSheetThisTurn = true,
                IsStructuralOperation = true
            });
        }

        [TestMethod]
        public void CreatingRegisteredWipWorksheet_IsScratch()
        {
            AssertEffect(ToolWriteEffectLevels.Scratch, new ExcelWriteEffectInput
            {
                IsNewRegisteredWipWorksheetCreation = true
            });
        }

        [TestMethod]
        public void UnregisteredWipSheet_IsClassifiedLikeAFormalSheet()
        {
            AssertEffect(ToolWriteEffectLevels.Modify, new ExcelWriteEffectInput
            {
                IntersectsBaselineUsedRange = true,
                NonEmptyCellCountInIntersection = 3
            });
        }

        [TestMethod]
        public void OtherWorkbookTarget_IsModifyEvenWhenRegisteredWip()
        {
            AssertEffect(ToolWriteEffectLevels.Modify, new ExcelWriteEffectInput
            {
                IsRegisteredWipSheet = true,
                IsOtherWorkbook = true
            });
        }

        [TestMethod]
        public void StructuralOperationOrContractSource_IsModify()
        {
            AssertEffect(ToolWriteEffectLevels.Modify, new ExcelWriteEffectInput { IsStructuralOperation = true });
            AssertEffect(ToolWriteEffectLevels.Modify, new ExcelWriteEffectInput { IsContractSource = true });
        }

        [TestMethod]
        public void WriteOutsideBaselineUsedRange_IsAppend()
        {
            AssertEffect(ToolWriteEffectLevels.Append, new ExcelWriteEffectInput
            {
                IntersectsBaselineUsedRange = false,
                NonEmptyCellCountInIntersection = 0
            });
        }

        [TestMethod]
        public void EmptyBaselineIntersection_IsAppend()
        {
            AssertEffect(ToolWriteEffectLevels.Append, new ExcelWriteEffectInput
            {
                IntersectsBaselineUsedRange = true,
                NonEmptyCellCountInIntersection = 0
            });
        }

        [TestMethod]
        public void NonEmptyOrUnknownBaselineIntersection_IsModify()
        {
            AssertEffect(ToolWriteEffectLevels.Modify, new ExcelWriteEffectInput
            {
                IntersectsBaselineUsedRange = true,
                NonEmptyCellCountInIntersection = 1
            });
            AssertEffect(ToolWriteEffectLevels.Modify, new ExcelWriteEffectInput
            {
                IntersectsBaselineUsedRange = true
            });
            AssertEffect(ToolWriteEffectLevels.Modify, null);
        }

        [TestMethod]
        public void FormattingInsideBaselineUsedRange_IsModify()
        {
            AssertEffect(ToolWriteEffectLevels.Modify, new ExcelWriteEffectInput
            {
                IntersectsBaselineUsedRange = true,
                IsFormatOperation = true,
                NonEmptyCellCountInIntersection = 0
            });
        }

        [TestMethod]
        public void Evaluation_IncludesTargetAndImpactCountsInSummary()
        {
            var result = ExcelWriteEffectEvaluator.Evaluate(new ExcelWriteEffectInput
            {
                TargetWorkbook = "sales.xlsx",
                TargetWorksheet = "Summary",
                TargetAddress = "A1:C5",
                IntersectsBaselineUsedRange = true,
                NonEmptyCellCountInIntersection = 4,
                FormulaCountInIntersection = 2,
                ListObjectNames = new[] { "SalesTable" },
                PivotTableNames = new[] { "SalesPivot" }
            });

            Assert.AreEqual(ToolWriteEffectLevels.Modify, result.Effect);
            Assert.AreEqual(4, result.OverwriteNonEmptyCellCount);
            Assert.AreEqual(2, result.OverwriteFormulaCount);
            StringAssert.Contains(result.OperationSummary, "Summary");
            StringAssert.Contains(result.OperationSummary, "覆盖非空单元格：4");
            StringAssert.Contains(result.OperationSummary, "其中公式：2");
            StringAssert.Contains(result.OperationSummary, "SalesTable");
            StringAssert.Contains(result.OperationSummary, "SalesPivot");
        }

        private static void AssertEffect(string expected, ExcelWriteEffectInput input)
        {
            Assert.AreEqual(expected, ExcelWriteEffectEvaluator.Evaluate(input).Effect);
        }
    }
}
