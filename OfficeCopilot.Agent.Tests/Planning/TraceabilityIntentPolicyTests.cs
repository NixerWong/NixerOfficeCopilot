using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Settings;

namespace OfficeCopilot.Agent.Tests.Planning
{
    /// <summary>「可验证 / 留痕 / 分步」意图识别：命中后应默认把中间结果物化到 _wip_NN_ 表。</summary>
    [TestClass]
    public class TraceabilityIntentPolicyTests
    {
        [DataTestMethod]
        [DataRow("分析要有步骤，可验证")]
        [DataRow("帮我统计每个姓氏的平均月薪，结果要留痕")]
        [DataRow("这个分析要可追溯")]
        [DataRow("分步骤计算并保留中间结果")]
        [DataRow("每一步都落到中间表，最后要能复查")]
        [DataRow("show your work and make it verifiable")]
        [DataRow("make the calculation traceable step by step")]
        public void RequiresTraceableSteps_DetectsIntent(string input)
        {
            Assert.IsTrue(TraceabilityIntentPolicy.RequiresTraceableSteps(input));
        }

        [DataTestMethod]
        [DataRow("求每个姓氏的平均月薪")]
        [DataRow("把 B 列求和")]
        [DataRow("步骤")]
        [DataRow("请解释一下")]
        [DataRow("sum column B")]
        public void RequiresTraceableSteps_DoesNotOverTrigger(string input)
        {
            Assert.IsFalse(TraceabilityIntentPolicy.RequiresTraceableSteps(input));
        }

        [TestMethod]
        public void RequiresTraceableSteps_NullOrWhitespace_ReturnsFalse()
        {
            Assert.IsFalse(TraceabilityIntentPolicy.RequiresTraceableSteps(null));
            Assert.IsFalse(TraceabilityIntentPolicy.RequiresTraceableSteps(string.Empty));
            Assert.IsFalse(TraceabilityIntentPolicy.RequiresTraceableSteps("   "));
        }

        [TestMethod]
        public void Build_AddsTraceabilityRuleOnlyForVerifiableIntent()
        {
            var selection = new SkillSelectionResult
            {
                SkillName = "read-analysis",
                AllowCodeExecution = true,
                AllowedToolNames = new System.Collections.Generic.List<string> { "excel_execute_vba", "excel_aggregate" }
            };

            var withIntent = SkillExecutionPromptBuilder.Build(null, selection, "分析要有步骤，可验证");
            StringAssert.Contains(withIntent, "traceable", "可验证意图必须触发 _wip_ 留痕规则。");

            var withoutIntent = SkillExecutionPromptBuilder.Build(null, selection, "求平均月薪");
            Assert.IsFalse(withoutIntent.Contains("traceable"), "无意图时不应追加留痕规则。");
        }

        [TestMethod]
        public void Build_TreatsExcelToolRecommendationsAsStrategyNotPermission()
        {
            var selection = new SkillSelectionResult
            {
                SkillName = "read-analysis",
                AllowCodeExecution = false,
                AllowedToolNames = new System.Collections.Generic.List<string>
                {
                    "excel_aggregate",
                    "excel_profile_range"
                }
            };

            var prompt = SkillExecutionPromptBuilder.Build(null, selection);

            StringAssert.Contains(prompt, "tool lists are recommendations, not permissions");
            StringAssert.Contains(prompt, "Only a worksheet registered as this session's WIP is staging scratch");
            Assert.IsFalse(prompt.Contains("active runtime execution contract"));
            Assert.IsFalse(prompt.Contains("execution limit and confirmation policy"));
            Assert.IsFalse(prompt.Contains("Prefer dedicated Excel analysis"));
            Assert.IsFalse(prompt.Contains("intentionally unavailable"));
            Assert.IsFalse(prompt.Contains("This skill has no code-execution authority"));
        }

        [TestMethod]
        public void Build_KeepsNonExcelToolListLabelUnchanged()
        {
            var selection = new SkillSelectionResult
            {
                SkillName = "ppt-edit-slide",
                AllowedToolNames = new System.Collections.Generic.List<string> { "ppt_read_slide" }
            };

            var prompt = SkillExecutionPromptBuilder.Build(null, selection);

            StringAssert.Contains(prompt, "Suggested tools for this turn");
            Assert.IsFalse(prompt.Contains("not an access-control list"));
        }

        [TestMethod]
        public void Build_AddsBasisClarificationPolicyForExcelCalculationTools()
        {
            var selection = new SkillSelectionResult
            {
                SkillName = "read-analysis",
                AllowedToolNames = new System.Collections.Generic.List<string>
                {
                    "excel_aggregate",
                    "runtime.ask_user_question",
                    "runtime.calculate"
                }
            };

            var prompt = SkillExecutionPromptBuilder.Build(null, selection);

            StringAssert.Contains(prompt, "actual workbook headers/profile");
            StringAssert.Contains(prompt, "runtime.ask_user_question");
            StringAssert.Contains(prompt, "duplicate keys or which record to keep");
            StringAssert.Contains(prompt, "Do not choose a default or substitute a text-only question");
            StringAssert.Contains(prompt, "make runtime.ask_user_question the first tool call");
            StringAssert.Contains(prompt, "Do not inspect the workbook to infer the rule");
            StringAssert.Contains(prompt, "excel_assert.basis");
            StringAssert.Contains(prompt, "source.column and target.column/columns are side-specific physical headers");
            StringAssert.Contains(prompt, "source Amount -> target Value");
            StringAssert.Contains(prompt, "runtime.calculate");
            StringAssert.Contains(prompt, "exact numeric operands");
            StringAssert.Contains(prompt, "0-to-1 ratio");
            StringAssert.Contains(prompt, "not the ratio multiplied by 100");
            StringAssert.Contains(prompt, "report only the results the user requested");
            StringAssert.Contains(prompt, "Do not add unrequested row counts");
            StringAssert.Contains(prompt, "complete UsedRange");
            StringAssert.Contains(prompt, "guessed partial range");
        }

        [TestMethod]
        public void Build_RecommendsBoundedKeySetComparisonInsteadOfEnumeratingKeys()
        {
            var selection = new SkillSelectionResult
            {
                SkillName = "read-analysis",
                AllowedToolNames = new System.Collections.Generic.List<string>
                {
                    "excel_compare_key_sets"
                }
            };

            var prompt = SkillExecutionPromptBuilder.Build(null, selection);

            StringAssert.Contains(prompt, "excel_compare_key_sets");
            StringAssert.Contains(prompt, "bounded deterministic samples");
            StringAssert.Contains(prompt, "Do not enumerate keys");
        }

        [TestMethod]
        public void Build_ProvidesGuidanceForDirtyNumbersAndCompleteHiddenRowScans()
        {
            var selection = new SkillSelectionResult
            {
                SkillName = "read-analysis",
                AllowedToolNames = new System.Collections.Generic.List<string>
                {
                    "excel_aggregate",
                    "excel_get_range_info"
                }
            };

            var prompt = SkillExecutionPromptBuilder.Build(null, selection);

            StringAssert.Contains(prompt, "isNativeNumber / isTextNumber");
            StringAssert.Contains(prompt, "errorCount");
            StringAssert.Contains(prompt, "Do not use countDistinct");
            StringAssert.Contains(prompt, "includeHiddenRows:true");
            StringAssert.Contains(prompt, "hiddenRowsScanComplete");
            StringAssert.Contains(prompt, "filter-hidden worksheet rows");
            StringAssert.Contains(prompt, "Do not infer or report visible-row counts");
            StringAssert.Contains(prompt, "A complete successful scan is the verification");
            StringAssert.Contains(prompt, "Do not add unrequested row counts");
            StringAssert.Contains(prompt, "arithmetic reconciliations");
        }

        [TestMethod]
        public void ExcelDefaultPrompts_RequireClarifyingAmbiguousBasis()
        {
            var askPrompt = BuiltInHostPrompts.Get("Excel", HostPromptMode.Ask);
            var agentPrompt = BuiltInHostPrompts.Get("Excel", HostPromptMode.Agent);

            StringAssert.Contains(askPrompt, "runtime.ask_user_question");
            StringAssert.Contains(agentPrompt, "runtime.ask_user_question");
            StringAssert.Contains(agentPrompt, "excel_execute_vba.expect.basis");
            StringAssert.Contains(agentPrompt, "basis.sources");
            StringAssert.Contains(agentPrompt, "topology (single/union/lookup/compare)");
            StringAssert.Contains(agentPrompt, "declare keys and join");
            StringAssert.Contains(agentPrompt, "Submit all sources[] in one request for a homogeneous union");
            StringAssert.Contains(agentPrompt, "group by the automatic __source column");
            StringAssert.Contains(agentPrompt, "excel_aggregate.lookups[]");
            StringAssert.Contains(agentPrompt, "source.column and target.column must use the actual column name on their respective sides");
            StringAssert.Contains(agentPrompt, "source.column=Amount and target.column=Value");
            StringAssert.Contains(agentPrompt, "duplicate keys must fail");
            StringAssert.Contains(agentPrompt, "runtime.calculate");
        }

        [TestMethod]
        public void ExcelDefaultPrompts_RequireHandlingProfileQualityFlags()
        {
            var askPrompt = BuiltInHostPrompts.Get("Excel", HostPromptMode.Ask);
            var agentPrompt = BuiltInHostPrompts.Get("Excel", HostPromptMode.Agent);

            foreach (var prompt in new[] { askPrompt, agentPrompt })
            {
                StringAssert.Contains(prompt, "dataQualityFlags");
                StringAssert.Contains(prompt, "dataQualityNotes");
                StringAssert.Contains(prompt, "subtotalLabelRows");
                StringAssert.Contains(prompt, "hiddenOrFilteredRows");
                StringAssert.Contains(prompt, "excludeRows");
                StringAssert.Contains(prompt, "appropriate target/source side");
                StringAssert.Contains(prompt, "do not claim visible-only results");
                StringAssert.Contains(prompt, "do not exclude only sample rows");
            }
        }

        [TestMethod]
        public void ExcelAgentPrompt_DoesNotTreatEveryWipNamedSheetAsScratch()
        {
            var agentPrompt = BuiltInHostPrompts.Get("Excel", HostPromptMode.Agent);

            StringAssert.Contains(agentPrompt, "Only worksheets registered as _wip_ during this session are scratch");
            StringAssert.Contains(agentPrompt, "Runtime gates determine actual impact");
            Assert.IsFalse(agentPrompt.Contains("Writing to a _wip_ sheet needs no user confirmation"));
        }
    }
}
