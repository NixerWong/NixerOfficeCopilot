using OfficeCopilot.Agent.Runtime.Context;

namespace OfficeCopilot.Agent.Runtime
{
    /// <summary>
    /// 会话存储模式。Phase 1.3 引入。
    /// </summary>
    public enum ConversationStorageMode
    {
        /// <summary>进程内字典；重启即丢。默认值，保留 Phase 1 之前的行为。</summary>
        InMemory = 0,

        /// <summary>每会话一个 JSONL 文件，落盘到 <see cref="AgentRuntimeOptions.JsonlRootDirectory"/>。</summary>
        Jsonl = 1
    }

    public sealed class AgentRuntimeOptions
    {
        public AgentRuntimeOptions()
        {
            RuntimeName = "OfficeCopilot.Agent.Runtime";
            MaxToolRoundCount = 300;
            EnableSkillPlanning = true;
            DefaultSkillName = "read-analysis";
            SkillPlannerMode = "keyword";
            StorageMode = ConversationStorageMode.Jsonl;
            JsonlRootDirectory = string.Empty;
        }

        public string RuntimeName { get; set; }

        /// <summary>
        /// 当前宿主应用名（Excel / Word / PowerPoint）。决定加载哪个宿主专属的用户 Skill 目录；为空时不加载任何用户 Skill。
        /// </summary>
        public string HostApplicationName { get; set; } = string.Empty;

        public int MaxToolRoundCount { get; set; }

        public bool EnableSkillPlanning { get; set; }

        public string DefaultSkillName { get; set; }

        public string SkillPlannerMode { get; set; }

        /// <summary>
        /// 会话存储模式。默认 <see cref="ConversationStorageMode.InMemory"/>（与 Phase 1 之前的行为一致）。
        /// Broker 端若要开启磁盘持久化，应设为 <see cref="ConversationStorageMode.Jsonl"/>。
        /// </summary>
        public ConversationStorageMode StorageMode { get; set; }

        /// <summary>
        /// JSONL 存储根目录。仅 <see cref="StorageMode"/> = <see cref="ConversationStorageMode.Jsonl"/> 时使用。
        /// 留空（默认）时由 <c>JsonlPaths.DefaultRootDirectory</c> 决定（<c>%APPDATA%/NixerOfficeCopilot</c>）。
        /// </summary>
        public string JsonlRootDirectory { get; set; }

        // ---- 阶段 1.5：上下文窗口管理 ----

        /// <summary>模型上下文窗口大小（token）。默认 200,000，对应 Claude Sonnet 4。</summary>
        public int ContextWindowSize { get; set; } = ContextWindowThresholds.DefaultContextWindowSize;

        /// <summary>是否启用 auto-compact（token 超标时自动摘要压缩）。默认 true。</summary>
        public bool AutoCompactEnabled { get; set; } = true;

        /// <summary>是否启用 micro-compact（单条 tool_result 过大时截断）。默认 true。</summary>
        public bool MicroCompactEnabled { get; set; } = true;

        /// <summary>Micro-compact 触发阈值（字符数）。默认 50,000。</summary>
        public int MicroCompactCharThreshold { get; set; } = ContextWindowThresholds.MicroCompactCharThreshold;

        // ---- 生成收敛与失败熔断（原为 BrokerTurnExecutor 内联魔数） ----

        /// <summary>上下文占用达到窗口的此比例时停止生成。默认 0.85。</summary>
        public double TokenBudgetStopThreshold { get; set; } = 0.85;

        /// <summary>小于此值的 text delta 视为「边际收益递减」。默认 500。</summary>
        public int TokenBudgetSmallDeltaTokenThreshold { get; set; } = 500;

        /// <summary>
        /// 连续小 delta 次数上限。默认 100：SSE 逐字返回时每个 delta 只有 1~5 字符，
        /// 阈值太低会把正常流式输出误判为“收敛”而提前截断。
        /// </summary>
        public int TokenBudgetMaxConsecutiveSmallDeltas { get; set; } = 100;

        /// <summary>单轮内连续模型调用失败多少次后放弃。默认 3。</summary>
        public int MaxConsecutiveTurnFailures { get; set; } = 3;
    }
}
