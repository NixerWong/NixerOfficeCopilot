using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Persistence
{
    /// <summary>
    /// 会话摘要 DTO —— 供面板"历史会话"列表展示；不含完整消息体。
    /// Phase 1.1 引入。Phase 1.1.2 加 Sqlite 索引后，<c>ListAsync</c> 直接读这张索引而非扫目录。
    /// </summary>
    public sealed class ConversationSummary
    {
        public ConversationSummary()
        {
            ConversationId = string.Empty;
            Title = string.Empty;
            HostApplicationName = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ConversationId { get; set; }

        /// <summary>会话标题。默认取用户首条消息前 N 字符（参考 cc-haha <c>sessionTitle.ts</c>）。</summary>
        public string Title { get; set; }

        /// <summary>宿主应用名（Excel / Word / PowerPoint），用于面板分类筛选。</summary>
        public string HostApplicationName { get; set; }

        /// <summary>会话总消息数（包括 user / assistant / tool_call / tool_result）。</summary>
        public int MessageCount { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        /// <summary>
        /// Phase 1.4：把 broker 会话 Metadata 透传给面板（如 modelProviderId、summary 等扩展字段）。
        /// </summary>
        public IDictionary<string, string> Metadata { get; set; }
    }
}
