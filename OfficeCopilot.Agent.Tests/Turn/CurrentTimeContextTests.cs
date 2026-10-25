using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.Agent.Tests.Harness;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class CurrentTimeContextTests
    {
        // 2026-10-12 是星期一。
        private static readonly DateTimeOffset Monday1857 = new DateTimeOffset(2026, 10, 12, 18, 57, 0, TimeSpan.FromHours(8));

        [TestMethod]
        public void Formatter_IncludesDateTimeWeekdayAndPositiveOffset()
        {
            Assert.AreEqual(
                "2026-10-12 18:57（星期一），时区 UTC+08:00",
                CurrentTimeFormatter.FormatLine(Monday1857));
        }

        [TestMethod]
        public void Formatter_UsesSignedOffsetForWesternTimeZones()
        {
            var newYork = new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.FromHours(-5));

            Assert.AreEqual(
                "2026-10-12 06:00（星期一），时区 UTC-05:00",
                CurrentTimeFormatter.FormatLine(newYork));
        }

        [TestMethod]
        public void ContextPrompt_UsesTimeCollectedIntoSnapshot()
        {
            var snapshot = new HostContextSnapshotDto
            {
                CurrentLocalTime = CurrentTimeFormatter.FormatLine(Monday1857)
            };

            var prompt = DefaultContextBuilder.BuildCurrentTimePrompt(snapshot);

            StringAssert.Contains(prompt, "Current local time: 2026-10-12 18:57（星期一），时区 UTC+08:00");
        }

        [TestMethod]
        public void ContextPrompt_FallsBackToCurrentClockWhenSnapshotHasNoTime()
        {
            var prompt = DefaultContextBuilder.BuildCurrentTimePrompt(new HostContextSnapshotDto());

            StringAssert.Matches(
                prompt,
                new System.Text.RegularExpressions.Regex(@"Current local time: \d{4}-\d{2}-\d{2} \d{2}:\d{2}（星期[日一二三四五六]），时区 UTC[+-]\d{2}:\d{2}"));
        }

        [TestMethod]
        public void Tool_ReturnsCurrentTimeLineForModelOnDemand()
        {
            var outcome = CurrentTimeTool.Execute("call-1", CurrentTimeTool.ToolName);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual(CurrentTimeTool.ToolName, outcome.ToolName);
            StringAssert.Matches(
                outcome.Content,
                new System.Text.RegularExpressions.Regex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}（星期[日一二三四五六]），时区 UTC[+-]\d{2}:\d{2}$"));
        }

        [TestMethod]
        public void Turn_InjectsCurrentTimeIntoModelContextWithoutUserAsking()
        {
            var harness = new TurnHarness();
            harness.Model.Then(ScriptedModelResponse.Text("好的。"));

            harness.Run("你好");

            var request = harness.Model.Requests[0];
            StringAssert.Contains(request.SystemText, "Current local time:");
            StringAssert.Contains(request.SystemText, "（星期");
            CollectionAssert.Contains(request.ToolNames, CurrentTimeTool.ToolName);
        }
    }
}
