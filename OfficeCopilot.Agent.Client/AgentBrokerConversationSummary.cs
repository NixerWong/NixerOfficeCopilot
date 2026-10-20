using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    /// <summary>
    /// Phase 1.4：会话列表项 DTO，与 broker 端的 <c>OfficeCopilot.Agent.Persistence.ConversationSummary</c> 形状对齐，
    /// 但本类型位于 netstandard2.0 Agent.Client，供 VSTO 面板反序列化使用。
    /// </summary>
    public sealed class AgentBrokerConversationSummary
    {
        public AgentBrokerConversationSummary()
        {
            ConversationId = string.Empty;
            Title = string.Empty;
            HostApplicationName = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ConversationId { get; set; }

        public string Title { get; set; }

        public string HostApplicationName { get; set; }

        public int MessageCount { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>
        /// 自由扩展元数据。Phase 1.4 用来回传 panel 关心的扩展字段（如 <c>modelProviderId</c> / <c>summary</c>）。
        /// </summary>
        public IDictionary<string, string> Metadata { get; set; }
    }
}
