using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Runtime.Context
{
    /// <summary>
    /// 滚动累积摘要器（设计文档 §6）。负责调用模型把「已有摘要 + 本次卷入的中段消息」
    /// 折叠成新的结构化工作记忆摘要。结构化字段让摘要保留：目标、决策、事实、工具结果、待办。
    /// </summary>
    internal sealed class CompactionSummarizer
    {
        private const int MaxSummaryOutputTokens = 20_000;

        /// <summary>单次摘要调用最多摄入的消息条数，防止摘要请求自身超限。</summary>
        private const int MaxMessagesPerCall = 100;

        /// <summary>摘要文本硬上限（字符），防模型幻觉 / 循环把摘要撑爆。</summary>
        private const int MaxSummaryChars = 12_000;

        private readonly IModelClient _modelClient;

        public CompactionSummarizer(IModelClient modelClient)
        {
            _modelClient = modelClient;
        }

        public bool CanSummarize => _modelClient != null;

        /// <summary>
        /// 生成滚动摘要。返回 <c>null</c> 表示不可用（无模型 / 无端点）。
        /// </summary>
        public async Task<string> SummarizeAsync(
            string existingSummary,
            IReadOnlyList<BrokerConversationMessage> newContent,
            AgentModelOptions modelOptions,
            int summaryBudgetTokens,
            CancellationToken cancellationToken)
        {
            if (_modelClient == null)
            {
                return null;
            }

            if (modelOptions == null || string.IsNullOrWhiteSpace(modelOptions.Endpoint))
            {
                return null;
            }

            var prompt = BuildSummarizePrompt(existingSummary, summaryBudgetTokens);
            var modelMessages = new List<ModelClientMessage>
            {
                new ModelClientMessage { Role = "system", Content = prompt }
            };

            if (newContent != null && newContent.Count > 0)
            {
                var content = newContent.Count > MaxMessagesPerCall
                    ? (IReadOnlyList<BrokerConversationMessage>)SliceTail(newContent, MaxMessagesPerCall)
                    : newContent;

                foreach (var msg in content)
                {
                    modelMessages.Add(ConvertToModelMessage(msg));
                }
            }

            modelMessages.Add(new ModelClientMessage
            {
                Role = "user",
                Content = "Now output the summary following the exact format and rules above."
            });

            RuntimeTrace.Write($"[CompactionSummarizer] Summarizing {(newContent == null ? 0 : newContent.Count)} messages, "
                + $"existingSummaryChars={(existingSummary == null ? 0 : existingSummary.Length)}, budget={summaryBudgetTokens} tokens.");

            var opts = BuildSummarizeModelOptions(modelOptions);
            var completion = await _modelClient.CompleteAsync(
                opts,
                modelMessages,
                new List<ModelClientTool>(),
                new ModelClientStreamingCallbacks(),
                cancellationToken).ConfigureAwait(false);

            var response = completion?.Content ?? string.Empty;
            if (string.IsNullOrWhiteSpace(response))
            {
                throw new InvalidOperationException("Summarizer returned empty content.");
            }

            var summary = ExtractSummaryBlock(response);

            // 用 SummaryBudget 反向钳制摘要长度（token → 字符粗估 ×4，另设硬上限）
            var capChars = Math.Max(200, Math.Min(MaxSummaryChars, summaryBudgetTokens * 4));
            if (summary.Length > capChars)
            {
                RuntimeTrace.Write($"[CompactionSummarizer] Summary truncated: {summary.Length} chars → {capChars} chars.");
                summary = summary.Substring(0, capChars) + "\n[... summary truncated ...]";
            }

            RuntimeTrace.Write($"[CompactionSummarizer] Summary produced: {summary.Length} chars.");
            return summary;
        }

        private static string BuildSummarizePrompt(string existingSummary, int summaryBudgetTokens)
        {
            var wordBudget = Math.Max(50, summaryBudgetTokens / 3);

            var sb = new StringBuilder();
            sb.AppendLine("You are a context-compaction assistant for a long-running agent conversation.");
            sb.AppendLine("Produce a structured working-memory summary so the conversation can continue without losing context.");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(existingSummary))
            {
                sb.AppendLine("=== Existing summary (must be folded in, not repeated verbatim) ===");
                sb.AppendLine(existingSummary);
                sb.AppendLine("=== End of existing summary ===");
                sb.AppendLine();
            }

            sb.AppendLine("Output ONLY the following structure (no other text):");
            sb.AppendLine();
            sb.AppendLine("<summary>");
            sb.AppendLine("Goal: [user's core goal / task, one line]");
            sb.AppendLine("Decisions: [conclusions reached and decisions made]");
            sb.AppendLine("Facts: [key facts, variables, file names, constraints]");
            sb.AppendLine("ToolResults: [important tool call results]");
            sb.AppendLine("Pending: [unresolved questions / todos]");
            sb.AppendLine("</summary>");
            sb.AppendLine();
            sb.AppendLine("RULES:");
            sb.AppendLine("- Mark irrelevant fields with \"-\"");
            sb.AppendLine("- Do NOT enumerate lists, repeat content, or predict next steps");
            sb.AppendLine("- The whole summary must stay under " + wordBudget.ToString() + " words");
            sb.AppendLine("- STOP immediately after </summary> — output NOTHING after the closing tag");

            return sb.ToString();
        }

        private static List<BrokerConversationMessage> SliceTail(IReadOnlyList<BrokerConversationMessage> messages, int count)
        {
            var result = new List<BrokerConversationMessage>(count);
            for (var i = messages.Count - count; i < messages.Count; i++)
            {
                result.Add(messages[i]);
            }

            return result;
        }

        private static ModelClientMessage ConvertToModelMessage(BrokerConversationMessage msg)
        {
            if (msg == null)
            {
                return new ModelClientMessage { Role = "system", Content = string.Empty };
            }

            var role = msg.Role;
            if (string.Equals(role, "tool_call", StringComparison.OrdinalIgnoreCase))
            {
                role = "assistant";
            }
            else if (string.Equals(role, "tool_result", StringComparison.OrdinalIgnoreCase))
            {
                role = "user";
            }

            var content = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(msg.Name) && role == "assistant")
            {
                content.Append("[").Append(msg.Name).Append("] ");
            }

            if (!string.IsNullOrWhiteSpace(msg.Content))
            {
                content.Append(msg.Content);
            }

            if (!string.IsNullOrWhiteSpace(msg.StructuredContentJson) && content.Length < 500)
            {
                content.Append('\n').Append(msg.StructuredContentJson);
            }

            return new ModelClientMessage { Role = role, Content = content.ToString(), Name = msg.Name ?? string.Empty };
        }

        private static AgentModelOptions BuildSummarizeModelOptions(AgentModelOptions source)
        {
            var opts = new AgentModelOptions
            {
                ProviderKind = source.ProviderKind,
                Endpoint = source.Endpoint,
                ApiKey = source.ApiKey,
                ModelId = source.ModelId,
                SystemPrompt = string.Empty,
            };

            foreach (var kv in source.HttpHeaders ?? new Dictionary<string, string>())
            {
                opts.HttpHeaders[kv.Key] = kv.Value;
            }

            opts.AdditionalParameters["Temperature"] = 0.1;
            opts.AdditionalParameters["MaxTokens"] = MaxSummaryOutputTokens;
            opts.AdditionalParameters["stream"] = false;

            if (source.AdditionalParameters != null)
            {
                foreach (var kv in source.AdditionalParameters)
                {
                    if (kv.Key == "thinking" || kv.Key == "enable_thinking" || kv.Key == "enableThinking")
                    {
                        opts.AdditionalParameters[kv.Key] = "disabled";
                    }
                }
            }

            return opts;
        }

        private static string ExtractSummaryBlock(string response)
        {
            var start = response.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
            var end = response.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start)
            {
                return response.Substring(start + "<summary>".Length, end - start - "<summary>".Length).Trim();
            }

            return response.Trim();
        }
    }
}
