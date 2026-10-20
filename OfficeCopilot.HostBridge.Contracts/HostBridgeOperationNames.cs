namespace OfficeCopilot.HostBridge.Contracts
{
    public static class HostBridgeOperationNames
    {
        public const string Handshake = "host.handshake";
        public const string GetCapabilities = "host.get_capabilities";
        public const string GetContextSnapshot = "host.get_context_snapshot";
        public const string BeginWriteEffectTurn = "host.begin_write_effect_turn";
        public const string PreflightToolCall = "host.preflight_tool_call";
        public const string InvokeTool = "host.invoke_tool";
        public const string InsertText = "host.insert_text";
        public const string ExecuteCode = "host.execute_code";
    }

    /// <summary>
    /// Broker 通过 <see cref="HostToolInvocationRequest.Metadata"/> 传给宿主的控制标记。
    /// </summary>
    public static class HostBridgeMetadataKeys
    {
        /// <summary>
        /// 值为 <c>"true"</c> 时表示 Broker 侧的权限门已经就本次调用取得用户确认，
        /// 宿主侧不应重复弹出确认框（备份等其余安全动作仍照常执行）。
        /// </summary>
        public const string ConfirmationGranted = "confirmation-granted";

        public const string WriteEffectPreflightToken = "write-effect-preflight-token";

        public const string WriteEffectApprovedLevel = "write-effect-approved-level";

        public const string StagedOnly = "staged-only";

        public const string VbaProtectionAllowedWorksheets = "vba-protection-allowed-worksheets";

        public const string TrueValue = "true";
    }

    public sealed class WriteEffectTurnRequest
    {
        public string ConversationId { get; set; }

        public string MessageId { get; set; }
    }

    public sealed class PreflightToolCallRequest
    {
        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public string ToolCallId { get; set; }

        public string ToolName { get; set; }

        public string ArgumentsJson { get; set; }
    }

    public sealed class PreflightToolCallResult
    {
        public bool Success { get; set; }

        public string Effect { get; set; }

        public string OperationSummary { get; set; }

        public string TargetWorkbookFullName { get; set; }

        public string TargetWorksheetName { get; set; }

        public string TargetAddress { get; set; }

        public string PreflightToken { get; set; }

        /// <summary>
        /// 宿主按会话绑定状态补全后的参数（目前用于 ppt_deck_build 省略 deckId 的情况）。
        /// 非空时 Broker 必须用它替换原参数，再生成确认摘要并执行。
        /// </summary>
        public string ResolvedArgumentsJson { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }

    public sealed class WriteEffectTurnResult
    {
        public bool Success { get; set; }

        public string WorkbookFullName { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}
