using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Settings.Abstractions
{
    public sealed class ToolConfirmationPolicySettings
    {
        public ToolConfirmationPolicySettings()
        {
            ToolName = string.Empty;
            Mode = string.Empty;
            UpdatedAtUtc = DateTime.UtcNow;
        }

        public string ToolName { get; set; }

        public string Mode { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }

    public sealed class ToolDefinitionOverrideSettings
    {
        public ToolDefinitionOverrideSettings()
        {
            ToolName = string.Empty;
            UpdatedAtUtc = DateTime.UtcNow;
        }

        public string ToolName { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// 为 null 时保持工具自身声明的确认策略；否则覆盖它。
        /// </summary>
        public bool? RequiresConfirmation { get; set; }

        public bool? RequiresBackup { get; set; }

        public string InputSchemaJson { get; set; }

        public string OutputSchemaJson { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }

    public sealed class ToolGroupRegistrationSettings
    {
        public ToolGroupRegistrationSettings()
        {
            GroupName = string.Empty;
            IsRegistered = true;
            UpdatedAtUtc = DateTime.UtcNow;
        }

        public string GroupName { get; set; }

        public bool IsRegistered { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }

    public sealed class ToolRegistrationSettings
    {
        public ToolRegistrationSettings()
        {
            GroupName = string.Empty;
            ToolName = string.Empty;
            IsRegistered = true;
            UpdatedAtUtc = DateTime.UtcNow;
        }

        public string GroupName { get; set; }

        public string ToolName { get; set; }

        public bool IsRegistered { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }

    public static class OfficeCopilotToolSettingsExtensions
    {
        public static bool HasToolRegistrationConfiguration(this OfficeCopilotSettings settings)
        {
            return settings != null
                && ((settings.ToolGroupRegistrations != null
                        && settings.ToolGroupRegistrations.Any(p => p != null && !string.IsNullOrWhiteSpace(p.GroupName)))
                    || (settings.ToolRegistrations != null
                        && settings.ToolRegistrations.Any(p => p != null && !string.IsNullOrWhiteSpace(p.ToolName))));
        }

        public static bool? GetToolGroupRegistration(this OfficeCopilotSettings settings, string groupName)
        {
            if (settings == null
                || settings.ToolGroupRegistrations == null
                || string.IsNullOrWhiteSpace(groupName))
            {
                return null;
            }

            var normalizedGroupName = groupName.Trim();
            var existing = settings.ToolGroupRegistrations.FirstOrDefault(p =>
                p != null
                && string.Equals(p.GroupName, normalizedGroupName, StringComparison.OrdinalIgnoreCase));

            return existing == null ? (bool?)null : existing.IsRegistered;
        }

        public static bool? GetToolRegistration(this OfficeCopilotSettings settings, string toolName)
        {
            if (settings == null
                || settings.ToolRegistrations == null
                || string.IsNullOrWhiteSpace(toolName))
            {
                return null;
            }

            var normalizedToolName = toolName.Trim();
            var existing = settings.ToolRegistrations.FirstOrDefault(p =>
                p != null
                && string.Equals(p.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase));

            return existing == null ? (bool?)null : existing.IsRegistered;
        }

        public static ISet<string> GetRegisteredToolGroupNames(this OfficeCopilotSettings settings)
        {
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings == null || settings.ToolGroupRegistrations == null)
            {
                return groups;
            }

            foreach (var item in settings.ToolGroupRegistrations)
            {
                if (item == null || !item.IsRegistered || string.IsNullOrWhiteSpace(item.GroupName))
                {
                    continue;
                }

                groups.Add(item.GroupName.Trim());
            }

            return groups;
        }

        public static ISet<string> GetRegisteredToolNames(this OfficeCopilotSettings settings)
        {
            var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings == null || settings.ToolRegistrations == null)
            {
                return tools;
            }

            foreach (var item in settings.ToolRegistrations)
            {
                if (item == null || !item.IsRegistered || string.IsNullOrWhiteSpace(item.ToolName))
                {
                    continue;
                }

                tools.Add(item.ToolName.Trim());
            }

            return tools;
        }

        public static bool IsToolAutoApproved(this OfficeCopilotSettings settings, string toolName)
        {
            if (settings == null
                || settings.ToolConfirmationPolicies == null
                || string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            var normalizedToolName = toolName.Trim();
            return settings.ToolConfirmationPolicies.Any(p =>
                p != null
                && string.Equals(p.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Mode, "allow-always", StringComparison.OrdinalIgnoreCase));
        }

        public static bool HasToolDefinitionOverrideConfiguration(this OfficeCopilotSettings settings)
        {
            return settings != null
                && settings.ToolDefinitionOverrides != null
                && settings.ToolDefinitionOverrides.Any(p => p != null && !string.IsNullOrWhiteSpace(p.ToolName));
        }

        public static ToolDefinitionOverrideSettings GetToolDefinitionOverride(this OfficeCopilotSettings settings, string toolName)
        {
            if (settings == null
                || settings.ToolDefinitionOverrides == null
                || string.IsNullOrWhiteSpace(toolName))
            {
                return null;
            }

            var normalizedToolName = toolName.Trim();
            return settings.ToolDefinitionOverrides.FirstOrDefault(p =>
                p != null
                && string.Equals(p.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<ToolDefinitionOverrideSettings> GetToolDefinitionOverrides(this OfficeCopilotSettings settings)
        {
            if (settings == null || settings.ToolDefinitionOverrides == null)
            {
                return new List<ToolDefinitionOverrideSettings>();
            }

            return settings.ToolDefinitionOverrides
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.ToolName))
                .OrderBy(p => p.ToolName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void SetToolGroupRegistration(this OfficeCopilotSettings settings, string groupName, bool isRegistered)
        {
            if (settings == null || string.IsNullOrWhiteSpace(groupName))
            {
                return;
            }

            if (settings.ToolGroupRegistrations == null)
            {
                settings.ToolGroupRegistrations = new List<ToolGroupRegistrationSettings>();
            }

            var normalizedGroupName = groupName.Trim();
            var existing = settings.ToolGroupRegistrations.FirstOrDefault(p =>
                p != null
                && string.Equals(p.GroupName, normalizedGroupName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new ToolGroupRegistrationSettings
                {
                    GroupName = normalizedGroupName
                };

                settings.ToolGroupRegistrations.Add(existing);
            }

            existing.IsRegistered = isRegistered;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        public static void SetToolRegistration(this OfficeCopilotSettings settings, string groupName, string toolName, bool isRegistered)
        {
            if (settings == null || string.IsNullOrWhiteSpace(toolName))
            {
                return;
            }

            if (settings.ToolRegistrations == null)
            {
                settings.ToolRegistrations = new List<ToolRegistrationSettings>();
            }

            var normalizedToolName = toolName.Trim();
            var existing = settings.ToolRegistrations.FirstOrDefault(p =>
                p != null
                && string.Equals(p.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new ToolRegistrationSettings
                {
                    ToolName = normalizedToolName
                };

                settings.ToolRegistrations.Add(existing);
            }

            existing.GroupName = string.IsNullOrWhiteSpace(groupName) ? string.Empty : groupName.Trim();
            existing.IsRegistered = isRegistered;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        public static void SetToolConfirmationMode(this OfficeCopilotSettings settings, string toolName, bool automatic)
        {
            if (settings == null || string.IsNullOrWhiteSpace(toolName))
            {
                return;
            }

            if (settings.ToolConfirmationPolicies == null)
            {
                settings.ToolConfirmationPolicies = new List<ToolConfirmationPolicySettings>();
            }

            var normalizedToolName = toolName.Trim();
            var existing = settings.ToolConfirmationPolicies.FirstOrDefault(p =>
                p != null
                && string.Equals(p.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase));

            if (!automatic)
            {
                if (existing != null)
                {
                    settings.ToolConfirmationPolicies.Remove(existing);
                }

                return;
            }

            if (existing == null)
            {
                existing = new ToolConfirmationPolicySettings
                {
                    ToolName = normalizedToolName
                };

                settings.ToolConfirmationPolicies.Add(existing);
            }

            existing.Mode = "allow-always";
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        public static void SetToolDefinitionOverride(this OfficeCopilotSettings settings, ToolDefinitionOverrideSettings definitionOverride)
        {
            if (settings == null || definitionOverride == null || string.IsNullOrWhiteSpace(definitionOverride.ToolName))
            {
                return;
            }

            if (settings.ToolDefinitionOverrides == null)
            {
                settings.ToolDefinitionOverrides = new List<ToolDefinitionOverrideSettings>();
            }

            var normalizedToolName = definitionOverride.ToolName.Trim();
            var existing = settings.ToolDefinitionOverrides.FirstOrDefault(p =>
                p != null
                && string.Equals(p.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                existing = new ToolDefinitionOverrideSettings
                {
                    ToolName = normalizedToolName
                };

                settings.ToolDefinitionOverrides.Add(existing);
            }

            existing.DisplayName = definitionOverride.DisplayName;
            existing.Description = definitionOverride.Description;
            existing.RequiresConfirmation = definitionOverride.RequiresConfirmation;
            existing.RequiresBackup = definitionOverride.RequiresBackup;
            existing.InputSchemaJson = definitionOverride.InputSchemaJson;
            existing.OutputSchemaJson = definitionOverride.OutputSchemaJson;
            existing.UpdatedAtUtc = definitionOverride.UpdatedAtUtc == default(DateTime)
                ? DateTime.UtcNow
                : definitionOverride.UpdatedAtUtc;
        }

        public static void RemoveToolDefinitionOverride(this OfficeCopilotSettings settings, string toolName)
        {
            if (settings == null
                || settings.ToolDefinitionOverrides == null
                || string.IsNullOrWhiteSpace(toolName))
            {
                return;
            }

            var normalizedToolName = toolName.Trim();
            var existing = settings.ToolDefinitionOverrides.FirstOrDefault(p =>
                p != null
                && string.Equals(p.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                settings.ToolDefinitionOverrides.Remove(existing);
            }
        }
    }

    public static class BuiltinToolSettingsExtensions
    {
        public static bool? GetToolGroupRegistration(this BuiltinToolSettings settings, string groupName)
        {
            if (settings == null
                || settings.ToolGroupRegistrations == null
                || string.IsNullOrWhiteSpace(groupName))
            {
                return null;
            }

            var normalizedGroupName = groupName.Trim();
            var existing = settings.ToolGroupRegistrations.LastOrDefault(p =>
                p != null
                && string.Equals(p.GroupName, normalizedGroupName, StringComparison.OrdinalIgnoreCase));

            return existing == null ? (bool?)null : existing.IsRegistered;
        }

        public static bool? GetToolRegistration(this BuiltinToolSettings settings, string toolName)
        {
            if (settings == null
                || settings.ToolRegistrations == null
                || string.IsNullOrWhiteSpace(toolName))
            {
                return null;
            }

            var normalizedToolName = toolName.Trim();
            var existing = settings.ToolRegistrations.LastOrDefault(p =>
                p != null
                && string.Equals(p.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase));

            return existing == null ? (bool?)null : existing.IsRegistered;
        }
    }
}
