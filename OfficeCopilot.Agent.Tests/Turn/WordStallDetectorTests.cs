using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class WordStallDetectorTests
    {
        [TestMethod]
        public void PerParagraphPattern_RemindsThenStopsWhenItContinues()
        {
            var detector = new WordStallDetector();
            var traces = new List<ToolCallTrace>();
            WordStallCheck check = WordStallCheck.None;
            for (var i = 1; i <= WordStallDetector.RepeatedPatternThreshold; i++)
            {
                traces.Add(Trace("word_select_paragraph", "{\"paragraphIndex\":" + i + "}"));
                traces.Add(Trace("word_set_paragraph_format", "{\"alignment\":\"center\",\"firstLineIndent\":" + i + "}"));
                check = detector.Evaluate(traces);
                if (i < WordStallDetector.RepeatedPatternThreshold)
                {
                    Assert.IsNull(check.Reminder, "no reminder before threshold at " + i);
                }
            }

            Assert.IsNotNull(check.Reminder);
            StringAssert.Contains(check.Reminder, "word_format_paragraphs");
            Assert.IsNull(check.StopDecision);

            for (var i = 0; i < WordStallDetector.RepeatedPatternAfterWarning - 1; i++)
            {
                traces.Add(Trace("word_select_paragraph", "{\"paragraphIndex\":" + (20 + i) + "}"));
                Assert.IsNull(detector.Evaluate(traces).StopDecision);
            }

            traces.Add(Trace("word_select_paragraph", "{\"paragraphIndex\":99}"));
            var stop = detector.Evaluate(traces);
            Assert.IsNotNull(stop.StopDecision);
            Assert.IsTrue(stop.StopDecision.ShouldStop);
        }

        [TestMethod]
        public void BatchFlow_DoesNotTrigger()
        {
            var detector = new WordStallDetector();
            var traces = new List<ToolCallTrace>
            {
                Trace("word_read_outline", "{}"),
                Trace("word_read_paragraphs", "{}"),
                Trace("word_format_paragraphs", "{\"items\":[]}"),
                Trace("word_read_paragraphs", "{}"),
                Trace("word_set_page_setup", "{\"topMargin\":72}"),
                Trace("word_format_paragraphs", "{\"items\":[]}")
            };

            var check = detector.Evaluate(traces);

            Assert.IsNull(check.Reminder);
            Assert.IsNull(check.StopDecision);
        }

        [TestMethod]
        public void NonWordTools_AreIgnored()
        {
            var detector = new WordStallDetector();
            var traces = new List<ToolCallTrace>();
            for (var i = 0; i < 20; i++)
            {
                traces.Add(Trace("excel_write_range", "{\"address\":\"A" + i + "\",\"values\":[]}"));
            }

            var check = detector.Evaluate(traces);

            Assert.IsNull(check.Reminder);
            Assert.IsNull(check.StopDecision);
        }

        [TestMethod]
        public void RepeatedFullReads_RemindThenStop()
        {
            var detector = new WordStallDetector();
            var traces = new List<ToolCallTrace> { Trace("word_read_paragraphs", "{}"), Trace("word_read_document", "{}") };
            Assert.IsNull(detector.Evaluate(traces).Reminder);

            traces.Add(Trace("word_read_paragraphs", "{\"maxCount\":0}"));
            var remind = detector.Evaluate(traces);
            StringAssert.Contains(remind.Reminder, "不要再重复读全文");

            traces.Add(Trace("word_format_paragraphs", "{\"items\":[]}"));
            traces.Add(Trace("word_read_paragraphs", "{}"));
            Assert.IsNull(detector.Evaluate(traces).StopDecision);

            traces.Add(Trace("word_read_document", "{}"));
            Assert.IsNotNull(detector.Evaluate(traces).StopDecision);
        }

        [TestMethod]
        public void PatternOf_UsesToolNameAndSortedKeys()
        {
            Assert.AreEqual(
                WordStallDetector.PatternOf(Trace("word_set_paragraph_format", "{\"b\":1,\"a\":2}")),
                WordStallDetector.PatternOf(Trace("word_set_paragraph_format", "{\"a\":5,\"b\":9}")));
            Assert.AreEqual("word_get_selection|", WordStallDetector.PatternOf(Trace("word_get_selection", "not json")));
        }

        private static ToolCallTrace Trace(string tool, string args)
        {
            return new ToolCallTrace(tool, args, true, string.Empty, string.Empty);
        }
    }
}
