using System.Collections.Generic;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Security
{
    /// <summary>
    /// PowerShell cmdlets that execute arbitrary code (dangerous by nature).
    /// Ported from cc-haha <c>utils/powershell/dangerousCmdlets.ts</c>.
    /// All entries are lowercase canonical names.
    /// </summary>
    public static class DangerousCmdletCatalog
    {
        /// <summary>
        /// Cmdlets that accept a -FilePath and execute the file's contents as a script.
        /// </summary>
        public static readonly HashSet<string> FilePathExecutionCmdlets = new HashSet<string>
        {
            "invoke-command",
            "start-job",
            "start-threadjob",
            "register-scheduledjob"
        };

        /// <summary>
        /// Cmdlets where a -ScriptBlock argument executes arbitrary code.
        /// </summary>
        public static readonly HashSet<string> DangerousScriptBlockCmdlets = new HashSet<string>
        {
            "invoke-command",
            "invoke-expression",
            "start-job",
            "start-threadjob",
            "register-scheduledjob",
            "register-engineevent",
            "register-objectevent",
            "register-wmievent",
            "new-pssession",
            "enter-pssession"
        };

        /// <summary>
        /// Cmdlets that load and execute module/script code.
        /// </summary>
        public static readonly HashSet<string> ModuleLoadingCmdlets = new HashSet<string>
        {
            "import-module",
            "ipmo",
            "install-module",
            "save-module",
            "update-module",
            "install-script",
            "save-script"
        };

        /// <summary>
        /// Shells and process spawners. These create child processes.
        /// </summary>
        public static readonly HashSet<string> ShellsAndSpawners = new HashSet<string>
        {
            "pwsh", "powershell", "cmd", "bash", "wsl", "sh",
            "start-process", "start",
            "add-type",
            "new-object"
        };

        /// <summary>
        /// Commands for which we should NEVER suggest a "remember" prefix
        /// in the permission dialog. These are always dangerous.
        /// </summary>
        public static readonly HashSet<string> NeverSuggest = new HashSet<string>
        {
            "powershell", "pwsh", "cmd", "bash", "wsl", "sh",
            "start-process", "start",
            "invoke-expression", "iex",
            "invoke-command", "icm",
            "add-type",
            "new-object"
        };

        /// <summary>
        /// Combined set of all dangerous cmdlets (union of all categories above).
        /// </summary>
        public static readonly HashSet<string> AllDangerous = BuildAllDangerous();

        private static HashSet<string> BuildAllDangerous()
        {
            var all = new HashSet<string>();
            foreach (var c in FilePathExecutionCmdlets) all.Add(c);
            foreach (var c in DangerousScriptBlockCmdlets) all.Add(c);
            foreach (var c in ModuleLoadingCmdlets) all.Add(c);
            foreach (var c in ShellsAndSpawners) all.Add(c);
            return all;
        }

        /// <summary>
        /// Check if a cmdlet name (lowercase, canonical) is in any dangerous category.
        /// </summary>
        public static bool IsDangerous(string canonicalName)
        {
            return AllDangerous.Contains(canonicalName);
        }
    }
}
