using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Skills;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Skill 创建/编辑工具。模型通过 AskUserQuestion 访谈用户后，调用此工具将结果写入 SKILL.md 文件。
    /// 对应 cc-haha bundled skill <c>skillify</c> 的 Step 3-4。
    ///
    /// 输入: { name, description, body, category, allowedTools, whenToUse, argumentHint, arguments, ... }
    /// 输出: { success, filePath, skillName }
    /// </summary>
    internal sealed class SkillCreatorTool
    {
        public const string ToolName = "runtime.create_skill";

        private readonly ISkillRegistry _skillRegistry;
        private readonly string _skillsDirectory;
        private readonly string _hostKind;

        /// <param name="hostApplicationName">当前宿主名。新 Skill 只写入该宿主的专属目录；无法识别时拒绝写入。</param>
        public SkillCreatorTool(ISkillRegistry skillRegistry = null, string hostApplicationName = null)
        {
            _skillRegistry = skillRegistry;
            _skillsDirectory = SkillStorageLocations.GetHostDirectory(hostApplicationName);
            _hostKind = SkillStorageLocations.GetHostFolderName(hostApplicationName);
        }

        /// <summary>
        /// 创建或更新 SKILL.md 文件。
        /// </summary>
        /// <param name="argumentsJson">工具参数 JSON。</param>
        /// <returns>执行结果。</returns>
        public ToolExecutionOutcome Execute(string toolCallId, string toolName, string argumentsJson)
        {
            try
            {
                var input = JsonSerializer.Deserialize<SkillCreatorInput>(argumentsJson ?? "{}",
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (input == null || string.IsNullOrWhiteSpace(input.Name))
                {
                    return Fail(toolCallId, toolName, "skill_creator.missing_name", "Skill name is required.");
                }

                if (string.IsNullOrWhiteSpace(input.Body))
                {
                    return Fail(toolCallId, toolName, "skill_creator.missing_body", "Skill body (instructions) is required.");
                }

                if (_skillsDirectory.Length == 0)
                {
                    return Fail(toolCallId, toolName, "skill_creator.unknown_host", "Current host is not recognized; custom skills cannot be saved.");
                }

                var skillsDir = _skillsDirectory;
                var skillDir = Path.Combine(skillsDir, SanitizeDirectoryName(input.Name));
                var skillFilePath = Path.Combine(skillDir, "SKILL.md");

                // Ensure directory exists
                if (!Directory.Exists(skillDir))
                    Directory.CreateDirectory(skillDir);

                // Build YAML frontmatter + body
                var content = BuildSkillMarkdown(input);

                // Check if file already exists
                var isUpdate = File.Exists(skillFilePath);
                if (isUpdate)
                {
                    // Create backup
                    var backupPath = skillFilePath + ".bak";
                    try { File.Copy(skillFilePath, backupPath, true); }
                    catch { /* non-critical */ }
                }

                File.WriteAllText(skillFilePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (_skillRegistry != null)
                {
                    try
                    {
                        var definition = BuildSkillDefinition(input);
                        definition.Metadata[SkillMetadataKeys.HostKind] = _hostKind;
                        _skillRegistry.AddOrUpdate(definition);
                    }
                    catch (Exception)
                    {
                    }
                }

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = true,
                    Content = $"Skill '{input.Name}' {(isUpdate ? "updated" : "created")} at:\n{skillFilePath}\n\n" +
                              $"The skill will be available after the next message. To invoke: /{input.Name}"
                };
            }
            catch (Exception ex)
            {
                return Fail(toolCallId, toolName, "skill_creator.error", $"Failed to create skill: {ex.Message}");
            }
        }

        private static string BuildSkillMarkdown(SkillCreatorInput input)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("name: " + (input.Name ?? string.Empty));
            sb.AppendLine("description: " + (input.Description ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(input.Category))
                sb.AppendLine("category: " + input.Category);

            if (input.AllowedTools != null && input.AllowedTools.Count > 0)
            {
                sb.AppendLine("allowedTools:");
                foreach (var tool in input.AllowedTools)
                    sb.AppendLine("  - " + tool);
            }

            if (!string.IsNullOrWhiteSpace(input.WhenToUse))
                sb.AppendLine("whenToUse: " + input.WhenToUse);

            if (!string.IsNullOrWhiteSpace(input.ArgumentHint))
                sb.AppendLine("argumentHint: \"" + input.ArgumentHint + "\"");

            if (input.Arguments != null && input.Arguments.Count > 0)
            {
                sb.AppendLine("arguments:");
                foreach (var arg in input.Arguments)
                    sb.AppendLine("  - " + arg);
            }

            if (input.DisableModelInvocation)
                sb.AppendLine("disableModelInvocation: true");

            if (!input.UserInvocable)
                sb.AppendLine("userInvocable: false");

            if (!string.IsNullOrWhiteSpace(input.Version))
                sb.AppendLine("version: " + input.Version);

            if (!string.IsNullOrWhiteSpace(input.Context))
                sb.AppendLine("context: " + input.Context);

            if (!string.IsNullOrWhiteSpace(input.Effort))
                sb.AppendLine("effort: " + input.Effort);

            if (input.RequiresConfirmation)
                sb.AppendLine("requiresConfirmation: true");

            if (input.MaxToolRoundCount > 0 && input.MaxToolRoundCount != 5)
                sb.AppendLine("maxToolRoundCount: " + input.MaxToolRoundCount);

            sb.AppendLine("---");
            sb.AppendLine();
            sb.Append(input.Body ?? string.Empty);

            return sb.ToString();
        }

        private static string SanitizeDirectoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "untitled";
            var sanitized = new StringBuilder();
            foreach (var c in name.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sanitized.Append(c);
                else if (c == ' ')
                    sanitized.Append('-');
            }
            var result = sanitized.ToString();
            return string.IsNullOrWhiteSpace(result) ? "untitled" : result;
        }

        /// <summary>从 SkillCreatorInput 构建 SkillDefinition，立即注册到运行时 Registry。</summary>
        private static SkillDefinition BuildSkillDefinition(SkillCreatorInput input)
        {
            var definition = new SkillDefinition
            {
                Name = input.Name?.Trim() ?? string.Empty,
                DisplayName = input.Name?.Trim() ?? string.Empty,
                Description = input.Description?.Trim() ?? string.Empty,
                Category = input.Category?.Trim() ?? string.Empty,
                RequiresConfirmation = input.RequiresConfirmation,
                MaxToolRoundCount = input.MaxToolRoundCount > 0 ? input.MaxToolRoundCount : 5,
                IsEnabled = true,
            };

            if (input.AllowedTools != null && input.AllowedTools.Count > 0)
                definition.AllowedToolNames = new List<string>(input.AllowedTools);

            // Store body as skill prompt
            if (!string.IsNullOrWhiteSpace(input.Body))
            {
                definition.Metadata[Skills.SkillDefinitionMapper.SkillPromptMetadataKey] = input.Body;
            }

            if (!string.IsNullOrWhiteSpace(input.WhenToUse))
                definition.Metadata[Skills.SkillDefinitionMapper.SkillWhenToUseKey] = input.WhenToUse;

            if (!string.IsNullOrWhiteSpace(input.ArgumentHint))
                definition.Metadata[Skills.SkillDefinitionMapper.SkillArgumentHintKey] = input.ArgumentHint;

            if (input.Arguments != null && input.Arguments.Count > 0)
                definition.Metadata[Skills.SkillDefinitionMapper.SkillArgumentsKey] = string.Join(", ", input.Arguments);

            if (input.DisableModelInvocation)
                definition.Metadata[Skills.SkillDefinitionMapper.SkillDisableModelInvocationKey] = "true";

            if (!input.UserInvocable)
                definition.Metadata[Skills.SkillDefinitionMapper.SkillUserInvocableKey] = "false";

            if (!string.IsNullOrWhiteSpace(input.Version))
                definition.Metadata[Skills.SkillDefinitionMapper.SkillVersionKey] = input.Version;

            if (!string.IsNullOrWhiteSpace(input.Context))
                definition.Metadata[Skills.SkillDefinitionMapper.SkillContextKey] = input.Context;

            if (!string.IsNullOrWhiteSpace(input.Effort))
                definition.Metadata[Skills.SkillDefinitionMapper.SkillEffortKey] = input.Effort;

            return definition;
        }

        private static ToolExecutionOutcome Fail(string toolCallId, string toolName, string errorCode, string message)
        {
            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = false,
                ErrorCode = errorCode,
                ErrorMessage = message
            };
        }
    }

    /// <summary>
    /// SkillCreator 工具的输入模型。
    /// </summary>
    internal sealed class SkillCreatorInput
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Body { get; set; }
        public string Category { get; set; }
        public List<string> AllowedTools { get; set; }
        public string WhenToUse { get; set; }
        public string ArgumentHint { get; set; }
        public List<string> Arguments { get; set; }
        public bool DisableModelInvocation { get; set; }
        public bool UserInvocable { get; set; } = true;
        public string Version { get; set; }
        public string Context { get; set; }
        public string Effort { get; set; }
        public bool RequiresConfirmation { get; set; }
        public int MaxToolRoundCount { get; set; } = 5;
    }
}
