using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Runtime.Context;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.Context
{
    [TestClass]
    public class WorkbookMapPromptTests
    {
        [TestMethod]
        public void HostContextPromptIncludesWorkbookSheetMap()
        {
            var prompt = DefaultContextBuilder.BuildModelHostContextPrompt(
                new AgentBrokerTurnRequest { InteractiveMode = "agent" },
                new HostContextSnapshotDto
                {
                    HostApplicationName = "Excel",
                    DocumentName = "sales.xlsx",
                    WorkbookMapSummary = "工作表 2 张：\n"
                        + "表头相似候选组（列名集合相似度≥80%）：\n- header-group-1 1月、2月；最低相似度 100%\n"
                        + "跨表共享列候选（唯一性未验证）：\n- 订单号：1月、客户\n"
                        + "- 1月 [可见]; UsedRange=A1:F20; 表头=订单号 | 金额\n"
                        + "- 汇总 [可见]; UsedRange=A1:B10"
                });

            StringAssert.Contains(prompt, "工作簿工作表地图");
            StringAssert.Contains(prompt, "订单号 | 金额");
            StringAssert.Contains(prompt, "表头相似候选组");
            StringAssert.Contains(prompt, "唯一性未验证");
            StringAssert.Contains(prompt, "汇总");
        }

        [TestMethod]
        public void SnapshotContainingOnlyWorkbookMapIsConsideredAvailable()
        {
            Assert.IsTrue(DefaultContextBuilder.HasSnapshotData(
                new HostContextSnapshotDto { WorkbookMapSummary = "工作表 1 张：\n- Data" }));
        }

        [TestMethod]
        public void StructuredWorkbookMapIsRenderedAndSerializedAcrossContextBoundary()
        {
            var snapshot = new HostContextSnapshotDto
            {
                HostApplicationName = "Excel",
                DocumentName = "sales.xlsx",
                WorkbookMap = new WorkbookMapSnapshotDto
                {
                    TotalWorksheetCount = 2,
                    Worksheets = new List<WorkbookWorksheetSnapshotDto>
                    {
                        new WorkbookWorksheetSnapshotDto
                        {
                            Name = "January",
                            CodeName = "Sheet1",
                            Index = 1,
                            IsVisible = true,
                            UsedRangeAddress = "A1:C20",
                            UsedRangeRows = 20,
                            UsedRangeColumns = 3,
                            HeaderSignature = "Order ID | Date | Revenue",
                            HeaderColumns = new List<string> { "Order ID", "Date", "Revenue" },
                            RoleGuess = "data"
                        }
                    },
                    WorksheetGroups = new List<WorkbookWorksheetHeaderGroupSnapshotDto>
                    {
                        new WorkbookWorksheetHeaderGroupSnapshotDto
                        {
                            GroupId = "header-group-1",
                            MinimumHeaderSimilarity = 0.8m,
                            WorksheetNames = new List<string> { "January", "February" }
                        }
                    },
                    SharedColumnCandidates = new List<WorkbookSharedColumnCandidateSnapshotDto>
                    {
                        new WorkbookSharedColumnCandidateSnapshotDto
                        {
                            ColumnName = "Order ID",
                            WorksheetNames = new List<string> { "January", "Customers" },
                            UniquenessVerified = false
                        }
                    }
                }
            };

            var prompt = DefaultContextBuilder.BuildModelHostContextPrompt(
                new AgentBrokerTurnRequest { InteractiveMode = "agent" },
                snapshot);
            var roundTrip = JsonSerializer.Deserialize<HostContextSnapshotDto>(
                JsonSerializer.Serialize(snapshot));

            StringAssert.Contains(prompt, "工作簿工作表地图");
            StringAssert.Contains(prompt, "CodeName=Sheet1");
            StringAssert.Contains(prompt, "Order ID | Date | Revenue");
            StringAssert.Contains(prompt, "header-group-1");
            StringAssert.Contains(prompt, "唯一性未验证");
            Assert.IsTrue(DefaultContextBuilder.HasSnapshotData(roundTrip));
            Assert.AreEqual(
                "Order ID",
                roundTrip.WorkbookMap.SharedColumnCandidates[0].ColumnName);

            var hostContextOutcome = DefaultToolDispatcher.ExecuteGetHostContext(
                "context-1",
                "runtime.get_host_context",
                roundTrip);
            StringAssert.Contains(hostContextOutcome.Content, "January");
            StringAssert.Contains(hostContextOutcome.StructuredContentJson, "SharedColumnCandidates");
        }

        [TestMethod]
        public void SnapshotContainingOnlyStructuredWorkbookMapIsConsideredAvailable()
        {
            Assert.IsTrue(DefaultContextBuilder.HasSnapshotData(
                new HostContextSnapshotDto
                {
                    WorkbookMap = new WorkbookMapSnapshotDto
                    {
                        TotalWorksheetCount = 1
                    }
                }));
        }

        [TestMethod]
        public void ExcelExecutionModePromptRequiresInteractionUiForUserChoices()
        {
            var prompt = DefaultContextBuilder.BuildExcelExecutionModePrompt(
                new HostContextSnapshotDto { HostApplicationName = "Microsoft Excel" });

            StringAssert.Contains(prompt, "runtime.ask_user_question");
            StringAssert.Contains(prompt, "clarifications");
            StringAssert.Contains(prompt, "候选只是模型预告");
            StringAssert.Contains(prompt, "HostBridge 预检结果才是实际级别");
            StringAssert.Contains(prompt, "单候选预告不能作为最终结论");
            StringAssert.Contains(prompt, "必须调用相应的目标写入工具触发宿主预检");
            StringAssert.Contains(prompt, "writeEffect.actualWriteEffect");
            StringAssert.Contains(prompt, "绝不能再报告“未写入”或“已取消”");
            StringAssert.Contains(prompt, "绝不能只在最终文本中列出选项");
            StringAssert.Contains(prompt, "必须遵守用户的停止条件");
        }

        [TestMethod]
        public void StructuredWorkbookMapSummaryRespectsCharacterBudget()
        {
            var workbookMap = new WorkbookMapSnapshotDto
            {
                TotalWorksheetCount = 50
            };
            for (var index = 1; index <= 50; index++)
            {
                workbookMap.Worksheets.Add(new WorkbookWorksheetSnapshotDto
                {
                    Name = "Worksheet" + index,
                    Index = index,
                    IsVisible = true,
                    UsedRangeAddress = "A1:Z1000",
                    UsedRangeRows = 1000,
                    UsedRangeColumns = 26,
                    HeaderSignature = new string('中', 300)
                });
            }

            var summary = DefaultContextBuilder.BuildWorkbookMapSummary(workbookMap);
            var tokenLimitedSummary = DefaultContextBuilder.LimitWorkbookMapSummary(summary);

            Assert.IsTrue(summary.Length <= 4000);
            StringAssert.Contains(summary, "工作簿地图摘要已截断");
            Assert.IsTrue(new RoughTokenEstimator().EstimateTokens(tokenLimitedSummary) <= 1500);
            StringAssert.Contains(tokenLimitedSummary, "估算 token 预算截断");
        }
    }
}
