using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.Agent.Tests.Harness;
using OfficeCopilot.HostBridge.Contracts;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Scenarios
{
    /// <summary>
    /// Agent 循环的确定性回放场景。每个用例锁住一条曾经出过问题、或必须永远成立的行为。
    /// </summary>
    [TestClass]
    public class TurnLoopScenarioTests
    {
        private const string ReadTool = "excel_read_range";
        private const string WriteTool = "excel_write_range";
        private const string VbaTool = "excel_execute_vba";

        [TestMethod]
        public void ToolTrail_ExecutesInScriptedOrderThenAnswers()
        {
            var harness = new TurnHarness()
                .WithTool(ReadTool, true)
                .WithTool(WriteTool, false);
            harness.Model
                .Then(ScriptedModelResponse.Call(ReadTool, "{\"range\":\"A1:B10\"}"))
                .Then(ScriptedModelResponse.Call(WriteTool, "{\"range\":\"C1\"}"))
                .Then(ScriptedModelResponse.Text("完成。"));

            var result = harness.Run("读一下再写一下");

            CollectionAssert.AreEqual(
                new[] { ReadTool, WriteTool },
                harness.Host.InvokedToolNames.ToArray());
            Assert.IsTrue(result.Success);
            Assert.AreEqual("stop", result.FinishReason);
            StringAssert.StartsWith(result.Content, "完成。");
            StringAssert.Contains(result.Content, "校验：C级：尚未完成有效验证");
        }

        [TestMethod]
        public void AggregateVerification_IsAppendedToFinalAnswerByRuntime()
        {
            const string aggregateTool = "excel_aggregate";
            var harness = new TurnHarness().WithTool(aggregateTool, true);
            harness.Host.On(aggregateTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "合计金额为 100。",
                StructuredContentJson = "{\"verification\":{"
                    + "\"tier\":\"A\",\"passed\":true,\"assertionCount\":1,\"coveredRows\":8,"
                    + "\"independent\":true,\"sampled\":false,\"producer\":\"managed\","
                    + "\"basis\":{\"source\":{\"worksheetName\":\"销售明细\"},"
                    + "\"measures\":[{\"column\":\"金额\",\"function\":\"sum\"}]},"
                    + "\"notes\":[],\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}}}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(aggregateTool, "{\"worksheetName\":\"销售明细\",\"measures\":[{\"column\":\"金额\",\"function\":\"sum\"}]}"))
                .Then(ScriptedModelResponse.Text("销售额合计为 100。"));

            var result = harness.Run("计算销售额合计");

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.Content, "销售额合计为 100。");
            StringAssert.Contains(result.Content, "校验：A级：全量独立复核通过（1 项断言通过，覆盖 8 行）");
            StringAssert.Contains(result.Content, "口径：销售明细（全局汇总 sum(金额)）");
            Assert.AreEqual(1, result.Content.Split(new[] { "校验：" }, System.StringSplitOptions.None).Length - 1);
            Assert.AreEqual(2, harness.Model.CallCount, "已有工具证据支持回答数字时不得触发订正。");
        }

        [TestMethod]
        public void ReadOnlyWorkbookInspection_UsesOneRuntimeTierAndNamesEachCheckedTarget()
        {
            var harness = new TurnHarness()
                .WithTool("excel_list_worksheets", true)
                .WithTool(ReadTool, true)
                .WithTool("excel_list_backups", true);
            harness.Model
                .Then(ScriptedModelResponse.Call("excel_list_worksheets", "{}"))
                .Then(ScriptedModelResponse.Call(
                    ReadTool,
                    "{\"worksheetName\":\"Monthly\",\"address\":\"A2\"}"))
                .Then(ScriptedModelResponse.Call("excel_list_backups", "{}"))
                .Then(ScriptedModelResponse.Text(
                    "只读结果：A2 原值为 S0001。\n校验：C级：没有登记产出项\n口径：未提供可解析的结构化口径\n\n2. 验证档位：C级"));

            var result = harness.Run("检查工作表、Monthly!A2 和备份清单");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, result.Content.Split(
                new[] { "校验：" },
                System.StringSplitOptions.None).Length - 1);
            Assert.AreEqual(1, result.Content.Split(
                new[] { "检查对象：" },
                System.StringSplitOptions.None).Length - 1);
            StringAssert.Contains(result.Content, "校验：B级：");
            StringAssert.Contains(result.Content, "工作表清单");
            StringAssert.Contains(result.Content, "读取 Monthly!A2");
            StringAssert.Contains(result.Content, "备份清单");
            Assert.IsFalse(result.Content.Contains("没有登记产出项"));
            Assert.IsFalse(result.Content.Contains("验证档位：C级"));
        }

        [TestMethod]
        public void ReadAnalysisInlineVerification_DoesNotRequireDuplicateVerifierCall()
        {
            const string compareTool = "excel_compare_key_sets";
            var skill = new SkillDefinition
            {
                Name = "test-read-analysis",
                DisplayName = "Test Read Analysis",
                Category = "read",
                Description = "Compare worksheet key sets.",
                AllowedToolNames = new List<string> { compareTool },
                PreferredToolNames = new List<string> { compareTool },
                VerificationToolNames = new List<string> { compareTool },
                RequireVerification = true,
                MaxToolRoundCount = 12
            };
            skill.Metadata[SkillMetadataKeys.HostKind] = "excel";

            var harness = new TurnHarness()
                .WithTool(compareTool, true)
                .WithSkill(skill);
            harness.Host.On(compareTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "target-only 150；source-only 150；matched 2850；total-unmatched 300。",
                StructuredContentJson = "{\"verification\":{"
                    + "\"tier\":\"A\",\"passed\":true,\"assertionCount\":0,\"coveredRows\":40,"
                    + "\"independent\":true,\"sampled\":false,\"producer\":\"managed\","
                    + "\"basis\":{\"target\":{\"worksheetName\":\"Orders\"},"
                    + "\"source\":{\"worksheetName\":\"Customers\"}},"
                    + "\"notes\":[],\"details\":{\"items\":[],\"diffs\":[],\"diffCount\":0}},"
                    + "\"targetOnlyCount\":150,\"sourceOnlyCount\":150,"
                    + "\"matchedCount\":2850,\"totalUnmatchedCount\":300}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(
                    compareTool,
                    "{\"target\":{\"tableName\":\"Orders\"},"
                    + "\"source\":{\"tableName\":\"Customers\"}}"))
                .Then(ScriptedModelResponse.Text(
                    "Orders 和 Customers 两侧各有 150 个未匹配键，共有 2850 个匹配键，"
                    + "双向未匹配合计 300 个。"));

            var result = harness.Run("比较客户表和订单表的键集合");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, harness.Model.CallCount);
            CollectionAssert.AreEqual(
                new[] { compareTool },
                harness.Host.InvokedToolNames.ToArray());
            StringAssert.Contains(result.Content, "校验：A级：全量独立复核通过");
            Assert.IsFalse(result.Content.Contains("验证未完成"));
            Assert.IsFalse(result.Content.Contains("**更正**"));
        }

        [TestMethod]
        public void WipWorksheetDryRunVerification_CompletesStructuralTransformTurn()
        {
            const string worksheetName = "_wip_01_restart_probe";
            const string listTool = "excel_list_worksheets";
            const string createTool = "excel_create_worksheet";
            const string cleanupTool = "excel_cleanup_workspace";
            var structuralSkill = OfficeSkillCatalog.CreateDefaultSkills()
                .Single(skill => skill.Name == "structural-transform");
            var harness = new TurnHarness()
                .WithTool(listTool, true)
                .WithTool(createTool, false)
                .WithTool(cleanupTool, false)
                .WithSkill(structuralSkill);
            harness.Host.On(createTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "工作表已创建。",
                StructuredContentJson = "{\"WorkbookName\":\"test.xlsx\","
                    + "\"WorksheetName\":\"" + worksheetName + "\",\"CodeName\":\"\"}"
            });
            harness.Host.On(cleanupTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "预演模式：将删除 1 个中间表，当前未做任何改动。",
                StructuredContentJson = "{\"prefix\":\"" + worksheetName + "\","
                    + "\"matchedSheets\":[\"" + worksheetName + "\"],"
                    + "\"removedSheets\":[],\"dryRun\":true}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(listTool, "{}"))
                .Then(ScriptedModelResponse.Call(
                    createTool,
                    "{\"worksheetName\":\"" + worksheetName + "\"}"))
                .Then(ScriptedModelResponse.Call(listTool, "{}"))
                .Then(ScriptedModelResponse.Call(
                    cleanupTool,
                    "{\"prefix\":\"" + worksheetName + "\",\"dryRun\":true}"))
                .Then(ScriptedModelResponse.Text("工作表已创建并确认登记，预演未删除内容。"));

            var result = harness.Run("新建中间工作表 _wip_01_restart_probe，并验证其登记");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(5, harness.Model.CallCount);
            CollectionAssert.AreEqual(
                new[] { listTool, createTool, listTool, cleanupTool },
                harness.Host.InvokedToolNames.ToArray());
            Assert.IsFalse(result.Content.Contains("验证未完成"));
            Assert.IsFalse(result.Content.Contains("Broker 执行失败"));
        }

        [TestMethod]
        public void WipWorksheetCleanupReadback_CompletesStructuralTransformTurn()
        {
            const string worksheetName = "_wip_01_restart_probe";
            const string listTool = "excel_list_worksheets";
            const string cleanupTool = "excel_cleanup_workspace";
            var structuralSkill = OfficeSkillCatalog.CreateDefaultSkills()
                .Single(skill => skill.Name == "structural-transform");
            var harness = new TurnHarness()
                .WithTool(listTool, true)
                .WithTool(cleanupTool, false)
                .WithSkill(structuralSkill);
            harness.Host.On(cleanupTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "已删除 1 个中间表。",
                StructuredContentJson = "{\"prefix\":\"" + worksheetName + "\","
                    + "\"matchedSheets\":[\"" + worksheetName + "\"],"
                    + "\"removedSheets\":[\"" + worksheetName + "\"],"
                    + "\"dryRun\":false}"
            });
            harness.Host.On(listTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "工作表数量：1\n1. Monthly",
                StructuredContentJson = "{\"WorkbookName\":\"test.xlsx\","
                    + "\"Worksheets\":[{\"Name\":\"Monthly\",\"Index\":1}],"
                    + "\"TotalWorksheetCount\":1,\"IsTruncated\":false}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(
                    cleanupTool,
                    "{\"prefix\":\"" + worksheetName + "\",\"dryRun\":false}"))
                .Then(ScriptedModelResponse.Call(listTool, "{}"))
                .Then(ScriptedModelResponse.Text("中间表已清理，Monthly 保持不变。"));

            var result = harness.Run("清理已登记的中间表并检查清理后的工作表清单");

            Assert.IsTrue(
                result.Success,
                result.Content + " | ModelCalls=" + harness.Model.CallCount
                    + " | Tools=" + string.Join(",", harness.Host.InvokedToolNames));
            Assert.AreEqual(3, harness.Model.CallCount);
            CollectionAssert.AreEqual(
                new[] { cleanupTool, listTool },
                harness.Host.InvokedToolNames.ToArray());
            Assert.IsFalse(result.Content.Contains("验证未完成"));
            Assert.IsFalse(result.Content.Contains("Broker 执行失败"));
        }

        [TestMethod]
        public void UntracedAnswerNumber_RequestsOneCorrectionAndDowngradesTier()
        {
            const string aggregateTool = "excel_aggregate";
            var harness = new TurnHarness().WithTool(aggregateTool, true);
            harness.Host.On(aggregateTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "产品 A 为 40，产品 B 为 60。",
                StructuredContentJson = "{\"verification\":{"
                    + "\"tier\":\"A\",\"passed\":true,\"assertionCount\":1,\"coveredRows\":8,"
                    + "\"independent\":true,\"sampled\":false,\"producer\":\"managed\","
                    + "\"basis\":{\"source\":{\"worksheetName\":\"销售明细\"}},"
                    + "\"notes\":[],\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}}}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(aggregateTool, "{\"worksheetName\":\"销售明细\"}"))
                .Then(ScriptedModelResponse.Text("总金额是 999。"))
                .Then(ScriptedModelResponse.Call("runtime.calculate", "{\"expression\":\"40 + 60\"}"))
                .Then(ScriptedModelResponse.Text("更正：总金额应为 100，来源是 A、B 两项金额经计算汇总。"));

            var result = harness.Run("计算总金额");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(4, harness.Model.CallCount);
            StringAssert.Contains(harness.Model.Requests[2].SystemText, "- Number \"999\"");
            StringAssert.Contains(harness.Model.Requests[2].SystemText, "complete, unrounded equation");
            StringAssert.Contains(harness.Model.Requests[2].SystemText, "both operands came from the same trusted message");
            StringAssert.Contains(harness.Model.Requests[2].SystemText, "Repeating an equation without sourced operands");
            StringAssert.Contains(harness.Model.Requests[2].SystemText, "claiming that a tool was used is not evidence");
            StringAssert.Contains(harness.Model.Requests[2].SystemText, "Do not state a number first and then claim the tool calculated it");
            StringAssert.Contains(harness.Model.Requests[2].SystemText, "do not repeat an untraceable number's original value");
            CollectionAssert.Contains(harness.Model.Requests[2].ToolNames, "runtime.calculate");
            StringAssert.Contains(result.Content, "总金额是 999。");
            StringAssert.Contains(result.Content, "**更正**");
            StringAssert.Contains(result.Content, "更正：总金额应为 100");
            StringAssert.Contains(result.Content, "校验：B级：已复核，但仍有局限（最终回答中的数字经过订正）");
            Assert.IsFalse(result.Content.Contains("未溯源数字："));
            var streamedContent = string.Concat(harness.Events.Events
                .Where(evt => evt.EventType == AgentBrokerEventTypes.ContentDelta)
                .Select(evt => evt.Text));
            StringAssert.Contains(streamedContent, "总金额是 999。");
            Assert.AreEqual(1, result.Content.Split(
                new[] { "校验：" },
                System.StringSplitOptions.None).Length - 1);
            Assert.IsFalse(result.Content.Contains("没有登记产出项"));
        }

        [TestMethod]
        public void UnresolvedCorrectionNumber_GetsOneMoreChanceToUseCalculationTool()
        {
            const string aggregateTool = "excel_aggregate";
            var harness = new TurnHarness().WithTool(aggregateTool, true);
            harness.Host.On(aggregateTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "06月平均月薪为 5534.5641025641；05月平均月薪为 4320.4698795181。",
                StructuredContentJson = "{\"verification\":{"
                    + "\"tier\":\"A\",\"passed\":true,\"assertionCount\":25,\"coveredRows\":1000,"
                    + "\"independent\":true,\"sampled\":false,\"producer\":\"com\","
                    + "\"basis\":{\"source\":{\"worksheetName\":\"Sheet1\"}},"
                    + "\"notes\":[],\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}}}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(aggregateTool, "{\"worksheetName\":\"Sheet1\"}"))
                .Then(ScriptedModelResponse.Text("6月比5月高约 1214 元。"))
                .Then(ScriptedModelResponse.Text(
                    "更正：差值为 1214.094223046 元，已由 runtime.calculate 核实。"))
                .Then(ScriptedModelResponse.Call(
                    "runtime.calculate",
                    "{\"expression\":\"5534.5641025641 - 4320.4698795181\"}"))
                .Then(ScriptedModelResponse.Text(
                    "更正：差值为 1214.094223046 元，计算结果来自 runtime.calculate。"));

            var result = harness.Run("比较六月和五月平均月薪差额");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(5, harness.Model.CallCount);
            StringAssert.Contains(harness.Model.Requests[3].SystemText, "Some numbers in the previous correction still cannot be traced");
            StringAssert.Contains(harness.Model.Requests[3].SystemText, "claiming that a tool was used is not evidence");
            CollectionAssert.Contains(harness.Model.Requests[3].ToolNames, "runtime.calculate");
            StringAssert.Contains(result.Content, "**再次核验**");
            StringAssert.Contains(result.Content, "校验：B级：已复核，但仍有局限（最终回答中的数字经过订正）");
            Assert.IsFalse(result.Content.Contains("未溯源数字："));
            var streamedContent = string.Concat(harness.Events.Events
                .Where(evt => evt.EventType == AgentBrokerEventTypes.ContentDelta)
                .Select(evt => evt.Text));
            StringAssert.Contains(streamedContent, "6月比5月高约 1214 元。");
            Assert.AreEqual(1, result.Content.Split(
                new[] { "校验：" },
                System.StringSplitOptions.None).Length - 1);
        }

        [TestMethod]
        public void SourcedSubtractionEquation_CorrectsAnUntracedDifferenceWithoutCalculationTool()
        {
            const string aggregateTool = "excel_aggregate";
            var harness = new TurnHarness().WithTool(aggregateTool, true);
            harness.Host.On(aggregateTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "06月平均月薪为 5534.5641025641；05月平均月薪为 4320.4698795181。",
                StructuredContentJson = "{\"verification\":{"
                    + "\"tier\":\"A\",\"passed\":true,\"assertionCount\":25,\"coveredRows\":1000,"
                    + "\"independent\":true,\"sampled\":false,\"producer\":\"com\","
                    + "\"basis\":{\"source\":{\"worksheetName\":\"Sheet1\"}},"
                    + "\"notes\":[],\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}}}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(aggregateTool, "{\"worksheetName\":\"Sheet1\"}"))
                .Then(ScriptedModelResponse.Text("6月比5月高约 1,214 元。"))
                .Then(ScriptedModelResponse.Text(
                    "更正：5534.5641025641 - 4320.4698795181 = 1214.094223046 元，约 1,214 元。"));

            var result = harness.Run("比较六月和五月平均月薪差额");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, harness.Model.CallCount);
            StringAssert.Contains(result.Content, "校验：B级：已复核，但仍有局限（最终回答中的数字经过订正）");
            Assert.IsFalse(result.Content.Contains("未溯源数字："));
            Assert.IsFalse(harness.Host.InvokedToolNames.Contains("runtime.calculate"));
        }

        [TestMethod]
        public void CorrectionCanRetractAnUnsupportedNumberWithoutRepeatingIt()
        {
            const string aggregateTool = "excel_aggregate";
            var harness = new TurnHarness().WithTool(aggregateTool, true);
            harness.Host.On(aggregateTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "06月平均月薪为 5534.5641025641；05月平均月薪为 4320.4698795181。",
                StructuredContentJson = "{\"verification\":{"
                    + "\"tier\":\"A\",\"passed\":true,\"assertionCount\":25,\"coveredRows\":1000,"
                    + "\"independent\":true,\"sampled\":false,\"producer\":\"com\","
                    + "\"basis\":{\"source\":{\"worksheetName\":\"Sheet1\"}},"
                    + "\"notes\":[],\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}}}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(aggregateTool, "{\"worksheetName\":\"Sheet1\"}"))
                .Then(ScriptedModelResponse.Text("两者相差约 1,214 元。"))
                .Then(ScriptedModelResponse.Text(
                    "更正：该差额无法核实，已从结论中删除。六月平均月薪为 5534.5641025641 元，五月平均月薪为 4320.4698795181 元。"));

            var result = harness.Run("比较六月和五月平均月薪");

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.Content, "校验：B级：已复核，但仍有局限（最终回答中的数字经过订正）");
            var correctionStart = result.Content.IndexOf("**更正**", System.StringComparison.Ordinal);
            Assert.IsTrue(correctionStart >= 0);
            Assert.IsFalse(result.Content.Substring(correctionStart).Contains("1,214"));
            Assert.IsFalse(result.Content.Contains("未溯源数字："));
        }

        [TestMethod]
        public void UntracedAnswerNumber_RemainsVisibleWhenCorrectionCannotSourceIt()
        {
            const string aggregateTool = "excel_aggregate";
            var harness = new TurnHarness().WithTool(aggregateTool, true);
            harness.Host.On(aggregateTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "合计金额为 100。",
                StructuredContentJson = "{\"verification\":{"
                    + "\"tier\":\"A\",\"passed\":true,\"assertionCount\":1,\"coveredRows\":8,"
                    + "\"independent\":true,\"sampled\":false,\"producer\":\"managed\","
                    + "\"basis\":{\"source\":{\"worksheetName\":\"销售明细\"}},"
                    + "\"notes\":[],\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}}}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(aggregateTool, "{\"worksheetName\":\"销售明细\"}"))
                .Then(ScriptedModelResponse.Text("总金额是 999。"))
                .Then(ScriptedModelResponse.Text("更正：目前无法从数据中确认这个数字。"));

            var result = harness.Run("计算总金额");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, harness.Model.CallCount);
            StringAssert.Contains(result.Content, "校验：C级：尚未完成有效验证 —— 有部分结论数字未能找到直接证据");
            StringAssert.Contains(result.Content, "未能找到直接证据的数字：999");
        }

        [TestMethod]
        public void NonExcelTurn_DoesNotReceiveTrustFooter()
        {
            var harness = new TurnHarness();
            harness.Model.Then(ScriptedModelResponse.Text("普通回答，答案是 42。"));

            var result = harness.Run("你好");

            Assert.AreEqual("普通回答，答案是 42。", result.Content);
            Assert.AreEqual(1, harness.Model.CallCount, "非 Excel 数据回合不得触发数字订正。");
        }

        [TestMethod]
        public void RepeatedDeckBuild_WithDifferentArgumentsRequiresUserDecision()
        {
            const string deckBuildTool = "ppt_deck_build";
            var harness = new TurnHarness().WithTool(deckBuildTool, false);
            harness.Model
                .Then(ScriptedModelResponse.Call(deckBuildTool, "{\"deckId\":\"deck-1\",\"workspaceTitle\":\"演示\",\"slideCount\":6}"))
                .Then(ScriptedModelResponse.Call(deckBuildTool, "{\"deckId\":\"deck-1\",\"workspaceTitle\":\"演示\",\"slideCount\":6,\"replace\":true,\"fromIndex\":6,\"toIndex\":6}"))
                .Then(ScriptedModelResponse.Text("已停止重复构建。"));

            var result = harness.Run("制作演示文稿");

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, harness.Host.CountOf(deckBuildTool));
            StringAssert.Contains(harness.Model.Requests.Last().GetToolMessageContent(deckBuildTool), "已经成功构建过");
        }

        [TestMethod]
        public async Task AcceptEdits_StillConfirmsHighRiskDocumentOperations()
        {
            const string toolName = "ppt_deck_build";
            var host = new FakeHostToolInvoker();
            var interaction = new ApprovingUserInteractionHandler();
            var dispatcher = new DefaultToolDispatcher(host, null, null, null, null, interaction);
            var interactionRegisteredWhenEmitted = false;
            dispatcher.CurrentUserInteractionEmitter = (request, cancellationToken) =>
            {
                interactionRegisteredWhenEmitted = interaction.RequestCount == 1;
                return Task.CompletedTask;
            };
            dispatcher.SetPermissionMode(PermissionMode.AcceptEdits);
            var tool = new HostBridgeToolDescriptor
            {
                Name = toolName,
                DisplayName = "Build deck",
                Description = "Build or replace slides",
                InputSchemaJson = "{\"type\":\"object\"}",
                IsReadOnly = false,
                RequiresConfirmation = true,
                RiskLevel = "high"
            };
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [toolName] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-1",
                toolName,
                "{\"deckId\":\"20261018-211613-pq9ehy\",\"workspaceTitle\":\"季度经营汇报\",\"slideCount\":10}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(1, interaction.RequestCount);
            Assert.IsTrue(interactionRegisteredWhenEmitted,
                "The pending response must be registered before the interaction is emitted.");
            using (var interactionData = System.Text.Json.JsonDocument.Parse(interaction.LastRequest.InteractionDataJson))
            {
                Assert.AreEqual(
                    "构建工作区「季度经营汇报」；ID 20261018-211613-pq9ehy；共 10 页。",
                    interactionData.RootElement.GetProperty("operationSummary").GetString());
            }
            Assert.AreEqual(1, host.CountOf(toolName));
        }

        [TestMethod]
        public async Task AcceptEdits_StillConfirmsCriticalRiskTools()
        {
            const string toolName = "runtime_powershell";
            var host = new FakeHostToolInvoker();
            var interaction = new ApprovingUserInteractionHandler();
            var dispatcher = new DefaultToolDispatcher(host, null, null, null, null, interaction);
            dispatcher.SetPermissionMode(PermissionMode.AcceptEdits);
            var tool = new HostBridgeToolDescriptor
            {
                Name = toolName,
                DisplayName = "Run PowerShell",
                Description = "Run a PowerShell script",
                InputSchemaJson = "{\"type\":\"object\"}",
                IsReadOnly = false,
                RequiresConfirmation = true,
                RiskLevel = "critical"
            };
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { [toolName] = tool };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "call-2",
                toolName,
                "{}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(1, interaction.RequestCount);
            Assert.AreEqual(1, host.CountOf(toolName));
        }

        /// <summary>
        /// 回归 2026-09-09 实测事故：工具失败但错误信息没进模型上下文，
        /// 模型只看到备份提示，凭空编造了失败原因并写进最终回答。
        /// </summary>
        [TestMethod]
        public void ToolFailure_ErrorMessageReachesModelContext()
        {
            var harness = new TurnHarness().WithTool(VbaTool, false);
            harness.Host.Fails(
                VbaTool,
                "excel.execute_vba_failed",
                "无法运行注入的 VBA 宏。最常见原因是代码编译失败。",
                "A pre-execution backup was created: C:\\backup\\161919.xlsx");
            harness.Model
                .Then(ScriptedModelResponse.Call(VbaTool, "{\"code\":\"Sub X()\\nEnd Sub\"}"))
                .Then(ScriptedModelResponse.Text("已说明失败原因。"));

            harness.Run("跑个宏");

            var lastRequest = harness.Model.Requests.Last();
            var toolMessage = lastRequest.GetToolMessageContent(VbaTool);
            Assert.IsNotNull(toolMessage, "模型上下文里必须存在该工具的结果消息。");
            StringAssert.Contains(toolMessage, "无法运行注入的 VBA 宏",
                "工具失败时错误原因必须进入模型上下文，否则模型只能靠猜。");
            StringAssert.Contains(toolMessage, "backup",
                "备份提示同样要保留，两者不是二选一。");
        }

        /// <summary>
        /// 回归 D3：工具失败但带着一份看起来正常的 structured JSON 时，
        /// 旧实现会直接把 JSON 当成工具消息返回，ErrorMessage 被整段吞掉。
        /// </summary>
        [TestMethod]
        public void ToolFailure_StructuredContentDoesNotSuppressErrorMessage()
        {
            var harness = new TurnHarness().WithTool(VbaTool, false);
            harness.Host.Fails(
                VbaTool,
                "excel.execute_vba_expectation_failed",
                "expect 校验未通过：实际 3 行，期望至少 10 行。",
                "步骤：拆分明细\n执行成功：是",
                "{\"success\":true,\"stageName\":\"拆分明细\",\"expectationsPassed\":false}");
            harness.Model
                .Then(ScriptedModelResponse.Call(VbaTool, "{\"code\":\"Sub X()\\nEnd Sub\"}"))
                .Then(ScriptedModelResponse.Text("已修正。"));

            harness.Run("分段算一下");

            var toolMessage = harness.Model.Requests.Last().GetToolMessageContent(VbaTool);
            Assert.IsNotNull(toolMessage);
            StringAssert.Contains(toolMessage, "expect 校验未通过",
                "结构化内容不得挤掉失败原因，否则模型会把 success:true 读成任务完成。");
        }

        /// <summary>
        /// 回归 D4：宿主能力集取不到时不能静默降级成"零工具"继续跑，
        /// 否则模型没有任何 Excel 工具，只能凭空编造答案，用户看不出异常。
        /// </summary>
        [TestMethod]
        public void CapabilitiesFailure_DoesNotSilentlyDegrade()
        {
            var harness = new TurnHarness { CapabilitiesThrow = true }.WithTool(ReadTool, true);
            harness.Model.ThenAlways(i => ScriptedModelResponse.Text("表里一共有 42 行数据。"));

            var result = harness.Run("这个表有多少行？");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("host.capabilities_unavailable", result.ErrorCode);
            Assert.AreEqual(0, harness.Model.CallCount, "拿不到工具能力集时不应该再去问模型。");
        }

        [TestMethod]
        public void RoundLimit_StopsToolCallsAndReportsMaxToolRounds()
        {
            var harness = new TurnHarness { MaxToolRoundCount = 3 }.WithTool(ReadTool, true);
            // 每轮换一次参数，避免被 LoopGuard 先行拦下——这里要验证的是轮次上限本身。
            harness.Model.ThenAlways(i => ScriptedModelResponse.Call(ReadTool, "{\"range\":\"A" + (i + 1) + "\"}"));

            var result = harness.Run("一直翻页读");

            Assert.AreEqual("max_tool_rounds", result.FinishReason);
            Assert.AreEqual(3, harness.Host.CountOf(ReadTool), "达到上限后不得再发起工具调用。");
        }

        [TestMethod]
        public void RoundLimit_FinalTurnHasNoToolsAndDemandsSummary()
        {
            var harness = new TurnHarness { MaxToolRoundCount = 2 }.WithTool(ReadTool, true);
            harness.Model.ThenAlways(i => i < 2
                ? ScriptedModelResponse.Call(ReadTool, "{\"range\":\"A" + (i + 1) + "\"}")
                : ScriptedModelResponse.Text("基于已有证据总结。"));

            var result = harness.Run("一直翻页读");

            var finalRequest = harness.Model.Requests.Last();
            Assert.AreEqual(0, finalRequest.ToolNames.Count, "收尾轮必须不下发任何工具。");
            StringAssert.Contains(finalRequest.SystemText, "round limit");
            StringAssert.StartsWith(result.Content, "基于已有证据总结。");
            StringAssert.Contains(result.Content, "校验：B级：已复核，但仍有局限");
            StringAssert.Contains(result.Content, "读取 excel_read_range");
        }

        [TestMethod]
        public void RoundLimit_SummaryLeakingDsmlMarkup_RetriesWithoutToolsAndDropsMarkup()
        {
            var harness = new TurnHarness { MaxToolRoundCount = 2 }.WithTool(ReadTool, true);
            harness.Model.ThenAlways(i =>
            {
                if (i < 2) return ScriptedModelResponse.Call(ReadTool, "{\"range\":\"A" + (i + 1) + "\"}");
                if (i == 2) return ScriptedModelResponse.Text("<｜｜DSML｜｜ invoke name=\"read\">");
                return ScriptedModelResponse.Text("基于已有证据总结。");
            });

            var result = harness.Run("一直翻页读");

            Assert.AreEqual(4, harness.Model.CallCount, "收尾轮泄漏后应重试一次再收敛。");
            foreach (var request in harness.Model.Requests.Skip(2))
            {
                Assert.AreEqual(0, request.ToolNames.Count, "收尾轮及其重试都不得下发工具。");
            }
            StringAssert.Contains(harness.Model.Requests.Last().SystemText, "DSML");
            Assert.AreEqual("max_tool_rounds", result.FinishReason);
            Assert.IsTrue(result.Success);
            StringAssert.StartsWith(result.Content, "基于已有证据总结。");
            Assert.IsFalse(result.Content.Contains("DSML"), "泄漏的标记不应原样进入最终正文。");
        }

        [TestMethod]
        public void RoundLimit_SummaryAlwaysLeaksDsmlMarkup_FinishesAsUnexecuted()
        {
            var harness = new TurnHarness { MaxToolRoundCount = 2 }.WithTool(ReadTool, true);
            harness.Model.ThenAlways(i => i < 2
                ? ScriptedModelResponse.Call(ReadTool, "{\"range\":\"A" + (i + 1) + "\"}")
                : ScriptedModelResponse.Text("<｜｜DSML｜｜ invoke name=\"read\">"));

            var result = harness.Run("一直翻页读");

            Assert.AreEqual(5, harness.Model.CallCount, "两轮工具调用后，收尾轮最多重试两次（共三次收尾请求）。");
            Assert.IsFalse(result.Success);
            Assert.AreEqual("tool_call_unexecuted", result.FinishReason);
            StringAssert.Contains(result.Content, "工具调用未被执行");
            StringAssert.Contains(result.Content, "read");
            StringAssert.Contains(result.Content, "本回合进度（程序统计）");
            StringAssert.Contains(result.Content, ReadTool + " ×2");
            StringAssert.Contains(result.Content, "工作簿");
            Assert.IsFalse(result.Content.Contains("工作区"), "Excel 不应出现 PPT 的“工作区”说法。");
            Assert.IsFalse(result.Content.Contains("DSML"), "泄漏的标记不应推给用户。");
        }

        [TestMethod]
        public void RoundLimit_EmptySummary_FallsBackToProgramProgress()
        {
            var harness = new TurnHarness { MaxToolRoundCount = 2 }.WithTool(ReadTool, true);
            harness.Model.ThenAlways(i => i < 2
                ? ScriptedModelResponse.Call(ReadTool, "{\"range\":\"A" + (i + 1) + "\"}")
                : ScriptedModelResponse.Text(string.Empty));

            var result = harness.Run("一直翻页读");

            Assert.AreEqual("max_tool_rounds", result.FinishReason);
            StringAssert.Contains(result.Content, "未能给出总结");
            StringAssert.Contains(result.Content, ReadTool + " ×2");
        }

        [TestMethod]
        public void RoundLimit_SummaryWithTextAndDsml_KeepsTextWithoutRetry()
        {
            var harness = new TurnHarness { MaxToolRoundCount = 2 }.WithTool(ReadTool, true);
            harness.Model.ThenAlways(i => i < 2
                ? ScriptedModelResponse.Call(ReadTool, "{\"range\":\"A" + (i + 1) + "\"}")
                : ScriptedModelResponse.Text("已读完两段。\n<｜｜DSML｜｜ calls>\n<｜｜DSML｜｜ invoke name=\"read\"></｜｜DSML｜｜ invoke>\n</｜｜DSML｜｜ calls>"));

            var result = harness.Run("一直翻页读");

            Assert.AreEqual(3, harness.Model.CallCount, "剥掉标记后仍有正文，不应再重试。");
            Assert.AreEqual("max_tool_rounds", result.FinishReason);
            StringAssert.StartsWith(result.Content, "已读完两段。");
            Assert.IsFalse(result.Content.Contains("DSML"));
        }

        [TestMethod]
        public void RoundBudget_WarnsModelBeforeLimit()
        {
            var harness = new TurnHarness { MaxToolRoundCount = 8 }.WithTool(ReadTool, true);
            harness.Model.ThenAlways(i => i < 8
                ? ScriptedModelResponse.Call(ReadTool, "{\"range\":\"A" + (i + 1) + "\"}")
                : ScriptedModelResponse.Text("总结。"));

            harness.Run("一直翻页读");

            Assert.IsFalse(harness.Model.Requests[2].SystemText.Contains("tool-call round(s) remain"), "剩余轮次充足时不提醒。");
            StringAssert.Contains(harness.Model.Requests[3].SystemText, "Only 5 tool-call round(s) remain");
        }

        [TestMethod]
        public void AskMode_ExposesReadOnlyToolsOnly()
        {
            var harness = new TurnHarness { InteractiveMode = "ask" }
                .WithTool(ReadTool, true)
                .WithTool(WriteTool, false)
                .WithTool(VbaTool, false);
            harness.Model.Then(ScriptedModelResponse.Text("只读回答。"));

            harness.Run("这个表有多少行？");

            var toolNames = harness.Model.Requests.Single().ToolNames;
            CollectionAssert.Contains(toolNames, ReadTool);
            CollectionAssert.DoesNotContain(toolNames, WriteTool);
            CollectionAssert.DoesNotContain(toolNames, VbaTool);
        }

        [TestMethod]
        public void ConsecutiveFailures_StopTurnAndReportFailure()
        {
            var harness = new TurnHarness().WithTool(WriteTool, false);
            harness.Host.Fails(WriteTool, "excel.write_failed", "写入失败。", string.Empty);
            harness.Model.ThenAlways(i => ScriptedModelResponse.Call(WriteTool, "{\"range\":\"A" + (i + 1) + "\"}"));

            var result = harness.Run("反复写");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("consecutive_failures", result.FinishReason);
            Assert.AreEqual(3, harness.Host.CountOf(WriteTool), "连续失败达上限后必须立即停手。");
        }

        /// <summary>
        /// 回归 D2：验证重试的五个方法长期零调用点——验证失败后不回退重做，
        /// 只把一段文本丢给模型自由发挥。这里锁住"失败必须触发回退并要求重做"。
        /// </summary>
        [TestMethod]
        public void VerificationFailure_RewindsToMutationAndDemandsRetry()
        {
            var harness = new TurnHarness()
                .WithTool(WriteTool, false)
                .WithTool(ReadTool, true)
                .WithSkill(BuildVerifiedWriteSkill());
            harness.Host.Fails(ReadTool, "excel.read_failed", "读回的值与写入不一致。", string.Empty);
            harness.Model
                .Then(ScriptedModelResponse.Call(WriteTool, "{\"range\":\"C1\"}"))
                .Then(ScriptedModelResponse.Call(ReadTool, "{\"range\":\"C1\"}"))
                .Then(ScriptedModelResponse.Text("已修正并复核。"));

            var result = harness.Run("把 C1 写成 100 并核对");

            var systemText = harness.Model.Requests.Last().SystemText;
            StringAssert.Contains(systemText, "Verification did not succeed",
                "验证失败必须回到模型上下文并要求重做，而不是让模型自由发挥。");
            StringAssert.Contains(systemText, "Retry: 1/2", "必须告知模型已用掉的重试次数。");
            Assert.IsTrue(result.Success, "受控的验证重试不是失控失败，不应把整回合判负。");
        }

        [TestMethod]
        public void ExcelWriteStage_AllowsMultipleWritesUntilLinkedVerification()
        {
            const string assertTool = "excel_assert";
            var harness = new TurnHarness()
                .WithTool(WriteTool, false)
                .WithTool(assertTool, true)
                .WithSkill(BuildStageVerifiedWriteSkill());
            harness.Host.On(assertTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "断言通过。",
                StructuredContentJson = "{\"verification\":{"
                    + "\"tier\":\"A\",\"passed\":true,\"assertionCount\":2,\"coveredRows\":2,"
                    + "\"independent\":true,\"sampled\":false,\"producer\":\"com\","
                    + "\"basis\":{\"source\":{\"worksheetName\":\"Sheet1\"}},"
                    + "\"notes\":[],\"details\":{\"items\":[{},{}],\"diffs\":[],\"diffCount\":0}}}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(WriteTool, "{\"worksheetName\":\"Sheet1\",\"address\":\"C1\"}"))
                .Then(ScriptedModelResponse.Call(WriteTool, "{\"worksheetName\":\"Sheet1\",\"address\":\"C2\"}"))
                .Then(ScriptedModelResponse.Call(assertTool,
                    "{\"assertions\":["
                        + "{\"target\":{\"worksheetName\":\"Sheet1\",\"address\":\"C1\"}},"
                        + "{\"target\":{\"worksheetName\":\"Sheet1\",\"address\":\"C2\"}}]}"))
                .Then(ScriptedModelResponse.Text("写入并验证完成。"));

            var result = harness.Run("把 C1 和 C2 都写入并确认结果");

            Assert.AreEqual(2, harness.Host.CountOf(WriteTool),
                "同一 mutation 阶段的多个写入应可先完成，再统一通过关联验证。");
            var completedEvent = harness.Events.Events.Single(
                evt => evt.EventType == AgentBrokerEventTypes.TurnCompleted);
            Assert.IsNotNull(completedEvent.QualitySummary);
            Assert.AreEqual("A", completedEvent.QualitySummary.TrustTier);
            StringAssert.Contains(result.Content, "校验：A级：全量独立复核通过");
            Assert.IsTrue(result.Success);
        }

        [TestMethod]
        public void ExcelWriteStages_AllowAdditionalWritesAfterEachLinkedVerification()
        {
            const string assertTool = "excel_assert";
            var harness = new TurnHarness()
                .WithTool(WriteTool, false)
                .WithTool(assertTool, true)
                .WithSkill(BuildStageVerifiedWriteSkill());
            harness.Host.On(assertTool, request => new HostToolInvocationResult
            {
                ToolCallId = request.ToolCallId,
                ToolName = request.ToolName,
                Success = true,
                Content = "断言通过。",
                StructuredContentJson = "{\"verification\":{"
                    + "\"tier\":\"A\",\"passed\":true,\"assertionCount\":1,\"coveredRows\":1,"
                    + "\"independent\":true,\"sampled\":false,\"producer\":\"com\","
                    + "\"basis\":{\"source\":{\"worksheetName\":\"Sheet1\"}},"
                    + "\"notes\":[],\"details\":{\"items\":[{}],\"diffs\":[],\"diffCount\":0}}}"
            });
            harness.Model
                .Then(ScriptedModelResponse.Call(WriteTool, "{\"worksheetName\":\"Sheet1\",\"address\":\"C1\"}"))
                .Then(ScriptedModelResponse.Call(assertTool,
                    "{\"assertions\":[{\"target\":{\"worksheetName\":\"Sheet1\",\"address\":\"C1\"}}]}"))
                .Then(ScriptedModelResponse.Call(WriteTool, "{\"worksheetName\":\"Sheet1\",\"address\":\"C2\"}"))
                .Then(ScriptedModelResponse.Call(assertTool,
                    "{\"assertions\":[{\"target\":{\"worksheetName\":\"Sheet1\",\"address\":\"C2\"}}]}"))
                .Then(ScriptedModelResponse.Text("两次写入均已验证。"));

            var result = harness.Run("把 C1 和 C2 分别写入并逐项确认结果");

            Assert.AreEqual(2, harness.Host.CountOf(WriteTool),
                "完成前一阶段的关联验证后，应允许追加写入阶段。");
            var completedEvent = harness.Events.Events.Single(
                evt => evt.EventType == AgentBrokerEventTypes.TurnCompleted);
            Assert.IsNotNull(completedEvent.QualitySummary);
            Assert.AreEqual("A", completedEvent.QualitySummary.TrustTier);
            StringAssert.Contains(result.Content, "校验：A级：全量独立复核通过");
            Assert.IsTrue(result.Success);
        }

        /// <summary>
        /// 受控重试必须有尽头：用完预算后改为要求模型说明局限，而不是继续无限重做。
        /// </summary>
        [TestMethod]
        public void VerificationFailure_StopsRetryingAfterBudgetExhausted()
        {
            var harness = new TurnHarness()
                .WithTool(WriteTool, false)
                .WithTool(ReadTool, true)
                .WithSkill(BuildVerifiedWriteSkill());
            harness.Host.Fails(ReadTool, "excel.read_failed", "读回的值与写入不一致。", string.Empty);
            harness.Model.ThenAlways(i => i == 0
                ? ScriptedModelResponse.Call(WriteTool, "{\"range\":\"C1\"}")
                : (i <= 3
                    ? ScriptedModelResponse.Call(ReadTool, "{\"range\":\"C" + i + "\"}")
                    : ScriptedModelResponse.Text("说明局限。")));

            harness.Run("把 C1 写成 100 并核对");

            var systemText = harness.Model.Requests.Last().SystemText;
            StringAssert.Contains(systemText, "Verification retry limit reached",
                "重试预算耗尽后必须明确禁止再试，否则受控重试就变成了新的死循环。");
        }

        /// <summary>
        /// 回归 P1-3：pending verification 的 continue 不消耗 round，模型若一直不调验证工具，
        /// 完成步数永远不变 —— 旧实现在这里无限循环。催促必须有次数上限。
        /// </summary>
        [TestMethod]
        public void PendingVerification_StopsNudgingAndFinishesWithNotice()
        {
            var harness = new TurnHarness()
                .WithTool(WriteTool, false)
                .WithTool(ReadTool, true)
                .WithSkill(BuildVerifiedWriteSkill());
            harness.Model.ThenAlways(i => i == 0
                ? ScriptedModelResponse.Call(WriteTool, "{\"range\":\"C1\"}")
                : ScriptedModelResponse.Text("已经写好了。"));

            var result = harness.Run("把 C1 写成 100 并核对");

            Assert.AreEqual("pending_step_unresolved", result.FinishReason);
            Assert.AreEqual(4, harness.Model.CallCount, "催促两次后必须收尾，不能无限追问。");
            StringAssert.Contains(result.Content, "验证未完成",
                "放行收尾时必须如实告知未验证，不能让未完成的步骤悄悄消失。");
            Assert.IsFalse(result.Success, "验证步没做完不算成功。");
        }

        /// <summary>写入 + 强制验证的两步计划，是 RewindForVerificationFailure 生效的最小结构。</summary>
        private static SkillDefinition BuildVerifiedWriteSkill()
        {
            return new SkillDefinition
            {
                Name = "test-verified-write",
                DisplayName = "Test Verified Write",
                Category = "write",
                Description = "写入后必须验证。",
                AllowedToolNames = new List<string> { WriteTool, ReadTool },
                PreferredToolNames = new List<string> { WriteTool },
                VerificationToolNames = new List<string> { ReadTool },
                RequireVerification = true,
                MaxToolRoundCount = 12
            };
        }

        private static SkillDefinition BuildStageVerifiedWriteSkill()
        {
            var skill = new SkillDefinition
            {
                Name = "test-stage-verified-write",
                DisplayName = "Test Stage Verified Write",
                Category = "write",
                Description = "写入后必须通过针对该写入阶段的断言。",
                AllowedToolNames = new List<string> { WriteTool, "excel_assert" },
                PreferredToolNames = new List<string> { WriteTool },
                VerificationToolNames = new List<string> { "excel_assert" },
                RequireVerification = true,
                MaxToolRoundCount = 12
            };
            skill.Metadata[SkillMetadataKeys.HostKind] = "excel";
            return skill;
        }

        /// <summary>
        /// 回归 2026-09-14 实测假死：模型输出被 max_tokens 截断成空文本时，
        /// 旧实现把它当作"成功收尾"静默结束，面板无任何输出。这里锁住"截断 → 重试一次"。
        /// </summary>
        [TestMethod]
        public void TruncatedEmptyResponse_RetriesOnceThenRecovers()
        {
            var harness = new TurnHarness().WithTool(ReadTool, true);
            harness.Model
                .Then(ScriptedModelResponse.TruncatedEmpty())
                .Then(ScriptedModelResponse.Text("完整回答。"));

            var result = harness.Run("分析一下");

            Assert.IsTrue(result.Success);
            Assert.AreEqual("完整回答。", result.Content);
            StringAssert.Contains(
                harness.Model.Requests[1].SystemText,
                "Your previous response was truncated before any text was produced",
                "重试轮必须让模型知道上一轮被截断。");
        }

        /// <summary>
        /// 截断重试仍有尽头：重试后仍产出空文本时，必须以失败 + 明确提示收尾，
        /// 绝不能静默返回 Success=true + 空正文（用户看到的就是"假死"）。
        /// </summary>
        [TestMethod]
        public void TruncatedEmptyResponse_Persists_FailsWithNotice()
        {
            var harness = new TurnHarness().WithTool(ReadTool, true);
            harness.Model.ThenAlways(i => ScriptedModelResponse.TruncatedEmpty());

            var result = harness.Run("分析一下");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("max_tokens", result.FinishReason);
            StringAssert.Contains(result.Content, "被截断", "失败收尾必须给出明确提示。");
            Assert.AreEqual(2, harness.Model.CallCount, "一次重试后必须收尾，不能无限重试。");
        }

        private sealed class ApprovingUserInteractionHandler : IUserInteractionHandler
        {
            public int RequestCount { get; private set; }

            public AgentBrokerToolUserInteractionRequest LastRequest { get; private set; }

            public Task<AgentBrokerToolUserInteractionResponse> RequestUserInteractionAsync(
                AgentBrokerToolUserInteractionRequest request,
                CancellationToken cancellationToken)
            {
                RequestCount++;
                LastRequest = request;
                return Task.FromResult(new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = request.ToolCallId
                });
            }
        }
    }
}
