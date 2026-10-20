using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.PowerShell
{
    /// <summary>
    /// Contract for executing PowerShell commands.
    /// Abstracted for testability (mock in unit tests).
    /// </summary>
    public interface IPowerShellRunner
    {
        /// <summary>
        /// Execute a PowerShell command synchronously in a process-internal runspace.
        /// </summary>
        /// <param name="command">The PowerShell command text.</param>
        /// <param name="timeoutMs">Max execution time in milliseconds. 0 = no timeout.</param>
        /// <param name="preventCwdChanges">If true, restore CWD after execution (for sub-agents).</param>
        /// <param name="cancellationToken">Cancellation token to abort execution.</param>
        /// <returns>Execution result with stdout, stderr, exit code.</returns>
        Task<PowerShellExecResult> ExecuteAsync(
            string command,
            int timeoutMs,
            bool preventCwdChanges,
            CancellationToken cancellationToken);
    }
}
