namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 工具调用结果。阶段 0.2b-i 从 <see cref="BrokerTurnExecutor"/> 的私有嵌套类提升为顶级 <c>internal</c>，
    /// 使 <see cref="DefaultToolDispatcher.InvokeHostToolAsync"/> 等迁出的方法仍能返回相同的形态，
    /// 调用方（执行器内的 <c>ExecuteToolCallAsync</c> 等）无需任何改动。
    /// </summary>
    internal sealed class ToolExecutionOutcome
    {
        public ToolExecutionOutcome()
        {
            ToolCallId = string.Empty;
            ToolName = string.Empty;
            Content = string.Empty;
            BackupPath = string.Empty;
            StructuredContentJson = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
            AutoVerificationToolName = string.Empty;
            AutoVerificationSummary = string.Empty;
        }

        public string ToolCallId { get; set; }

        public string ToolName { get; set; }

        public bool Success { get; set; }

        public string Content { get; set; }

        public string BackupPath { get; set; }

        public string StructuredContentJson { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public string AutoVerificationToolName { get; set; }

        public bool AutoVerificationSucceeded { get; set; }

        public bool AutoVerificationAttempted { get; set; }

        public string AutoVerificationSummary { get; set; }

        /// <summary>工具返回的图像；只在本轮内存中转交模型，不持久化。</summary>
        public System.Collections.Generic.List<OfficeCopilot.HostBridge.Contracts.HostToolContentPart> ContentParts { get; set; }
    }
}
