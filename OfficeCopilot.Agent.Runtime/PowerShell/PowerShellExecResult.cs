using System;

namespace OfficeCopilot.Agent.Runtime.PowerShell
{
    /// <summary>
    /// Result of a PowerShell command execution.
    /// Mirrors cc-haha <c>ExecResult</c> + <c>Out</c> schema.
    /// </summary>
    public sealed class PowerShellExecResult
    {
        /// <summary>Standard output text (trimmed, max 30K chars).</summary>
        public string Stdout { get; set; } = string.Empty;

        /// <summary>Standard error text.</summary>
        public string Stderr { get; set; } = string.Empty;

        /// <summary>Process exit code. 0 = success.</summary>
        public int ExitCode { get; set; }

        /// <summary>Whether execution was interrupted by cancellation.</summary>
        public bool Interrupted { get; set; }

        /// <summary>Wall-clock duration of the command.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Pre-spawn error (e.g. CWD deleted before command ran).
        /// When non-null, the command never actually executed.
        /// </summary>
        public string PreSpawnError { get; set; }

        /// <summary>Total output byte count before truncation.</summary>
        public int TotalBytes { get; set; }

        /// <summary>Whether output was truncated to max length.</summary>
        public bool IsTruncated { get; set; }

        /// <summary>Creates a pre-spawn error result (command never ran).</summary>
        public static PowerShellExecResult CreateFailed(string error)
        {
            return new PowerShellExecResult
            {
                ExitCode = 1,
                Stderr = error ?? string.Empty,
                PreSpawnError = error,
                Interrupted = false
            };
        }
    }
}
