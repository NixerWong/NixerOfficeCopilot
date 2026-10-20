using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Persistence
{
    public sealed class BrokerConversationState
    {
        public BrokerConversationState()
        {
            ConversationId = Guid.NewGuid().ToString("N");
            Messages = new List<BrokerConversationMessage>();
            CreatedAtUtc = DateTime.UtcNow;
            UpdatedAtUtc = DateTime.UtcNow;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ConversationId { get; set; }

        /// <summary>完整会话链，用于 Panel 渲染和历史记录。</summary>
        public IList<BrokerConversationMessage> Messages { get; set; }

        /// <summary>
        /// 压缩会话链，用于模型上下文。null 表示未压缩，此时应使用 <see cref="Messages"/>。
        /// 从压缩点开始，此链与 <see cref="Messages"/> 独立增长。
        /// </summary>
        public IList<BrokerConversationMessage> CompactMessages { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}
