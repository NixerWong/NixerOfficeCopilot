using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.Host.Excel.Tools;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public class ExcelToolSafetyMetadataTests
    {
        [TestMethod]
        public void ScreenshotAndPowerQueryValidation_AreReadOnly()
        {
            var tools = ExcelBuiltInToolCatalog.CreateAllTools();

            Assert.IsTrue(tools.Single(tool => tool.Definition.Name == "excel_capture_worksheet_screenshot").Definition.IsReadOnly);
            Assert.IsTrue(tools.Single(tool => tool.Definition.Name == "excel_validate_power_query").Definition.IsReadOnly);
        }

        [TestMethod]
        public void PowerQueryRefresh_RequiresConfirmation()
        {
            var tool = ExcelBuiltInToolCatalog.CreateAllTools()
                .Single(candidate => candidate.Definition.Name == "excel_refresh_power_query");

            Assert.IsFalse(tool.Definition.IsReadOnly);
            Assert.IsTrue(tool.Definition.RequiresConfirmation);
        }

        [TestMethod]
        public void WorksheetCreation_IsAWriteToolWithAConservativeUpperBound()
        {
            var tool = ExcelBuiltInToolCatalog.CreateAllTools()
                .Single(candidate => candidate.Definition.Name == "excel_create_worksheet");

            Assert.IsFalse(tool.Definition.IsReadOnly);
            Assert.IsTrue(tool.Definition.RequiresConfirmation);
            Assert.AreEqual(ToolWriteEffectLevels.Modify, tool.Definition.MaxWriteEffect);
        }

        [TestMethod]
        public void VbaExecution_RequiresBackupWhenPreflightClassifiesItAsModify()
        {
            var tool = ExcelBuiltInToolCatalog.CreateAllTools()
                .Single(candidate => candidate.Definition.Name == "excel_execute_vba");

            Assert.IsTrue(tool.Definition.RequiresBackup);
        }

        [TestMethod]
        public void ExcelTools_DeclareWriteEffectUpperBounds()
        {
            var tools = ExcelBuiltInToolCatalog.CreateAllTools();

            Assert.IsTrue(tools.All(tool => ToolWriteEffectLevels.IsValid(tool.Definition.MaxWriteEffect)));
            Assert.AreEqual(
                ToolWriteEffectLevels.None,
                tools.Single(tool => tool.Definition.Name == "excel_capture_worksheet_screenshot").Definition.MaxWriteEffect);
            Assert.AreEqual(
                ToolWriteEffectLevels.Modify,
                tools.Single(tool => tool.Definition.Name == "excel_refresh_power_query").Definition.MaxWriteEffect);
        }

        [TestMethod]
        public void InvalidWriteEffectMetadata_FallsBackToModify()
        {
            var definition = new ToolDefinition
            {
                Name = "excel_example_write",
                IsReadOnly = true,
                MaxWriteEffect = "unknown"
            };

            ToolSafetyPolicy.Normalize(definition);

            Assert.AreEqual(ToolWriteEffectLevels.Modify, definition.MaxWriteEffect);
            Assert.IsFalse(definition.IsReadOnly);
            Assert.AreEqual(ToolWriteEffectLevels.Modify, definition.Clone().MaxWriteEffect);
        }

        [TestMethod]
        public void UnclassifiedExternalWriteTool_DefaultsToModify()
        {
            var descriptor = RuntimeToolDescriptors.Create(
                "mcp_unrecognized_write",
                "Unrecognized external write tool.",
                false,
                true,
                false,
                "medium",
                "{}");

            Assert.AreEqual(ToolWriteEffectLevels.Modify, descriptor.MaxWriteEffect);
            Assert.IsFalse(descriptor.IsReadOnly);
            Assert.IsTrue(descriptor.RequiresConfirmation);
        }
    }
}
