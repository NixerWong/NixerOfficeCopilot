using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 轮次上限收尾时模型总结失败（DSML 泄漏或为空）的兜底：由程序根据本回合工具调用生成进度摘要。
    /// 摘要作为 assistant 内容持久化，用户回复“继续”时下一回合能看到已经做过什么。
    /// </summary>
    internal static class RoundLimitProgressSummary
    {
        internal const string AskUserToolName = "runtime.ask_user_question";
        private const int MaxDistributionEntries = 8;
        private const int MaxRecentWrites = 8;
        private const int MaxAskUserEntries = 4;
        private const int MaxArgumentPreviewLength = 160;
        private const int MaxAnswerPreviewLength = 200;

        public static string Build(
            IList<ToolCallTrace> traces,
            Func<string, bool> isWriteTool,
            string hostApplicationName,
            int maxToolRounds,
            IList<string> unexecutedTools)
        {
            var builder = new StringBuilder();
            builder.Append("已达到本回合工具调用轮次上限");
            if (maxToolRounds > 0) builder.Append("（").Append(maxToolRounds).Append(" 轮）");
            if (unexecutedTools != null && unexecutedTools.Count > 0)
            {
                builder.Append("，模型仍想继续调用工具（").Append(string.Join("、", unexecutedTools)).Append("），这些工具调用未被执行");
            }
            else
            {
                builder.Append("，模型未能给出总结");
            }

            builder.Append("。\n\n");

            var list = traces == null ? new List<ToolCallTrace>() : traces.Where(t => t != null).ToList();
            if (list.Count > 0)
            {
                AppendProgress(builder, list, isWriteTool ?? (_ => false));
            }

            builder.Append(BuildResumeHint(hostApplicationName));
            return builder.ToString();
        }

        /// <summary>按宿主给出“已完成的修改在哪里、如何继续”的说明；“工作区”只属于 PPT 的构建流程。</summary>
        public static string BuildResumeHint(string hostApplicationName)
        {
            var host = hostApplicationName ?? string.Empty;
            if (host.IndexOf("PowerPoint", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "已完成的写入保留在工作区，可回复“继续”接着完成。";
            }

            if (host.IndexOf("Word", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "已完成的修改已保留在文档中（可用 Word 撤销回退）。回复“继续”将先读取文档现状，只完成剩余部分。";
            }

            if (host.IndexOf("Excel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "已完成的修改已保留在工作簿中。回复“继续”将从当前状态接着完成。";
            }

            return "已完成的修改已保留。回复“继续”将从当前状态接着完成。";
        }

        private static void AppendProgress(StringBuilder builder, List<ToolCallTrace> list, Func<string, bool> isWriteTool)
        {
            var successCount = list.Count(t => t.Success);
            var writes = list.Where(t => isWriteTool(t.ToolName)).ToList();

            builder.Append("**本回合进度（程序统计）**\n");
            builder.Append("- 工具调用 ").Append(list.Count).Append(" 次（成功 ").Append(successCount)
                .Append("，失败 ").Append(list.Count - successCount).Append("），其中写操作 ").Append(writes.Count).Append(" 次。\n");

            var distribution = list
                .GroupBy(t => t.ToolName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            builder.Append("- 调用分布：")
                .Append(string.Join("、", distribution.Take(MaxDistributionEntries).Select(g => g.Name + " ×" + g.Count)));
            if (distribution.Count > MaxDistributionEntries) builder.Append(" 等 ").Append(distribution.Count).Append(" 种");
            builder.Append("\n");

            var asks = list
                .Where(t => t.Success && string.Equals(t.ToolName, AskUserToolName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (asks.Count > 0)
            {
                builder.Append("- 用户确认：\n");
                foreach (var ask in asks.Skip(Math.Max(0, asks.Count - MaxAskUserEntries)))
                {
                    builder.Append("  - ").Append(Truncate(OneLine(ask.ResultPreview), MaxAnswerPreviewLength)).Append("\n");
                }
            }

            if (writes.Count > 0)
            {
                var recent = writes.Skip(Math.Max(0, writes.Count - MaxRecentWrites)).ToList();
                builder.Append("- 最近 ").Append(recent.Count).Append(" 次写操作：\n");
                foreach (var write in recent)
                {
                    builder.Append("  - ").Append(write.Success ? "✓ " : "✗ ").Append(write.ToolName)
                        .Append(" ").Append(Truncate(write.NormalizedArguments, MaxArgumentPreviewLength));
                    if (!write.Success && !string.IsNullOrWhiteSpace(write.ErrorMessage))
                    {
                        builder.Append("（").Append(Truncate(OneLine(write.ErrorMessage), 80)).Append("）");
                    }

                    builder.Append("\n");
                }
            }

            builder.Append("\n");
        }

        private static string OneLine(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
        }
    }
}
