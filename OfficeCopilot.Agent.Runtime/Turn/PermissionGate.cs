namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 权限模式——控制工具执行的许可级别。
    /// 与 cc-haha <c>PermissionMode.ts</c> 对齐，当前支持 3 种模式。
    /// </summary>
    public enum PermissionMode
    {
        /// <summary>默认模式：写操作需用户确认。</summary>
        Default = 0,

        /// <summary>自动接受编辑：写操作自动放行。</summary>
        AcceptEdits = 1,

        /// <summary>计划模式：仅允许只读工具，模型只能输出计划。</summary>
        Plan = 2,

        /// <summary>绕过所有权限检查（仅 dev/CI 使用）。</summary>
        BypassPermissions = 3
    }

    /// <summary>
    /// 权限门禁：根据当前 <see cref="PermissionMode"/> 判断工具是否允许执行。
    /// </summary>
    public interface IPermissionGate
    {
        /// <summary>当前权限模式。</summary>
        PermissionMode Mode { get; }

        /// <summary>
        /// 检查指定工具在当前模式下是否允许执行。
        /// </summary>
        /// <param name="toolName">工具名。</param>
        /// <param name="isReadOnly">工具是否只读。</param>
        /// <returns>允许执行返回 <c>true</c>；被阻止返回 <c>false</c>。</returns>
        bool CanExecute(string toolName, bool isReadOnly);

        /// <summary>切换权限模式。</summary>
        void SetMode(PermissionMode mode);

        /// <summary>获取阻止原因（用于诊断/错误消息）。</summary>
        string GetBlockReason(string toolName, bool isReadOnly);
    }

    /// <summary>
    /// 默认权限门禁实现。
    /// Plan 模式下阻止所有非只读工具。
    /// </summary>
    public sealed class DefaultPermissionGate : IPermissionGate
    {
        public PermissionMode Mode { get; private set; } = PermissionMode.Default;

        public bool CanExecute(string toolName, bool isReadOnly)
        {
            switch (Mode)
            {
                case PermissionMode.BypassPermissions:
                    return true;

                case PermissionMode.Plan:
                    // Plan 模式：仅允许只读工具 + 内置管理工具
                    if (isReadOnly)
                        return true;

                    // 允许进入/退出 Plan 模式的工具本身
                    if (IsPlanManagementTool(toolName))
                        return true;

                    return false;

                case PermissionMode.AcceptEdits:
                case PermissionMode.Default:
                default:
                    // Default/AcceptEdits 模式下不在此层阻断（由上层确认流程处理）
                    return true;
            }
        }

        public void SetMode(PermissionMode mode)
        {
            Mode = mode;
        }

        public string GetBlockReason(string toolName, bool isReadOnly)
        {
            if (Mode == PermissionMode.Plan && !isReadOnly && !IsPlanManagementTool(toolName))
            {
                return $"Plan mode is active — tool '{toolName}' is blocked because it may modify data. "
                    + "Only read-only tools are allowed in Plan mode. "
                    + "Call ExitPlanMode to leave Plan mode and execute changes.";
            }

            return string.Empty;
        }

        private static bool IsPlanManagementTool(string toolName)
        {
            return string.Equals(toolName, EnterPlanModeTool.ToolName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, ExitPlanModeTool.ToolName, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
