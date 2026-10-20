using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Abstractions
{
    public sealed class ToolDefinition
    {
        public ToolDefinition()
        {
            Name = string.Empty;
            DisplayName = string.Empty;
            Description = string.Empty;
            SourceKind = ToolSourceKind.BuiltIn;
            SourceName = string.Empty;
            InputSchemaJson = "{}";
            OutputSchemaJson = string.Empty;

            // 刻意留空：空值表示"工具作者未声明风险等级"，由 ToolSafetyPolicy 按只读性与
            // 工具语义推导。旧实现默认 "low"，导致"未声明"与"确实是低风险"无法区分，
            // SkillPlanSupport.IsHighRiskTool 因此永远返回 false。
            RiskLevel = string.Empty;
            MaxWriteEffect = string.Empty;
            Tags = new List<string>();
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IsEnabled = true;
            AffectsDocument = true;
            TimeoutSeconds = 90;
        }

        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public ToolSourceKind SourceKind { get; set; }

        public string SourceName { get; set; }

        public string InputSchemaJson { get; set; }

        public string OutputSchemaJson { get; set; }

        public string RiskLevel { get; set; }

        /// <summary>工具单次调用允许产生的最高写入级别；实际级别由宿主按目标与基线计算。</summary>
        public string MaxWriteEffect { get; set; }

        public bool IsReadOnly { get; set; }

        public bool RequiresConfirmation { get; set; }

        public bool RequiresBackup { get; set; }

        /// <summary>
        /// 工具是否会修改宿主文档。非只读但只写工作区（例如 ppt_deck_* 草稿）的工具置为 false，
        /// 此时不再要求执行前备份文档。默认 true。
        /// </summary>
        public bool AffectsDocument { get; set; }

        public int TimeoutSeconds { get; set; }

        public bool IsEnabled { get; set; }

        public IList<string> Tags { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }

    public sealed class ToolDefinitionOverride
    {
        public ToolDefinitionOverride()
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

    public static class ToolDefinitionExtensions
    {
        public static ToolDefinition Clone(this ToolDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new ToolDefinition
            {
                Name = definition.Name ?? string.Empty,
                DisplayName = definition.DisplayName ?? string.Empty,
                Description = definition.Description ?? string.Empty,
                SourceKind = definition.SourceKind,
                SourceName = definition.SourceName ?? string.Empty,
                InputSchemaJson = definition.InputSchemaJson ?? string.Empty,
                OutputSchemaJson = definition.OutputSchemaJson ?? string.Empty,
                RiskLevel = definition.RiskLevel ?? string.Empty,
                MaxWriteEffect = definition.MaxWriteEffect ?? string.Empty,
                IsReadOnly = definition.IsReadOnly,
                RequiresConfirmation = definition.RequiresConfirmation,
                RequiresBackup = definition.RequiresBackup,
                AffectsDocument = definition.AffectsDocument,
                TimeoutSeconds = definition.TimeoutSeconds,
                IsEnabled = definition.IsEnabled,
                Tags = definition.Tags == null
                    ? new List<string>()
                    : new List<string>(definition.Tags),
                Metadata = definition.Metadata == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(definition.Metadata, StringComparer.OrdinalIgnoreCase)
            };
        }

        public static void ApplyOverride(this ToolDefinition definition, ToolDefinitionOverride definitionOverride)
        {
            if (definition == null || definitionOverride == null)
            {
                return;
            }

            if (definitionOverride.DisplayName != null)
            {
                definition.DisplayName = definitionOverride.DisplayName;
            }

            if (definitionOverride.Description != null)
            {
                definition.Description = definitionOverride.Description;
            }

            if (definitionOverride.RequiresConfirmation.HasValue)
            {
                definition.RequiresConfirmation = definitionOverride.RequiresConfirmation.Value;
            }

            if (definitionOverride.RequiresBackup.HasValue)
            {
                definition.RequiresBackup = definitionOverride.RequiresBackup.Value;
            }

            if (definitionOverride.InputSchemaJson != null)
            {
                definition.InputSchemaJson = definitionOverride.InputSchemaJson;
            }

            if (definitionOverride.OutputSchemaJson != null)
            {
                definition.OutputSchemaJson = definitionOverride.OutputSchemaJson;
            }
        }
    }
}