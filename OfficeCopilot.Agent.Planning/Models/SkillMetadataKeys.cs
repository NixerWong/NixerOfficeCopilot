namespace OfficeCopilot.Agent.Planning
{
    public static class SkillMetadataKeys
    {
        public const string Scope = "scope";
        public const string HostKind = "host";
        public const string PreferredTool = "preferred-tool";
        public const string VerificationRequired = "verification-required";
        public const string FallbackSkill = "fallback-skill";

        /// <summary>SKILL.md <c>disable-model-invocation</c>：true 时模型不得自动选用或调用，仅用户可显式触发。</summary>
        public const string DisableModelInvocation = "skill.disableModelInvocation";

        /// <summary>SKILL.md <c>user-invocable</c>：false 时用户不能显式点名触发，仅模型可自动选用。</summary>
        public const string UserInvocable = "skill.userInvocable";

        /// <summary>true 时自动规划只在用户输入命中 UseWhen 关键词才会选用（点名触发不受影响），避免宿主无关的元技能被意图/相似度误选。</summary>
        public const string RequireKeywordMatch = "skill.requireKeywordMatch";
    }
}
