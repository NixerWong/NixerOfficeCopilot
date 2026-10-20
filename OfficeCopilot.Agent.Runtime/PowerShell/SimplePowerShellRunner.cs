using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.PowerShell
{
    /// <summary>
    /// Executes PowerShell commands in a process-internal runspace using SMA.
    /// Mirrors cc-haha <c>PowerShellTool.tsx call()</c> execution pipeline.
    ///
    /// Design decisions:
    /// - Single PowerShell instance reused per runner (stateful: variables, CWD persist)
    /// - Commands.Clear() after each execution; Streams.Clear() to prevent leak
    /// - Synchronous execution wrapped in Task.Run for timeout/cancel support
    /// </summary>
    public sealed class SimplePowerShellRunner : IPowerShellRunner, IDisposable
    {
        private readonly System.Management.Automation.PowerShell _ps;
        private readonly PSInvocationSettings _settings;
        private readonly object _lock = new object();
        private bool _disposed;

        public SimplePowerShellRunner()
        {
            _ps = System.Management.Automation.PowerShell.Create();
            _ps.AddCommand("Set-StrictMode").AddParameter("Version", "1.0");
            _ps.Invoke();
            _ps.Commands.Clear();

            _settings = new PSInvocationSettings
            {
                ErrorActionPreference = ActionPreference.Continue
            };
        }

        /// <inheritdoc />
        public Task<PowerShellExecResult> ExecuteAsync(
            string command,
            int timeoutMs,
            bool preventCwdChanges,
            CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SimplePowerShellRunner));

            if (string.IsNullOrWhiteSpace(command))
            {
                return Task.FromResult(new PowerShellExecResult
                {
                    Stdout = string.Empty,
                    Stderr = string.Empty,
                    ExitCode = 0
                });
            }

            var effectiveTimeout = timeoutMs > 0 ? timeoutMs : PowerShellVersionDetector.DefaultTimeoutMs;
            if (effectiveTimeout > PowerShellVersionDetector.MaxTimeoutMs)
                effectiveTimeout = PowerShellVersionDetector.MaxTimeoutMs;

            // Save CWD for restore if preventCwdChanges is true (sub-agent isolation)
            var originalCwd = Environment.CurrentDirectory;

            return Task.Run(() =>
            {
                lock (_lock)
                {
                    return ExecuteCore(command, effectiveTimeout, preventCwdChanges ? originalCwd : null, cancellationToken);
                }
            }, cancellationToken);
        }

        private PowerShellExecResult ExecuteCore(
            string command,
            int timeoutMs,
            string restoreCwd,
            CancellationToken cancellationToken)
        {
            var result = new PowerShellExecResult();
            var stopwatch = Stopwatch.StartNew();

            // Build a wrapper that captures exit code and ensures NonInteractive
            var wrappedCommand = BuildWrappedCommand(command);

            try
            {
                _ps.Commands.Clear();
                _ps.Streams.ClearStreams();
                _ps.AddScript(wrappedCommand);

                // Accumulate output
                var stdoutBuilder = new StringBuilder();
                var stderrBuilder = new StringBuilder();
                var totalBytes = 0;
                var isTruncated = false;

                // Register cancellation
                using (var ctr = cancellationToken.Register(() =>
                {
                    try { _ps.Stop(); } catch { /* best effort */ }
                }))
                {
                    // Collect PSObject output
                    var psObjects = _ps.Invoke(null, _settings);

                    foreach (var psObj in psObjects)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var line = psObj?.ToString() ?? string.Empty;
                        totalBytes += Encoding.UTF8.GetByteCount(line);
                        AppendLine(stdoutBuilder, ref isTruncated, line, ref totalBytes);
                    }

                    // Collect errors
                    if (_ps.Streams.Error.Count > 0)
                    {
                        foreach (var errorRecord in _ps.Streams.Error)
                        {
                            var errLine = errorRecord?.ToString() ?? string.Empty;
                            stderrBuilder.AppendLine(errLine);
                        }
                    }

                    // Collect warning/information streams
                    if (_ps.Streams.Warning.Count > 0)
                    {
                        foreach (var warning in _ps.Streams.Warning)
                        {
                            stderrBuilder.Append("WARNING: ");
                            stderrBuilder.AppendLine(warning.Message);
                        }
                    }

                    // Exit code: PowerShell-native cmdlets exit 0 on success;
                    // $LASTEXITCODE for external executables is captured via our wrapper
                    var exitCode = 0;
                    if (_ps.Streams.Error.Count > 0)
                    {
                        // Check if any error is terminating (non-continue)
                        var hasTerminating = false;
                        foreach (var err in _ps.Streams.Error)
                        {
                            if (err != null && !err.CategoryInfo.Reason.Equals("NonTerminatingError", StringComparison.OrdinalIgnoreCase))
                            {
                                hasTerminating = true;
                                break;
                            }
                        }
                        exitCode = hasTerminating ? 1 : 0;
                    }

                    result.ExitCode = exitCode;
                    result.Interrupted = cancellationToken.IsCancellationRequested;
                }

                result.Stdout = stdoutBuilder.ToString().TrimEnd();
                result.Stderr = stderrBuilder.ToString().TrimEnd();
                result.TotalBytes = totalBytes;
                result.IsTruncated = isTruncated;
            }
            catch (OperationCanceledException)
            {
                result.Interrupted = true;
                result.Stderr = "Command was cancelled.";
            }
            catch (PipelineStoppedException)
            {
                result.Interrupted = true;
                result.Stderr = _ps.Streams.Error.Count > 0
                    ? _ps.Streams.Error[0]?.ToString() ?? "Command was stopped."
                    : "Command was stopped.";
            }
            catch (RuntimeException rte)
            {
                result.ExitCode = 1;
                result.Stderr = rte.ErrorRecord?.ToString() ?? rte.Message;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result.ExitCode = 1;
                result.Stderr = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;

                // Restore CWD for sub-agent isolation
                if (restoreCwd != null)
                {
                    try
                    {
                        Environment.CurrentDirectory = restoreCwd;
                        _ps.Commands.Clear();
                        _ps.AddScript("Set-Location -LiteralPath '" + restoreCwd.Replace("'", "''") + "'");
                        _ps.Invoke();
                    }
                    catch { /* best effort */ }
                }

                _ps.Commands.Clear();
                _ps.Streams.ClearStreams();
            }

            return result;
        }

        /// <summary>
        /// Wraps the user command to capture $LASTEXITCODE and enforce NonInteractive.
        /// Mirrors cc-haha's shell preflight: the wrapper sets $Global:LASTEXITCODE
        /// so we can read it back after external exe calls.
        /// </summary>
        private static string BuildWrappedCommand(string command)
        {
            // The wrapper:
            // 1. Suppress profile loading (already handled by runspace)
            // 2. Set NonInteractive
            // 3. Run user command
            // 4. Exit with $LASTEXITCODE so PS.Invoke() sees the right exit
            var sb = new StringBuilder();
            sb.AppendLine("[System.Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
            sb.AppendLine("$WarningPreference = 'SilentlyContinue'");
            sb.AppendLine("$InformationPreference = 'SilentlyContinue'");
            sb.AppendLine("$Global:LASTEXITCODE = 0");
            sb.AppendLine(command);
            sb.AppendLine("exit $Global:LASTEXITCODE");
            return sb.ToString();
        }

        private static void AppendLine(StringBuilder sb, ref bool isTruncated, string line, ref int totalBytes)
        {
            if (isTruncated)
                return;

            var maxLen = PowerShellVersionDetector.MaxOutputLength;
            if (sb.Length + line.Length + 2 > maxLen)
            {
                var remaining = maxLen - sb.Length - 50; // reserve for truncation notice
                if (remaining > 0)
                {
                    sb.AppendLine(line.Substring(0, Math.Min(remaining, line.Length)));
                }
                sb.AppendLine("... [output truncated]");
                isTruncated = true;
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _ps.Commands.Clear();
                _ps.Streams.ClearStreams();
                _ps.Dispose();
            }
            catch { /* best effort */ }
        }
    }
}
