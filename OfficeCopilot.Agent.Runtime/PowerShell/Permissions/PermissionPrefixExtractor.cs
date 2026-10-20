using System.Collections.Generic;
using OfficeCopilot.Agent.Runtime.PowerShell.Security;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Permissions
{
    /// <summary>
    /// Extracts permission rule prefixes from PowerShell commands.
    /// Ported from cc-haha <c>utils/powershell/staticPrefix.ts</c>.
    ///
    /// When the user clicks "Yes, and don't ask again for: ___", this generates
    /// the suggested prefix for the permission rule.
    /// </summary>
    public static class PermissionPrefixExtractor
    {
        /// <summary>
        /// Extract a permission rule prefix from a PowerShell command.
        /// The prefix is the minimal string that uniquely identifies the operation
        /// for which a "remember" rule should apply.
        /// </summary>
        /// <param name="command">The full PowerShell command text.</param>
        /// <returns>A suggested prefix for the permission rule, or null if none can be extracted.</returns>
        public static string ExtractPrefix(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            var trimmed = command.Trim();

            // Get the first pipeline segment
            var firstSegment = GetFirstPipelineSegment(trimmed);
            if (string.IsNullOrWhiteSpace(firstSegment)) return null;

            // Extract the base command (first word)
            var parts = firstSegment.Trim().Split(' ');
            if (parts.Length == 0) return null;

            var baseCmd = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(baseCmd)) return null;

            // Resolve to canonical name
            var canonical = ReadOnlyClassifier.ResolveToCanonical(baseCmd);

            // Never suggest prefixes for shell spawners and dangerous commands
            if (DangerousCmdletCatalog.NeverSuggest.Contains(canonical))
                return null;

            // For cmdlets, suggest the canonical cmdlet name as prefix
            if (IsCmdlet(canonical))
            {
                return canonical;
            }

            // For external commands (git, npm, dotnet, etc.), include subcommand if present
            if (parts.Length >= 2)
            {
                var sub = parts[1].Trim();
                if (!string.IsNullOrWhiteSpace(sub) && !sub.StartsWith("-") && !sub.StartsWith("/"))
                {
                    return canonical + " " + sub;
                }
            }

            return canonical;
        }

        /// <summary>
        /// Check if a canonical name is a PowerShell cmdlet (Verb-Noun pattern).
        /// </summary>
        private static bool IsCmdlet(string canonicalName)
        {
            if (string.IsNullOrWhiteSpace(canonicalName)) return false;
            return canonicalName.Contains("-") && !canonicalName.EndsWith(".exe");
        }

        /// <summary>
        /// Get the first pipeline segment (before any |, ;, &, &&, ||).
        /// </summary>
        private static string GetFirstPipelineSegment(string command)
        {
            // Split by common statement separators but respect quoted strings
            var depth = 0;
            for (int i = 0; i < command.Length; i++)
            {
                var ch = command[i];
                if (ch == '"' || ch == '\'')
                {
                    // Skip quoted strings
                    var quote = ch;
                    i++;
                    while (i < command.Length && command[i] != quote)
                    {
                        if (command[i] == '`') i++; // Skip escaped chars
                        i++;
                    }
                    continue;
                }

                if (ch == '|')
                {
                    return command.Substring(0, i).Trim();
                }
                if (ch == ';')
                {
                    return command.Substring(0, i).Trim();
                }
                if (ch == '&' && i + 1 < command.Length && command[i + 1] != '&')
                {
                    return command.Substring(0, i).Trim();
                }
                if (ch == '&' && i + 1 < command.Length && command[i + 1] == '&')
                {
                    return command.Substring(0, i).Trim();
                }
                if (ch == '(' || ch == '{' || ch == '[') depth++;
                if (ch == ')' || ch == '}' || ch == ']') depth--;
            }

            return command.Trim();
        }
    }
}
