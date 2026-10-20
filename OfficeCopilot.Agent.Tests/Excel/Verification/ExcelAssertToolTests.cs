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
            StringAssert.Contains(tool.Definition.Description, "单工作表 target");
            StringAssert.Contains(tool.Definition.Description, "basis.measures 定义源侧指标");
            StringAssert.Contains(tool.Definition.Description, "源 Amount 对比输出 Value");
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
                    "target 与 source 分别填写");
                StringAssert.Contains(
                    properties.GetProperty("assertions").GetProperty("items")
                        .GetProperty("properties").GetProperty("target")
                        .GetProperty("description").GetString(),
                    "不会自动沿用 basis.measures");
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
