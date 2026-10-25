using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Excel.Tools;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class ExcelAssertToolTests
    {
        [TestMethod]
        public void ExcelAssert_IsRegisteredAsReadOnlyDataTool()
        {
            var tool = ExcelBuiltInToolCatalog.CreateDataTools()
                .Single(candidate => candidate.Definition.Name == "excel_assert");

            Assert.IsTrue(tool.Definition.IsReadOnly);
            Assert.IsFalse(tool.Definition.RequiresConfirmation);
            StringAssert.Contains(tool.Definition.Description, "a single-sheet target cannot represent an aggregate");
            StringAssert.Contains(tool.Definition.Description, "basis.measures defines source-side metrics");
            StringAssert.Contains(tool.Definition.Description, "compare source Amount with output Value");
            StringAssert.Contains(tool.Definition.InputSchemaJson, "\"basis\"");
            StringAssert.Contains(tool.Definition.InputSchemaJson, "\"assertions\"");
            using (var schema = JsonDocument.Parse(tool.Definition.InputSchemaJson))
            {
                var properties = schema.RootElement.GetProperty("properties");
                Assert.IsTrue(properties.TryGetProperty("assertions", out _));
                var sideProperties = schema.RootElement.GetProperty("definitions")
                    .GetProperty("side").GetProperty("properties");
                StringAssert.Contains(
                    sideProperties.GetProperty("column").GetProperty("description").GetString(),
                    "specify target and source separately");
                StringAssert.Contains(
                    properties.GetProperty("assertions").GetProperty("items")
                        .GetProperty("properties").GetProperty("target")
                        .GetProperty("description").GetString(),
                    "basis.measures is not inherited automatically");
                Assert.AreEqual(
                    10000,
                    sideProperties.GetProperty("excludeRows").GetProperty("maxItems").GetInt32());
                Assert.AreEqual(
                    "boolean",
                    sideProperties.GetProperty("excludeLabelRows").GetProperty("type").GetString());
                var derivedGroups = properties.GetProperty("basis").GetProperty("properties")
                    .GetProperty("derivedGroupBy").GetProperty("items");
                Assert.IsTrue(derivedGroups.GetProperty("properties")
                    .TryGetProperty("transform", out _));
                var basisProperties = properties.GetProperty("basis").GetProperty("properties");
                Assert.IsTrue(basisProperties.TryGetProperty("sources", out _));
                Assert.AreEqual(
                    2,
                    properties.GetProperty("basis").GetProperty("oneOf").GetArrayLength());
            }
        }
    }
}
