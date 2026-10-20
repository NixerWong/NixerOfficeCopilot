using System;
using System.Collections.Generic;
using System.Management.Automation.Language;
using OfficeCopilot.Agent.Runtime.PowerShell.Security;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Permissions
{
    /// <summary>
    /// Permission check result.
    /// </summary>
    public sealed class PowerShellPermissionResult
    {
        /// <summary>Whether the command is allowed to execute.</summary>
        public bool Allowed { get; set; }

        /// <summary>Whether the command needs user confirmation (ask).</summary>
        public bool NeedsConfirmation { get; set; }

        /// <summary>Human-readable reason for the permission decision.</summary>
        public string Reason { get; set; }

        /// <summary>Suggested permission rule prefix for "remember" dialog.</summary>
        public string SuggestedPrefix { get; set; }

        /// <summary>Whether the command is classified as read-only.</summary>
        public bool IsReadOnly { get; set; }

        public static PowerShellPermissionResult Allow(bool isReadOnly = true)
            => new PowerShellPermissionResult { Allowed = true, NeedsConfirmation = false, IsReadOnly = isReadOnly };

        public static PowerShellPermissionResult Ask(string reason, string suggestedPrefix = null)
            => new PowerShellPermissionResult { Allowed = false, NeedsConfirmation = true, Reason = reason, SuggestedPrefix = suggestedPrefix };

        public static PowerShellPermissionResult Deny(string reason)
            => new PowerShellPermissionResult { Allowed = false, NeedsConfirmation = false, Reason = reason };
    }

    /// <summary>
    /// Main permission checker for PowerShell commands.
    /// Ported from cc-haha <c>tools/PowerShellTool/powershellPermissions.ts</c>.
    ///
    /// Implements the 7-step permission check flow:
    /// Step 1: BypassPermissions mode → allow
    /// Step 2: Plan mode + non-read-only → deny
    /// Step 3: Parse AST → if fail, ask
    /// Step 4: Security check (dangerous cmdlet/CLM) → ask
    /// Step 5: Permission mode auto-allow (AcceptEdits etc.)
    /// Step 6: Read-only → allow
    /// Step 7: Default → ask
    /// </summary>
    public sealed class PowerShellPermissionChecker
    {
        private readonly PermissionMode _mode;
        private readonly HashSet<string> _allowedDirectories;

        public PowerShellPermissionChecker(PermissionMode mode, HashSet<string> allowedDirectories = null)
        {
            _mode = mode;
            _allowedDirectories = allowedDirectories;
        }

        /// <summary>
        /// Check permissions for a PowerShell command.
        /// </summary>
        /// <param name="command">The PowerShell command text.</param>
        /// <returns>Permission result indicating allow/ask/deny.</returns>
        public PowerShellPermissionResult Check(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return PowerShellPermissionResult.Allow();

            // Step 1: BypassPermissions
            if (_mode == PermissionMode.BypassPermissions)
                return PowerShellPermissionResult.Allow(false);

            // Step 2: Plan mode — only read-only
            if (_mode == PermissionMode.Plan)
            {
                if (ReadOnlyClassifier.HasSyncSecurityConcerns(command))
                    return PowerShellPermissionResult.Deny("Plan mode: only read-only commands are allowed.");

                if (!ReadOnlyClassifier.IsReadOnlyCommandSync(command))
                    return PowerShellPermissionResult.Deny("Plan mode: this command may modify the system.");
            }

            // Step 3: Parse AST
            Ast ast;
            try
            {
                ast = Parser.ParseInput(command, out var tokens, out var errors);
                if (ast == null)
                    return PowerShellPermissionResult.Ask("Could not parse PowerShell command for permission analysis.");
            }
            catch (Exception ex)
            {
                return PowerShellPermissionResult.Ask("Could not parse PowerShell command: " + ex.Message);
            }

            // Step 4: Security check
            var securityResult = PowerShellSecurityValidator.Validate(command, ast);
            if (securityResult.Verdict == SecurityVerdict.Deny)
            {
                return PowerShellPermissionResult.Deny(securityResult.Reason ?? "Command blocked by security policy.");
            }
            if (securityResult.Verdict == SecurityVerdict.Ask)
            {
                var prefix = PermissionPrefixExtractor.ExtractPrefix(command);
                return PowerShellPermissionResult.Ask(securityResult.Reason ?? "Command needs confirmation.", prefix);
            }

            // Determine if command is read-only via AST analysis
            var isReadOnly = IsReadOnlyViaAst(ast, command);

            // Step 5: Mode-based auto-allow
            var firstCmdCanonical = GetFirstCommandCanonical(ast);
            if (ModeBasedAutoAllowPolicy.ShouldAutoAllow(firstCmdCanonical, _mode, isReadOnly))
            {
                // Additional path validation for write cmdlets in AcceptEdits mode
                if (!isReadOnly && _mode == PermissionMode.AcceptEdits)
                {
                    if (!ValidatePaths(command))
                        return PowerShellPermissionResult.Ask("Command accesses paths outside allowed directories.");
                }

                return PowerShellPermissionResult.Allow(isReadOnly);
            }

            // Step 6: Read-only → allow
            if (isReadOnly)
                return PowerShellPermissionResult.Allow(true);

            // Step 7: Default → ask
            var suggestPrefix = PermissionPrefixExtractor.ExtractPrefix(command);
            return PowerShellPermissionResult.Ask("Command may modify the system.", suggestPrefix);
        }

        /// <summary>
        /// Check if the command is read-only using AST analysis.
        /// More thorough than the sync check — can inspect pipeline segments and statements.
        /// </summary>
        private bool IsReadOnlyViaAst(Ast ast, string command)
        {
            if (ast == null) return ReadOnlyClassifier.IsReadOnlyCommandSync(command);

            try
            {
                // Find all CommandAst nodes
                var commands = ast.FindAll(n => n is CommandAst, true);
                foreach (CommandAst cmdAst in commands)
                {
                    var cmdName = cmdAst.GetCommandName();
                    if (string.IsNullOrWhiteSpace(cmdName)) continue;

                    var canonical = ReadOnlyClassifier.ResolveToCanonical(cmdName);

                    // If any command in the pipeline is NOT read-only, the whole thing is not read-only
                    if (!ReadOnlyClassifier.IsReadOnlyCmdlet(canonical))
                    {
                        // Check if it's an external command with read-only subcommand (e.g., "git status")
                        if (canonical == "git")
                        {
                            var subcommand = GetSubcommand(cmdAst);
                            if (!ReadOnlyClassifier.IsGitReadOnly(subcommand))
                                return false;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    // Check for dangerous parameters on otherwise-read-only cmdlets
                    if (HasDangerousReadOnlyUsage(cmdAst, canonical))
                        return false;
                }

                return true;
            }
            catch
            {
                // Fall back to sync check on AST failure
                return ReadOnlyClassifier.IsReadOnlyCommandSync(command);
            }
        }

        private static bool HasDangerousReadOnlyUsage(CommandAst cmdAst, string canonical)
        {
            // Get-Content on a variable or with -Raw that pipes to iex
            if (canonical == "invoke-expression" || canonical == "iex")
                return true;

            // Select-String piped to non-read-only
            // (simplified — full check would need pipeline analysis)

            return false;
        }

        private static string GetSubcommand(CommandAst cmdAst)
        {
            var elements = cmdAst.CommandElements;
            if (elements.Count >= 2)
            {
                var secondElem = elements[1];
                if (secondElem is StringConstantExpressionAst strAst)
                {
                    return strAst.Value?.ToLowerInvariant() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        private static string GetFirstCommandCanonical(Ast ast)
        {
            if (ast == null) return string.Empty;

            var firstCmd = ast.Find(n => n is CommandAst, false);
            if (firstCmd is CommandAst cmdAst)
            {
                var cmdName = cmdAst.GetCommandName();
                if (!string.IsNullOrWhiteSpace(cmdName))
                    return ReadOnlyClassifier.ResolveToCanonical(cmdName);
            }
            return string.Empty;
        }

        private bool ValidatePaths(string command)
        {
            if (_allowedDirectories == null || _allowedDirectories.Count == 0) return true;

            var paths = PowerShellPathValidator.ExtractPaths(command);
            if (paths.Count == 0) return true;

            // Check for dangerous paths first
            foreach (var path in paths)
            {
                if (PowerShellPathValidator.IsDangerousPath(path))
                    return false;
            }

            return PowerShellPathValidator.ValidatePaths(paths, _allowedDirectories);
        }
    }
}
