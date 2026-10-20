namespace OfficeCopilot.Agent.Abstractions
{
    public static class ToolMetadataKeys
    {
        public const string GroupName = "group";
        public const string HostKind = "host";
        public const string CapabilityScope = "scope";
        public const string SelfTestEnabled = "self-test-enabled";
        public const string RiskLevel = "risk-level";
        public const string RequiresBackup = "requires-backup";
        public const string TimeoutSeconds = "timeout-seconds";
        public const string SessionId = "session-id";
        public const string ConversationId = "conversation-id";
        public const string SourceMessageId = "source-message-id";
        public const string UserIntent = "user-intent";
        public const string BackupPath = "backup-path";
        public const string ToolTrustMode = "tool-trust-mode";

        /// <summary>取值与 OfficeCopilot.Settings.Abstractions.ToolTrustModeValues 保持一致。</summary>
        public const string ToolTrustModeNeedConfirm = "need-confirm";
        public const string ToolTrustModeAlwaysAllow = "always-allow";
    }
}