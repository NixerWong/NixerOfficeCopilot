using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.HostBridge
{
    /// <summary>
    /// 参数级确认豁免（只写 _wip_ 中间表免确认）的判定。该策略从 HostBridge.Server
    /// 上移到 HostBridge.Contracts，供 Broker 与宿主共用，这里锁住它的边界行为。
    /// </summary>
    [TestClass]
    public class StagedWriteConfirmationPolicyTests
    {
        private static string Args(string outputSheet, string code, bool isNewSheet = false)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                outputSheet = outputSheet,
                declaredWriteTargets = new[]
                {
                    new { sheet = outputSheet, isNewSheet = isNewSheet }
                },
                code = code
            });
        }

        [TestMethod]
        public void WipOnlyWrite_IsExempt()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    Dim out As Worksheet\n" +
                "    Set out = ThisWorkbook.Worksheets.Add\n" +
                "    out.Name = \"_wip_01_汇总\"\n" +
                "    out.Cells(1, 1).Value = 5\n" +
                "End Sub",
                isNewSheet: true);

            Assert.IsTrue(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        [TestMethod]
        public void NonWipOutputSheet_IsNotExempt()
        {
            var args = Args("季度报表", "Sub Build()\nEnd Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        [TestMethod]
        public void WritesToNonWipLiteral_IsNotExempt()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    ThisWorkbook.Worksheets(\"Sheet1\").Cells(1, 1).Value = 5\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        /// <summary>
        /// 记录当前已知限制：从源表读取数据（经变量绑定）也会被判定为触碰既有数据而要求确认。
        /// 这是保守实现，宁可多确认也不放过潜在写入。
        /// </summary>
        [TestMethod]
        public void ReadsSourceViaBoundVariable_IsNotExempt()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    Dim ws As Worksheet\n" +
                "    Set ws = ThisWorkbook.Worksheets(\"Sheet1\")\n" +
                "    Dim v As Variant\n" +
                "    v = ws.Cells(1, 1).Value\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        [TestMethod]
        public void NonVbaTool_IsNotExempt()
        {
            var args = Args("_wip_01_汇总", "Sub Build()\nEnd Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_write_range", args));
        }

        [TestMethod]
        public void WithFormalWorksheetBlock_IsNotExempt()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    With ThisWorkbook.Worksheets(\"销售明细\")\n" +
                "        .Range(\"A1\").Value = 5\n" +
                "    End With\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        [TestMethod]
        public void CodeNameReference_IsNotExempt()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    Sheet1.Range(\"A1\").Value = 5\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        [TestMethod]
        public void IndexedWorksheetReferences_AreNotExempt()
        {
            var numericIndexArgs = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    Worksheets(1).Range(\"A1\").Value = 5\n" +
                "End Sub");
            var variableIndexArgs = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    Sheets(sheetIndex).Range(\"A1\").Value = 5\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", numericIndexArgs));
            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", variableIndexArgs));
        }

        [TestMethod]
        public void WorksheetProtectionCalls_AreNotExempt()
        {
            var protectArgs = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    ThisWorkbook.Worksheets(\"_wip_01_汇总\").Protect\n" +
                "End Sub");
            var unprotectArgs = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    ThisWorkbook.Worksheets(\"_wip_01_汇总\").Unprotect\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", protectArgs));
            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", unprotectArgs));
        }

        [TestMethod]
        public void NewlyAddedFormalWorksheet_IsNotExempt()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    Dim output As Worksheet\n" +
                "    Set output = ThisWorkbook.Worksheets.Add\n" +
                "    output.Name = \"季度报表\"\n" +
                "    output.Cells(1, 1).Value = 5\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        [TestMethod]
        public void NewlyAddedWorksheetWrittenBeforeWipRename_IsNotExempt()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    Dim output As Worksheet\n" +
                "    Set output = ThisWorkbook.Worksheets.Add\n" +
                "    output.Cells(1, 1).Value = 5\n" +
                "    output.Name = \"_wip_01_汇总\"\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        [TestMethod]
        public void UnassignedWorksheetAdd_IsNotExempt()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    ThisWorkbook.Worksheets.Add\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        [TestMethod]
        public void WithExplicitWipWorksheetBlock_IsNotStaticallyProvenForDeclaration()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    With ThisWorkbook.Worksheets(\"_wip_01_汇总\")\n" +
                "        .Range(\"A1\").Value = 5\n" +
                "    End With\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }

        [TestMethod]
        public void DeclaredScope_ExplicitWriteToDeclaredWorksheet_IsProvable()
        {
            var code = "Sub Build()\n"
                + "    Dim output As Worksheet\n"
                + "    Set output = ThisWorkbook.Worksheets(\"Summary\")\n"
                + "    output.Cells(1, 1).Value = 5\n"
                + "End Sub";

            Assert.IsTrue(StagedWriteConfirmationPolicy.IsDeclaredVbaWriteScopeProvable(
                code,
                new[] { "Summary" },
                new string[0]));
        }

        [TestMethod]
        public void DeclaredScope_UnqualifiedOrUndeclaredWrites_AreNotProvable()
        {
            var unqualifiedRange = "Sub Build()\nRange(\"A1\").Value = 5\nEnd Sub";
            var undeclaredSheet = "Sub Build()\nThisWorkbook.Worksheets(\"Data\").Range(\"A1\").Value = 5\nEnd Sub";

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsDeclaredVbaWriteScopeProvable(
                unqualifiedRange,
                new[] { "Summary" },
                new string[0]));
            Assert.IsFalse(StagedWriteConfirmationPolicy.IsDeclaredVbaWriteScopeProvable(
                undeclaredSheet,
                new[] { "Summary" },
                new string[0]));
        }

        [TestMethod]
        public void DeclaredScope_NewWorksheetMustBeExplicitlyDeclared()
        {
            var code = "Sub Build()\n"
                + "    Dim output As Worksheet\n"
                + "    Set output = ThisWorkbook.Worksheets.Add\n"
                + "    output.Name = \"Summary\"\n"
                + "    output.Cells(1, 1).Value = 5\n"
                + "End Sub";

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsDeclaredVbaWriteScopeProvable(
                code,
                new[] { "Summary" },
                new string[0]));
            Assert.IsTrue(StagedWriteConfirmationPolicy.IsDeclaredVbaWriteScopeProvable(
                code,
                new[] { "Summary" },
                new[] { "Summary" }));
        }

        [TestMethod]
        public void DeclaredScope_NewWorksheetCannotBeBoundThroughAnExistingSheetLiteral()
        {
            var code = "Sub Build()\n"
                + "    Dim output As Worksheet\n"
                + "    Set output = ThisWorkbook.Worksheets(\"Summary\")\n"
                + "    output.Cells(1, 1).Value = 5\n"
                + "End Sub";

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsDeclaredVbaWriteScopeProvable(
                code,
                new[] { "Summary" },
                new[] { "Summary" }));
        }

        [TestMethod]
        public void WipExemption_RequiresTheDeclaredTargetToMatchOutputSheet()
        {
            var args = Args("_wip_01_汇总",
                "Sub Build()\n" +
                "    ThisWorkbook.Worksheets(\"_wip_02_其他\").Range(\"A1\").Value = 5\n" +
                "End Sub");

            Assert.IsFalse(StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite("excel_execute_vba", args));
        }
    }
}
