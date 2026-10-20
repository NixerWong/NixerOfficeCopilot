using OfficeCopilot.Agent.Runtime.PowerShell.Security;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Permissions
{
    /// <summary>
    /// Determines whether a PowerShell command should be auto-allowed
    /// based on the current <see cref="PermissionMode"/>.
    /// Ported from cc-haha <c>tools/PowerShellTool/modeValidation.ts</c>.
    /// </summary>
    public static class ModeBasedAutoAllowPolicy
    {
        /// <summary>
        /// Filesystem-modifying cmdlets that are auto-allowed in AcceptEdits mode.
        /// Simple write cmdlets only — complex ones (New-Item, Copy-Item, Move-Item)
        /// are excluded because of complex parameter binding.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> AcceptEditsAllowedCmdlets =
            new System.Collections.Generic.HashSet<string>
            {
                "set-content",
                "add-content",
                "remove-item",
                "clear-content"
            };

        /// <summary>
        /// New-Item -ItemType values that create filesystem links (symlink/junction/hardlink).
        /// These redirect path resolution and can bypass validator checks.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> LinkItemTypes =
            new System.Collections.Generic.HashSet<string>
            {
                "symboliclink",
                "junction",
                "hardlink"
            };

        /// <summary>
        /// Check if a command should be auto-allowed in the current permission mode.
        /// </summary>
        /// <param name="canonicalCmdletName">Lowercase canonical cmdlet name.</param>
        /// <param name="mode">Current permission mode.</param>
        /// <param name="isReadOnly">Whether the command is classified as read-only.</param>
        /// <returns>True if the command should be auto-allowed.</returns>
        public static bool ShouldAutoAllow(string canonicalCmdletName, PermissionMode mode, bool isReadOnly)
        {
            switch (mode)
            {
                case PermissionMode.BypassPermissions:
                    return true;

                case PermissionMode.Plan:
                    // Plan mode: only read-only commands allowed
                    return isReadOnly;

                case PermissionMode.AcceptEdits:
                    // AcceptEdits: read-only + simple write cmdlets
                    if (isReadOnly) return true;
                    return AcceptEditsAllowedCmdlets.Contains(canonicalCmdletName);

                case PermissionMode.Default:
                default:
                    // Default mode: only read-only commands auto-allowed
                    return isReadOnly;
            }
        }

        /// <summary>
        /// Check if a New-Item command creates a filesystem link (symlink/junction/hardlink).
        /// </summary>
        public static bool IsLinkCreatingCommand(string canonicalCmdletName)
        {
            // Only New-Item can create links
            if (canonicalCmdletName != "new-item") return false;

            // This requires AST analysis to check -ItemType parameter — handled
            // by the caller (PowerShellPermissionChecker) before auto-allow.
            return true; // Flag for further AST inspection
        }

        /// <summary>
        /// Check if a specific -ItemType value creates a filesystem link.
        /// </summary>
        public static bool IsLinkItemType(string itemTypeValue)
        {
            if (string.IsNullOrWhiteSpace(itemTypeValue)) return false;
            return LinkItemTypes.Contains(itemTypeValue.ToLowerInvariant().Trim());
        }
    }
}
