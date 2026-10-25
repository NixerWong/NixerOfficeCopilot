using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Persistence.Jsonl;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime.Context;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.HostBridge.Client;
using OfficeCopilot.Mcp;
using OfficeCopilot.Skills;
using OfficeCopilot.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace OfficeCopilot.Agent.Runtime
{
    public sealed class AgentRuntimeService : IAgentRuntime
    {
        private readonly IHostContextReader _hostContextReader;
        private readonly IConversationStore _conversationStore;
        private readonly BrokerTurnExecutor _turnExecutor;
        private readonly IMcpRegistry _mcpRegistry;
        private readonly IModelClient _modelClient;

        /// <summary>
        /// 依赖全部显式注入。装配请走
        /// <see cref="Composition.AgentRuntimeServiceCollectionExtensions.AddOfficeCopilotAgentRuntime"/>。
        /// </summary>
        /// <param name="mcpRegistry">可为 <c>null</c>，此时 MCP 不激活。</param>
        internal AgentRuntimeService(
            AgentRuntimeOptions options,
            IHostContextReader hostContextReader,
            IConversationStore conversationStore,
            IModelClient modelClient,
            BrokerTurnExecutor turnExecutor,
            IMcpRegistry mcpRegistry)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _hostContextReader = hostContextReader ?? throw new ArgumentNullException(nameof(hostContextReader));
            _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
            _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
            _turnExecutor = turnExecutor ?? throw new ArgumentNullException(nameof(turnExecutor));
            _mcpRegistry = mcpRegistry;
        }

        public async Task<AgentBrokerCompletionResult> CompleteAsync(
            AgentBrokerCompletionRequest request,
            CancellationToken cancellationToken)
        {
            request = request ?? new AgentBrokerCompletionRequest();
            try
            {
                var messages = (request.Messages ?? new List<AgentBrokerCompletionMessage>())
                    .Select(message => new ModelClientMessage
                    {
                        Role = message?.Role ?? string.Empty,
                        Content = message?.Content ?? string.Empty
                    })
                    .ToList();
                var completion = await _modelClient.CompleteAsync(
                    request.ModelOptions ?? new AgentModelOptions(),
                    messages,
                    new List<ModelClientTool>(),
                    null,
                    cancellationToken).ConfigureAwait(false);
                return new AgentBrokerCompletionResult
                {
                    Success = true,
                    Content = completion?.Content ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                return new AgentBrokerCompletionResult
                {
                    Success = false,
                    ErrorCode = "broker.completion.failed",
                    ErrorMessage = ex.Message
                };
            }
        }

        public AgentRuntimeOptions Options { get; private set; }

        public async Task<AgentBrokerTurnResult> ExecuteTurnAsync(
            AgentBrokerTurnRequest request,
            IAgentBrokerEventSink eventSink,
            CancellationToken cancellationToken)
        {
            request = request ?? new AgentBrokerTurnRequest();

            var conversation = await _conversationStore.GetOrCreateAsync(
                request.ConversationId,
                CancellationToken.None).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(request.UserInput))
            {
                var userMsg = new BrokerConversationMessage
                {
                    MessageId = request.MessageId ?? Guid.NewGuid().ToString("N"),
                    MessageType = "user",
                    Role = "user",
                    Content = request.UserInput,
                    StructuredContentJson = SerializePersistedUserInputContentParts(request.UserInputContentParts),
                    CreatedAtUtc = DateTime.UtcNow
                };
                conversation.Messages.Add(userMsg);
                if (conversation.CompactMessages != null)
                {
                    conversation.CompactMessages.Add(userMsg);
                }

                await PersistConversationSafelyAsync(conversation).ConfigureAwait(false);
            }

            AgentBrokerTurnResult result;
            try
            {
                var snapshot = await _hostContextReader.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

                // 每个 turn 前刷新 MCP 配置，确保用户在设置面板中增删 MCP server 后即时生效
                if (_mcpRegistry != null)
                {
                    try
                    {
                        var settingsPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "NixerOfficeCopilot",
                            "settings.json");
                        await _mcpRegistry.RefreshFromSettingsAsync(settingsPath, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        RuntimeTrace.Write($"[AgentRuntimeService] MCP refresh failed, keeping existing connections: {ex.Message}");
                    }
                }

                var context = new BrokerTurnContext(request, conversation, eventSink, snapshot, cancellationToken);
                var previousUiCulture = CultureInfo.CurrentUICulture;
                try
                {
                    CultureInfo.CurrentUICulture = SupportedLanguages.ResolveCulture(request.UiCulture);
                    result = await _turnExecutor.ExecuteAsync(context).ConfigureAwait(false);
                }
                finally
                {
                    CultureInfo.CurrentUICulture = previousUiCulture;
                }

                if (!string.IsNullOrWhiteSpace(result == null ? null : result.Content))
                {
                    var assistantMsg = new BrokerConversationMessage
                    {
                        MessageId = result.MessageId ?? Guid.NewGuid().ToString("N"),
                        MessageType = "assistant",
                        Role = "assistant",
                        Content = result.Content,
                        Success = result.Success,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    conversation.Messages.Add(assistantMsg);
                    if (conversation.CompactMessages != null)
                    {
                        conversation.CompactMessages.Add(assistantMsg);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                var cancelledMsg = new BrokerConversationMessage
                {
                    MessageId = request.MessageId ?? Guid.NewGuid().ToString("N"),
                    MessageType = "assistant",
                    Role = "assistant",
                    Content = "（本回合已中止；中止前已完成的交互记录已保留。）",
                    Success = false,
                    CreatedAtUtc = DateTime.UtcNow
                };
                conversation.Messages.Add(cancelledMsg);
                if (conversation.CompactMessages != null)
                {
                    conversation.CompactMessages.Add(cancelledMsg);
                }

                var interruptedSummary = new BrokerConversationMessage
                {
                    MessageType = "turn_interrupted",
                    Role = "system",
                    Content = BuildInterruptedTurnSummary(conversation, request.MessageId),
                    Success = false,
                    CreatedAtUtc = DateTime.UtcNow
                };
                conversation.Messages.Add(interruptedSummary);
                if (conversation.CompactMessages != null)
                {
                    conversation.CompactMessages.Add(interruptedSummary);
                }

                throw;
            }
            catch (Exception ex)
            {
                var message = string.IsNullOrWhiteSpace(ex.Message) ? "Broker 执行失败。" : ex.Message;
                result = new AgentBrokerTurnResult
                {
                    ConversationId = conversation.ConversationId ?? string.Empty,
                    MessageId = request.MessageId ?? string.Empty,
                    Success = false,
                    Content = message,
                    ErrorCode = "runtime.turn_failed",
                    ErrorParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "details", message }
                    },
                    ErrorMessage = message,
                    FinishReason = "exception"
                };
                var failureMsg = new BrokerConversationMessage
                {
                    MessageId = result.MessageId ?? Guid.NewGuid().ToString("N"),
                    MessageType = "assistant",
                    Role = "assistant",
                    Content = message,
                    Success = false,
                    CreatedAtUtc = DateTime.UtcNow
                };
                conversation.Messages.Add(failureMsg);
                if (conversation.CompactMessages != null)
                {
                    conversation.CompactMessages.Add(failureMsg);
                }
                SkillLog.Write("[AgentRuntime] Turn failed and was persisted: " + message);
            }
            finally
            {
                // 1.4c：head 中保留宿主和标题，供会话列表筛选与显示。
                if (request.Metadata != null && request.Metadata.Count > 0)
                {
                    if (conversation.Metadata == null)
                    {
                        conversation.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }

                    string hostApp;
                    if (request.Metadata.TryGetValue("hostApplicationName", out hostApp) && !string.IsNullOrWhiteSpace(hostApp))
                    {
                        conversation.Metadata["hostApplicationName"] = hostApp.Trim();
                    }

                    string title;
                    if (request.Metadata.TryGetValue("title", out title) && !string.IsNullOrWhiteSpace(title))
                    {
                        conversation.Metadata["title"] = title.Trim();
                    }
                }

                await PersistConversationSafelyAsync(conversation).ConfigureAwait(false);
            }

            return result ?? new AgentBrokerTurnResult();
        }

        private async Task PersistConversationSafelyAsync(BrokerConversationState conversation)
        {
            using (var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    await _conversationStore.SaveAsync(conversation, saveTimeout.Token).ConfigureAwait(false);
                    SkillLog.Write($"[AgentRuntime] Turn saved: convId={conversation.ConversationId}, msgCount={conversation.Messages?.Count ?? 0}");
                }
                catch (Exception ex)
                {
                    SkillLog.Write("[AgentRuntime] Conversation persistence failed: " + ex.Message);
                }
            }
        }

        private static string BuildInterruptedTurnSummary(BrokerConversationState conversation, string messageId)
        {
            var messages = conversation?.Messages;
            var userMessageIndex = -1;
            if (messages != null)
            {
                for (var index = messages.Count - 1; index >= 0; index--)
                {
                    if (messages[index] != null
                        && string.Equals(messages[index].Role, "user", StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(messageId)
                            || string.Equals(messages[index].MessageId, messageId, StringComparison.OrdinalIgnoreCase)))
                    {
                        userMessageIndex = index;
                        break;
                    }
                }
            }

            var toolCalls = messages == null || userMessageIndex < 0
                ? new List<BrokerConversationMessage>()
                : messages.Skip(userMessageIndex + 1)
                    .Where(message => message != null && string.Equals(message.MessageType, "tool_call", StringComparison.OrdinalIgnoreCase))
                    .Take(20)
                    .ToList();

            var summary = new System.Text.StringBuilder("turn_interrupted: 本回合已中止。继续前先核对 Office 当前状态，不要重复成功的写操作。");
            if (toolCalls.Count == 0)
            {
                summary.AppendLine().Append("本回合尚无工具调用记录。");
                return summary.ToString();
            }

            summary.AppendLine().AppendLine("中止前的工具执行记录：");
            foreach (var toolCall in toolCalls)
            {
                var toolResult = messages.Skip(userMessageIndex + 1).FirstOrDefault(message =>
                    message != null
                    && string.Equals(message.MessageType, "tool_result", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(message.ToolCallId, toolCall.ToolCallId, StringComparison.OrdinalIgnoreCase));
                summary.Append("- ").Append(toolCall.Name).Append(": ");
                if (toolResult == null)
                {
                    summary.Append("没有结果记录，执行状态未知；继续前必须核对文档。");
                }
                else
                {
                    summary.Append(toolResult.Success ? "已成功" : "失败或未确认");
                    if (!string.IsNullOrWhiteSpace(toolResult.Content))
                    {
                        var result = toolResult.Content.Length <= 300 ? toolResult.Content : toolResult.Content.Substring(0, 300) + "...";
                        summary.Append("；结果：").Append(result);
                    }
                }

                summary.AppendLine();
            }

            return summary.ToString().TrimEnd();
        }

        /// <summary>
        /// Phase 1.3b 种子注入。默认非覆盖语义：仅当 broker store 中该会话尚无消息时才写入。
        /// </summary>
        public async Task<AgentBrokerSeedConversationResult> SeedConversationAsync(
            AgentBrokerSeedConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerSeedConversationRequest();

            if (string.IsNullOrWhiteSpace(request.ConversationId))
            {
                return new AgentBrokerSeedConversationResult
                {
                    Success = false,
                    ErrorCode = "broker.seed.missing_conversation_id",
                    ErrorMessage = "ConversationId is required."
                };
            }

            var conversation = await _conversationStore.GetOrCreateAsync(request.ConversationId, cancellationToken).ConfigureAwait(false);
            if (conversation.Messages == null)
            {
                conversation.Messages = new List<BrokerConversationMessage>();
            }

            var hasExistingMessages = conversation.Messages.Count > 0;
            var incomingMessageCount = request.Messages == null ? 0 : request.Messages.Count;
            var shouldSeedMessages = incomingMessageCount > 0
                && (!hasExistingMessages || request.OverwriteExisting);

            // 1.4c：即使不写消息，也允许仅做 metadata 更新（如 panel 在标题生成后回灌 title）。
            var hasMetadataUpdates = !string.IsNullOrWhiteSpace(request.HostApplicationName)
                || !string.IsNullOrWhiteSpace(request.Title)
                || (request.Metadata != null && request.Metadata.Count > 0);

            if (!shouldSeedMessages && !hasMetadataUpdates)
            {
                return new AgentBrokerSeedConversationResult
                {
                    ConversationId = request.ConversationId,
                    Success = true,
                    WasSeeded = false,
                    SkippedReason = hasExistingMessages ? "conversation_not_empty" : "no_messages",
                    WrittenMessageCount = conversation.Messages.Count
                };
            }

            if (shouldSeedMessages)
            {
                if (hasExistingMessages)
                {
                    conversation.Messages.Clear();
                    if (conversation.CompactMessages != null)
                    {
                        conversation.CompactMessages.Clear();
                    }
                }

                foreach (var seedMessage in request.Messages)
                {
                    if (seedMessage == null)
                    {
                        continue;
                    }

                    var msg = ConvertSeedToMessage(seedMessage);
                    conversation.Messages.Add(msg);
                    if (conversation.CompactMessages != null)
                    {
                        conversation.CompactMessages.Add(msg);
                    }
                }
            }

            // metadata 合入（hostApp / title 等可被 ListAsync 显示）
            if (conversation.Metadata == null)
            {
                conversation.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(request.HostApplicationName))
            {
                conversation.Metadata["hostApplicationName"] = request.HostApplicationName;
            }

            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                conversation.Metadata["title"] = request.Title;
            }

            if (request.Metadata != null)
            {
                foreach (var pair in request.Metadata)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        conversation.Metadata[pair.Key] = pair.Value ?? string.Empty;
                    }
                }
            }

            await _conversationStore.SaveAsync(conversation, cancellationToken).ConfigureAwait(false);

            return new AgentBrokerSeedConversationResult
            {
                ConversationId = request.ConversationId,
                Success = true,
                WasSeeded = shouldSeedMessages,
                SkippedReason = shouldSeedMessages ? string.Empty : "metadata_only_update",
                WrittenMessageCount = conversation.Messages.Count
            };
        }

        public async Task<AgentBrokerClearExecutionModePreferenceResult> ClearExecutionModePreferenceAsync(
            AgentBrokerClearExecutionModePreferenceRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new AgentBrokerClearExecutionModePreferenceRequest();

            if (string.IsNullOrWhiteSpace(request.ConversationId))
            {
                return new AgentBrokerClearExecutionModePreferenceResult
                {
                    Success = false,
                    ErrorCode = "broker.execution_mode.missing_conversation_id",
                    ErrorMessage = "ConversationId is required."
                };
            }

            var conversation = await _conversationStore
                .GetOrCreateAsync(request.ConversationId, cancellationToken)
                .ConfigureAwait(false);
            var wasCleared = conversation.Metadata != null
                && conversation.Metadata.Remove(TurnExecutionContract.SessionPreferenceMetadataKey);

            if (wasCleared)
            {
                await _conversationStore.SaveAsync(conversation, cancellationToken).ConfigureAwait(false);
            }

            return new AgentBrokerClearExecutionModePreferenceResult
            {
                ConversationId = request.ConversationId,
                Success = true,
                WasCleared = wasCleared
            };
        }

        /// <summary>
        /// Phase 1.4：列出 broker 已持久化的会话摘要。可按宿主应用名过滤。
        /// </summary>
        public async Task<AgentBrokerListConversationsResult> ListConversationsAsync(
            AgentBrokerListConversationsRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerListConversationsRequest();

            // take<=0 表示不限。底层 ListAsync 在 take<=0 时用 int.MaxValue。
            var summaries = await _conversationStore.ListAsync(request.Take, cancellationToken).ConfigureAwait(false);

            SkillLog.Write($"[AgentRuntime] ListConversations: store returned {summaries.Count} summaries, hostFilter='{(request.HostApplicationName ?? "")}'");

            var hostFilter = (request.HostApplicationName ?? string.Empty).Trim();
            var result = new AgentBrokerListConversationsResult { Success = true };

            foreach (var s in summaries)
            {
                if (s == null)
                {
                    continue;
                }

                // 1.4c：宽松过滤——只在「过滤器有值且存储值也有值且不匹配」时排除；
                // 存储值为空（legacy / 未打标签的会话）一律保留，避免冷启动看不到历史。
                var storedHost = (s.HostApplicationName ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(hostFilter)
                    && !string.IsNullOrEmpty(storedHost)
                    && !string.Equals(storedHost, hostFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dto = new AgentBrokerConversationSummary
                {
                    ConversationId = s.ConversationId ?? string.Empty,
                    Title = s.Title ?? string.Empty,
                    HostApplicationName = s.HostApplicationName ?? string.Empty,
                    MessageCount = s.MessageCount,
                    CreatedAtUtc = s.CreatedAtUtc,
                    UpdatedAtUtc = s.UpdatedAtUtc
                };

                if (s.Metadata != null)
                {
                    foreach (var kv in s.Metadata)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Key))
                        {
                            dto.Metadata[kv.Key] = kv.Value ?? string.Empty;
                        }
                    }
                }

                result.Summaries.Add(dto);
            }

            SkillLog.Write($"[AgentRuntime] ListConversations: returning {result.Summaries.Count} summaries after host filter");
            return result;
        }

        /// <summary>
        /// Phase 1.4：加载某会话完整消息体。会话不存在时返回 Success=true, Found=false（不视为错误）。
        /// </summary>
        public async Task<AgentBrokerLoadConversationResult> LoadConversationAsync(
            AgentBrokerLoadConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerLoadConversationRequest();

            if (string.IsNullOrWhiteSpace(request.ConversationId))
            {
                return new AgentBrokerLoadConversationResult
                {
                    Success = false,
                    ErrorCode = "broker.load.missing_conversation_id",
                    ErrorMessage = "ConversationId is required."
                };
            }

            // 用 ListAsync 探测一下是否存在；存在则 GetOrCreateAsync 拉完整体（GetOrCreateAsync 是幂等的，缺失会创建空壳）。
            // 这里为了避免误创建空 store 项，先看 list 里有没有这个 conversationId。
            var summaries = await _conversationStore.ListAsync(0, cancellationToken).ConfigureAwait(false);
            var summary = summaries.FirstOrDefault(s => string.Equals(s?.ConversationId ?? string.Empty, request.ConversationId, StringComparison.OrdinalIgnoreCase));
            if (summary == null)
            {
                return new AgentBrokerLoadConversationResult
                {
                    Success = true,
                    Found = false,
                    ConversationId = request.ConversationId
                };
            }

            var conversation = await _conversationStore.GetOrCreateAsync(request.ConversationId, cancellationToken).ConfigureAwait(false);

            var result = new AgentBrokerLoadConversationResult
            {
                Success = true,
                Found = true,
                ConversationId = conversation.ConversationId ?? request.ConversationId,
                CreatedAtUtc = conversation.CreatedAtUtc,
                UpdatedAtUtc = conversation.UpdatedAtUtc
            };

            if (conversation.Metadata != null)
            {
                string title;
                if (conversation.Metadata.TryGetValue("title", out title))
                {
                    result.Title = title ?? string.Empty;
                }

                string hostApp;
                if (conversation.Metadata.TryGetValue("hostApplicationName", out hostApp))
                {
                    result.HostApplicationName = hostApp ?? string.Empty;
                }

                foreach (var kv in conversation.Metadata)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                    {
                        result.Metadata[kv.Key] = kv.Value ?? string.Empty;
                    }
                }
            }

            if (conversation.Messages != null)
            {
                foreach (var m in conversation.Messages)
                {
                    if (m == null)
                    {
                        continue;
                    }

                    result.Messages.Add(new AgentBrokerSeedMessage
                    {
                        MessageId = m.MessageId ?? string.Empty,
                        MessageType = string.IsNullOrWhiteSpace(m.MessageType) ? "message" : m.MessageType,
                        Role = m.Role ?? string.Empty,
                        Name = m.Name ?? string.Empty,
                        ToolCallId = m.ToolCallId ?? string.Empty,
                        Content = m.Content ?? string.Empty,
                        StructuredContentJson = m.StructuredContentJson ?? string.Empty,
                        Success = m.Success,
                        BackupPath = m.BackupPath ?? string.Empty,
                        CreatedAtUtc = m.CreatedAtUtc
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Phase 1.4：删除某会话。
        /// </summary>
        public async Task<AgentBrokerDeleteConversationResult> DeleteConversationAsync(
            AgentBrokerDeleteConversationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            request = request ?? new AgentBrokerDeleteConversationRequest();

            if (string.IsNullOrWhiteSpace(request.ConversationId))
            {
                return new AgentBrokerDeleteConversationResult
                {
                    Success = false,
                    ErrorCode = "broker.delete.missing_conversation_id",
                    ErrorMessage = "ConversationId is required."
                };
            }

            var deleted = await _conversationStore.DeleteAsync(request.ConversationId, cancellationToken).ConfigureAwait(false);
            return new AgentBrokerDeleteConversationResult
            {
                Success = true,
                WasDeleted = deleted,
                ConversationId = request.ConversationId
            };
        }

        private static BrokerConversationMessage ConvertSeedToMessage(AgentBrokerSeedMessage seed)
        {
            return new BrokerConversationMessage
            {
                MessageId = string.IsNullOrWhiteSpace(seed.MessageId) ? Guid.NewGuid().ToString("N") : seed.MessageId,
                MessageType = string.IsNullOrWhiteSpace(seed.MessageType) ? "message" : seed.MessageType,
                Role = seed.Role ?? string.Empty,
                Name = seed.Name ?? string.Empty,
                ToolCallId = seed.ToolCallId ?? string.Empty,
                Content = seed.Content ?? string.Empty,
                StructuredContentJson = seed.StructuredContentJson ?? string.Empty,
                Success = seed.Success,
                BackupPath = seed.BackupPath ?? string.Empty,
                CreatedAtUtc = seed.CreatedAtUtc == default ? DateTime.UtcNow : seed.CreatedAtUtc
            };
        }

        private static string SerializePersistedUserInputContentParts(IList<AgentBrokerInputContentPart> contentParts)
        {
            if (contentParts == null || contentParts.Count == 0)
            {
                return string.Empty;
            }

            var normalized = contentParts
                .Where(p => p != null
                    && !string.IsNullOrWhiteSpace(p.Type)
                    && (string.Equals(p.Type, "input_text", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p.Type, "text", StringComparison.OrdinalIgnoreCase))
                    && !string.IsNullOrWhiteSpace(p.Text))
                .Select(p => new AgentBrokerInputContentPart
                {
                    Type = p.Type ?? string.Empty,
                    Text = p.Text ?? string.Empty,
                    MediaType = string.Empty,
                    Url = string.Empty,
                    FileName = string.Empty,
                    FileContentBase64 = string.Empty
                })
                .ToList();

            if (normalized.Count == 0)
            {
                return string.Empty;
            }

            try
            {
                return JsonSerializer.Serialize(normalized);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
