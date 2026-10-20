using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Skills;

namespace OfficeCopilot.Agent.Tests.Skills
{
    [TestClass]
    public class SkillDefinitionMapperTests
    {
        private static SkillDefinition Map(string yaml, string body = "Do the thing.")
        {
            var parsed = new ParsedSkillFile
            {
                Frontmatter = SkillFrontmatterParser.Parse(yaml, "test"),
                Body = body,
                FilePath = @"C:\skills\demo\SKILL.md",
            };

            return parsed.ToSkillDefinition();
        }

        [TestMethod]
        public void ToSkillDefinition_EnabledFalse_MapsToIsEnabledFalse()
        {
            var definition = Map("name: demo\nenabled: false\n");

            Assert.IsFalse(definition.IsEnabled);
        }

        [TestMethod]
        public void ToSkillDefinition_EnabledAbsent_MapsToIsEnabledTrue()
        {
            var definition = Map("name: demo\n");

            Assert.IsTrue(definition.IsEnabled);
        }

        [TestMethod]
        public void ToSkillDefinition_NoDisplayName_FallsBackToName()
        {
            var definition = Map("name: demo\n");

            Assert.AreEqual("demo", definition.DisplayName);
        }

        [TestMethod]
        public void ToSkillDefinition_EmptyAllowedTools_LeavesAllowListUnset()
        {
            var definition = Map("name: demo\n");

            Assert.IsTrue(definition.AllowedToolNames == null || definition.AllowedToolNames.Count == 0);
        }

        [TestMethod]
        public void ToSkillDefinition_AllowedToolsKebabKey_IsMapped()
        {
            var definition = Map("name: demo\nallowed-tools:\n  - read_range\n");

            CollectionAssert.AreEqual(new[] { "read_range" }, definition.AllowedToolNames.ToArray());
        }

        [TestMethod]
        public void ToSkillDefinition_CriticalGrants_AreCarriedOver()
        {
            var definition = Map("name: demo\nallow-high-risk-tools: true\nallow-code-execution: true\n");

            Assert.IsTrue(definition.AllowHighRiskTools);
            Assert.IsTrue(definition.AllowCodeExecution);
        }

        [TestMethod]
        public void ToSkillDefinition_Model_IsStoredInMetadata()
        {
            var definition = Map("name: demo\nmodel: gpt-x\n");

            Assert.AreEqual("gpt-x", definition.Metadata[SkillDefinitionMapper.SkillModelKey]);
        }

        [TestMethod]
        public void ToSkillDefinition_Body_IsStoredAsPrompt()
        {
            var definition = Map("name: demo\n", "Step one.");

            Assert.AreEqual("Step one.", definition.Metadata[SkillDefinitionMapper.SkillPromptMetadataKey]);
        }

        [TestMethod]
        public void ToSkillDefinition_DisableModelInvocation_IsDetectedByPolicy()
        {
            var definition = Map("name: demo\ndisable-model-invocation: true\n");

            Assert.IsTrue(SkillInvocationPolicy.IsModelInvocationDisabled(definition));
        }

        [TestMethod]
        public void ToSkillDefinition_DefaultFrontmatter_IsNotExcludedFromModelInvocation()
        {
            var definition = Map("name: demo\n");

            Assert.IsFalse(SkillInvocationPolicy.IsModelInvocationDisabled(definition));
        }

        [TestMethod]
        public void IsModelInvocationDisabled_NullSkill_ReturnsFalse()
        {
            Assert.IsFalse(SkillInvocationPolicy.IsModelInvocationDisabled(null));
        }

        [TestMethod]
        public void ToSkillDefinition_UserInvocableFalse_IsDetectedByPolicy()
        {
            var definition = Map("name: demo\nuser-invocable: false\n");

            Assert.IsTrue(SkillInvocationPolicy.IsUserInvocationDisabled(definition));
        }

        [TestMethod]
        public void ToSkillDefinition_DefaultFrontmatter_IsUserInvocable()
        {
            var definition = Map("name: demo\n");

            Assert.IsFalse(SkillInvocationPolicy.IsUserInvocationDisabled(definition));
        }
    }
}
