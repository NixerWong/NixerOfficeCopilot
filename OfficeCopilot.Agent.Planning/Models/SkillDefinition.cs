using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Agent.Planning
{
    public sealed class SkillDefinition
    {
        public SkillDefinition()
        {
            Name = string.Empty;
            DisplayName = string.Empty;
            Description = string.Empty;
            Category = string.Empty;
            UseWhen = new List<string>();
            AvoidWhen = new List<string>();
            AllowedToolNames = new List<string>();
            PreferredToolNames = new List<string>();
            VerificationToolNames = new List<string>();
            CompanionSkillNames = new List<string>();
            FallbackSkillNames = new List<string>();
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IsEnabled = true;
            MaxToolRoundCount = 3;
        }

        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public string Category { get; set; }

        public IList<string> UseWhen { get; set; }

        public IList<string> AvoidWhen { get; set; }

        public IList<string> AllowedToolNames { get; set; }

        public IList<string> PreferredToolNames { get; set; }

        public IList<string> VerificationToolNames { get; set; }

        public IList<string> CompanionSkillNames { get; set; }

        public IList<string> FallbackSkillNames { get; set; }

        public bool RequireVerification { get; set; }

        public bool RequiresConfirmation { get; set; }

        public bool RequiresBackup { get; set; }

        public bool AllowHighRiskTools { get; set; }

        /// <summary>这档任务本来就要写代码（VBA / 脚本），不是“实在不行才降级”。</summary>
        public bool AllowCodeExecution { get; set; }

        public bool IsEnabled { get; set; }

        public int MaxToolRoundCount { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }

    public static class SkillDefinitionExtensions
    {
        public static SkillDefinition Clone(this SkillDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new SkillDefinition
            {
                Name = definition.Name ?? string.Empty,
                DisplayName = definition.DisplayName ?? string.Empty,
                Description = definition.Description ?? string.Empty,
                Category = definition.Category ?? string.Empty,
                UseWhen = definition.UseWhen == null ? new List<string>() : definition.UseWhen.ToList(),
                AvoidWhen = definition.AvoidWhen == null ? new List<string>() : definition.AvoidWhen.ToList(),
                AllowedToolNames = definition.AllowedToolNames == null ? new List<string>() : definition.AllowedToolNames.ToList(),
                PreferredToolNames = definition.PreferredToolNames == null ? new List<string>() : definition.PreferredToolNames.ToList(),
                VerificationToolNames = definition.VerificationToolNames == null ? new List<string>() : definition.VerificationToolNames.ToList(),
                CompanionSkillNames = definition.CompanionSkillNames == null ? new List<string>() : definition.CompanionSkillNames.ToList(),
                FallbackSkillNames = definition.FallbackSkillNames == null ? new List<string>() : definition.FallbackSkillNames.ToList(),
                RequireVerification = definition.RequireVerification,
                RequiresConfirmation = definition.RequiresConfirmation,
                RequiresBackup = definition.RequiresBackup,
                AllowHighRiskTools = definition.AllowHighRiskTools,
                AllowCodeExecution = definition.AllowCodeExecution,
                IsEnabled = definition.IsEnabled,
                MaxToolRoundCount = definition.MaxToolRoundCount,
                Metadata = definition.Metadata == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(definition.Metadata, StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
