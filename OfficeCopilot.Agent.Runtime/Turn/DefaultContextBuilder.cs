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
            var fallbackLanguage = string.Equals(
                preferredCulture.Name,
                SupportedLanguages.SimplifiedChinese,
                StringComparison.OrdinalIgnoreCase)
                ? "Simplified Chinese"
                : "English";

            return "Follow any explicit response-language request from the user. Otherwise, reply in the same language as the user's latest message. "
                + "If its language is unclear or mixed, reply in " + fallbackLanguage + ".";
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
            "- 工作簿地图摘要已按估算 token 预算截断；需要更多结构时调用 excel_list_worksheets。";
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

            AppendSystemPromptSection(
                builder,
                ResponseLanguagePolicy.BuildInstruction(request == null ? null : request.UiCulture));

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
                return "你是一个 Office 智能体。请使用中文回复，遵守标准 Markdown 语法。若调用工具能帮助得到更准确的结论，应优先调用工具，不要凭空猜测。";
            }

            return "你是一个 Office 助手。请使用中文回复，遵守标准 Markdown 语法。回答应简洁、准确、结构清晰。你可以调用只读工具查看文档真实内容，但不能修改文档。";
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

            return "工具选择约束：任何对当前文档（Excel / Word / PowerPoint）的读取与修改，必须使用官方提供的原生工具"
                + "（`excel_*` / `word_*` / `ppt_*` / `host.*`）。这些工具带有备份、确认与回滚保障。\n"
                + "- 严禁使用 `runtime.powershell`（或其它代码执行工具）去连接 Office COM 对象，"
                + "例如 `Marshal.GetActiveObject`、`New-Object -ComObject Excel.Application` 等。"
                + "这会绕过全部安全门禁，且不可撤销。\n"
                + "- `runtime.powershell` 仅用于与当前文档无关的本地文件系统 / 进程任务。\n"
                + "- 若当前工具集中找不到能完成任务的原生工具，不要自行降级到代码执行；"
                + "请先调用 `runtime.list_tools` 确认，仍无可用工具时向用户说明缺口并给出手动步骤。";
        }

        internal static string BuildReadOnlyModeConstraintPrompt(AgentBrokerTurnRequest request)
        {
            if (request == null || string.Equals(request.InteractiveMode, "agent", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return "本轮为 Ask 模式：你只能调用**只读**工具来查看文档真实内容，需要依赖当前数据才能判断时请优先读取，不要凭空猜测。"
                + "任何修改文档的工具均不可用，也不要声称你已经修改了文档。"
                + "若用户需要实际修改，请给出具体步骤或可直接使用的代码，并提示用户切换到 Agent 模式执行。";
        }

        internal static string BuildExcelExecutionModePrompt(HostContextSnapshotDto snapshot)
        {
            if (snapshot == null
                || string.IsNullOrWhiteSpace(snapshot.HostApplicationName)
                || snapshot.HostApplicationName.IndexOf("Excel", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return string.Empty;
            }

            return "Excel 执行方式约束：只要本轮需要写入工作簿，且用户没有通过已继承的执行方式指定范围，"
                + "必须先调用 `runtime.declare_execution_mode`，再调用任何写入工具。纯读取/只在对话中回答时不调用。"
                + "明确且唯一的方式提交一个候选作为预告；目标、静态值/公式或是否改原表存在实质歧义时提交多个候选，由运行时询问用户。"
                + "候选必须写明真实目标工作表/区域、数据源和结果说明；候选只是模型预告，不是宿主实测的 write effect，也不是用户选择。只有写入工具触发的 HostBridge 预检结果才是实际级别；未收到预检结果不得声称已经判定实际级别。"
                + "如果用户要求在选择/批准前先看到 HostBridge 实测的 write effect，单候选预告不能作为最终结论，也不得据此停止；必须调用相应的目标写入工具触发宿主预检。预检结果返回并进入确认流程前，不得声称已知实际级别；若实测结果不满足用户条件，必须取消且不得提交写入。"
                + "写入工具回执中的 `writeEffect.actualWriteEffect` 才是该次调用的宿主实测级别；`MaxWriteEffect` 是声明上限，不能当成实测值。写入工具回执 `success=true` 表示写入已执行，绝不能再报告“未写入”或“已取消”；回执缺少实测字段时，必须明确说无法核实，不得猜测。"
                + "若实际 write effect 不满足用户明确给出的批准条件，必须遵守用户的停止条件；若需要用户在继续、取消或更换目标间选择，必须调用 `runtime.ask_user_question` 显示交互卡并等待回答，"
                + "或在 `runtime.declare_execution_mode` 的 `clarifications` 中提交选择以显示同一交互卡。绝不能只在最终文本中列出选项或以文字提问。"
                + "用户选择后，目标和写入影响是本轮硬上限，超出时必须停止并重新询问。";
        }

        internal static string BuildCurrentTimePrompt(HostContextSnapshotDto snapshot)
        {
            var localTime = !string.IsNullOrWhiteSpace(snapshot?.CurrentLocalTime)
                ? snapshot.CurrentLocalTime
                : CurrentTimeFormatter.FormatLine(DateTimeOffset.Now);
            return "当前本地时间：" + localTime + "。涉及“今天”“昨天”“本周”“截止日期”等相对时间时以此为准。";
        }

        internal static string BuildModelHostContextPrompt(AgentBrokerTurnRequest request, HostContextSnapshotDto snapshot)
        {
            if (request == null || !HasSnapshotData(snapshot))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Equals(request.InteractiveMode, "agent", StringComparison.OrdinalIgnoreCase)
                ? "当前 Office 宿主上下文如下，请优先结合这些信息回答："
                : "当前 Office 宿主上下文如下。如果与用户问题相关，请优先结合这些信息回答：");
            builder.AppendLine("- 应用：" + Safe(snapshot.HostApplicationName));
            builder.AppendLine("- 文档：" + Safe(snapshot.DocumentName));
            builder.AppendLine("- 工作表：" + Safe(snapshot.WorksheetName));
            builder.AppendLine("- 选区：" + Safe(snapshot.SelectionAddress));
            builder.AppendLine("- 摘要：" + Safe(snapshot.Summary));
            var workbookMapSummary = !string.IsNullOrWhiteSpace(snapshot.WorkbookMapSummary)
                ? snapshot.WorkbookMapSummary
                : BuildWorkbookMapSummary(snapshot.WorkbookMap);
            workbookMapSummary = LimitWorkbookMapSummary(workbookMapSummary);
            if (!string.IsNullOrWhiteSpace(workbookMapSummary))
            {
                builder.AppendLine("- 工作簿工作表地图：");
                builder.AppendLine(workbookMapSummary.Trim());
            }

            if (!string.IsNullOrWhiteSpace(snapshot.SelectionPreview))
            {
                builder.AppendLine("- 选区内容预览：");
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
            builder.Append("工作表 ").Append(worksheetCount).Append(" 张");
            if (workbookMap.IsTruncated)
            {
                builder.Append("（仅展示前 ").Append(displayedWorksheetCount).Append(" 张）");
            }

            builder.AppendLine("：");
            if (workbookMap.WorksheetGroups != null && workbookMap.WorksheetGroups.Count > 0)
            {
                if (!AppendWorkbookMapSummaryLine(
                    builder,
                    "表头相似候选组（列名集合相似度≥80%；不代表列顺序或类型兼容）：",
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
                        + string.Join("、", group.WorksheetNames ?? new List<string>())
                        + "；最低相似度 "
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
                    "跨表共享列候选（唯一性未验证，使用前需画像确认）：",
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

                    var candidateLine = "- " + (candidate.ColumnName ?? string.Empty) + "："
                        + string.Join("、", candidate.WorksheetNames ?? new List<string>());
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
                        + (worksheet.IsVisible ? " [可见]" : " [隐藏]");
                    if (!string.IsNullOrWhiteSpace(worksheet.CodeName))
                    {
                        line += " (CodeName=" + worksheet.CodeName + ")";
                    }

                    line += "; UsedRange="
                        + (string.IsNullOrWhiteSpace(worksheet.UsedRangeAddress)
                            ? "-"
                            : worksheet.UsedRangeAddress)
                        + " (" + worksheet.UsedRangeRows + "行×" + worksheet.UsedRangeColumns + "列)"
                        + "; 角色=" + (worksheet.RoleGuess ?? "other")
                        + "; 公式=" + (worksheet.HasFormulas ? "有" : "无");
                    if (!string.IsNullOrWhiteSpace(worksheet.HeaderSignature))
                    {
                        line += "; 表头=" + worksheet.HeaderSignature;
                    }

                    if (!string.IsNullOrWhiteSpace(worksheet.InspectionError))
                    {
                        line += "; 结构读取失败=" + worksheet.InspectionError;
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
                const string truncatedLine = "- 工作簿地图摘要已截断；请调用 excel_list_worksheets 查看完整结构。";
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
