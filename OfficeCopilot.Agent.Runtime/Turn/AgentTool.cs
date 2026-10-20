using OfficeCopilot.Agent.Client;
using OfficeCopilot.HostBridge.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Agent tool: the model calls this tool to delegate a sub-task to a sub-agent
    /// that is restricted to an explicit tool whitelist.
    /// Simplified port of the core logic in cc-haha <c>AgentTool.tsx</c>.
    ///
    /// Tool name: <c>runtime.run_agent</c>
    /// Input: <c>{ description, prompt, allowedTools }</c>
    /// Output: the sub-agent's response text.
    /// </summary>
    internal sealed class AgentTool
    {
        public const string ToolName = "runtime.run_agent";

        private readonly SubagentRunner _runner;
        private readonly IModelClient _modelClient;

        public AgentTool(IModelClient modelClient)
        {
            _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
            _runner = new SubagentRunner(modelClient);
        }

        /// <summary>
        /// Executes the sub-agent call asynchronously. The turn does not continue until the
        /// sub-agent completes, but the calling thread is no longer blocked.
        /// </summary>
        public async System.Threading.Tasks.Task<ToolExecutionOutcome> ExecuteAsync(
            string toolCallId,
            string toolName,
            string argumentsJson,
            AgentModelOptions modelOptions,
            IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog,
            HostContextSnapshotDto snapshot,
            Action<string> onThinkingDelta,
            System.Threading.CancellationToken cancellationToken)
        {
            var description = string.Empty;
            var prompt = string.Empty;
            var allowedTools = new List<string>();

            try
            {
                using (var doc = JsonDocument.Parse(argumentsJson ?? "{}"))
                {
                    if (doc.RootElement.TryGetProperty("description", out var descProp))
                        description = descProp.GetString() ?? string.Empty;

                    if (doc.RootElement.TryGetProperty("prompt", out var promptProp))
                        prompt = promptProp.GetString() ?? string.Empty;

                    if (doc.RootElement.TryGetProperty("allowedTools", out var toolsProp)
                        && toolsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tool in toolsProp.EnumerateArray())
                        {
                            var name = tool.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                                allowedTools.Add(name);
                        }
                    }
                }
            }
            catch
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "agent.parse_error",
                    ErrorMessage = "Failed to parse agent arguments. Expected JSON with 'description' and 'prompt' fields."
                };
            }

            if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(prompt))
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "agent.missing_params",
                    ErrorMessage = "Both 'description' and 'prompt' are required for sub-agent invocation."
                };
            }

            try
            {
                var result = await _runner.RunAsync(
                    modelOptions,
                    prompt,
                    allowedTools,
                    toolCatalog,
                    snapshot,
                    onThinkingDelta,
                    cancellationToken).ConfigureAwait(false);

                if (!result.Success)
                {
                    return new ToolExecutionOutcome
                    {
                        ToolCallId = toolCallId ?? string.Empty,
                        ToolName = toolName ?? string.Empty,
                        Success = false,
                        ErrorCode = "agent.execution_failed",
                        ErrorMessage = result.Error ?? "Sub-agent execution failed."
                    };
                }

                var content = BuildAgentResultContent(description, result);

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = true,
                    Content = content
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "agent.exception",
                    ErrorMessage = $"Sub-agent execution failed: {ex.Message}"
                };
            }
        }

        private static string BuildAgentResultContent(string description, SubagentResult result)
        {
            var sb = new StringBuilder();

            sb.Append("Sub-agent completed: ").AppendLine(description);
            if (result.ReachedRoundLimit)
            {
                sb.AppendLine("(reached round limit)");
            }
            sb.AppendLine();
            sb.AppendLine("=== Agent Response ===");
            sb.Append(result.Content);

            return sb.ToString();
        }
    }
}
