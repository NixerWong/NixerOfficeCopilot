using System;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Ask / Agent 两种交互模式的唯一语义来源。
    ///
    /// 边界定义（三个正交维度）：
    /// <list type="bullet">
    /// <item>读文档：两种模式都允许——Ask 也能调用只读工具取真实数据。</item>
    /// <item>改文档：仅 Agent 允许；Ask 映射到 <see cref="PermissionMode.Plan"/>，写工具既不下发也不放行。</item>
    /// <item>多轮循环：两种模式都允许。</item>
    /// </list>
    /// 此前 Ask 被实现成"完全无工具"，把上述三件事捆在一个布尔上，
    /// 导致模型明知需要读数据却无工具可用，转而把工具调用协议当正文输出。
    /// </summary>
    internal static class InteractiveModePolicy
    {
        public const string Agent = "agent";
        public const string Ask = "ask";

        public static bool IsAgent(string interactiveMode)
            => string.Equals(interactiveMode, Agent, StringComparison.OrdinalIgnoreCase);

        /// <summary>Ask 模式下只读工具可用、写工具一律阻断。</summary>
        public static bool IsReadOnlyMode(string interactiveMode)
            => !IsAgent(interactiveMode);

        public static PermissionMode ToPermissionMode(string interactiveMode)
            => IsAgent(interactiveMode) ? PermissionMode.Default : PermissionMode.Plan;
    }
}
