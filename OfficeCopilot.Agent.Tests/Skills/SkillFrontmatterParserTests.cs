using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Skills;

namespace OfficeCopilot.Agent.Tests.Skills
{
    [TestClass]
    public class SkillFrontmatterParserTests
    {
        private static SkillFrontmatter Parse(string yaml)
            => SkillFrontmatterParser.Parse(yaml, "test");

        [TestMethod]
        public void Parse_KebabCamelAndSnakeKeys_AreEquivalent()
        {
            const string list = "  - read_range\n  - write_range\n";

            var kebab = Parse("allowed-tools:\n" + list);
            var camel = Parse("allowedTools:\n" + list);
            var snake = Parse("allowed_tools:\n" + list);

            CollectionAssert.AreEqual(new[] { "read_range", "write_range" }, kebab.AllowedTools);
            CollectionAssert.AreEqual(kebab.AllowedTools, camel.AllowedTools);
            CollectionAssert.AreEqual(kebab.AllowedTools, snake.AllowedTools);
        }

        [TestMethod]
        public void Parse_ScalarValue_IsAcceptedAsSingleItemList()
        {
            var fm = Parse("allowed-tools: read_range\n");

            CollectionAssert.AreEqual(new[] { "read_range" }, fm.AllowedTools);
        }

        [TestMethod]
        public void Parse_MissingAllowedTools_YieldsEmptyList()
        {
            var fm = Parse("name: demo\n");

            Assert.IsNotNull(fm.AllowedTools);
            Assert.AreEqual(0, fm.AllowedTools.Count);
        }

        [TestMethod]
        public void Parse_EnabledFalse_IsRespected()
        {
            var fm = Parse("name: demo\nenabled: false\n");

            Assert.IsFalse(fm.Enabled);
        }

        [TestMethod]
        public void Parse_EnabledAbsent_DefaultsToTrue()
        {
            var fm = Parse("name: demo\n");

            Assert.IsTrue(fm.Enabled);
        }

        [TestMethod]
        public void Parse_InvalidEnabledValue_FallsBackToTrue()
        {
            var fm = Parse("enabled: maybe\n");

            Assert.IsTrue(fm.Enabled);
        }

        [TestMethod]
        public void Parse_AllowVbaFallbackAlias_SetsAllowCodeExecution()
        {
            var fm = Parse("allow-vba-fallback: true\n");

            Assert.IsTrue(fm.AllowCodeExecution);
        }

        [TestMethod]
        public void Parse_CriticalFlags_ReadFromKebabKeys()
        {
            var fm = Parse("allow-high-risk-tools: true\nallow-code-execution: true\n");

            Assert.IsTrue(fm.AllowHighRiskTools);
            Assert.IsTrue(fm.AllowCodeExecution);
        }

        [TestMethod]
        public void Parse_InvalidBool_FallsBackToDefault()
        {
            var fm = Parse("allow-high-risk-tools: perhaps\n");

            Assert.IsFalse(fm.AllowHighRiskTools);
        }

        [TestMethod]
        public void Parse_DisableModelInvocationAndUserInvocable_AreRead()
        {
            var fm = Parse("disable-model-invocation: true\nuser-invocable: false\n");

            Assert.IsTrue(fm.DisableModelInvocation);
            Assert.IsFalse(fm.UserInvocable);
        }

        [TestMethod]
        public void Parse_UserInvocable_DefaultsToTrue()
        {
            var fm = Parse("name: demo\n");

            Assert.IsTrue(fm.UserInvocable);
            Assert.IsFalse(fm.DisableModelInvocation);
        }

        [TestMethod]
        public void Parse_NonPositiveMaxToolRoundCount_FallsBackToFive()
        {
            var fm = Parse("max-tool-round-count: 0\n");

            Assert.AreEqual(5, fm.MaxToolRoundCount);
        }

        [TestMethod]
        public void Parse_DisplayNameAndModel_AreTrimmed()
        {
            var fm = Parse("display-name: '  Demo  '\nmodel: gpt-x\n");

            Assert.AreEqual("Demo", fm.DisplayName);
            Assert.AreEqual("gpt-x", fm.Model);
        }

        [TestMethod]
        public void Normalize_StripsSeparatorsAndLowercases()
        {
            Assert.AreEqual("allowedtools", SkillFrontmatterParser.Normalize("Allowed-Tools"));
            Assert.AreEqual("allowedtools", SkillFrontmatterParser.Normalize("allowed_tools"));
            Assert.AreEqual(string.Empty, SkillFrontmatterParser.Normalize(null));
        }
    }
}
