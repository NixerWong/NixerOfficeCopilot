using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Runtime.PowerShell
{
    /// <summary>
    /// Interprets exit codes from external executables run via PowerShell.
    /// Ported from cc-haha <c>tools/PowerShellTool/commandSemantics.ts</c>.
    ///
    /// Many external tools use non-zero exit codes to convey information
    /// rather than failure (grep: 1 = no match, robocopy: 0-7 = success).
    /// Without this interpreter, any non-zero exit would be reported as error.
    /// </summary>
    public static class CommandSemanticInterpreter
    {
        /// <summary>
        /// Result of semantic exit code interpretation.
        /// </summary>
        public sealed class Interpretation
        {
            /// <summary>Whether the exit code indicates an actual error.</summary>
            public bool IsError { get; set; }

            /// <summary>Human-readable interpretation message (null if no special meaning).</summary>
            public string Message { get; set; }
        }

        /// <summary>
        /// Command-specific semantic rules.
        /// Keys are lowercase command names WITHOUT .exe suffix.
        /// </summary>
        private static readonly Dictionary<string, Func<int, Interpretation>> Semantics =
            new Dictionary<string, Func<int, Interpretation>>(StringComparer.OrdinalIgnoreCase)
            {
                // grep / ripgrep: 0 = matches found, 1 = no matches, 2+ = error
                ["grep"] = code => new Interpretation
                {
                    IsError = code >= 2,
                    Message = code == 1 ? "No matches found" : null
                },
                ["rg"] = code => new Interpretation
                {
                    IsError = code >= 2,
                    Message = code == 1 ? "No matches found" : null
                },

                // findstr.exe: Windows native text search
                // 0 = match found, 1 = no match, 2 = error
                ["findstr"] = code => new Interpretation
                {
                    IsError = code >= 2,
                    Message = code == 1 ? "No matches found" : null
                },

                // robocopy.exe: Windows native robust file copy
                // Exit codes are a BITFIELD — 0-7 are success, 8+ indicates failure
                ["robocopy"] = code => InterpretRobocopy(code),

                // xcopy.exe: 0 = success, 1 = no files found, 2 = user terminated, 4 = error, 5 = disk error
                ["xcopy"] = code => new Interpretation
                {
                    IsError = code >= 2,
                    Message = code == 1 ? "No files found to copy" : null
                },

                // reg.exe: 0 = success, 1 = failure
                // No special semantics — all non-zero are errors
            };

        /// <summary>
        /// Interpret the exit code of a command.
        /// </summary>
        /// <param name="command">The original command text.</param>
        /// <param name="exitCode">The exit code from execution.</param>
        /// <param name="stdout">Standard output (for context, not parsed).</param>
        /// <param name="stderr">Standard error (for context, not parsed).</param>
        /// <returns>Interpretation of the exit code.</returns>
        public static Interpretation Interpret(string command, int exitCode, string stdout, string stderr)
        {
            if (exitCode == 0)
            {
                return new Interpretation { IsError = false };
            }

            // Extract the base command name
            var baseCommand = ExtractBaseCommand(command);
            if (string.IsNullOrWhiteSpace(baseCommand))
            {
                return new Interpretation
                {
                    IsError = true,
                    Message = "Command failed with exit code " + exitCode
                };
            }

            // Look up semantic rules
            if (Semantics.TryGetValue(baseCommand, out var semanticFn))
            {
                return semanticFn(exitCode);
            }

            // Default: any non-zero exit code is an error
            return new Interpretation
            {
                IsError = true,
                Message = exitCode == 0 ? null : "Command failed with exit code " + exitCode
            };
        }

        /// <summary>
        /// Quick check: does this command have special exit code semantics?
        /// Used to decide whether to throw ShellError or pass through.
        /// </summary>
        public static bool HasSpecialSemantics(string command)
        {
            var baseCmd = ExtractBaseCommand(command);
            return !string.IsNullOrWhiteSpace(baseCmd) && Semantics.ContainsKey(baseCmd);
        }

        private static string ExtractBaseCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            var trimmed = command.Trim();

            // Strip leading & or . call operators
            if (trimmed.StartsWith("& ") || trimmed.StartsWith(". "))
                trimmed = trimmed.Substring(2).Trim();

            // Get first word
            var spaceIdx = trimmed.IndexOf(' ');
            var firstWord = spaceIdx > 0 ? trimmed.Substring(0, spaceIdx) : trimmed;

            if (string.IsNullOrWhiteSpace(firstWord)) return null;

            // Lowercase, strip .exe
            var lower = firstWord.ToLowerInvariant().Trim();
            if (lower.EndsWith(".exe"))
                lower = lower.Substring(0, lower.Length - 4);

            // Strip path components (e.g., "C:\\tools\\grep.exe" → "grep")
            var lastSlash = lower.LastIndexOf('\\');
            var lastFwdSlash = lower.LastIndexOf('/');
            var lastSep = Math.Max(lastSlash, lastFwdSlash);
            if (lastSep >= 0)
                lower = lower.Substring(lastSep + 1);

            return lower;
        }

        private static Interpretation InterpretRobocopy(int exitCode)
        {
            // Robocopy exit codes are bitfields:
            // 0 = no files copied (already in sync)
            // 1 = files copied successfully
            // 2 = extra files/dirs detected
            // 4 = mismatched files/dirs detected
            // 8 = some files could not be copied (errors)
            // 16 = serious error (no files copied at all)
            //
            // 0-7 = success, 8+ = at least one failure

            if (exitCode >= 8)
            {
                return new Interpretation
                {
                    IsError = true,
                    Message = null // Let the stderr speak
                };
            }

            var message = exitCode switch
            {
                0 => "No files copied (already in sync)",
                _ when (exitCode & 1) != 0 => "Files copied successfully",
                _ => "Robocopy completed (no errors)"
            };

            return new Interpretation
            {
                IsError = false,
                Message = message
            };
        }
    }
}
