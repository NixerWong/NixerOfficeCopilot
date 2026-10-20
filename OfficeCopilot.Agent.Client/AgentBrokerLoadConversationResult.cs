using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    /// <summary>
    /// Phase 1.4：加载会话完整消息。Messages 与 SeedConversation 路径共享 <see cref="AgentBrokerSeedMessage"/> 结构。
    /// </summary>
    public sealed class AgentBrokerLoadConversationResult
    {
        public AgentBrokerLoadConversationResult()
        {
            ConversationId = string.Empty;
            Title = string.Empty;
            HostApplicationName = string.Empty;
            Messages = new List<AgentBrokerSeedMessage>();
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }

        /// <summary>true = broker 找到了该会话；false = 未找到（仍可 Success=true，由调用方按缺失语义处理）。</summary>
        public bool Found { get; set; }

        public string ConversationId { get; set; }

        public string Title { get; set; }

        public string HostApplicationName { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public IList<AgentBrokerSeedMessage> Messages { get; set; }

        public IDictionary<string, string> Metadata { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}
