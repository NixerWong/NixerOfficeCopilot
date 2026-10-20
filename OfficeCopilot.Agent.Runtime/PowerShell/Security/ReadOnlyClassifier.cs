using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Security
{
    /// <summary>
    /// Classifies PowerShell commands as read-only or potentially modifying.
    /// Ported from cc-haha <c>tools/PowerShellTool/readOnlyValidation.ts</c>.
    ///
    /// All matching is case-insensitive. Cmdlet names are stored in lowercase canonical form.
    /// </summary>
    public static class ReadOnlyClassifier
    {
        // ------------------------------------------------------------------
        // Aliases: short alias → canonical cmdlet name (lowercase)
        // Ported from cc-haha COMMON_ALIASES in parser.ts
        // ------------------------------------------------------------------
        private static readonly Dictionary<string, string> CommonAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ls", "get-childitem" },
            { "dir", "get-childitem" },
            { "gci", "get-childitem" },
            { "cd", "set-location" },
            { "sl", "set-location" },
            { "cat", "get-content" },
            { "gc", "get-content" },
            { "type", "get-content" },
            { "rm", "remove-item" },
            { "del", "remove-item" },
            { "rd", "remove-item" },
            { "rmdir", "remove-item" },
            { "ri", "remove-item" },
            { "ps", "get-process" },
            { "gps", "get-process" },
            { "kill", "stop-process" },
            { "spps", "stop-process" },
            { "cp", "copy-item" },
            { "copy", "copy-item" },
            { "mv", "move-item" },
            { "move", "move-item" },
            { "mi", "move-item" },
            { "ni", "new-item" },
            { "mkdir", "new-item" },
            { "md", "new-item" },
            { "ren", "rename-item" },
            { "rni", "rename-item" },
            { "echo", "write-output" },
            { "write", "write-output" },
            { "pwd", "get-location" },
            { "gl", "get-location" },
            { "sleep", "start-sleep" },
            { "iex", "invoke-expression" },
            { "icm", "invoke-command" },
            { "ipmo", "import-module" },
            { "fl", "format-list" },
            { "ft", "format-table" },
            { "fw", "format-wide" },
            { "select", "select-object" },
            { "where", "where-object" },
            { "foreach", "foreach-object" },
            { "measure", "measure-object" },
            { "sort", "sort-object" },
            { "group", "group-object" },
            { "diff", "compare-object" },
            { "compare", "compare-object" },
            { "tee", "tee-object" },
            { "epcsv", "export-csv" },
            { "ipcsv", "import-csv" },
            { "sc", "set-content" },
            { "ac", "add-content" },
            { "clc", "clear-content" },
            { "gal", "get-alias" },
            { "start", "start-process" },
            { "saps", "start-process" },
            { "iwr", "invoke-webrequest" },
            { "curl", "invoke-webrequest" },
            { "wget", "invoke-webrequest" },
            { "irm", "invoke-restmethod" },
            { "man", "get-help" },
            { "help", "get-help" },
            { "history", "get-history" },
            { "gcm", "get-command" },
            { "gm", "get-member" }
        };

        // ------------------------------------------------------------------
        // Read-only cmdlets (and their aliases via CommonAliases)
        // ------------------------------------------------------------------
        private static readonly HashSet<string> ReadOnlyCmdlets = new HashSet<string>
        {
            "get-childitem", "get-content", "get-item", "test-path",
            "resolve-path", "get-process", "get-service", "get-location",
            "get-filehash", "get-acl", "format-hex", "get-date",
            "get-history", "get-command", "get-member", "get-alias",
            "get-module", "get-psdrive", "get-psprovider", "get-variable",
            "get-help", "select-string", "select-object", "where-object",
            "foreach-object", "sort-object", "group-object", "measure-object",
            "compare-object", "format-list", "format-table", "format-wide",
            "tee-object", "get-culture", "get-uiculture", "get-host",
            "get-random", "get-unique", "get-winevent", "get-eventlog",
            "get-counter", "get-psreadlineoption", "get-clipboard",
            "out-string", "out-null", "write-output", "write-host",
            "write-information", "write-verbose", "write-debug"
        };

        // ------------------------------------------------------------------
        // Semantic-neutral commands (don't change read/write classification)
        // ------------------------------------------------------------------
        private static readonly HashSet<string> SemanticNeutral = new HashSet<string>
        {
            "write-output", "write-host"
        };

        // ------------------------------------------------------------------
        // Search commands (collapsible display in UI)
        // ------------------------------------------------------------------
        private static readonly HashSet<string> SearchCommands = new HashSet<string>
        {
            "select-string", "get-childitem", "findstr", "where.exe"
        };

        // ------------------------------------------------------------------
        // External executables that are read-only when called with certain flags
        // ------------------------------------------------------------------
        private static readonly HashSet<string> GitReadOnlySubcommands = new HashSet<string>
        {
            "status", "log", "diff", "show", "branch", "tag", "rev-parse",
            "rev-list", "ls-files", "ls-tree", "cat-file", "describe",
            "remote", "config", "stash", "blame", "shortlog", "reflog",
            "worktree", "notes", "for-each-ref", "name-rev", "merge-base",
            "cherry", "format-patch", "check-attr", "check-ignore",
            "check-ref-format", "check-mailmap"
        };

        private static readonly HashSet<string> DotNetReadOnlyFlags = new HashSet<string>
        {
            "--version", "--info", "--list-runtimes", "--list-sdks"
        };

        // Regex patterns for sync security concerns (hasSyncSecurityConcerns)
        private static readonly Regex SubExpressionRegex = new Regex(@"\$\(|@\(|@\{", RegexOptions.Compiled);
        private static readonly Regex AssignmentRegex = new Regex(@"(?:^|[|;&\n\r])\s*\$[\w:]+\s*[+\-*/%]?=", RegexOptions.Compiled);
        private static readonly Regex DynamicInvocationRegex = new Regex(@"(?:^|[|;&\n\r])\s*[.&]\s*\$", RegexOptions.Compiled);
        private static readonly Regex MemberInvocationRegex = new Regex(@"\.\w+\s*\(", RegexOptions.Compiled);
        private static readonly Regex PipelineVariableRegex = new Regex(@"\$\w+\s*\|", RegexOptions.Compiled);

        /// <summary>
        /// Resolve an alias or cmdlet name to its canonical (lowercase) form.
        /// </summary>
        public static string ResolveToCanonical(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var lower = name.Trim().ToLowerInvariant();
            // Strip .exe suffix for external commands
            if (lower.EndsWith(".exe")) lower = lower.Substring(0, lower.Length - 4);
            return CommonAliases.TryGetValue(lower, out var canonical) ? canonical : lower;
        }

        /// <summary>
        /// Quick sync check: does the command have obvious non-read-only patterns?
        /// Used by the sync isReadOnly() path (before AST parse is available).
        /// </summary>
        public static bool HasSyncSecurityConcerns(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            return SubExpressionRegex.IsMatch(command)
                || AssignmentRegex.IsMatch(command)
                || DynamicInvocationRegex.IsMatch(command)
                || MemberInvocationRegex.IsMatch(command);
        }

        /// <summary>
        /// Check if a single cmdlet name (canonical) is read-only.
        /// </summary>
        public static bool IsReadOnlyCmdlet(string canonicalName)
        {
            return ReadOnlyCmdlets.Contains(canonicalName);
        }

        /// <summary>
        /// Check if a first-level command classification (without AST) is likely read-only.
        /// This is a best-effort sync check; full AST-based validation is done async.
        /// </summary>
        public static bool IsReadOnlyCommandSync(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return true;
            if (HasSyncSecurityConcerns(command)) return false;

            // Extract first pipeline segment's base command
            var firstSegment = command.Trim().Split('|', ';', '&')[0].Trim();
            var firstWord = firstSegment.Split(' ')[0].Trim();
            if (string.IsNullOrWhiteSpace(firstWord)) return false;

            var canonical = ResolveToCanonical(firstWord);
            if (SemanticNeutral.Contains(canonical)) return true;
            return ReadOnlyCmdlets.Contains(canonical);
        }

        /// <summary>
        /// Check if a command is a git read-only subcommand.
        /// </summary>
        public static bool IsGitReadOnly(string subcommand)
        {
            return GitReadOnlySubcommands.Contains(subcommand?.ToLowerInvariant() ?? string.Empty);
        }

        /// <summary>
        /// Check if a dotnet flag is read-only.
        /// </summary>
        public static bool IsDotNetReadOnlyFlag(string flag)
        {
            return DotNetReadOnlyFlags.Contains(flag?.ToLowerInvariant() ?? string.Empty);
        }

        /// <summary>
        /// Check if a cmdlet is a search/read command (for UI collapse display).
        /// </summary>
        public static bool IsSearchOrReadCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            var firstWord = command.Trim().Split('|', ';', '&', ' ')[0].Trim();
            if (string.IsNullOrWhiteSpace(firstWord)) return false;
            var canonical = ResolveToCanonical(firstWord);
            return SearchCommands.Contains(canonical) || ReadOnlyCmdlets.Contains(canonical);
        }
    }
}
