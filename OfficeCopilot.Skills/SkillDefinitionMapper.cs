using OfficeCopilot.Agent.Planning;
using System;
using System.Collections.Generic;

namespace OfficeCopilot.Skills
{
    /// <summary>
    /// 将 <see cref="ParsedSkillFile"/> 转换为 <see cref="SkillDefinition"/>。
    /// Frontmatter 字段与 SkillDefinition 属性的映射：
    /// <list type="bullet">
    /// <item><c>name</c> → <c>Name</c></item>
    /// <item><c>description</c> → <c>Description</c></item>
    /// <item><c>useWhen</c> → <c>UseWhen</c></item>
    /// <item><c>avoidWhen</c> → <c>AvoidWhen</c></item>
    /// <item><c>allowedTools</c> → <c>AllowedToolNames</c></item>
    /// <item><c>verificationTools</c> → <c>VerificationToolNames</c></item>
    /// <item><c>requiresConfirmation</c> → <c>RequiresConfirmation</c></item>
    /// <item><c>maxToolRoundCount</c> → <c>MaxToolRoundCount</c></item>
    /// </list>
    /// Skill 正文（<c>Body</c>）存储到 <c>Metadata["skill.prompt"]</c>。
    /// </summary>
    public static class SkillDefinitionMapper
    {
        /// <summary>Metadata key for storing the SKILL.md body (Markdown instructions).</summary>
        public const string SkillPromptMetadataKey = "skill.prompt";

        /// <summary>Metadata key for storing the SKILL.md file path.</summary>
        public const string SkillFilePathMetadataKey = "skill.filePath";

        // Phase 0.2: New metadata keys for cc-haha fields
        public const string SkillWhenToUseKey = "skill.whenToUse";
        public const string SkillUserInvocableKey = "skill.userInvocable";
        public const string SkillDisableModelInvocationKey = SkillMetadataKeys.DisableModelInvocation;
        public const string SkillModelKey = "skill.model";
        public const string SkillVersionKey = "skill.version";
        public const string SkillArgumentHintKey = "skill.argumentHint";
        public const string SkillArgumentsKey = "skill.arguments";
        public const string SkillEffortKey = "skill.effort";
        public const string SkillContextKey = "skill.context";
        public const string SkillAgentKey = "skill.agent";
        public const string SkillPathsKey = "skill.paths";

        /// <summary>
        /// 将 ParsedSkillFile 转换为 SkillDefinition。
        /// </summary>
        public static SkillDefinition ToSkillDefinition(this ParsedSkillFile parsed)
        {
            if (parsed == null || parsed.Frontmatter == null)
            {
                return null;
            }

            var fm = parsed.Frontmatter;

            var definition = new SkillDefinition
            {
                Name = fm.Name?.Trim() ?? string.Empty,
                DisplayName = string.IsNullOrWhiteSpace(fm.DisplayName) ? (fm.Name?.Trim() ?? string.Empty) : fm.DisplayName.Trim(),
                Description = fm.Description?.Trim() ?? string.Empty,
                Category = fm.Category?.Trim() ?? string.Empty,
                RequiresConfirmation = fm.RequiresConfirmation,
                RequiresBackup = fm.RequiresBackup,
                RequireVerification = fm.RequireVerification,
                MaxToolRoundCount = fm.MaxToolRoundCount > 0 ? fm.MaxToolRoundCount : 5,
                IsEnabled = fm.Enabled,
                AllowHighRiskTools = fm.AllowHighRiskTools,
                AllowCodeExecution = fm.AllowCodeExecution,
            };

            if (fm.UseWhen != null && fm.UseWhen.Count > 0)
                definition.UseWhen = new List<string>(fm.UseWhen);

            if (fm.AvoidWhen != null && fm.AvoidWhen.Count > 0)
                definition.AvoidWhen = new List<string>(fm.AvoidWhen);

            if (fm.AllowedTools != null && fm.AllowedTools.Count > 0)
                definition.AllowedToolNames = new List<string>(fm.AllowedTools);

            if (fm.PreferredTools != null && fm.PreferredTools.Count > 0)
                definition.PreferredToolNames = new List<string>(fm.PreferredTools);

            if (fm.VerificationTools != null && fm.VerificationTools.Count > 0)
                definition.VerificationToolNames = new List<string>(fm.VerificationTools);

            if (fm.CompanionSkills != null && fm.CompanionSkills.Count > 0)
                definition.CompanionSkillNames = new List<string>(fm.CompanionSkills);

            if (fm.FallbackSkills != null && fm.FallbackSkills.Count > 0)
                definition.FallbackSkillNames = new List<string>(fm.FallbackSkills);

            if (definition.AllowedToolNames == null || definition.AllowedToolNames.Count == 0)
            {
                // 空白名单 = 不限制工具（fail-open），这是既定行为，但需要可见。
                SkillLog.Write($"Skill '{definition.Name}' declares no allowed-tools; all tools are permitted while it is active.");
            }

            if (fm.AllowHighRiskTools || fm.AllowCodeExecution)
            {
                SkillLog.Write($"WARNING: Skill '{definition.Name}' ({parsed.FilePath}) grants critical authorization: " +
                    $"allow-high-risk-tools={fm.AllowHighRiskTools}, allow-code-execution={fm.AllowCodeExecution}.");
            }

            if (!string.IsNullOrWhiteSpace(fm.Model))
                definition.Metadata[SkillModelKey] = fm.Model;

            // Phase 0.2: Map new cc-haha fields to metadata
            MapBool(definition, SkillUserInvocableKey, fm.UserInvocable, true);  // only store non-default
            MapBool(definition, SkillDisableModelInvocationKey, fm.DisableModelInvocation, false);
            MapString(definition, SkillWhenToUseKey, fm.WhenToUse);
            MapString(definition, SkillVersionKey, fm.Version);
            MapString(definition, SkillArgumentHintKey, fm.ArgumentHint);
            MapString(definition, SkillEffortKey, fm.Effort);
            MapString(definition, SkillContextKey, fm.Context);
            MapString(definition, SkillAgentKey, fm.Agent);
            if (fm.Arguments != null && fm.Arguments.Count > 0)
                definition.Metadata[SkillArgumentsKey] = string.Join(", ", fm.Arguments);
            if (fm.Paths != null && fm.Paths.Count > 0)
                definition.Metadata[SkillPathsKey] = string.Join(", ", fm.Paths);

            // Store the SKILL.md body (instructions) in metadata
            if (!string.IsNullOrWhiteSpace(parsed.Body))
            {
                definition.Metadata[SkillPromptMetadataKey] = parsed.Body;
            }

            if (!string.IsNullOrWhiteSpace(parsed.FilePath))
            {
                definition.Metadata[SkillFilePathMetadataKey] = parsed.FilePath;
            }

            // Carry over any extra frontmatter metadata
            if (fm.Metadata != null)
            {
                foreach (var kvp in fm.Metadata)
                {
                    if (!definition.Metadata.ContainsKey(kvp.Key))
                        definition.Metadata[kvp.Key] = kvp.Value;
                }
            }

            return definition;
        }

        /// <summary>
        /// 获取 Skill 的正文指令（SKILL.md 的 Markdown 部分）。
        /// </summary>
        public static string GetSkillPromptBody(this SkillDefinition definition)
        {
            if (definition?.Metadata == null)
                return string.Empty;

            return definition.Metadata.TryGetValue(SkillPromptMetadataKey, out var value)
                ? value ?? string.Empty
                : string.Empty;
        }

        private static void MapString(SkillDefinition def, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                def.Metadata[key] = value;
        }

        private static void MapBool(SkillDefinition def, string key, bool value, bool defaultValue)
        {
            if (value != defaultValue)
                def.Metadata[key] = value ? "true" : "false";
        }
    }
}
