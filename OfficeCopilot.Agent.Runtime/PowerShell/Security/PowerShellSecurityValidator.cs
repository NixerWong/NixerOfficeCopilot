using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Security
{
    /// <summary>
    /// Result of a PowerShell security validation.
    /// </summary>
    public sealed class SecurityValidationResult
    {
        /// <summary>Is the command safe to execute?</summary>
        public bool IsSafe { get; set; }

        /// <summary>Security verdict: passthrough (safe), ask (needs confirmation), deny (blocked).</summary>
        public SecurityVerdict Verdict { get; set; }

        /// <summary>Human-readable reason for the verdict.</summary>
        public string Reason { get; set; }

        /// <summary>Whether the command is classified as read-only.</summary>
        public bool IsReadOnly { get; set; }

        public static SecurityValidationResult Passthrough()
            => new SecurityValidationResult { IsSafe = true, Verdict = SecurityVerdict.Passthrough };

        public static SecurityValidationResult Ask(string reason)
            => new SecurityValidationResult { IsSafe = false, Verdict = SecurityVerdict.Ask, Reason = reason };

        public static SecurityValidationResult Deny(string reason)
            => new SecurityValidationResult { IsSafe = false, Verdict = SecurityVerdict.Deny, Reason = reason };
    }

    /// <summary>
    /// Security verdict for a PowerShell command.
    /// </summary>
    public enum SecurityVerdict
    {
        /// <summary>Command is safe — allow without confirmation.</summary>
        Passthrough = 0,

        /// <summary>Command needs user confirmation.</summary>
        Ask = 1,

        /// <summary>Command is blocked (never allowed).</summary>
        Deny = 2
    }

    /// <summary>
    /// Analyzes PowerShell commands for security concerns.
    /// Ported from cc-haha <c>tools/PowerShellTool/powershellSecurity.ts</c>.
    ///
    /// Five categories of checks:
    /// 1. Dangerous ScriptBlock cmdlets (Invoke-Expression, etc.)
    /// 2. FilePath execution cmdlets (Invoke-Command -FilePath, etc.)
    /// 3. Module loading cmdlets (Import-Module, etc.)
    /// 4. Shell/process spawners (Start-Process, cmd, pwsh, etc.)
    /// 5. CLM type violations (non-whitelist [Type] literals)
    /// </summary>
    public static class PowerShellSecurityValidator
    {
        // PowerShell executables that create child PowerShell processes
        private static readonly HashSet<string> PowerShellExecutables = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "pwsh", "pwsh.exe", "powershell", "powershell.exe"
        };

        // Regex patterns for additional threats not covered by AST
        private static readonly Regex DownloadCradleRegex = new Regex(
            @"(Invoke-WebRequest|Invoke-RestMethod|iwr|irm|curl|wget)\s.*\bhttp",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ComObjectRegex = new Regex(
            @"New-Object\s.*-ComObject",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AddTypeRegex = new Regex(
            @"Add-Type",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Validate a PowerShell command for security concerns.
        /// Uses SMA's Parser.ParseInput() for AST analysis.
        /// </summary>
        /// <param name="command">The PowerShell command text.</param>
        /// <param name="parsedAst">Pre-parsed AST (optional). If null, parses internally.</param>
        /// <returns>Security validation result.</returns>
        public static SecurityValidationResult Validate(string command, Ast parsedAst = null)
        {
            if (string.IsNullOrWhiteSpace(command))
                return SecurityValidationResult.Passthrough();

            Ast ast;
            ParseError[] errors;

            try
            {
                if (parsedAst != null)
                {
                    ast = parsedAst;
                    errors = Array.Empty<ParseError>();
                }
                else
                {
                        ast = Parser.ParseInput(command, out var tokens, out var parseErrors);
                        errors = parseErrors;
                }
            }
            catch (Exception ex)
            {
                // If parsing fails entirely, be cautious
                return SecurityValidationResult.Ask("Unable to parse PowerShell command: " + ex.Message);
            }

            // If the AST couldn't be parsed meaningfully, ask
            if (ast == null)
                return SecurityValidationResult.Ask("Could not parse PowerShell command AST.");

            // Run the five security checks
            var result = CheckDangerousScriptBlock(ast);
            if (result != null) return result;

            result = CheckFilePathExecution(ast);
            if (result != null) return result;

            result = CheckModuleLoading(ast);
            if (result != null) return result;

            result = CheckShellSpawning(ast);
            if (result != null) return result;

            result = CheckClmTypes(ast);
            if (result != null) return result;

            // Additional regex-based checks (not AST-detectable)
            result = CheckRegexPatterns(command);
            if (result != null) return result;

            return SecurityValidationResult.Passthrough();
        }

        private static SecurityValidationResult CheckDangerousScriptBlock(Ast ast)
        {
            var cmdlets = FindAllCommandAsts(ast);
            foreach (var cmd in cmdlets)
            {
                var cmdName = GetCommandName(cmd);
                var canonical = ReadOnlyClassifier.ResolveToCanonical(cmdName);

                if (DangerousCmdletCatalog.DangerousScriptBlockCmdlets.Contains(canonical))
                {
                    // Check if a -ScriptBlock argument is present
                    if (HasParameter(cmd, "ScriptBlock", "sb"))
                    {
                        return SecurityValidationResult.Ask(
                            "Command uses " + cmdName + " with a -ScriptBlock, which executes arbitrary code.");
                    }
                }
            }
            return null;
        }

        private static SecurityValidationResult CheckFilePathExecution(Ast ast)
        {
            var cmdlets = FindAllCommandAsts(ast);
            foreach (var cmd in cmdlets)
            {
                var cmdName = GetCommandName(cmd);
                var canonical = ReadOnlyClassifier.ResolveToCanonical(cmdName);

                if (DangerousCmdletCatalog.FilePathExecutionCmdlets.Contains(canonical))
                {
                    if (HasParameter(cmd, "FilePath", "f"))
                    {
                        return SecurityValidationResult.Ask(
                            "Command uses " + cmdName + " with -FilePath, which executes file contents.");
                    }
                }
            }
            return null;
        }

        private static SecurityValidationResult CheckModuleLoading(Ast ast)
        {
            var cmdlets = FindAllCommandAsts(ast);
            foreach (var cmd in cmdlets)
            {
                var cmdName = GetCommandName(cmd);
                var canonical = ReadOnlyClassifier.ResolveToCanonical(cmdName);

                if (DangerousCmdletCatalog.ModuleLoadingCmdlets.Contains(canonical))
                {
                    return SecurityValidationResult.Ask(
                        "Command loads a module (" + cmdName + "), which executes module code.");
                }
            }
            return null;
        }

        private static SecurityValidationResult CheckShellSpawning(Ast ast)
        {
            var cmdlets = FindAllCommandAsts(ast);
            foreach (var cmd in cmdlets)
            {
                var cmdName = GetCommandName(cmd);
                var canonical = ReadOnlyClassifier.ResolveToCanonical(cmdName);

                if (DangerousCmdletCatalog.ShellsAndSpawners.Contains(canonical))
                {
                    return SecurityValidationResult.Ask(
                        "Command spawns a child process (" + cmdName + "), which may execute arbitrary code.");
                }

                // Also check for PowerShell executable invocations
                if (PowerShellExecutables.Contains(cmdName))
                {
                    return SecurityValidationResult.Ask(
                        "Command invokes a nested PowerShell process (" + cmdName + ").");
                }
            }
            return null;
        }

        private static SecurityValidationResult CheckClmTypes(Ast ast)
        {
            // Find all TypeExpressionAst nodes in the AST
            var typeExprs = ast.FindAll(n => n is TypeExpressionAst, true);
            foreach (TypeExpressionAst typeExpr in typeExprs)
            {
                var typeName = typeExpr.TypeName?.Name;
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    if (!ConstrainedLanguageModeCatalog.IsAllowed(typeName))
                    {
                        return SecurityValidationResult.Ask(
                            "Command uses type [" + typeName + "] which is not in the CLM allowlist.");
                    }
                }
            }
            return null;
        }

        private static SecurityValidationResult CheckRegexPatterns(string command)
        {
            // Download cradle detection
            if (DownloadCradleRegex.IsMatch(command))
            {
                return SecurityValidationResult.Ask(
                    "Command downloads content from a URL — potential download cradle.");
            }

            // COM object creation
            if (ComObjectRegex.IsMatch(command))
            {
                return SecurityValidationResult.Ask(
                    "Command creates a COM object, which may execute arbitrary code.");
            }

            // Add-Type with DllImport or code injection
            if (AddTypeRegex.IsMatch(command))
            {
                return SecurityValidationResult.Ask(
                    "Command uses Add-Type with custom code, which may execute arbitrary code.");
            }

            return null;
        }

        // ------------------------------------------------------------------
        // AST helper methods
        // ------------------------------------------------------------------

        private static List<CommandAst> FindAllCommandAsts(Ast ast)
        {
            return ast.FindAll(n => n is CommandAst, true)
                .Cast<CommandAst>()
                .ToList();
        }

        private static string GetCommandName(CommandAst commandAst)
        {
            var cmdName = commandAst.GetCommandName();
            return cmdName ?? string.Empty;
        }

        private static bool HasParameter(CommandAst commandAst, string paramName, string abbreviation)
        {
            foreach (var element in commandAst.CommandElements)
            {
                if (element is CommandParameterAst param)
                {
                    var name = param.ParameterName?.ToLowerInvariant() ?? string.Empty;
                    if (name == paramName.ToLowerInvariant() || name == abbreviation.ToLowerInvariant())
                        return true;
                }
            }
            return false;
        }
    }
}
