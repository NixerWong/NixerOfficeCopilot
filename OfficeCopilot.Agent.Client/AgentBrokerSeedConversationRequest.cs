using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    /// <summary>
    /// Phase 1.3b 种子注入请求：把已有会话的消息历史一次性写到 broker 的 <c>IConversationStore</c>。
    /// 典型用法：panel 加载某个旧会话（从本地 sessions.json 来的）时，转换成此请求发给 broker，
    /// 保证 broker 下一次 turn 能看到完整对话上下文。
    /// </summary>
    public sealed class AgentBrokerSeedConversationRequest
    {
        public AgentBrokerSeedConversationRequest()
        {
            ConversationId = string.Empty;
            Title = string.Empty;
            HostApplicationName = string.Empty;
            Messages = new List<AgentBrokerSeedMessage>();
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            OverwriteExisting = false;
        }

        public string ConversationId { get; set; }

        public string Title { get; set; }

        /// <summary>会话所属宿主 ("Excel" / "Word" / "PowerPoint")，写入会话 metadata。</summary>
        public string HostApplicationName { get; set; }

        public IList<AgentBrokerSeedMessage> Messages { get; set; }

        public IDictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// 默认 false：broker 仅在自己 JSONL 该 conversationId 还没有任何消息时才接受种子，避免覆盖已有上下文。
        /// 设为 true 强制覆盖；正常迁移路径不应使用。
        /// </summary>
        public bool OverwriteExisting { get; set; }
    }

    public sealed class AgentBrokerClearExecutionModePreferenceRequest
    {
        public AgentBrokerClearExecutionModePreferenceRequest()
        {
            ConversationId = string.Empty;
        }

        public string ConversationId { get; set; }
    }

    public sealed class AgentBrokerClearExecutionModePreferenceResult
    {
        public AgentBrokerClearExecutionModePreferenceResult()
        {
            ConversationId = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
        }

        public string ConversationId { get; set; }

        public bool Success { get; set; }

        public bool WasCleared { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}
