using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime.Context;
using OfficeCopilot.Agent.Runtime.Planning;
using OfficeCopilot.HostBridge.Contracts;
using OfficeCopilot.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    public static class ResponseLanguagePolicy
    {
        public static string BuildInstruction(string uiCulture)
        {
            var preferredCulture = SupportedLanguages.ResolveCulture(uiCulture);
            switch (preferredCulture.Name)
            {
                case SupportedLanguages.SimplifiedChinese:
                    return "回答语言：默认使用简体中文回答；本默认规则优先于其他系统或宿主提示中的语言指令。只有用户明确要求其他回复语言时，才改用指定语言。";
                case SupportedLanguages.Japanese:
                    return "回答言語：デフォルトでは日本語で回答してください。この既定の言語設定は、他のシステム指示やホスト指示にある競合する言語指定より優先されます。ユーザーが別の回答言語を明示的に指定した場合に限り、その言語を使用してください。";
                case SupportedLanguages.German:
                    return "Antwortsprache: Antworten Sie standardmäßig auf Deutsch. Diese Standardeinstellung hat Vorrang vor widersprüchlichen Sprachanweisungen in anderen System- oder Host-Prompts. Verwenden Sie nur dann eine andere Sprache, wenn der Benutzer dies ausdrücklich verlangt.";
                case SupportedLanguages.Spanish:
                    return "Idioma de respuesta: Responde en español de forma predeterminada. Esta preferencia tiene prioridad sobre instrucciones de idioma contradictorias en otros mensajes del sistema o del host. Usa otro idioma solo si el usuario lo solicita explícitamente.";
                default:
                    return "Response language: By default, respond in English. This default takes precedence over conflicting language instructions in other system or host prompts. Use another language only when the user explicitly requests it.";
            }
        }
    }

    /// <summary>
    /// 把会话历史 + 宿主上下文快照 + 技能计划组装成模型消息序列的实现，从 <see cref="BrokerTurnExecutor"/> 迁出。
    /// 当前以静态方法对外暴露，调用方（执行器）通过 <see cref="BuildModelMessages"/> 等入口使用。
    ///
    /// 设计目标：所有迁出代码与原版**逐字段等价**，外部行为零变化（除把若干 mojibake 字符串还原为可读 UTF-8 中文外）。
    /// </summary>
    internal static class DefaultContextBuilder
    {
        private const int MaximumWorkbookMapSummaryTokens = 1500;
        private const string WorkbookMapTruncationNotice =
            "- The workbook map summary was truncated to fit the estimated token budget. Call excel_list_worksheets for more structure.";
        private static readonly RoughTokenEstimator WorkbookMapTokenEstimator =
            new RoughTokenEstimator();

        // =====================================================================
        // 模型消息列表（migrated from BrokerTurnExecutor.BuildModelMessages）
        // =====================================================================

        /// <summary>
        /// 把会话状态拼装成一次模型调用所需的消息列表：system prompt + 历史 user/assistant 消息。
        /// 历史消息中 tool_call / tool_result 类不会被包含（它们由 SDK 侧或显式调用路径维护）。
        /// </summary>
        public static List<ModelClientMessage> BuildModelMessages(
            AgentBrokerTurnRequest request,
            BrokerConversationState conversation,
            HostContextSnapshotDto snapshot,
            RuntimeSkillPlan skillPlan)
        {
            var messages = new List<ModelClientMessage>();

            var combinedSystemPrompt = BuildCombinedModelSystemPrompt(request, snapshot, skillPlan);
            if (!string.IsNullOrWhiteSpace(combinedSystemPrompt))
            {
                messages.Add(new ModelClientMessage
                {
                    Role = "system",
                    Content = combinedSystemPrompt
                });
            }

            if (conversation != null)
            {
                // Use CompactMessages when available (compressed chain for model context)
                var sourceMessages = conversation.CompactMessages != null && conversation.CompactMessages.Count > 0
                    ? conversation.CompactMessages
                    : conversation.Messages;

                if (sourceMessages != null)
                {
                    foreach (var message in sourceMessages)
                    {
                        if (message == null)
                    {
                        continue;
                    }

                    if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(message.MessageType, "compact_summary", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(message.MessageType, "compact_boundary", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(message.MessageType, "turn_interrupted", StringComparison.OrdinalIgnoreCase)))
                    {
                        // 压缩摘要/边界必须以 system 消息呈现给模型，
                        // 否则 compact 后模型会丢失意图与历史（此前被静默丢弃）。
                        messages.Add(new ModelClientMessage
                        {
                            Role = "system",
                            Content = message.Content ?? string.Empty
                        });
                        continue;
                    }

                    if (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                    {
                        if (request != null
                            && string.Equals(message.MessageId, request.MessageId, StringComparison.OrdinalIgnoreCase))
                        {
                            var deckWorkspaceNote = PriorTurnDeckWorkspaceCarryOver.BuildNote(
                                conversation.Messages != null && conversation.Messages.Count > 0
                                    ? conversation.Messages
                                    : sourceMessages,
                                request.MessageId);
                            if (!string.IsNullOrWhiteSpace(deckWorkspaceNote))
                            {
                                messages.Add(new ModelClientMessage
                                {
                                    Role = "system",
                                    Content = deckWorkspaceNote
                                });
                            }

                            var wordProgressNote = PriorTurnWordProgressCarryOver.BuildNote(
                                conversation.Messages != null && conversation.Messages.Count > 0
                                    ? conversation.Messages
                                    : sourceMessages,
                                request.MessageId);
                            if (!string.IsNullOrWhiteSpace(wordProgressNote))
                            {
                                messages.Add(new ModelClientMessage
                                {
                                    Role = "system",
                                    Content = wordProgressNote
                                });
                            }
                        }

                        if (request != null
                            && string.Equals(message.MessageId, request.MessageId, StringComparison.OrdinalIgnoreCase)
                            && ConversationHistoryFollowUpPolicy.IsProcessFollowUp(message.Content))
                        {
                            var toolTrail = ConversationHistoryFollowUpPolicy.BuildPriorTurnToolTrail(
                                sourceMessages,
                                request.MessageId);
                            if (!string.IsNullOrWhiteSpace(toolTrail))
                            {
                                messages.Add(new ModelClientMessage
                                {
                                    Role = "system",
                                    Content = toolTrail
                                });
                            }
                        }

                        var contentParts = string.Equals(message.MessageId, request == null ? null : request.MessageId, StringComparison.OrdinalIgnoreCase)
                            && request != null
                            && request.UserInputContentParts != null
                            && request.UserInputContentParts.Count > 0
                            ? request.UserInputContentParts
                            : DeserializeBrokerInputContentParts(message.StructuredContentJson);

                        messages.Add(BuildUserModelMessage(
                            message.Content ?? string.Empty,
                            contentParts));
                        continue;
                    }

                    if (string.Equals(message.MessageType, "assistant", StringComparison.OrdinalIgnoreCase)
                        || (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                            && string.IsNullOrWhiteSpace(message.ToolCallId)))
                    {
                        messages.Add(new ModelClientMessage
                        {
                            Role = "assistant",
                            Content = message.Content ?? string.Empty
                        });
                    }
                    }
                }
            }

            LogBuiltModelMessages(messages, conversation?.ConversationId);
            return messages;
        }

        /// <summary>
        /// 记录本次实际发给模型的完整消息列表（供诊断：确认 compact 摘要、tool 消息等是否可见）。
        /// </summary>
        private static void LogBuiltModelMessages(List<ModelClientMessage> messages, string conversationId)
        {
            try
            {
                if (messages == null || messages.Count == 0) return;
                var totalChars = messages.Sum(m => (m?.Content?.Length ?? 0) + (m?.Name?.Length ?? 0));
                var sb = new StringBuilder();
                sb.Append("[BuildModelMessages] conversation=").Append(conversationId ?? "-")
                  .Append(" messages=").Append(messages.Count)
                  .Append(" totalChars=").Append(totalChars)
                  .Append(" estTokens=").Append(totalChars / 4);
                for (var i = 0; i < messages.Count; i++)
                {
                    var m = messages[i];
                    if (m == null) { sb.Append(Environment.NewLine).Append("  [").Append(i).Append("] <null>"); continue; }
                    var content = m.Content ?? string.Empty;
                    var preview = content.Replace('\r', ' ').Replace('\n', ' ');
                    if (preview.Length > 160) preview = preview.Substring(0, 160) + "...";
                    sb.Append(Environment.NewLine).Append("  [").Append(i).Append("] role=").Append(m.Role)
                      .Append(" name=").Append(m.Name ?? "-")
                      .Append(" toolCalls=").Append(m.ToolCalls?.Count ?? 0)
                      .Append(" len=").Append(content.Length)
                      .Append(" preview=\"").Append(preview).Append('"');
                }
                RuntimeTrace.Write(sb.ToString());
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }

        /// <summary>
        /// 合成最终的 system prompt：基础 prompt + 宿主上下文 + 技能计划 prompt + 临时 system 消息。
        /// 各段之间以双换行分隔（见 <see cref="AppendSystemPromptSection"/>）。
        /// 阶段 1 切到 <c>IContextBuilder</c> 时，本方法对应 cc-haha 的 <c>buildEffectiveSystemPrompt</c>。
        /// </summary>
        public static string BuildCombinedModelSystemPrompt(
            AgentBrokerTurnRequest request,
            HostContextSnapshotDto snapshot,
            RuntimeSkillPlan skillPlan)
        {
            var builder = new StringBuilder();

            AppendSystemPromptSection(
                builder,
                ResponseLanguagePolicy.BuildInstruction(request == null ? null : request.UiCulture));
            AppendSystemPromptSection(builder, BuildModelSystemPrompt(request));
            AppendSystemPromptSection(builder, BuildNativeToolPreferencePrompt(request));
            AppendSystemPromptSection(builder, BuildModelHostContextPrompt(request, snapshot));
            AppendSystemPromptSection(builder, skillPlan == null ? null : skillPlan.Prompt);
            AppendSystemPromptSection(builder, BuildReadOnlyModeConstraintPrompt(request));
            AppendSystemPromptSection(builder, BuildExcelExecutionModePrompt(snapshot));
            AppendSystemPromptSection(builder, BuildCurrentTimePrompt(snapshot));

            if (request != null && request.TransientSystemMessages != null)
            {
                foreach (var transientMessage in request.TransientSystemMessages)
                {
                    AppendSystemPromptSection(builder, transientMessage);
                }
            }

            return builder.ToString().Trim();
        }

        // =====================================================================
        // 单段 prompt 构造
        // =====================================================================

        internal static string BuildModelSystemPrompt(AgentBrokerTurnRequest request)
        {
            var configuredPrompt = request == null || request.ModelOptions == null
                ? string.Empty
                : request.ModelOptions.SystemPrompt ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(configuredPrompt))
            {
                return configuredPrompt.Trim();
            }

            if (request != null && string.Equals(request.InteractiveMode, "agent", StringComparison.OrdinalIgnoreCase))
            {
                return "You are an Office agent. Use standard Markdown. When tools can provide a more accurate answer, use them instead of guessing.";
            }

            return "You are an Office assistant. Use standard Markdown and keep answers concise, accurate, and well structured. "
                + "You may use read-only tools to inspect document content, but you must not modify the document.";
        }

        /// <summary>
        /// Ask 模式本轮只下发只读工具，需显式告知模型写入能力不可用，
        /// 否则它会反复尝试写入工具并被权限门禁驳回，白烧轮次。
        /// </summary>
        /// <summary>
        /// 原生工具优先约束。与 <see cref="BuildModelSystemPrompt"/> 分开是因为后者在用户
        /// 自定义了 <c>ModelOptions.SystemPrompt</c> 时会被整段替换掉，而本约束必须始终生效。
        /// </summary>
        internal static string BuildNativeToolPreferencePrompt(AgentBrokerTurnRequest request)
        {
            if (request == null || !string.Equals(request.InteractiveMode, "agent", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return "Tool selection constraints: Use official native tools (`excel_*`, `word_*`, `ppt_*`, or `host.*`) for any reads or edits to the current Office document. "
                + "These tools provide backup, confirmation, and rollback safeguards.\n"
                + "- Never use `runtime.powershell` or another code-execution tool to connect to Office COM objects, such as `Marshal.GetActiveObject` or `New-Object -ComObject Excel.Application`. "
                + "That bypasses all safety gates and cannot be undone.\n"
                + "- Use `runtime.powershell` only for local file-system or process tasks unrelated to the current document.\n"
                + "- If no available native tool can perform the task, do not fall back to code execution. First confirm with `runtime.list_tools`; if no suitable tool exists, explain the limitation and provide manual steps.";
        }

        internal static string BuildReadOnlyModeConstraintPrompt(AgentBrokerTurnRequest request)
        {
            if (request == null || string.Equals(request.InteractiveMode, "agent", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return "This is Ask mode: You may call **read-only** tools to inspect the actual document. When an answer depends on current data, inspect it instead of guessing. "
                + "No document-modification tools are available; do not claim to have modified the document. "
                + "If the user wants changes made, provide specific steps or usable code and tell them to switch to Agent mode.";
        }

        internal static string BuildExcelExecutionModePrompt(HostContextSnapshotDto snapshot)
        {
            if (snapshot == null
                || string.IsNullOrWhiteSpace(snapshot.HostApplicationName)
                || snapshot.HostApplicationName.IndexOf("Excel", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return string.Empty;
            }

            return "Excel execution-mode constraints: If this turn needs to write to the workbook and the user has not already specified the scope through an inherited execution mode, "
                + "call `runtime.declare_execution_mode` before any write tool. Do not call it for read-only work or answers that stay in the conversation. "
                + "When there is one clear approach, submit one candidate as a preview. When the target, static values versus formulas, or whether to edit the source is materially ambiguous, submit multiple candidates so the runtime can ask the user. "
                + "Candidates must state the actual target worksheet/range, data source, and outcome. A candidate is only a model preview, not a host-measured write effect or the user's choice. Only a HostBridge preflight triggered by a write tool establishes the actual level; do not claim it is known before receiving that result. "
                + "If the user asks to see the HostBridge-measured write effect before choosing or approving, a single-candidate preview is not a final result and must not end the turn. Call the relevant target write tool to trigger host preflight. Before the preflight result is returned and the confirmation flow begins, do not claim the actual level is known. If the measured effect does not meet the user's condition, cancel and do not commit the write. "
                + "In a write-tool result, `writeEffect.actualWriteEffect` is the host-measured level for that call; `MaxWriteEffect` is only the declared limit. `success=true` means the write was executed, so never report that it was not written or cancelled. If the measured field is missing, explicitly say it cannot be verified; do not guess. "
                + "Respect any stop condition the user sets if the actual write effect is unacceptable. If the user must choose whether to continue, cancel, or change the target, call `runtime.ask_user_question` to display an interactive card and wait, or submit the choices in `runtime.declare_execution_mode.clarifications` to display the same card. Never present choices or ask only in final prose. "
                + "After the user chooses, the target and write effect are hard limits for this turn; stop and ask again if either would be exceeded.";
        }

        internal static string BuildCurrentTimePrompt(HostContextSnapshotDto snapshot)
        {
            var localTime = !string.IsNullOrWhiteSpace(snapshot?.CurrentLocalTime)
                ? snapshot.CurrentLocalTime
                : CurrentTimeFormatter.FormatLine(DateTimeOffset.Now);
            return "Current local time: " + localTime + ". Use this value when interpreting relative dates such as today, yesterday, this week, or a deadline.";
        }

        internal static string BuildModelHostContextPrompt(AgentBrokerTurnRequest request, HostContextSnapshotDto snapshot)
        {
            if (request == null || !HasSnapshotData(snapshot))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Equals(request.InteractiveMode, "agent", StringComparison.OrdinalIgnoreCase)
                ? "Current Office host context follows. Prefer using it when answering:"
                : "Current Office host context follows. Prefer using it when relevant to the user's question:");
            builder.AppendLine("- Application: " + Safe(snapshot.HostApplicationName));
            builder.AppendLine("- Document: " + Safe(snapshot.DocumentName));
            builder.AppendLine("- Worksheet: " + Safe(snapshot.WorksheetName));
            builder.AppendLine("- Selection: " + Safe(snapshot.SelectionAddress));
            builder.AppendLine("- Summary: " + Safe(snapshot.Summary));
            var workbookMapSummary = !string.IsNullOrWhiteSpace(snapshot.WorkbookMapSummary)
                ? snapshot.WorkbookMapSummary
                : BuildWorkbookMapSummary(snapshot.WorkbookMap);
            workbookMapSummary = LimitWorkbookMapSummary(workbookMapSummary);
            if (!string.IsNullOrWhiteSpace(workbookMapSummary))
            {
                builder.AppendLine("- Workbook worksheet map:");
                builder.AppendLine(workbookMapSummary.Trim());
            }

            if (!string.IsNullOrWhiteSpace(snapshot.SelectionPreview))
            {
                builder.AppendLine("- Selection preview:");
                builder.AppendLine(snapshot.SelectionPreview.Trim());
            }

            return builder.ToString().Trim();
        }

        internal static void AppendSystemPromptSection(StringBuilder builder, string content)
        {
            if (builder == null || string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(content.Trim());
        }

        // =====================================================================
        // 用户消息构造 + 输入分片反序列化
        // =====================================================================

        internal static ModelClientMessage BuildUserModelMessage(
            string content,
            IList<AgentBrokerInputContentPart> contentParts)
        {
            var message = new ModelClientMessage
            {
                Role = "user",
                Content = content ?? string.Empty
            };

            if (contentParts == null || contentParts.Count == 0)
            {
                return message;
            }

            message.ContentParts = contentParts
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Type))
                .Select(p => new ModelClientMessageContentPart
                {
                    Type = p.Type ?? string.Empty,
                    Text = p.Text ?? string.Empty,
                    MediaType = p.MediaType ?? string.Empty,
                    Url = p.Url ?? string.Empty,
                    FileName = p.FileName ?? string.Empty,
                    FileContentBase64 = p.FileContentBase64 ?? string.Empty
                })
                .ToList();

            return message;
        }

        internal static IList<AgentBrokerInputContentPart> DeserializeBrokerInputContentParts(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<AgentBrokerInputContentPart>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<AgentBrokerInputContentPart>>(json)
                    ?? new List<AgentBrokerInputContentPart>();
            }
            catch
            {
                return new List<AgentBrokerInputContentPart>();
            }
        }

        // =====================================================================
        // 共享纯函数辅助
        // =====================================================================

        /// <summary>
        /// 判断快照是否包含可用信息（任一关键字段非空白）。
        /// 与 <see cref="BrokerTurnExecutor"/> 内的同名 private 拷贝保持等价。
        /// </summary>
        internal static bool HasSnapshotData(HostContextSnapshotDto snapshot)
        {
            return snapshot != null
                && (!string.IsNullOrWhiteSpace(snapshot.Summary)
                    || !string.IsNullOrWhiteSpace(snapshot.HostApplicationName)
                    || !string.IsNullOrWhiteSpace(snapshot.DocumentName)
                    || !string.IsNullOrWhiteSpace(snapshot.SelectionAddress)
                    || !string.IsNullOrWhiteSpace(snapshot.WorkbookMapSummary)
                    || HasWorkbookMapData(snapshot.WorkbookMap));
        }

        internal static string BuildWorkbookMapSummary(WorkbookMapSnapshotDto workbookMap)
        {
            if (!HasWorkbookMapData(workbookMap))
            {
                return string.Empty;
            }

            const int maximumSummaryCharacters = 4000;
            var displayedWorksheetCount = workbookMap.Worksheets == null
                ? 0
                : workbookMap.Worksheets.Count;
            var worksheetCount = workbookMap.TotalWorksheetCount > 0
                ? workbookMap.TotalWorksheetCount
                : displayedWorksheetCount;
            var builder = new StringBuilder();
            builder.Append(worksheetCount).Append(" worksheets");
            if (workbookMap.IsTruncated)
            {
                builder.Append(" (showing the first ").Append(displayedWorksheetCount).Append(")");
            }

            builder.AppendLine(":");
            if (workbookMap.WorksheetGroups != null && workbookMap.WorksheetGroups.Count > 0)
            {
                if (!AppendWorkbookMapSummaryLine(
                    builder,
                    "Candidate groups with similar headers (column-name set similarity >= 80%; order/type compatibility is not implied):",
                    maximumSummaryCharacters))
                {
                    return builder.ToString().TrimEnd();
                }

                foreach (var group in workbookMap.WorksheetGroups.Take(8))
                {
                    if (group == null)
                    {
                        continue;
                    }

                    var groupLine = "- " + (group.GroupId ?? string.Empty) + " "
                        + string.Join(", ", group.WorksheetNames ?? new List<string>())
                        + "; minimum similarity "
                        + group.MinimumHeaderSimilarity.ToString("P0", CultureInfo.InvariantCulture);
                    if (!AppendWorkbookMapSummaryLine(builder, groupLine, maximumSummaryCharacters))
                    {
                        return builder.ToString().TrimEnd();
                    }
                }
            }

            if (workbookMap.SharedColumnCandidates != null
                && workbookMap.SharedColumnCandidates.Count > 0)
            {
                if (!AppendWorkbookMapSummaryLine(
                    builder,
                    "Candidate columns shared across worksheets (uniqueness is unverified; profile before use):",
                    maximumSummaryCharacters))
                {
                    return builder.ToString().TrimEnd();
                }

                foreach (var candidate in workbookMap.SharedColumnCandidates.Take(8))
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    var candidateLine = "- " + (candidate.ColumnName ?? string.Empty) + ": "
                        + string.Join(", ", candidate.WorksheetNames ?? new List<string>());
                    if (!AppendWorkbookMapSummaryLine(builder, candidateLine, maximumSummaryCharacters))
                    {
                        return builder.ToString().TrimEnd();
                    }
                }
            }

            if (workbookMap.Worksheets != null)
            {
                foreach (var worksheet in workbookMap.Worksheets)
                {
                    if (worksheet == null)
                    {
                        continue;
                    }

                    var line = "- " + (worksheet.Name ?? string.Empty)
                        + (worksheet.IsVisible ? " [visible]" : " [hidden]");
                    if (!string.IsNullOrWhiteSpace(worksheet.CodeName))
                    {
                        line += " (CodeName=" + worksheet.CodeName + ")";
                    }

                    line += "; UsedRange="
                        + (string.IsNullOrWhiteSpace(worksheet.UsedRangeAddress)
                            ? "-"
                            : worksheet.UsedRangeAddress)
                        + " (" + worksheet.UsedRangeRows + " rows x " + worksheet.UsedRangeColumns + " columns)"
                        + "; role=" + (worksheet.RoleGuess ?? "other")
                        + "; formulas=" + (worksheet.HasFormulas ? "yes" : "no");
                    if (!string.IsNullOrWhiteSpace(worksheet.HeaderSignature))
                    {
                        line += "; headers=" + worksheet.HeaderSignature;
                    }

                    if (!string.IsNullOrWhiteSpace(worksheet.InspectionError))
                    {
                        line += "; inspection error=" + worksheet.InspectionError;
                    }

                    if (!AppendWorkbookMapSummaryLine(builder, line, maximumSummaryCharacters))
                    {
                        break;
                    }
                }
            }

            return builder.ToString().TrimEnd();
        }

        internal static string LimitWorkbookMapSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)
                || WorkbookMapTokenEstimator.EstimateTokens(summary) <= MaximumWorkbookMapSummaryTokens)
            {
                return string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim();
            }

            var builder = new StringBuilder();
            var lines = summary.Trim().Split(
                new[] { "\r\n", "\n" },
                StringSplitOptions.None);
            foreach (var line in lines)
            {
                var candidate = builder.Length == 0
                    ? line
                    : builder.ToString() + Environment.NewLine + line;
                var candidateWithNotice = candidate + Environment.NewLine + WorkbookMapTruncationNotice;
                if (WorkbookMapTokenEstimator.EstimateTokens(candidate) > MaximumWorkbookMapSummaryTokens
                    || WorkbookMapTokenEstimator.EstimateTokens(candidateWithNotice)
                        > MaximumWorkbookMapSummaryTokens)
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine(WorkbookMapTruncationNotice);
                    }
                    else
                    {
                        builder.Append(WorkbookMapTruncationNotice);
                    }

                    return builder.ToString().TrimEnd();
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(line);
            }

            return builder.ToString().TrimEnd();
        }

        private static bool HasWorkbookMapData(WorkbookMapSnapshotDto workbookMap)
        {
            return workbookMap != null
                && (workbookMap.TotalWorksheetCount > 0
                    || (workbookMap.Worksheets != null && workbookMap.Worksheets.Count > 0));
        }

        private static bool AppendWorkbookMapSummaryLine(
            StringBuilder builder,
            string line,
            int maximumCharacters)
        {
            if (builder.Length + line.Length + Environment.NewLine.Length > maximumCharacters)
            {
                const string truncatedLine = "- Workbook map summary truncated; call excel_list_worksheets for the complete structure.";
                if (builder.Length + truncatedLine.Length + Environment.NewLine.Length <= maximumCharacters)
                {
                    builder.AppendLine(truncatedLine);
                }

                return false;
            }

            builder.AppendLine(line);
            return true;
        }

        internal static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }
}
