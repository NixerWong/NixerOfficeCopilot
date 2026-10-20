using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Panel.WebView.Models;

namespace OfficeCopilot.Panel.WebView.Services
{
    /// <summary>
    /// Phase 1.4c: 将 broker 持久化的扁平消息列表（user / assistant / tool_call / tool_result）
    /// 还原为 panel UI 所需的 <see cref="ChatMessageState"/> 结构（tool_call/result 折叠到所属 assistant 消息的 <see cref="ChatMessageState.ToolCalls"/>）。
    /// </summary>
    internal static class BrokerToPanelConverter
    {
        public static ChatSessionState ConvertConversation(AgentBrokerLoadConversationResult source)
        {
            if (source == null)
            {
                return null;
            }

            Guid id;
            if (!Guid.TryParse(source.ConversationId ?? string.Empty, out id))
            {
                id = Guid.NewGuid();
            }

            var createdAt = source.CreatedAtUtc == default(DateTime) ? DateTime.Now : source.CreatedAtUtc.ToLocalTime();
            var updatedAt = source.UpdatedAtUtc == default(DateTime) ? createdAt : source.UpdatedAtUtc.ToLocalTime();

            int? modelProviderId = null;
            string title = source.Title ?? string.Empty;
            if (source.Metadata != null)
            {
                string modelIdRaw;
                if (source.Metadata.TryGetValue("modelProviderId", out modelIdRaw)
                    && !string.IsNullOrWhiteSpace(modelIdRaw))
                {
                    int parsed;
                    if (int.TryParse(modelIdRaw, out parsed))
                    {
                        modelProviderId = parsed;
                    }
                }
            }

            var session = new ChatSessionState
            {
                Id = id,
                Title = string.IsNullOrWhiteSpace(title) ? ChatSessionFactory.DefaultNewSessionTitle : title,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                ModelProviderId = modelProviderId,
                Messages = ConvertMessages(source.Messages)
            };

            return session;
        }

        public static ChatSessionState ConvertSummary(AgentBrokerConversationSummary summary)
        {
            if (summary == null)
            {
                return null;
            }

            Guid id;
            if (!Guid.TryParse(summary.ConversationId ?? string.Empty, out id))
            {
                id = Guid.NewGuid();
            }

            var createdAt = summary.CreatedAtUtc == default(DateTime) ? DateTime.Now : summary.CreatedAtUtc.ToLocalTime();
            var updatedAt = summary.UpdatedAtUtc == default(DateTime) ? createdAt : summary.UpdatedAtUtc.ToLocalTime();

            int? modelProviderId = null;
            if (summary.Metadata != null)
            {
                string modelIdRaw;
                if (summary.Metadata.TryGetValue("modelProviderId", out modelIdRaw)
                    && !string.IsNullOrWhiteSpace(modelIdRaw))
                {
                    int parsed;
                    if (int.TryParse(modelIdRaw, out parsed))
                    {
                        modelProviderId = parsed;
                    }
                }
            }

            return new ChatSessionState
            {
                Id = id,
                Title = string.IsNullOrWhiteSpace(summary.Title) ? ChatSessionFactory.DefaultNewSessionTitle : summary.Title,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                ModelProviderId = modelProviderId,
                Messages = new List<ChatMessageState>()
            };
        }

        private static List<ChatMessageState> ConvertMessages(IList<AgentBrokerSeedMessage> source)
        {
            var result = new List<ChatMessageState>();
            if (source == null || source.Count == 0)
            {
                return result;
            }

            var pendingToolCalls = new Dictionary<string, ChatToolCallState>(StringComparer.OrdinalIgnoreCase);
            var pendingOrder = new List<ChatToolCallState>();

            // 合并连续的 assistant 消息（中间穿插 tool_call/tool_result）为单张卡片
            ChatMessageState mergedAssistant = null;

            foreach (var msg in source)
            {
                if (msg == null)
                {
                    continue;
                }

                var messageType = (msg.MessageType ?? string.Empty).Trim().ToLowerInvariant();
                var role = (msg.Role ?? string.Empty).Trim().ToLowerInvariant();

                if (string.Equals(messageType, "tool_call", StringComparison.Ordinal))
                {
                    var toolCallId = msg.ToolCallId ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(toolCallId))
                    {
                        continue;
                    }

                    ChatToolCallState existing;
                    if (!pendingToolCalls.TryGetValue(toolCallId, out existing))
                    {
                        existing = new ChatToolCallState
                        {
                            ToolCallId = toolCallId,
                            ToolName = msg.Name ?? string.Empty,
                            Status = "running",
                            StartedAt = msg.CreatedAtUtc == default(DateTime) ? DateTime.Now : msg.CreatedAtUtc.ToLocalTime(),
                            RequiresBackup = !string.IsNullOrWhiteSpace(msg.BackupPath),
                            BackupPath = msg.BackupPath ?? string.Empty
                        };
                        pendingToolCalls[toolCallId] = existing;
                        pendingOrder.Add(existing);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(existing.ToolName))
                        {
                            existing.ToolName = msg.Name ?? string.Empty;
                        }
                        if (string.IsNullOrEmpty(existing.BackupPath) && !string.IsNullOrWhiteSpace(msg.BackupPath))
                        {
                            existing.BackupPath = msg.BackupPath;
                            existing.RequiresBackup = true;
                        }
                    }

                    // 将工具调用引用追加到当前合并助手的 ContentParts
                    if (mergedAssistant != null)
                    {
                        EnsureContentParts(mergedAssistant).Add(new ChatContentPart
                        {
                            Type = "tool_call",
                            ToolCallId = toolCallId
                        });
                    }

                    continue;
                }

                if (string.Equals(messageType, "tool_result", StringComparison.Ordinal))
                {
                    var toolCallId = msg.ToolCallId ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(toolCallId))
                    {
                        continue;
                    }

                    ChatToolCallState existing;
                    if (!pendingToolCalls.TryGetValue(toolCallId, out existing))
                    {
                        existing = new ChatToolCallState
                        {
                            ToolCallId = toolCallId,
                            ToolName = msg.Name ?? string.Empty,
                            StartedAt = msg.CreatedAtUtc == default(DateTime) ? DateTime.Now : msg.CreatedAtUtc.ToLocalTime()
                        };
                        pendingToolCalls[toolCallId] = existing;
                        pendingOrder.Add(existing);
                    }

                    existing.Success = msg.Success;
                    existing.Status = msg.Success ? "completed" : "failed";
                    existing.Content = msg.Content ?? string.Empty;
                    existing.CompletedAt = msg.CreatedAtUtc == default(DateTime) ? DateTime.Now : msg.CreatedAtUtc.ToLocalTime();
                    if (!msg.Success)
                    {
                        existing.ErrorMessage = msg.Content ?? string.Empty;
                    }
                    if (!string.IsNullOrWhiteSpace(msg.BackupPath))
                    {
                        existing.BackupPath = msg.BackupPath;
                        existing.RequiresBackup = true;
                    }

                    continue;
                }

                // 普通 message：user / assistant / system
                if (!string.Equals(messageType, "user", StringComparison.Ordinal)
                    && !string.Equals(messageType, "assistant", StringComparison.Ordinal)
                    && !string.Equals(messageType, "system", StringComparison.Ordinal)
                    && !string.Equals(messageType, "message", StringComparison.Ordinal))
                {
                    continue;
                }

                var isAssistant = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(messageType, "assistant", StringComparison.OrdinalIgnoreCase);

                if (isAssistant)
                {
                    // 合并到当前助手卡片
                    if (mergedAssistant == null)
                    {
                        mergedAssistant = new ChatMessageState
                        {
                            Id = string.IsNullOrWhiteSpace(msg.MessageId) ? Guid.NewGuid().ToString("N") : msg.MessageId,
                            Role = "assistant",
                            Content = string.Empty,
                            CreatedAt = msg.CreatedAtUtc == default(DateTime) ? DateTime.Now : msg.CreatedAtUtc.ToLocalTime(),
                            InfoItems = new List<string>(),
                            ToolCalls = new List<ChatToolCallState>(),
                            CodeExecutions = new List<ChatCodeExecutionState>(),
                            ContentParts = new List<ChatContentPart>()
                        };
                    }

                    // 先把累积的 tool_call 挂到合并助手上
                    if (pendingOrder.Count > 0)
                    {
                        foreach (var tc in pendingOrder)
                        {
                            if (!mergedAssistant.ToolCalls.Any(t => string.Equals(t.ToolCallId, tc.ToolCallId, StringComparison.OrdinalIgnoreCase)))
                            {
                                mergedAssistant.ToolCalls.Add(tc);
                            }
                        }
                        pendingOrder.Clear();
                        pendingToolCalls.Clear();
                    }

                    var text = msg.Content ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        EnsureContentParts(mergedAssistant).Add(new ChatContentPart
                        {
                            Type = "text",
                            Text = text
                        });

                        // 累积 Content（用双换行分隔文本段）
                        if (!string.IsNullOrWhiteSpace(mergedAssistant.Content))
                        {
                            mergedAssistant.Content += "\n\n" + text;
                        }
                        else
                        {
                            mergedAssistant.Content = text;
                        }
                    }

                    continue;
                }

                // 非 assistant 消息（user / system）：先 flush 合并的助手卡片
                FlushMergedAssistant(result, ref mergedAssistant);

                var chatMessage = new ChatMessageState
                {
                    Id = string.IsNullOrWhiteSpace(msg.MessageId) ? Guid.NewGuid().ToString("N") : msg.MessageId,
                    Role = string.IsNullOrWhiteSpace(role) ? messageType : role,
                    Content = msg.Content ?? string.Empty,
                    CreatedAt = msg.CreatedAtUtc == default(DateTime) ? DateTime.Now : msg.CreatedAtUtc.ToLocalTime(),
                    InfoItems = new List<string>(),
                    ToolCalls = new List<ChatToolCallState>(),
                    CodeExecutions = new List<ChatCodeExecutionState>()
                };

                result.Add(chatMessage);
            }

            // 末尾 flush
            FlushMergedAssistant(result, ref mergedAssistant);

            // 若末尾还有 pending 工具调用，挂到最后一条 assistant 消息上
            if (pendingOrder.Count > 0)
            {
                var lastAssistant = result.LastOrDefault(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase));
                if (lastAssistant != null)
                {
                    if (lastAssistant.ToolCalls == null)
                    {
                        lastAssistant.ToolCalls = new List<ChatToolCallState>();
                    }
                    foreach (var tc in pendingOrder)
                    {
                        if (!lastAssistant.ToolCalls.Any(t => string.Equals(t.ToolCallId, tc.ToolCallId, StringComparison.OrdinalIgnoreCase)))
                        {
                            lastAssistant.ToolCalls.Add(tc);
                        }
                        EnsureContentParts(lastAssistant).Add(new ChatContentPart
                        {
                            Type = "tool_call",
                            ToolCallId = tc.ToolCallId ?? string.Empty
                        });
                    }
                }
            }

            return result;
        }

        private static List<ChatContentPart> EnsureContentParts(ChatMessageState message)
        {
            if (message.ContentParts == null)
            {
                message.ContentParts = new List<ChatContentPart>();
            }
            return message.ContentParts;
        }

        private static void FlushMergedAssistant(List<ChatMessageState> result, ref ChatMessageState mergedAssistant)
        {
            if (mergedAssistant != null)
            {
                result.Add(mergedAssistant);
                mergedAssistant = null;
            }
        }
    }
}
