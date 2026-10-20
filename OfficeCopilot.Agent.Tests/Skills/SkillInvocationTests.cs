using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Skills
{
    [TestClass]
    public class SkillInvocationTests
    {
        private static SkillDefinition Skill(string name, bool modelDisabled = false, bool userDisabled = false)
        {
            var skill = new SkillDefinition { Name = name, Description = name, IsEnabled = true };
            if (modelDisabled) skill.Metadata[SkillMetadataKeys.DisableModelInvocation] = "true";
            if (userDisabled) skill.Metadata[SkillMetadataKeys.UserInvocable] = "false";
            return skill;
        }

        private static SkillSelectionResult Plan(SkillPlannerRequest request, params SkillDefinition[] skills)
        {
            return new KeywordSkillPlanner()
                .SelectSkillAsync(request, new InMemorySkillRegistry(skills), System.Threading.CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        [TestMethod]
        public void Planner_ExplicitlyRequestedSkill_IsSelected()
        {
            var result = Plan(
                new SkillPlannerRequest { UserInput = "anything", RequestedSkillName = "plain" },
                Skill("plain"));

            Assert.AreEqual("plain", result.SkillName);
        }

        [TestMethod]
        public void Planner_ExplicitlyRequestedUserInvocableFalse_IsNotSelected()
        {
            var result = Plan(
                new SkillPlannerRequest { UserInput = "anything", RequestedSkillName = "private" },
                Skill("private", userDisabled: true),
                Skill("other"));

            Assert.AreNotEqual("private", result.SkillName);
        }

        [TestMethod]
        public void Planner_ExplicitlyRequestedModelInvocationDisabled_IsStillSelected()
        {
            var result = Plan(
                new SkillPlannerRequest { UserInput = "anything", RequestedSkillName = "hidden" },
                Skill("hidden", modelDisabled: true));

            Assert.AreEqual("hidden", result.SkillName);
        }

        [TestMethod]
        public void Planner_DefaultSkillModelInvocationDisabled_IsNotAutoSelected()
        {
            var result = Plan(
                new SkillPlannerRequest { UserInput = "anything", DefaultSkillName = "hidden" },
                Skill("hidden", modelDisabled: true));

            Assert.AreNotEqual("hidden", result.SkillName);
            Assert.IsFalse(result.CandidateSkillNames.Contains("hidden"));
        }

        [TestMethod]
        public void Planner_ExplicitlyRequestedDisabledSkill_ReportsDisabledWithoutSelecting()
        {
            var disabled = Skill("off");
            disabled.IsEnabled = false;

            var result = Plan(
                new SkillPlannerRequest { UserInput = "anything", RequestedSkillName = "off" },
                disabled,
                Skill("other"));

            Assert.AreEqual(string.Empty, result.SkillName);
            Assert.AreEqual("off", result.DisabledRequestedSkillName);
        }

        [TestMethod]
        public void Planner_ExplicitlyRequestedDisabledSkill_WhenAllSkillsDisabled_ReportsDisabled()
        {
            var disabled = Skill("off");
            disabled.IsEnabled = false;

            var result = Plan(
                new SkillPlannerRequest { UserInput = "anything", RequestedSkillName = "off" },
                disabled);

            Assert.AreEqual(string.Empty, result.SkillName);
            Assert.AreEqual("off", result.DisabledRequestedSkillName);
        }

        [TestMethod]
        public void SkillTool_EnabledSkill_Succeeds()
        {
            var tool = new SkillTool(new InMemorySkillRegistry(new[] { Skill("plain") }));

            var outcome = tool.Execute("call-1", SkillTool.ToolName, "{\"skillName\":\"plain\"}");

            Assert.IsTrue(outcome.Success);
        }

        [TestMethod]
        public void SkillTool_ModelInvocationDisabled_IsRejected()
        {
            var tool = new SkillTool(new InMemorySkillRegistry(new[] { Skill("hidden", modelDisabled: true) }));

            var outcome = tool.Execute("call-2", SkillTool.ToolName, "{\"skillName\":\"hidden\"}");

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("skill.model_invocation_disabled", outcome.ErrorCode);
        }

        [TestMethod]
        public void SkillTool_DisabledSkill_IsRejectedWithDisabledCode()
        {
            var disabled = Skill("off");
            disabled.IsEnabled = false;
            var tool = new SkillTool(new InMemorySkillRegistry(new[] { disabled }));

            var outcome = tool.Execute("call-4", SkillTool.ToolName, "{\"skillName\":\"off\"}");

            Assert.IsFalse(outcome.Success);
            Assert.AreEqual("skill.disabled", outcome.ErrorCode);
            StringAssert.Contains(outcome.ErrorMessage, "enable it in the settings panel");
        }

        [TestMethod]
        public void SkillTool_UserInvocableFalse_IsStillAllowedForModel()
        {
            var tool = new SkillTool(new InMemorySkillRegistry(new[] { Skill("private", userDisabled: true) }));

            var outcome = tool.Execute("call-3", SkillTool.ToolName, "{\"skillName\":\"private\"}");

            Assert.IsTrue(outcome.Success);
        }
    }
}
