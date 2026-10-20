using System.Collections.Generic;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Security
{
    /// <summary>
    /// PowerShell common parameters available on all advanced functions/cmdlets.
    /// Ported from cc-haha <c>tools/PowerShellTool/commonParameters.ts</c>.
    /// Source: about_CommonParameters (PowerShell docs).
    /// </summary>
    public static class PowerShellCommonParameters
    {
        /// <summary>Switch parameters (take no value). Stored with leading dash.</summary>
        public static readonly HashSet<string> CommonSwitches = new HashSet<string>
        {
            "-verbose",
            "-debug"
        };

        /// <summary>Value parameters (take a value, consumed but not a path). Stored with leading dash.</summary>
        public static readonly HashSet<string> CommonValueParams = new HashSet<string>
        {
            "-erroraction",
            "-warningaction",
            "-informationaction",
            "-progressaction",
            "-errorvariable",
            "-warningvariable",
            "-informationvariable",
            "-outvariable",
            "-outbuffer",
            "-pipelinevariable"
        };

        /// <summary>All common parameters (switches + value params).</summary>
        public static readonly HashSet<string> All = BuildAll();

        private static HashSet<string> BuildAll()
        {
            var all = new HashSet<string>();
            foreach (var s in CommonSwitches) all.Add(s);
            foreach (var v in CommonValueParams) all.Add(v);
            return all;
        }

        /// <summary>Check if a parameter name is a PowerShell common parameter.</summary>
        public static bool IsCommonParameter(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return false;
            return All.Contains(parameterName.ToLowerInvariant());
        }
    }
}
