namespace OfficeCopilot.Settings.Abstractions
{
    /// <summary>
    /// 工具执行权限模式（TrustMode）的取值。
    /// 按宿主应用分别保存，键为规范宿主名（Excel / Word / PowerPoint）。
    /// </summary>
    public static class ToolTrustModeValues
    {
        /// <summary>提示确认（默认）：保持现有的权限确认逻辑。</summary>
        public const string NeedConfirm = "need-confirm";

        /// <summary>始终放行：Agent 模式下跳过所有确认与高危硬闸；Ask 模式仍保持只读。</summary>
        public const string AlwaysAllow = "always-allow";

        public static bool IsValid(string value)
        {
            return string.Equals(value, NeedConfirm, System.StringComparison.Ordinal)
                || string.Equals(value, AlwaysAllow, System.StringComparison.Ordinal);
        }
    }
}
