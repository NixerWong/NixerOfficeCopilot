using OfficeCopilot.Agent.Client;
using OfficeCopilot.HostBridge.Contracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Sub-agent runner: forks an independent model call inside the current turn,
    /// using a custom system prompt and a restricted tool whitelist.
    /// Simplified port of the core logic in cc-haha <c>runAgent.ts</c>.
    /// </summary>
    internal sealed class SubagentRunner
    {
        internal const int DefaultMaxSubagentRounds = 3;
        internal const int DefaultMaxSubagentTokens = 4096;

        private readonly IModelClient _modelClient;

        public SubagentRunner(IModelClient modelClient)
        {
            _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
        }

        /// <summary>
        /// Runs the sub-agent.
        /// </summary>
        /// <param name="options">Model configuration for the sub-agent (endpoint/model, etc.).</param>
        /// <param name="subagentPrompt">Task description for the sub-agent; becomes its system prompt.</param>
        /// <param name="allowedTools">Allowed tool list (empty = no tools are permitted).</param>
        /// <param name="toolCatalog">Full tool catalog, filtered by <paramref name="allowedTools"/>.</param>
        /// <param name="snapshot">Optional host context snapshot.</param>
        /// <param name="cancellationToken"></param>
        /// <returns>The sub-agent's response text.</returns>
        public async Task<SubagentResult> RunAsync(
            AgentModelOptions options,
            string subagentPrompt,
            IReadOnlyList<string> allowedTools,
            IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog,
            HostContextSnapshotDto snapshot,
            Action<string> onThinkingDelta,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(subagentPrompt))
            {
                return new SubagentResult { Success = false, Error = "Subagent prompt is required." };
            }

            var messages = new List<ModelClientMessage>();

            // Build sub-agent system prompt
            var systemPrompt = BuildSubagentSystemPrompt(subagentPrompt, allowedTools);
            messages.Add(new ModelClientMessage { Role = "system", Content = systemPrompt });

            // Add host context if available
            if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.Summary))
            {
                messages.Add(new ModelClientMessage
                {
                    Role = "system",
                    Content = "Current Office context: " + snapshot.Summary
                });
            }

            // Add user message
            messages.Add(new ModelClientMessage { Role = "user", Content = subagentPrompt });

            // Build allowed model tools
            var modelTools = BuildSubagentModelTools(allowedTools, toolCatalog);

            var totalRounds = DefaultMaxSubagentRounds;
            var lastContent = string.Empty;
            var hadError = false;

            for (var round = 0; round < totalRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var completion = await _modelClient.CompleteAsync(
                    options,
                    messages,
                    round == 0 ? modelTools : new List<ModelClientTool>(),
                    new ModelClientStreamingCallbacks
                    {
                        OnReasoningDeltaAsync = (delta, ct) =>
                        {
                            onThinkingDelta?.Invoke(delta);
                            return Task.CompletedTask;
                        }
                    },
                    cancellationToken).ConfigureAwait(false)
                    ?? new ModelClientCompletion();

                lastContent = completion.Content ?? string.Empty;

                // If no tool calls, return the response
                if (!completion.HasToolCalls)
                {
                    return new SubagentResult
                    {
                        Success = !hadError,
                        Content = lastContent
                    };
                }

                // Add assistant message with tool calls
                messages.Add(new ModelClientMessage
                {
                    Role = "assistant",
                    Content = lastContent,
                    ToolCalls = completion.ToolCalls
                        ?.Select(t => new ModelClientToolCall
                        {
                            Id = t.Id ?? string.Empty,
                            Name = t.Name ?? string.Empty,
                            ArgumentsJson = t.ArgumentsJson ?? "{}"
                        })
                        .ToList()
                });

                // For MVP subagent: tools are NOT executed. Instead, we tell the subagent
                // that tool execution is not available and to work with what it has.
                messages.Add(new ModelClientMessage
                {
                    Role = "system",
                    Content = "Tool execution is not available in this sub-agent context. "
                        + "Please provide your best answer using your existing knowledge. "
                        + "Do not request further tool calls."
                });

                // On next round, no tools available (empty list passed above)
            }

            return new SubagentResult
            {
                Success = !hadError,
                Content = string.IsNullOrWhiteSpace(lastContent)
                    ? "Sub-agent reached the round limit without producing a final answer."
                    : lastContent,
                ReachedRoundLimit = true
            };
        }

        private static string BuildSubagentSystemPrompt(string task, IReadOnlyList<string> allowedTools)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are a specialized sub-agent. Your task:");
            sb.AppendLine(task);
            sb.AppendLine();

            if (allowedTools != null && allowedTools.Count > 0)
            {
                sb.AppendLine("Allowed tools: " + string.Join(", ", allowedTools));
            }
            else
            {
                sb.AppendLine("No tools are available. Answer using your existing knowledge only.");
            }

            sb.AppendLine();
            sb.AppendLine("Instructions:");
            sb.AppendLine("- Be concise and focused on the specific task.");
            sb.AppendLine("- If you have tools, use them to gather information, then synthesize.");
            sb.AppendLine("- Do NOT ask the user questions - you are running autonomously.");
            sb.AppendLine("- Respond in Chinese if the task involves Office/Excel data.");
            sb.AppendLine("- Provide your final answer directly without requesting confirmation.");

            return sb.ToString();
        }

        private static List<ModelClientTool> BuildSubagentModelTools(
            IReadOnlyList<string> allowedTools,
            IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog)
        {
            var tools = new List<ModelClientTool>();

            if (allowedTools == null || allowedTools.Count == 0 || toolCatalog == null)
                return tools;

            foreach (var toolName in allowedTools)
            {
                if (string.IsNullOrWhiteSpace(toolName))
                    continue;

                if (toolCatalog.TryGetValue(toolName, out var def) && def != null)
                {
                    tools.Add(new ModelClientTool
                    {
                        Name = def.Name ?? string.Empty,
                        Description = def.Description ?? string.Empty,
                        InputSchemaJson = def.InputSchemaJson ?? "{\"type\":\"object\",\"properties\":{}}"
                    });
                }
            }

            return tools;
        }
    }

    /// <summary>
    /// Result of a sub-agent run.
    /// </summary>
    internal sealed class SubagentResult
    {
        public bool Success { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Error { get; set; }
        public bool ReachedRoundLimit { get; set; }
    }
}
