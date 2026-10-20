using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Skills;
using System;
using System.Text;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Skill 调用工具：模型主动选择一个 Skill 名，框架返回该 Skill 的完整指令。
    /// 对应 cc-haha <c>SkillTool.ts</c> 的"inline"模式。
    ///
    /// 工具名：<c>runtime.invoke_skill</c>
    /// 输入：<c>{ "skillName": "excel-find-and-replace", "args": "..." }</c>
    /// 输出：Skill 的 SKILL.md 正文（Markdown 指令）+ 允许的工具列表。
    /// </summary>
    internal sealed class SkillTool
    {
        public const string ToolName = "runtime.invoke_skill";

        private readonly ISkillRegistry _skillRegistry;

        public SkillTool(ISkillRegistry skillRegistry)
        {
            _skillRegistry = skillRegistry ?? throw new ArgumentNullException(nameof(skillRegistry));
        }

        /// <summary>
        /// 执行 Skill 调用：解析 JSON 参数 → 查找 Skill → 返回指令文本。
        /// </summary>
        public ToolExecutionOutcome Execute(string toolCallId, string toolName, string argumentsJson)
        {
            var skillName = string.Empty;
            var args = string.Empty;

            try
            {
                using (var doc = JsonDocument.Parse(argumentsJson ?? "{}"))
                {
                    if (doc.RootElement.TryGetProperty("skillName", out var nameProp))
                        skillName = nameProp.GetString() ?? string.Empty;

                    if (doc.RootElement.TryGetProperty("args", out var argsProp))
                        args = argsProp.GetString() ?? string.Empty;
                }
            }
            catch
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "skill.parse_error",
                    ErrorMessage = "Failed to parse skill invocation arguments. Expected JSON with 'skillName' field."
                };
            }

            if (string.IsNullOrWhiteSpace(skillName))
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "skill.missing_name",
                    ErrorMessage = "Skill name is required. Available: " + FormatAvailableSkills()
                };
            }

            var skill = _skillRegistry.FindByName(skillName);
            if (skill == null)
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "skill.not_found",
                    ErrorMessage = $"Skill '{skillName}' not found. Available: " + FormatAvailableSkills()
                };
            }

            if (!skill.IsEnabled)
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "skill.disabled",
                    ErrorMessage = $"Skill '{skillName}' is disabled in settings, so it cannot be used. Tell the user to enable it in the settings panel first. Do not suggest typing the command as a workaround, and do not change the skill's configuration yourself."
                };
            }

            if (SkillInvocationPolicy.IsModelInvocationDisabled(skill))
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "skill.model_invocation_disabled",
                    ErrorMessage = $"Skill '{skillName}' has disable-model-invocation set, so the model cannot invoke it. The user can still trigger it explicitly by typing /{skillName}. Do not change the skill's configuration yourself."
                };
            }

            var content = BuildSkillInvocationResult(skill, args);

            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = content
            };
        }

        private string BuildSkillInvocationResult(SkillDefinition skill, string args)
        {
            var sb = new StringBuilder();

            sb.Append("Skill '").Append(skill.Name).Append("' activé.");

            if (!string.IsNullOrWhiteSpace(skill.Description))
            {
                sb.AppendLine().Append("Description: ").Append(skill.Description);
            }

            var body = skill.GetSkillPromptBody();
            if (!string.IsNullOrWhiteSpace(body))
            {
                sb.AppendLine().AppendLine().AppendLine("=== Instructions ===").AppendLine(body);
            }
            else
            {
                sb.AppendLine().AppendLine().AppendLine("=== Instructions ===").Append(skill.Description);
            }

            if (skill.AllowedToolNames != null && skill.AllowedToolNames.Count > 0)
            {
                sb.AppendLine().Append("Allowed tools: ").Append(string.Join(", ", skill.AllowedToolNames));
            }

            if (!string.IsNullOrWhiteSpace(args))
            {
                sb.AppendLine().Append("Context: ").Append(args);
            }

            sb.AppendLine().AppendLine().Append("Follow the instructions above. When finished, summarize in Chinese.");

            return sb.ToString();
        }

        private string FormatAvailableSkills()
        {
            var skills = _skillRegistry.GetSkills();
            if (skills == null || skills.Count == 0)
                return "(none)";

            var sb = new StringBuilder();
            foreach (var skill in skills)
            {
                if (skill == null || string.IsNullOrWhiteSpace(skill.Name))
                    continue;

                if (!skill.IsEnabled || SkillInvocationPolicy.IsModelInvocationDisabled(skill))
                    continue;

                if (sb.Length > 0)
                    sb.Append(", ");

                sb.Append(skill.Name);
            }

            return sb.ToString();
        }
    }
}

