using System.Collections.Generic;

namespace OfficeCopilot.Skills
{
    /// <summary>
    /// SKILL.md YAML frontmatter 模型，与 Claude Code Skill 格式兼容。
    /// 参考 cc-haha <c>loadSkillsDir.ts</c> 中的 frontmatter 字段。
    /// Phase 0.1+0.2: 补齐所有 cc-haha 字段。
    /// </summary>
    public sealed class SkillFrontmatter
    {
        /// <summary>Skill 名称（必填）。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>一句话描述（必填）。用于 Skill 选择。</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>触发条件关键词列表（CamelCase 兼容）。</summary>
        public List<string> UseWhen { get; set; } = new List<string>();

        /// <summary>不触发条件关键词列表。</summary>
        public List<string> AvoidWhen { get; set; } = new List<string>();

        /// <summary>允许使用的工具列表。</summary>
        public List<string> AllowedTools { get; set; } = new List<string>();

        /// <summary>推荐的验证工具列表。</summary>
        public List<string> VerificationTools { get; set; } = new List<string>();

        /// <summary>是否需要用户确认。</summary>
        public bool RequiresConfirmation { get; set; }

        /// <summary>是否需要创建备份。</summary>
        public bool RequiresBackup { get; set; }

        /// <summary>最大工具调用轮数。</summary>
        public int MaxToolRoundCount { get; set; } = 5;

        /// <summary>Skill 分类。</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>模型覆盖（可选）。</summary>
        public string Model { get; set; }

        /// <summary>额外元数据。</summary>
        public Dictionary<string, string> Metadata { get; set; }

        // ========== Phase 0.1: 补齐 cc-haha 字段 ==========

        /// <summary>cc-haha: when_to_use — 详细触发条件描述（自然语言）。</summary>
        public string WhenToUse { get; set; }

        /// <summary>cc-haha: user-invocable — 用户是否可手动调用。</summary>
        public bool UserInvocable { get; set; } = true;

        /// <summary>cc-haha: disable-model-invocation — 禁止模型自动调用。</summary>
        public bool DisableModelInvocation { get; set; }

        /// <summary>cc-haha: version — Skill 版本号。</summary>
        public string Version { get; set; }

        /// <summary>cc-haha: argument-hint — 参数用法提示（如 "[file-path]"）。</summary>
        public string ArgumentHint { get; set; }

        /// <summary>cc-haha: arguments — 参数名列表。</summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>cc-haha: effort — 执行深度级别。</summary>
        public string Effort { get; set; }

        /// <summary>cc-haha: context — 执行上下文（inline / fork）。</summary>
        public string Context { get; set; }

        /// <summary>cc-haha: agent — 指定 Agent 类型。</summary>
        public string Agent { get; set; }

        /// <summary>cc-haha: paths — 基于文件路径模式的自动触发。</summary>
        public List<string> Paths { get; set; } = new List<string>();

        // ========== 编辑器与运行时共用的字段 ==========

        /// <summary>是否启用。启用状态以 SKILL.md 为唯一来源。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>显示名称（为空时使用 Name）。</summary>
        public string DisplayName { get; set; }

        /// <summary>优先使用的工具列表。</summary>
        public List<string> PreferredTools { get; set; } = new List<string>();

        /// <summary>配套 Skill 名称列表。</summary>
        public List<string> CompanionSkills { get; set; } = new List<string>();

        /// <summary>回退 Skill 名称列表。</summary>
        public List<string> FallbackSkills { get; set; } = new List<string>();

        /// <summary>是否要求验证。</summary>
        public bool RequireVerification { get; set; }

        /// <summary>授权高风险工具（critical）。加载时打印告警。</summary>
        public bool AllowHighRiskTools { get; set; }

        /// <summary>授权代码执行（critical）。加载时打印告警。</summary>
        public bool AllowCodeExecution { get; set; }
    }

    /// <summary>
    /// 解析后的 SKILL.md 文件：frontmatter + Markdown 正文。
    /// </summary>
    public sealed class ParsedSkillFile
    {
        public SkillFrontmatter Frontmatter { get; set; } = new SkillFrontmatter();

        /// <summary>YAML 之后的 Markdown 正文（Skill 指令）。</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>SKILL.md 文件的完整路径。</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>来源类型。</summary>
        public SkillSourceKind SourceKind { get; set; } = SkillSourceKind.FileSystem;
    }

    public enum SkillSourceKind
    {
        FileSystem = 0,
        Bundled = 1,
        BuiltIn = 2
    }
}
