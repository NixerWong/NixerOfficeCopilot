using System;

namespace OfficeCopilot.Agent.Runtime.PowerShell
{
    /// <summary>
    /// Detects the installed PowerShell edition and version.
    /// Corresponds to cc-haha <c>utils/shell/powershellDetection.ts</c>.
    /// </summary>
    public static class PowerShellVersionDetector
    {
        /// <summary>Maximum output length before truncation (matches cc-haha).</summary>
        public const int MaxOutputLength = 30_000;

        /// <summary>Default command timeout in milliseconds.</summary>
        public const int DefaultTimeoutMs = 120_000; // 2 minutes

        /// <summary>Maximum allowed timeout in milliseconds.</summary>
        public const int MaxTimeoutMs = 600_000; // 10 minutes

        /// <summary>
        /// Detect the PowerShell edition (Desktop 5.1 vs Core 7+).
        /// Uses <see cref="System.Management.Automation.PSVersionInfo"/> when SMA is loaded.
        /// </summary>
        public static PowerShellEdition DetectEdition()
        {
            try
            {
                // PSVersionInfo is not in reference assemblies; use reflection
                var psiType = typeof(System.Management.Automation.PSObject).Assembly.GetType("System.Management.Automation.PSVersionInfo");
                var psVersionProp = psiType?.GetProperty("PSVersion");
                var version = (Version)psVersionProp?.GetValue(null);
                if (version == null) return PowerShellEdition.Desktop;
                if (version.Major >= 7)
                    return PowerShellEdition.Core;
                if (version.Major == 5)
                    return PowerShellEdition.Desktop;
                // Unknown future version — assume Core (more feature-rich)
                return version.Major > 5 ? PowerShellEdition.Core : PowerShellEdition.Desktop;
            }
            catch
            {
                // SMA not available — assume Desktop (most conservative)
                return PowerShellEdition.Desktop;
            }
        }

        /// <summary>
        /// Get the PS version string for diagnostics (e.g. "5.1.19041.1").
        /// </summary>
        public static string GetVersionString()
        {
            try
            {
                var psiType = typeof(System.Management.Automation.PSObject).Assembly.GetType("System.Management.Automation.PSVersionInfo");
                var psVersionProp = psiType?.GetProperty("PSVersion");
                var version = (Version)psVersionProp?.GetValue(null);
                return version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }

    /// <summary>
    /// PowerShell edition: Desktop (Windows PowerShell 5.1) or Core (PowerShell 7+).
    /// </summary>
    public enum PowerShellEdition
    {
        /// <summary>Windows PowerShell 5.1 (powershell.exe). No &&, ||, ?: operators.</summary>
        Desktop = 0,

        /// <summary>PowerShell 7+ (pwsh.exe). Modern operators available.</summary>
        Core = 1
    }
}
