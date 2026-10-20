using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Word 回合内的停滞检测。逐段排版时每次调用参数都不同，<see cref="LoopGuardPolicy"/> 的“相同调用”判断抓不到，
    /// 模型可以这样一直耗到轮次上限。这里按“工具名 + 参数键集合”统计调用模式：第一次命中时提醒模型，
    /// 提醒后仍然重复就提前收尾。每个回合使用一个新实例。
    /// </summary>
    internal sealed class WordStallDetector
    {
        internal const int RecentWindow = 30;
        internal const int RepeatedPatternThreshold = 8;
        internal const int RepeatedPatternAfterWarning = 6;
        internal const int FullReadThreshold = 3;
        internal const int FullReadsAfterWarning = 2;

        private static readonly HashSet<string> FullReadTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "word_read_document",
            "word_read_paragraphs"
        };

        private int _patternWarnedAt = -1;
        private string _warnedPattern;
        private int _fullReadWarnedAt = -1;

        public WordStallCheck Evaluate(IList<ToolCallTrace> traces)
        {
            if (traces == null || traces.Count == 0)
            {
                return WordStallCheck.None;
            }

            var fullReads = CountFullReads(traces, 0);
            if (_fullReadWarnedAt < 0 && fullReads >= FullReadThreshold)
            {
                _fullReadWarnedAt = traces.Count;
                return WordStallCheck.Remind(
                    "本回合已经完整读取文档 " + fullReads + " 次。不要再重复读全文：基于已有结果完成剩余修改；"
                    + "需要核对时只读一次，读完直接给出结论。");
            }

            if (_fullReadWarnedAt >= 0 && CountFullReads(traces, _fullReadWarnedAt) >= FullReadsAfterWarning)
            {
                return WordStallCheck.Stop(new LoopGuardDecision(
                    true,
                    "提醒后仍在反复读取全文，停止工具调用并总结当前进度。",
                    "The document was re-read repeatedly after a warning."));
            }

            if (_patternWarnedAt >= 0)
            {
                if (CountPattern(traces, _patternWarnedAt, _warnedPattern) >= RepeatedPatternAfterWarning)
                {
                    return WordStallCheck.Stop(new LoopGuardDecision(
                        true,
                        "提醒改用批量工具后仍在逐段调用 " + ToolNameOf(_warnedPattern) + "，停止工具调用并总结当前进度。",
                        "Per-paragraph calls continued after a batch-tool reminder."));
                }

                return WordStallCheck.None;
            }

            var start = Math.Max(0, traces.Count - RecentWindow);
            var top = traces
                .Skip(start)
                .Where(IsCountedCall)
                .GroupBy(PatternOf, StringComparer.Ordinal)
                .Select(group => new { Pattern = group.Key, Count = group.Count() })
                .OrderByDescending(item => item.Count)
                .FirstOrDefault();
            if (top == null || top.Count < RepeatedPatternThreshold)
            {
                return WordStallCheck.None;
            }

            _patternWarnedAt = traces.Count;
            _warnedPattern = top.Pattern;
            return WordStallCheck.Remind(
                "最近 " + Math.Min(RecentWindow, traces.Count) + " 次工具调用中，" + ToolNameOf(top.Pattern) + " 以相同方式调用了 "
                + top.Count + " 次，这是在逐段处理。剩余段落请合并为一次 word_format_paragraphs 批量调用"
                + "（items 中按段号列出样式、字体、字号、对齐、缩进、行距），不要继续逐段选择和设置。");
        }

        internal static string PatternOf(ToolCallTrace trace)
        {
            var keys = new List<string>();
            try
            {
                using (var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(trace.NormalizedArguments) ? "{}" : trace.NormalizedArguments))
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        keys.AddRange(document.RootElement.EnumerateObject().Select(property => property.Name));
                    }
                }
            }
            catch (JsonException)
            {
            }

            keys.Sort(StringComparer.Ordinal);
            return trace.ToolName + "|" + string.Join(",", keys);
        }

        private static bool IsCountedCall(ToolCallTrace trace)
        {
            return trace != null
                && trace.ToolName.StartsWith("word_", StringComparison.OrdinalIgnoreCase)
                && !FullReadTools.Contains(trace.ToolName);
        }

        private static int CountFullReads(IList<ToolCallTrace> traces, int start)
        {
            var count = 0;
            for (var index = start; index < traces.Count; index++)
            {
                if (traces[index] != null && traces[index].Success && FullReadTools.Contains(traces[index].ToolName))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountPattern(IList<ToolCallTrace> traces, int start, string pattern)
        {
            var count = 0;
            for (var index = start; index < traces.Count; index++)
            {
                if (IsCountedCall(traces[index]) && string.Equals(PatternOf(traces[index]), pattern, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static string ToolNameOf(string pattern)
        {
            var separator = (pattern ?? string.Empty).IndexOf('|');
            return separator < 0 ? pattern ?? string.Empty : pattern.Substring(0, separator);
        }
    }

    internal sealed class WordStallCheck
    {
        public static readonly WordStallCheck None = new WordStallCheck(null, null);

        private WordStallCheck(string reminder, LoopGuardDecision stopDecision)
        {
            Reminder = reminder;
            StopDecision = stopDecision;
        }

        public string Reminder { get; }

        public LoopGuardDecision StopDecision { get; }

        public static WordStallCheck Remind(string reminder)
        {
            return new WordStallCheck(reminder, null);
        }

        public static WordStallCheck Stop(LoopGuardDecision decision)
        {
            return new WordStallCheck(null, decision);
        }
    }
}
