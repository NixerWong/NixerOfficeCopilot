using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime.Planning;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public class SkillPlanSupportTests
    {
        private static HostBridgeToolDescriptor Tool(
            string name,
            bool isReadOnly = false,
            string riskLevel = "medium")
        {
            return new HostBridgeToolDescriptor
            {
                Name = name,
                Description = name,
                IsReadOnly = isReadOnly,
                RiskLevel = riskLevel,
                InputSchemaJson = "{}"
            };
        }

        private static RuntimeSkillPlan Plan(string skillName, params string[] allowedTools)
        {
            var plan = new RuntimeSkillPlan { SkillName = skillName };
            foreach (var tool in allowedTools)
            {
                plan.AllowedToolNames.Add(tool);
            }

            return plan;
        }

        // ---- 规则 1：无技能 / 无白名单一律放行 ----

        [TestMethod]
        public void IsToolPermitted_AllowsEverythingWhenNoSkillSelected()
        {
            Assert.IsTrue(SkillPlanSupport.IsToolPermittedBySkillPlan(Tool("excel.write_range"), null));
            Assert.IsTrue(SkillPlanSupport.IsToolPermittedBySkillPlan(Tool("excel.write_range"), new RuntimeSkillPlan()));
        }

        [TestMethod]
        public void IsToolPermitted_ExcelToolsAreNotRestrictedBySkillRecommendations()
        {
            var plan = Plan("read-analysis");

            Assert.IsTrue(SkillPlanSupport.IsToolPermittedBySkillPlan(Tool("excel_write_range"), plan));
            Assert.IsTrue(SkillPlanSupport.IsToolPermittedBySkillPlan(
                Tool("excel_execute_vba", isReadOnly: false, riskLevel: "critical"), plan));
        }

        [TestMethod]
        public void IsToolPermitted_RejectsNullTool()
        {
            Assert.IsFalse(SkillPlanSupport.IsToolPermittedBySkillPlan(null, Plan("read-analysis")));
        }

        // ---- 规则 2：基础设施工具豁免 ----

        [TestMethod]
        public void IsInfrastructureTool_MatchesRuntimeAndHostPrefixes()
        {
            Assert.IsTrue(SkillPlanSupport.IsInfrastructureTool("runtime.list_tools"));
            Assert.IsTrue(SkillPlanSupport.IsInfrastructureTool("host.get_context"));
            Assert.IsTrue(SkillPlanSupport.IsInfrastructureTool(null));
            Assert.IsFalse(SkillPlanSupport.IsInfrastructureTool("excel.write_range"));
        }

        [TestMethod]
        public void IsToolPermitted_AllowsInfrastructureToolOutsideWhitelist()
        {
            var plan = Plan("read-analysis", "excel.read_range");

            Assert.IsTrue(SkillPlanSupport.IsToolPermittedBySkillPlan(Tool("runtime.list_tools"), plan));
        }

        // ---- 规则 3：白名单命中判定 ----

        [TestMethod]
        public void IsToolPermitted_EnforcesWhitelistForNonExcelDomainTools()
        {
            var plan = Plan("read-analysis", "ppt_read_slide");

            Assert.IsTrue(SkillPlanSupport.IsToolPermittedBySkillPlan(Tool("ppt_read_slide"), plan));
            Assert.IsFalse(SkillPlanSupport.IsToolPermittedBySkillPlan(Tool("ppt_update_slide"), plan));
        }

        [TestMethod]
        public void CollectSkillPlanToolNames_MergesPlanAndStepLevelNames()
        {
            var plan = Plan("edit", "excel.read_range");
            plan.PreferredToolNames.Add("excel.select_range");
            plan.VerificationToolNames.Add("excel.read_range");
            plan.ExecutionPlanSteps.Add(new SkillExecutionStep
            {
                Title = "write",
                AllowedToolNames = new List<string> { "excel.write_range" },
                PreferredToolNames = new List<string> { " excel.format " },
                VerificationToolNames = new List<string> { string.Empty }
            });

            var names = SkillPlanSupport.CollectSkillPlanToolNames(plan);

            Assert.AreEqual(4, names.Count);
            Assert.IsTrue(names.Contains("excel.write_range"));
            Assert.IsTrue(names.Contains("excel.format"), "名字应被 Trim");
            Assert.IsFalse(names.Contains(string.Empty));
        }

        [TestMethod]
        public void CollectSkillPlanToolNames_IsCaseInsensitive()
        {
            var names = SkillPlanSupport.CollectSkillPlanToolNames(Plan("edit", "Excel.Read_Range"));

            Assert.IsTrue(names.Contains("excel.read_range"));
        }

        // ---- 规则 4：AllowHighRiskTools 硬闸 ----

        [TestMethod]
        public void IsCriticalTool_RequiresWritableAndCriticalRisk()
        {
            Assert.IsTrue(SkillPlanSupport.IsCriticalTool(Tool("host.execute_code", isReadOnly: false, riskLevel: "critical")));
            Assert.IsFalse(SkillPlanSupport.IsCriticalTool(Tool("host.read", isReadOnly: true, riskLevel: "critical")));
            Assert.IsFalse(SkillPlanSupport.IsCriticalTool(Tool("excel.write_range", isReadOnly: false, riskLevel: "high")));
            Assert.IsFalse(SkillPlanSupport.IsCriticalTool(null));
        }

        [TestMethod]
        public void IsToolPermitted_CriticalToolBlockedEvenWhenInfrastructurePrefixed()
        {
            var plan = Plan("read-analysis");
            var critical = Tool("host.execute_code", isReadOnly: false, riskLevel: "critical");

            Assert.IsFalse(
                SkillPlanSupport.IsToolPermittedBySkillPlan(critical, plan),
                "critical 硬闸必须先于 runtime./host. 豁免判定");
        }

        [TestMethod]
        public void IsToolPermitted_NonExcelCriticalToolBlockedEvenWhenExplicitlyWhitelisted()
        {
            var plan = Plan("read-analysis", "host.execute_code");
            var critical = Tool("host.execute_code", isReadOnly: false, riskLevel: "critical");

            Assert.IsFalse(SkillPlanSupport.IsToolPermittedBySkillPlan(critical, plan));
        }

        [TestMethod]
        public void IsToolPermitted_CriticalToolAllowedWhenSkillOptsIn()
        {
            var plan = Plan("vba-author", "excel.run_vba");
            plan.AllowCodeExecution = true;

            Assert.IsTrue(SkillPlanSupport.IsToolPermittedBySkillPlan(
                Tool("host.execute_code", isReadOnly: false, riskLevel: "critical"), plan));
        }

        [TestMethod]
        public void IsToolPermitted_CriticalToolBlockedWhenOnlyHighRiskFlagSet()
        {
            // high-risk-mutation 类技能只应解锁“破坏性但可回滚的写入”，
            // 不应顺带解锁 VBA / PowerShell 等任意代码执行。
            var plan = Plan("high-risk-mutation", "excel.run_vba");
            plan.AllowHighRiskTools = true;
            plan.AllowCodeExecution = false;

            Assert.IsFalse(SkillPlanSupport.IsToolPermittedBySkillPlan(
                Tool("host.execute_code", isReadOnly: false, riskLevel: "critical"), plan));
        }

        [TestMethod]
        public void IsToolPermitted_RuntimePowerShellAllowedWithoutCodeExecutionGrant()
        {
            // runtime.powershell 由 PowerShellPermissionChecker 控制，不受 skill 代码执行硬门约束，
            // 以便查询 IP、进程等与文档无关的本地信息。
            var plan = Plan("read-analysis");
            var powershell = Tool("runtime.powershell", isReadOnly: false, riskLevel: "critical");
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { { powershell.Name, powershell } };

            Assert.IsFalse(plan.AllowCodeExecution);
            Assert.IsTrue(SkillPlanSupport.IsToolPermittedBySkillPlan(powershell, plan));
            Assert.AreEqual(string.Empty, SkillPlanSupport.GetSkillPlanViolationMessage("runtime.powershell", catalog, plan));

            // 其它 critical 任意代码工具仍受硬门约束。
            var execute = Tool("host.execute_code", isReadOnly: false, riskLevel: "critical");
            Assert.IsFalse(SkillPlanSupport.IsToolPermittedBySkillPlan(execute, plan));
        }

        [TestMethod]
        public void IsToolPermitted_McpToolsSurviveSkillWhitelist()
        {
            // MCP 工具由用户配置，内置技能白名单无法预先列举，不豁免会整体消失。
            var plan = Plan("read-analysis", "excel.read_range");

            Assert.IsTrue(SkillPlanSupport.IsToolPermittedBySkillPlan(
                Tool("mcp__filesystem__read_file", isReadOnly: true, riskLevel: "low"), plan));

            // 但 critical 硬门依然适用于 MCP 工具。
            Assert.IsFalse(SkillPlanSupport.IsToolPermittedBySkillPlan(
                Tool("mcp__shell__run_command", isReadOnly: false, riskLevel: "critical"), plan));
        }

        // ---- 规则 5：违规提示文本 ----

        [TestMethod]
        public void GetViolationMessage_EmptyWhenNoSkillOrEmptyToolName()
        {
            var catalog = new Dictionary<string, HostBridgeToolDescriptor>();

            Assert.AreEqual(string.Empty, SkillPlanSupport.GetSkillPlanViolationMessage("excel.write_range", catalog, null));
            Assert.AreEqual(string.Empty, SkillPlanSupport.GetSkillPlanViolationMessage(" ", catalog, Plan("edit")));
        }

        [TestMethod]
        public void GetViolationMessage_ExplainsHighRiskGate()
        {
            var critical = Tool("host.execute_code", isReadOnly: false, riskLevel: "critical");
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { { critical.Name, critical } };

            var message = SkillPlanSupport.GetSkillPlanViolationMessage("host.execute_code", catalog, Plan("read-analysis"));

            StringAssert.Contains(message, "code-execution authority");
            StringAssert.Contains(message, "runtime.invoke_skill only returns guidance");
            Assert.IsFalse(message.Contains("then retry"));
        }

        [TestMethod]
        public void GetViolationMessage_ListsAllowedToolsPresentInCatalog()
        {
            var allowed = Tool("word.read_document", isReadOnly: true, riskLevel: "low");
            var denied = Tool("word.insert_text");
            var catalog = new Dictionary<string, HostBridgeToolDescriptor>
            {
                { allowed.Name, allowed },
                { denied.Name, denied }
            };

            var message = SkillPlanSupport.GetSkillPlanViolationMessage(
                "word.insert_text", catalog, Plan("read-analysis", "word.read_document"));

            StringAssert.Contains(message, "word.read_document");
        }

        [TestMethod]
        public void GetViolationMessage_SkipsEnforcementWhenWhitelistMissesHostCatalogEntirely()
        {
            var wordTool = Tool("word.insert_text");
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { { wordTool.Name, wordTool } };

            // Excel 技能在 Word 宿主里生效——白名单对本次会话无意义，不应拦截。
            var message = SkillPlanSupport.GetSkillPlanViolationMessage(
                "word.insert_text", catalog, Plan("excel-edit", "excel.write_range"));

            Assert.AreEqual(string.Empty, message);
        }

        // ---- 辅助判定 ----

        [TestMethod]
        public void IsHighRiskTool_CoversHighAndCritical()
        {
            Assert.IsTrue(SkillPlanSupport.IsHighRiskTool(Tool("a", isReadOnly: false, riskLevel: "high")));
            Assert.IsTrue(SkillPlanSupport.IsHighRiskTool(Tool("b", isReadOnly: false, riskLevel: "critical")));
            Assert.IsFalse(SkillPlanSupport.IsHighRiskTool(Tool("c", isReadOnly: true, riskLevel: "high")));
            Assert.IsFalse(SkillPlanSupport.IsHighRiskTool(Tool("d", isReadOnly: false, riskLevel: "low")));
        }

        [TestMethod]
        public void IsVbaFallbackTool_MatchesVbaMacroAndScriptNames()
        {
            Assert.IsTrue(SkillPlanSupport.IsVbaFallbackTool("excel.run_VBA"));
            Assert.IsTrue(SkillPlanSupport.IsVbaFallbackTool("host.run_macro"));
            Assert.IsTrue(SkillPlanSupport.IsVbaFallbackTool("runtime.powershell_script"));
            Assert.IsFalse(SkillPlanSupport.IsVbaFallbackTool("excel.read_range"));
            Assert.IsFalse(SkillPlanSupport.IsVbaFallbackTool(null));
        }

        [TestMethod]
        public void IsSelfVerificationFailureCode_RecognizesExcelAssertionFailure()
        {
            Assert.IsTrue(SkillPlanSupport.IsSelfVerificationFailureCode("excel.assertion_failed"));
            Assert.IsTrue(SkillPlanSupport.IsSelfVerificationFailureCode(
                "excel.execute_vba_expectation_failed"));
            Assert.IsFalse(SkillPlanSupport.IsSelfVerificationFailureCode("excel.aggregate_failed"));

            var plan = Plan("read-analysis");
            plan.ExecutionPlanSteps.Add(new SkillExecutionStep());
            Assert.IsTrue(SkillPlanSupport.CanRecoverFromVerificationFailure(
                plan,
                "excel_assert",
                "excel.assertion_failed"));
        }

        [TestMethod]
        public void ExecutionPlan_RequiresCurrentWriteStageVerificationBeforeNextMutation()
        {
            var firstMutation = new SkillExecutionStep
            {
                StepKind = "formula",
                StepId = "mutation:first",
                SkillName = "live-result",
                AllowedToolNames = new List<string> { "excel_set_range_formulas" }
            };
            var firstVerification = new SkillExecutionStep
            {
                StepKind = "verify",
                StepId = "verify:first",
                VerifiesStepId = firstMutation.StepId,
                VerificationToolNames = new List<string> { "excel_assert" }
            };
            var secondMutation = new SkillExecutionStep
            {
                StepKind = "transform",
                StepId = "mutation:second",
                SkillName = "structural-transform",
                AllowedToolNames = new List<string> { "excel_write_range" }
            };
            var plan = new RuntimeSkillPlan
            {
                SkillName = "live-result",
                ExecutionPlanSteps = new List<SkillExecutionStep>
                {
                    firstMutation,
                    firstVerification,
                    secondMutation
                }
            };
            plan.InitializeExecutionPlanProgress();

            Assert.IsFalse(plan.RegisterSuccessfulToolCall("excel_set_range_formulas", true, false));
            Assert.AreEqual("mutation:first", plan.GetCurrentExecutionStep().StepId);
            Assert.IsTrue(plan.HasStartedMutationAwaitingVerification());
            Assert.IsTrue(SkillPlanSupport.ShouldContinueForPendingVerification(plan));
            Assert.IsFalse(SkillPlanSupport.ShouldContinueForPendingMutation(
                plan,
                new TurnExecutionContract
                {
                    IsDeclared = true,
                    MaxWriteEffect = ToolWriteEffectLevels.Append
                }));
            Assert.IsFalse(SkillPlanSupport.ShouldBlockWriteForPendingVerification(plan, true));
            Assert.IsFalse(SkillPlanSupport.ShouldBlockWriteForPendingVerification(plan, false));
            Assert.IsTrue(plan.IsVerificationTool("excel_assert"));
            Assert.IsFalse(plan.IsVerificationTool("excel_read_range"));

            Assert.IsFalse(plan.RegisterSuccessfulToolCall("excel_write_range", true, false));
            Assert.AreEqual("mutation:first", plan.GetCurrentExecutionStep().StepId);
            Assert.IsFalse(plan.RegisterSuccessfulToolCall("excel_assert", false, false));
            Assert.AreEqual("mutation:first", plan.GetCurrentExecutionStep().StepId);

            Assert.IsTrue(plan.RegisterSuccessfulToolCall("excel_assert", false, true));
            Assert.AreEqual("mutation:second", plan.GetCurrentExecutionStep().StepId);
            Assert.IsFalse(SkillPlanSupport.ShouldContinueForPendingMutation(
                plan,
                new TurnExecutionContract
                {
                    IsDeclared = true,
                    Mode = "result_only",
                    MaxWriteEffect = ToolWriteEffectLevels.Scratch
                }));
            Assert.IsFalse(SkillPlanSupport.ShouldContinueForPendingMutation(
                plan,
                new TurnExecutionContract()));
            Assert.IsTrue(SkillPlanSupport.ShouldContinueForPendingMutation(
                plan,
                new TurnExecutionContract
                {
                    IsDeclared = true,
                    Mode = "new_sheet_static",
                    MaxWriteEffect = ToolWriteEffectLevels.Append
                }));
            Assert.IsFalse(SkillPlanSupport.ShouldContinueForPendingVerification(plan));
            Assert.IsFalse(SkillPlanSupport.ShouldBlockWriteForPendingVerification(plan, true));
        }

        [TestMethod]
        public void WipRegistryDryRunCompletesWorksheetCreationVerificationStage()
        {
            var mutation = new SkillExecutionStep
            {
                StepKind = "structural",
                StepId = "mutation:structural-transform",
                AllowedToolNames = new List<string> { "excel_create_worksheet" }
            };
            var verification = new SkillExecutionStep
            {
                StepKind = "verify",
                StepId = "verify:structural-transform",
                VerifiesStepId = mutation.StepId,
                VerificationToolNames = new List<string> { "excel_cleanup_workspace" }
            };
            var plan = new RuntimeSkillPlan
            {
                RequireVerification = true,
                ExecutionPlanSteps = new List<SkillExecutionStep> { mutation, verification }
            };
            plan.InitializeExecutionPlanProgress();

            Assert.IsFalse(plan.RegisterSuccessfulToolCall(
                "excel_create_worksheet",
                isWriteTool: true,
                hasPassingVerification: false));
            Assert.IsTrue(plan.HasStartedMutationAwaitingVerification());
            Assert.IsTrue(SkillPlanSupport.ShouldContinueForPendingVerification(plan));

            Assert.IsTrue(plan.IsVerificationTool("excel_cleanup_workspace"));
            Assert.IsTrue(plan.RegisterSuccessfulToolCall(
                "excel_cleanup_workspace",
                isWriteTool: false,
                hasPassingVerification: true));
            Assert.IsNull(plan.GetCurrentExecutionStep());
            Assert.IsFalse(SkillPlanSupport.ShouldContinueForPendingVerification(plan));
        }

        [TestMethod]
        public void ReadOnlyInlineVerification_CompletesFinalCheckButNotPlannedMutation()
        {
            const string verificationTool = "excel_compare_key_sets";
            var inspect = new SkillExecutionStep
            {
                StepKind = "inspect",
                StepId = "inspect:read-analysis",
                AllowedToolNames = new List<string> { verificationTool }
            };
            var finalVerification = new SkillExecutionStep
            {
                StepKind = "verify",
                StepId = "verify:final",
                VerificationToolNames = new List<string> { verificationTool }
            };
            var readOnlyPlan = new RuntimeSkillPlan
            {
                RequireVerification = true,
                ExecutionPlanSteps = new List<SkillExecutionStep>
                {
                    inspect,
                    finalVerification
                }
            };
            readOnlyPlan.InitializeExecutionPlanProgress();

            Assert.IsTrue(readOnlyPlan.RegisterSuccessfulToolCall(
                verificationTool,
                isWriteTool: false,
                hasPassingVerification: true));
            Assert.IsNull(readOnlyPlan.GetCurrentExecutionStep());
            Assert.IsFalse(SkillPlanSupport.ShouldContinueForPendingVerification(readOnlyPlan));

            var planWithMutation = new RuntimeSkillPlan
            {
                RequireVerification = true,
                ExecutionPlanSteps = new List<SkillExecutionStep>
                {
                    new SkillExecutionStep
                    {
                        StepKind = "inspect",
                        StepId = "inspect:read-analysis",
                        AllowedToolNames = new List<string> { verificationTool }
                    },
                    new SkillExecutionStep
                    {
                        StepKind = "transform",
                        StepId = "mutation:transform",
                        AllowedToolNames = new List<string> { "excel_write_range" }
                    },
                    new SkillExecutionStep
                    {
                        StepKind = "verify",
                        StepId = "verify:final",
                        VerificationToolNames = new List<string> { verificationTool }
                    }
                }
            };
            planWithMutation.InitializeExecutionPlanProgress();

            Assert.IsTrue(planWithMutation.RegisterSuccessfulToolCall(
                verificationTool,
                isWriteTool: false,
                hasPassingVerification: true));
            Assert.AreEqual("mutation:transform", planWithMutation.GetCurrentExecutionStep().StepId);
        }

        [TestMethod]
        public void ExecutionPlan_VerificationFailureRewindsOnlyItsPrecedingMutation()
        {
            var mutation = new SkillExecutionStep
            {
                StepKind = "formula",
                StepId = "mutation:formula",
                AllowedToolNames = new List<string> { "excel_set_range_formulas" }
            };
            var verification = new SkillExecutionStep
            {
                StepKind = "verify",
                StepId = "verify:formula",
                VerifiesStepId = mutation.StepId,
                VerificationToolNames = new List<string> { "excel_assert" }
            };
            var plan = new RuntimeSkillPlan
            {
                ExecutionPlanSteps = new List<SkillExecutionStep> { mutation, verification }
            };
            plan.InitializeExecutionPlanProgress();
            plan.RegisterSuccessfulToolCall("excel_set_range_formulas", true, false);

            Assert.IsTrue(plan.RewindForVerificationFailure());
            Assert.AreEqual("mutation:formula", plan.GetCurrentExecutionStep().StepId);
            Assert.AreEqual(0, plan.CompletedExecutionStepCount);
        }

        [TestMethod]
        public void ShouldExposeToolToModel_DelegatesToPermissionRule()
        {
            var plan = Plan("read-analysis", "excel_read_range");

            Assert.IsTrue(SkillPlanSupport.ShouldExposeToolToModel(Tool("excel_read_range"), plan));
            Assert.IsTrue(SkillPlanSupport.ShouldExposeToolToModel(Tool("excel_write_range"), plan));
        }

        [TestMethod]
        public void GetOrderedModelToolNames_PutsSkillToolsFirstThenRemainderAlphabetically()
        {
            var catalog = new Dictionary<string, HostBridgeToolDescriptor>
            {
                { "excel.write_range", Tool("excel.write_range") },
                { "excel.read_range", Tool("excel.read_range") },
                { "runtime.list_tools", Tool("runtime.list_tools") }
            };

            var ordered = new List<string>(
                SkillPlanSupport.GetOrderedModelToolNames(catalog, Plan("read-analysis", "excel.write_range")));

            Assert.AreEqual("excel.write_range", ordered[0]);
            Assert.AreEqual(3, ordered.Count);
            CollectionAssert.AllItemsAreUnique(ordered);
        }

        [TestMethod]
        public void GetOrderedModelToolNames_ReturnsEmptyForEmptyCatalog()
        {
            Assert.AreEqual(0, new List<string>(SkillPlanSupport.GetOrderedModelToolNames(null, Plan("x"))).Count);
        }
    }
}
