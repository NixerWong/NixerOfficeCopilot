using System;

namespace OfficeCopilot.Agent.Client
{
    /// <summary>
    /// Phase 1.3b 种子注入用的轻量消息 DTO。形状对齐 broker 端的 <c>BrokerConversationMessage</c>（在 Agent.Persistence），
    /// 但本类型位于 netstandard2.0 的 Agent.Client，确保 VSTO 端可序列化、broker 端可反序列化。
    /// </summary>
    public sealed class AgentBrokerSeedMessage
    {
        public AgentBrokerSeedMessage()
        {
            MessageId = string.Empty;
            MessageType = "message";
            Role = string.Empty;
            Name = string.Empty;
            ToolCallId = string.Empty;
            Content = string.Empty;
            StructuredContentJson = string.Empty;
            BackupPath = string.Empty;
            CreatedAtUtc = DateTime.UtcNow;
        }

        public string MessageId { get; set; }

        /// <summary>"message" / "tool_call" / "tool_result" / "user" / "assistant" 等。</summary>
        public string MessageType { get; set; }

        /// <summary>"user" / "assistant" / "tool" / "system"。</summary>
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

        public DateTime CreatedAtUtc { get; set; }
    }
}
