using System;
using System.Collections.Generic;

namespace OfficeCopilot.Settings.Abstractions
{
    /// <summary>
    /// Per-app or shared built-in tool configuration, stored in
    /// builtin_tools.{app}.json / builtin_tools.shared.json. The PowerPoint app key is
    /// "powerpoint" because it comes from the host application's name.
    /// </summary>
    public sealed class BuiltinToolSettings
    {
        public BuiltinToolSettings()
        {
            ConfigurationVersion = 1;
            CreatedAtUtc = DateTime.UtcNow;
            UpdatedAtUtc = DateTime.UtcNow;
            ToolGroupRegistrations = new List<ToolGroupRegistrationSettings>();
            ToolRegistrations = new List<ToolRegistrationSettings>();
            ToolConfirmationPolicies = new List<ToolConfirmationPolicySettings>();
            ToolDefinitionOverrides = new List<ToolDefinitionOverrideSettings>();
        }

        public int ConfigurationVersion { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public List<ToolGroupRegistrationSettings> ToolGroupRegistrations { get; set; }

        public List<ToolRegistrationSettings> ToolRegistrations { get; set; }

        public List<ToolConfirmationPolicySettings> ToolConfirmationPolicies { get; set; }

        public List<ToolDefinitionOverrideSettings> ToolDefinitionOverrides { get; set; }
    }
}
