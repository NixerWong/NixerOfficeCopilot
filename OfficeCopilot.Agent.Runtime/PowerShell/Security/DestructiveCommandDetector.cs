using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Security
{
    /// <summary>
    /// Detects potentially destructive PowerShell command patterns.
    /// Ported from cc-haha <c>tools/PowerShellTool/destructiveCommandWarning.ts</c>.
    /// Returns human-readable warning strings for permission dialogs.
    /// Does NOT affect permission logic — purely informational.
    /// </summary>
    public static class DestructiveCommandDetector
    {
        private sealed class DestructivePattern
        {
            public Regex Pattern { get; set; }
            public string Warning { get; set; }
        }

        private static readonly List<DestructivePattern> Patterns = new List<DestructivePattern>
        {
            // Remove-Item with -Recurse and/or -Force
            new DestructivePattern
            {
                Pattern = new Regex(@"(?:^|[|;&\n({])\s*(Remove-Item|rm|del|rd|rmdir|ri)\b[^|;&\n}]*-(Recurse|Force)\b",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: may force-remove files"
            },

            // Clear-Content on broad paths (with wildcard)
            new DestructivePattern
            {
                Pattern = new Regex(@"\bClear-Content\b[^|;&\n]*\*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: may clear content of multiple files"
            },

            // Format-Volume
            new DestructivePattern
            {
                Pattern = new Regex(@"\bFormat-Volume\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: may format a disk volume"
            },

            // Clear-Disk
            new DestructivePattern
            {
                Pattern = new Regex(@"\bClear-Disk\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: may clear a disk"
            },

            // git reset --hard
            new DestructivePattern
            {
                Pattern = new Regex(@"\bgit\s+reset\s+--hard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: may discard uncommitted changes"
            },

            // git push --force / --force-with-lease / -f
            new DestructivePattern
            {
                Pattern = new Regex(@"\bgit\s+push\b[^|;&\n]*\s+(--force|--force-with-lease|-f)\b",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: may overwrite remote history"
            },

            // git clean -f (without -n dry-run)
            new DestructivePattern
            {
                Pattern = new Regex(@"\bgit\s+clean\b(?![^|;&\n]*(?:-[a-zA-Z]*n|--dry-run))[^|;&\n]*-[a-zA-Z]*f",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: may permanently delete untracked files"
            },

            // git stash drop / clear
            new DestructivePattern
            {
                Pattern = new Regex(@"\bgit\s+stash\s+(drop|clear)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: may permanently remove stashed changes"
            },

            // DROP / TRUNCATE TABLE / DATABASE / SCHEMA
            new DestructivePattern
            {
                Pattern = new Regex(@"\b(DROP|TRUNCATE)\s+(TABLE|DATABASE|SCHEMA)\b",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: may drop or truncate database objects"
            },

            // Stop-Computer
            new DestructivePattern
            {
                Pattern = new Regex(@"\bStop-Computer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: will shut down the computer"
            },

            // Restart-Computer
            new DestructivePattern
            {
                Pattern = new Regex(@"\bRestart-Computer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: will restart the computer"
            },

            // Clear-RecycleBin
            new DestructivePattern
            {
                Pattern = new Regex(@"\bClear-RecycleBin\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                Warning = "Note: permanently deletes recycled files"
            }
        };

        /// <summary>
        /// Check if a PowerShell command matches known destructive patterns.
        /// Returns a human-readable warning string, or null if no destructive pattern is detected.
        /// </summary>
        public static string Detect(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;

            foreach (var entry in Patterns)
            {
                if (entry.Pattern.IsMatch(command))
                {
                    return entry.Warning;
                }
            }
            return null;
        }
    }
}
