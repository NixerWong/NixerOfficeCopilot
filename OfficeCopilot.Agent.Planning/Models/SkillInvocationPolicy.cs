using System;

namespace OfficeCopilot.Agent.Planning
{
    /// <summary>
    /// Skill 调用策略：判断模型是否可以自动选用或调用某个 Skill。
    /// 用户显式点名的 Skill 不受此限制。
    /// </summary>
    public static class SkillInvocationPolicy
    {
        public static bool IsModelInvocationDisabled(SkillDefinition skill)
        {
            return skill?.Metadata != null
                && skill.Metadata.TryGetValue(SkillMetadataKeys.DisableModelInvocation, out var value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUserInvocationDisabled(SkillDefinition skill)
        {
            return skill?.Metadata != null
                && skill.Metadata.TryGetValue(SkillMetadataKeys.UserInvocable, out var value)
                && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
