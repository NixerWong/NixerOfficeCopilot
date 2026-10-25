using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerTurnResult
    {
        public AgentBrokerTurnResult()
        {
            ConversationId = string.Empty;
            MessageId = string.Empty;
            Content = string.Empty;
            FinishReason = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
            ErrorParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public bool Success { get; set; }

        public string Content { get; set; }

        public string FinishReason { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public IDictionary<string, string> ErrorParameters { get; set; }

        /// <summary>
        /// Identifies transport/runtime failures separately from content-bearing incomplete assistant turns.
        /// </summary>
        public static bool IsOperationalFailure(AgentBrokerTurnResult result)
        {
            return IsOperationalFailure(result, false);
        }

        public static bool IsOperationalFailure(
            AgentBrokerTurnResult result,
            bool hasVisibleAssistantContent)
        {
            return result == null
                || (!result.Success
                    && (!string.IsNullOrWhiteSpace(result.ErrorCode)
                        || !string.IsNullOrWhiteSpace(result.ErrorMessage)
                        || (string.IsNullOrWhiteSpace(result.Content)
                            && !hasVisibleAssistantContent)));
        }

        /// <summary>上下文用量百分比（0-100），null 表示未计算。</summary>
        public int? ContextUsagePercent { get; set; }

        /// <summary>上下文已用 token 估算值，null 表示未计算。</summary>
        public int? ContextUsageTokens { get; set; }

        /// <summary>模型上下文窗口大小（token），null 表示未知。</summary>
        public int? ContextWindowSize { get; set; }
    }
}
