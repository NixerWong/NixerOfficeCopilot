using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public class RoundLimitProgressSummaryTests
    {
        [TestMethod]
        public void Build_ReportsDistributionAskUserAndRecentWrites()
        {
            var traces = new List<ToolCallTrace>
            {
                new ToolCallTrace("word_read_paragraphs", "{}", true, null, null),
                new ToolCallTrace("runtime.ask_user_question", "{}", true, null, null, "格式要求：本科论文（宋体小四）"),
                new ToolCallTrace("word_read_paragraphs", "{}", true, null, null),
                new ToolCallTrace("word_apply_style", "{\"styleName\": \"Heading 1\"}", false, "word.error", "样式不存在"),
                new ToolCallTrace("word_format_paragraphs", "{\"defaultRole\":\"body\"}", true, null, null)
            };

            var text = RoundLimitProgressSummary.Build(
                traces,
                name => name == "word_apply_style" || name == "word_format_paragraphs",
                "Word",
                300,
                new List<string> { "word_select_paragraph" });

            StringAssert.Contains(text, "300 轮");
            StringAssert.Contains(text, "word_select_paragraph");
            StringAssert.Contains(text, "工具调用 5 次（成功 4，失败 1），其中写操作 2 次");
            StringAssert.Contains(text, "word_read_paragraphs ×2");
            StringAssert.Contains(text, "宋体小四");
            StringAssert.Contains(text, "✗ word_apply_style {\"styleName\":\"Heading1\"}（样式不存在）");
            StringAssert.Contains(text, "✓ word_format_paragraphs");
            StringAssert.Contains(text, "保留在文档中");
            Assert.IsFalse(text.Contains("工作区"), "Word 不应出现 PPT 的“工作区”说法。");
        }

        [DataTestMethod]
        [DataRow("PowerPoint", "工作区")]
        [DataRow("Word", "文档")]
        [DataRow("Excel", "工作簿")]
        [DataRow("", "已保留")]
        public void BuildResumeHint_IsHostSpecific(string host, string expected)
        {
            StringAssert.Contains(RoundLimitProgressSummary.BuildResumeHint(host), expected);
        }

        [TestMethod]
        public void Build_WithoutTraces_StillExplainsAndHints()
        {
            var text = RoundLimitProgressSummary.Build(null, null, "Excel", 20, null);

            StringAssert.Contains(text, "20 轮");
            StringAssert.Contains(text, "未能给出总结");
            Assert.IsFalse(text.Contains("本回合进度"));
            StringAssert.Contains(text, "工作簿");
        }
    }
}
