using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Runtime.PowerShell;
using OfficeCopilot.Agent.Runtime.PowerShell.Permissions;
using OfficeCopilot.Agent.Runtime.PowerShell.Security;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// PowerShell execution tool — allows the model to run PowerShell commands.
    /// Ported from cc-haha <c>tools/PowerShellTool/PowerShellTool.tsx</c>.
    ///
    /// Tool name: <c>runtime.powershell</c>
    /// Input: { command, timeout?, description? }
    /// Output: { stdout, stderr, exitCode, interrupted }
    /// </summary>
    internal sealed class PowerShellTool : IDisposable
    {
        public const string ToolName = "runtime.powershell";

        private readonly IPowerShellRunner _runner;
        private bool _disposed;

        public PowerShellTool(IPowerShellRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        /// <summary>
        /// Execute a PowerShell command asynchronously.
        /// Called by DefaultToolDispatcher as part of the tool dispatch pipeline.
        /// </summary>
        public async Task<ToolExecutionOutcome> ExecuteAsync(
            string toolCallId,
            string toolName,
            string argumentsJson,
            PermissionMode currentMode,
            CancellationToken cancellationToken)
        {
            var command = string.Empty;
            var timeoutMs = 0;
            var description = string.Empty;

            RuntimeTrace.Write($"[PowerShellTool] Execute called: toolCallId={toolCallId}, mode={currentMode}");

            // Parse input
            try
            {
                using var doc = JsonDocument.Parse(argumentsJson ?? "{}");
                if (doc.RootElement.TryGetProperty("command", out var cmdProp))
                    command = cmdProp.GetString() ?? string.Empty;

                if (doc.RootElement.TryGetProperty("timeout", out var toProp) && toProp.TryGetInt32(out var t))
                    timeoutMs = t;

                if (doc.RootElement.TryGetProperty("description", out var descProp))
                    description = descProp.GetString() ?? string.Empty;
            }
            catch (JsonException ex)
            {
                RuntimeTrace.Write($"[PowerShellTool] Invalid JSON input: {ex.Message}");
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "powershell.invalid_input",
                    ErrorMessage = "Invalid JSON input: " + ex.Message
                };
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                RuntimeTrace.Write("[PowerShellTool] Missing 'command' parameter.");
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "powershell.missing_command",
                    ErrorMessage = "The 'command' parameter is required."
                };
            }

            // Permission check
            var permChecker = new PowerShellPermissionChecker(currentMode);
            var permResult = permChecker.Check(command);

            if (!permResult.Allowed && !permResult.NeedsConfirmation)
            {
                // Denied outright
                RuntimeTrace.Write($"[PowerShellTool] Permission denied: {permResult.Reason}");
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "powershell.permission_denied",
                    ErrorMessage = permResult.Reason ?? "Command blocked by permission policy."
                };
            }

            if (permResult.NeedsConfirmation)
            {
                // In a fuller implementation, this would trigger user confirmation via Panel.
                // For now, in Default mode, writable commands are still allowed but logged.
                // The PermissionGate in DefaultToolDispatcher handles the acceptEdits/bypass logic.
                RuntimeTrace.Write(
                    $"[PowerShellTool] Command needs confirmation: {permResult.Reason}. " +
                    $"Suggested prefix: {permResult.SuggestedPrefix ?? "none"}");
            }

            // Security check (destructive warning)
            var destructiveWarning = DestructiveCommandDetector.Detect(command);
            if (destructiveWarning != null)
            {
                RuntimeTrace.Write(
                    $"[PowerShellTool] Destructive pattern detected: {destructiveWarning}");
            }

            // Git safety check
            if (GitSafetyChecker.HasGitCompoundRisk(command))
            {
                RuntimeTrace.Write(
                    "[PowerShellTool] Potential git compound attack detected.");
            }

            // Log the command being executed (sanitized: first 200 chars)
            var cmdPreview = command.Length > 200 ? command.Substring(0, 200) + "..." : command;
            RuntimeTrace.Write($"[PowerShellTool] Executing: {cmdPreview.Replace("\r", "\\r").Replace("\n", "\\n")}");

            // Execute
            try
            {
                var execResult = await _runner.ExecuteAsync(
                    command,
                    timeoutMs,
                    preventCwdChanges: false, // Main agent can change CWD
                    cancellationToken).ConfigureAwait(false);

                // Interpret exit code semantics
                var interpretation = CommandSemanticInterpreter.Interpret(
                    command, execResult.ExitCode, execResult.Stdout, execResult.Stderr);

                // Build output
                var outputSb = new System.Text.StringBuilder();
                if (!string.IsNullOrWhiteSpace(execResult.Stdout))
                {
                    outputSb.AppendLine(execResult.Stdout.TrimEnd());
                }
                if (!string.IsNullOrWhiteSpace(execResult.Stderr))
                {
                    outputSb.AppendLine(execResult.Stderr.TrimEnd());
                }
                if (execResult.IsTruncated)
                {
                    outputSb.AppendLine("... [output truncated at " + PowerShellVersionDetector.MaxOutputLength + " characters]");
                }
                if (execResult.Interrupted)
                {
                    outputSb.AppendLine("<Command was interrupted>");
                }

                var success = !interpretation.IsError && !execResult.Interrupted;

                // Build error message that includes stderr context so the model can self-correct
                var errorMessage = string.Empty;
                if (!success)
                {
                    var baseError = interpretation.Message ?? "Command failed with exit code " + execResult.ExitCode;
                    if (!string.IsNullOrWhiteSpace(execResult.Stderr))
                    {
                        // Include first 300 chars of stderr so model sees "cmdlet not found" etc.
                        var stderrPreview = execResult.Stderr.Trim();
                        if (stderrPreview.Length > 300)
                            stderrPreview = stderrPreview.Substring(0, 300) + "...";
                        // Extract first meaningful line from stderr (skip PowerShell's stack-trace noise)
                        var firstLine = stderrPreview.Split('\n')[0].Trim();
                        errorMessage = string.IsNullOrWhiteSpace(firstLine) ? baseError : firstLine;
                    }
                    else
                    {
                        errorMessage = baseError;
                    }
                }

                RuntimeTrace.Write(
                    $"[PowerShellTool] Result: exitCode={execResult.ExitCode}, success={success}, " +
                    $"duration={execResult.Duration.TotalMilliseconds:F0}ms, " +
                    $"stdoutLen={execResult.Stdout?.Length ?? 0}, stderrLen={execResult.Stderr?.Length ?? 0}, " +
                    $"interrupted={execResult.Interrupted}, truncated={execResult.IsTruncated}" +
                    (string.IsNullOrEmpty(errorMessage) ? "" : $", error=\"{errorMessage.Replace("\r", "").Replace("\n", " | ")}\""));

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = success,
                    Content = outputSb.ToString().TrimEnd(),
                    ErrorCode = success ? string.Empty : "powershell.execution_error",
                    ErrorMessage = errorMessage
                };
            }
            catch (OperationCanceledException)
            {
                RuntimeTrace.Write("[PowerShellTool] Execution cancelled by token.");
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "powershell.cancelled",
                    ErrorMessage = "Command execution was cancelled."
                };
            }
            catch (Exception ex)
            {
                RuntimeTrace.Write($"[PowerShellTool] Execution failed with exception: {ex.GetType().Name}: {ex.Message}");
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "powershell.runtime_error",
                    ErrorMessage = "PowerShell execution failed: " + ex.Message
                };
            }
        }

        /// <summary>
        /// Get the JSON schema for the tool's input parameters.
        /// </summary>
        public static string GetSchemaJson()
        {
            return PowerShellPromptBuilder.GetInputSchemaJson();
        }

        /// <summary>
        /// Get the tool description for the model.
        /// </summary>
        public static string GetDescription()
        {
            return PowerShellPromptBuilder.GetToolDescription();
        }

        /// <summary>
        /// Get the full system prompt for the PowerShell tool.
        /// </summary>
        public static string GetPrompt()
        {
            return PowerShellPromptBuilder.GetPrompt();
        }

        /// <summary>
        /// Quick sync check: is the command likely read-only?
        /// </summary>
        public static bool IsReadOnly(string command)
        {
            if (ReadOnlyClassifier.HasSyncSecurityConcerns(command))
                return false;
            return ReadOnlyClassifier.IsReadOnlyCommandSync(command);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            (_runner as IDisposable)?.Dispose();
        }
    }
}
