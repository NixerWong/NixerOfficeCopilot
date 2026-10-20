using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Turn
{
    /// <summary>
    /// 回归 2026-09-14 实测：用户说"分析要有步骤，可验证"时，"步骤"被误判为
    /// 追问上一轮过程，导致工具被禁用、模型编造工具调用文本漏到面板。
    /// </summary>
    [TestClass]
    public class ConversationHistoryFollowUpPolicyTests
    {
        [TestMethod]
        public void FreshAnalysisRequestWithStepWord_IsNotFollowUp()
        {
            Assert.IsFalse(ConversationHistoryFollowUpPolicy.IsProcessFollowUp(
                "帮我分析一下表格，看看哪个月份出生的人，平均月薪最高？分析要有步骤，可验证。"));
        }

        [TestMethod]
        public void ToolMentionInFreshWriteRequest_IsNotFollowUp()
        {
            Assert.IsFalse(ConversationHistoryFollowUpPolicy.IsProcessFollowUp(
                "请创建输出表并写入标记；不要把“工具调用成功”当作读回验证。"));
        }

        [TestMethod]
        public void ActionReferringToPreviousDryRun_IsNotFollowUp()
        {
            Assert.IsFalse(ConversationHistoryFollowUpPolicy.IsProcessFollowUp(
                "现在执行刚才预演的清理。完成后只读列出工作表，"
                + "确认登记的 _wip_01_probe 已删除；如果目标不符就停止。"));
        }

        [TestMethod]
        public void ExplicitQuestionAboutPreviousSteps_IsFollowUp()
        {
            Assert.IsTrue(ConversationHistoryFollowUpPolicy.IsProcessFollowUp("你刚才的步骤是什么？"));
            Assert.IsTrue(ConversationHistoryFollowUpPolicy.IsProcessFollowUp("复盘一下上一轮的过程"));
        }

        [TestMethod]
        public void UnambiguousProcessKeywords_StillDetected()
        {
            Assert.IsTrue(ConversationHistoryFollowUpPolicy.IsProcessFollowUp("你刚才用了哪些工具调用？"));
            Assert.IsTrue(ConversationHistoryFollowUpPolicy.IsProcessFollowUp("回顾一下之前的操作"));
        }

        [DataTestMethod]
        [DataRow("好的")]
        [DataRow("好的，写进去")]
        [DataRow("就这样")]
        [DataRow("继续")]
        [DataRow("改成静态值")]
        public void ExecutionContinuation_IsRecognized(string input)
        {
            Assert.IsTrue(ConversationHistoryFollowUpPolicy.IsSkillPlanContinuation(input));
        }

        [DataTestMethod]
        [DataRow("请重新分析一月和二月的销售")]
        [DataRow("帮我检查当前工作表的数据")]
        [DataRow("你好，能帮我看看吗")]
        public void NewRequest_IsNotExecutionContinuation(string input)
        {
            Assert.IsFalse(ConversationHistoryFollowUpPolicy.IsSkillPlanContinuation(input));
        }
    }
}
