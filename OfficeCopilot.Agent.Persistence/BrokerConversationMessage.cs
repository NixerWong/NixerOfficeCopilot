using System;

namespace OfficeCopilot.Agent.Persistence
{
    public sealed class BrokerConversationMessage
    {
        public BrokerConversationMessage()
        {
            MessageId = Guid.NewGuid().ToString("N");
            MessageType = "message";
            Role = string.Empty;
            Name = string.Empty;
            ToolCallId = string.Empty;
            Content = string.Empty;
            StructuredContentJson = string.Empty;
            BackupPath = string.Empty;
            CoveredMessageIdStart = string.Empty;
            CoveredMessageIdEnd = string.Empty;
            CreatedAtUtc = DateTime.UtcNow;
        }

        public string MessageId { get; set; }

        public string MessageType { get; set; }

        public string Role { get; set; }

        public string Name { get; set; }

        public string ToolCallId { get; set; }

        public string Content { get; set; }

        public string StructuredContentJson { get; set; }

        public bool Success { get; set; }

        /// <summary>
        /// 工具执行产生的备份文件路径。仅 tool_result 消息有效。
        /// </summary>
        public string BackupPath { get; set; }

        /// <summary>
        /// 仅 compact_summary 消息有效：该摘要块覆盖的原始 <see cref="Messages"/> 起始消息 id。
        /// 用于调试与「展开原文」（设计文档 §1 的双数组 id 区间追踪）。
        /// </summary>
        public string CoveredMessageIdStart { get; set; }

        /// <summary>
        /// 仅 compact_summary 消息有效：该摘要块覆盖的原始 <see cref="Messages"/> 结束消息 id。
        /// </summary>
        public string CoveredMessageIdEnd { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
