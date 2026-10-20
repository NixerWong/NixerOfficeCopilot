using OfficeCopilot.HostBridge.Contracts;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Mcp;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime.PowerShell;
using OfficeCopilot.Agent.Runtime.Verification;
using OfficeCopilot.Memory;
using OfficeCopilot.Skills;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    internal sealed class DefaultToolDispatcher
    {
        internal const string EmptyObjectSchemaJson = "{\"type\":\"object\",\"properties\":{}}";
        internal const string GenericObjectSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":true}";
        internal const string RuntimeListToolsName = "runtime.list_tools";
        internal const string RuntimeGetHostContextName = "runtime.get_host_context";
        internal const string RuntimeMcpStatusName = "runtime.mcp_status";
        internal const string RuntimeInvokeSkillName = SkillTool.ToolName;
        internal const string RuntimeEnterPlanModeName = EnterPlanModeTool.ToolName;
        internal const string RuntimeExitPlanModeName = ExitPlanModeTool.ToolName;
        internal const string RuntimeAskUserQuestionName = "runtime.ask_user_question";
        internal const string RuntimeDeclareExecutionModeName = "runtime.declare_execution_mode";
        internal const string RuntimeTodoWriteName = TodoWriteTool.ToolName;
        internal const string RuntimeBriefName = BriefTool.ToolName;
        internal const string RuntimePushNotificationName = PushNotificationTool.ToolName;
        internal const string RuntimeMonitorName = MonitorTool.ToolName;
        internal const string RuntimeCreateSkillName = SkillCreatorTool.ToolName;
        internal const string RuntimePowerShellName = PowerShellTool.ToolName;
        internal const string RuntimeRecordMemoryName = MemoryWriteTool.ToolName;
        internal const string RuntimeCalculateName = CalculateTool.ToolName;
        internal const string RuntimeCompactName = "runtime.compact";
        internal const string RuntimeGetCurrentTimeName = CurrentTimeTool.ToolName;
        internal const string BackupPathMetadataKey = "backup-path";

        private readonly IHostToolInvoker _hostToolInvoker;
        private readonly IMcpRegistry _mcpRegistry;
        private readonly SkillTool _skillTool;
        private readonly EnterPlanModeTool _enterPlanModeTool;
        private readonly ExitPlanModeTool _exitPlanModeTool;
        private readonly IPermissionGate _permissionGate;
        private readonly AgentTool _agentTool;
        private readonly IUserInteractionHandler _userInteractionHandler;
        private readonly TodoWriteTool _todoWriteTool;
        private readonly BriefTool _briefTool;
        private readonly PushNotificationTool _pushNotificationTool;
        private readonly MonitorTool _monitorTool;
        private readonly SkillCreatorTool _skillCreatorTool;
        private readonly PowerShellTool _powerShellTool;
        private readonly MemoryWriteTool _memoryWriteTool;

        internal AgentModelOptions CurrentModelOptions { get; set; }
        internal Action<string> CurrentOnThinkingDelta { get; set; }

        /// <summary>
        /// 当前生效的技能计划，用于在派发时执行技能工具白名单。
        /// 由 <see cref="BrokerTurnExecutor"/> 在每次工具调用前设置；为 null 表示不限制。
        /// </summary>
        internal Planning.RuntimeSkillPlan CurrentSkillPlan { get; set; }

        /// <summary>
        /// 工具信任模式是否为「始终放行」（已由 <see cref="BrokerTurnExecutor"/> 结合交互模式解析）。
        /// 生效时跳过所有权限确认；Plan 模式的阻止仍然保留，见 <see cref="IsTrustAlwaysAllowActive"/>。
        /// </summary>
        internal bool CurrentAlwaysAllowTools { get; set; }

        /// <summary>
        /// 工具状态/通知消息通道（Brief、PushNotification、Monitor 等）。
        /// 与 <see cref="CurrentOnThinkingDelta"/> 分离，避免工具消息被误显示为"思考中"。
        /// 由 <see cref="BrokerTurnExecutor"/> 在 turn 开始时设置。
        /// </summary>
        internal Action<string> CurrentOnStatus { get; set; }

        /// <summary>
        /// Phase 2: 当前正在处理的会话 ID，供 <see cref="TodoWriteTool"/> 等需要会话上下文的工具使用。
        /// 由 <see cref="BrokerTurnExecutor"/> 在 turn 开始时设置。
        /// </summary>
        internal string CurrentConversationId { get; set; }

        internal string CurrentTurnMessageId { get; set; }

        internal bool CurrentWriteEffectBaselineReady { get; set; }

        internal ISet<string> CurrentConfirmedAppendScopes { get; set; }

        internal TurnExecutionContract CurrentExecutionContract { get; set; }

        internal IDictionary<string, string> CurrentConversationMetadata { get; set; }

        internal EvidenceLedger CurrentEvidenceLedger { get; set; }

        /// <summary>
        /// Phase 1: 用户交互事件发射器。当工具需要用户交互时，通过此回调将请求发送到 Panel；
        /// 响应等待项会在发出事件前注册，以避免快速响应到达时尚无 pending 项。
        /// 由 <see cref="BrokerTurnExecutor"/> 在 turn 开始时设置。
        /// </summary>
        internal Func<AgentBrokerToolUserInteractionRequest, CancellationToken, Task> CurrentUserInteractionEmitter { get; set; }

        internal Func<TurnExecutionContract, bool, CancellationToken, Task> CurrentExecutionModeEmitter { get; set; }

        internal Func<AgentBrokerEvent, CancellationToken, Task> CurrentWriteEffectEventEmitter { get; set; }

        /// <summary>
        /// Phase 6: 上下文压缩回调。由 BrokerTurnExecutor 设置，调用时执行 AutoCompact + MicroCompact。
        /// </summary>
        internal Func<CancellationToken, Task<string>> CompactCallback { get; set; }

        public DefaultToolDispatcher(IHostToolInvoker hostToolInvoker)
            : this(hostToolInvoker, null, null, null, null, null, null) { }

        public DefaultToolDispatcher(
            IHostToolInvoker hostToolInvoker,
            IMcpRegistry mcpRegistry,
            ISkillRegistry skillRegistry = null,
            IPermissionGate permissionGate = null,
            IModelClient modelClient = null,
            IUserInteractionHandler userInteractionHandler = null,
            IMemoryStore memoryStore = null,
            string hostApplicationName = null)
        {
            _hostToolInvoker = hostToolInvoker ?? throw new ArgumentNullException(nameof(hostToolInvoker));
            _mcpRegistry = mcpRegistry;
            _skillTool = skillRegistry != null ? new SkillTool(skillRegistry) : null;
            _permissionGate = permissionGate ?? new DefaultPermissionGate();
            _enterPlanModeTool = new EnterPlanModeTool(_permissionGate);
            _exitPlanModeTool = new ExitPlanModeTool(_permissionGate);
            _agentTool = modelClient != null ? new AgentTool(modelClient) : null;
            _userInteractionHandler = userInteractionHandler ?? new NullUserInteractionHandler();
            _todoWriteTool = new TodoWriteTool();
            _briefTool = new BriefTool(statusJson => { if (CurrentOnStatus != null) CurrentOnStatus(statusJson); });
            _pushNotificationTool = new PushNotificationTool(statusJson => { if (CurrentOnStatus != null) CurrentOnStatus(statusJson); });
            _monitorTool = new MonitorTool(statusJson => { if (CurrentOnStatus != null) CurrentOnStatus(statusJson); });
            _skillCreatorTool = new SkillCreatorTool(skillRegistry, hostApplicationName);
            _powerShellTool = new PowerShellTool(new SimplePowerShellRunner());
            _memoryWriteTool = memoryStore != null ? new MemoryWriteTool(memoryStore) : null;
        }

        public static Dictionary<string, HostBridgeToolDescriptor> BuildToolCatalog(HostBridgeCapabilities capabilities)
        {
            var toolCatalog = new Dictionary<string, HostBridgeToolDescriptor>(StringComparer.OrdinalIgnoreCase);

            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeListToolsName, "List all registered runtime tools.", true, false, false, "low", EmptyObjectSchemaJson));
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeGetHostContextName, "Get current host context snapshot.", true, false, false, "low", EmptyObjectSchemaJson));
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(
                RuntimeCalculateName,
                "Evaluate deterministic decimal arithmetic for derived figures. Every operand must already appear in user/tool evidence, except integer constants from 0 to 100. Supports + - * / parentheses, sum(...), avg(...), and round(x, n) with midpoint rounding away from zero.",
                true,
                false,
                false,
                "low",
                "{\"type\":\"object\",\"properties\":{\"expression\":{\"type\":\"string\",\"maxLength\":4096}},\"required\":[\"expression\"],\"additionalProperties\":false}"));
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeGetCurrentTimeName, "Get the current local date, time, weekday and UTC offset of the user's machine. No args.", true, false, false, "low", EmptyObjectSchemaJson));
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeCompactName, "Manually trigger context compaction. Prunes old messages and truncates large tool results to free up context window space. Use when context usage is high (>70%).", true, false, false, "low", EmptyObjectSchemaJson));
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeMcpStatusName, "Show MCP server connection status and discovered tools.", true, false, false, "low", EmptyObjectSchemaJson));
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeInvokeSkillName, "Invoke a registered Skill. Args: skillName (string, required).", true, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"skillName\":{\"type\":\"string\",\"description\":\"Skill name\"},\"args\":{\"type\":\"string\",\"description\":\"Optional context\"}},\"required\":[\"skillName\"]}"));
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeEnterPlanModeName, "Enter plan mode - only read-only tools are allowed.", true, false, false, "low", "{\"type\":\"object\",\"properties\":{}}"));
            // Phase 3: ExitPlanMode requires user interaction — Panel shows plan approval dialog
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeExitPlanModeName, "Exit plan mode and get user approval for the plan. Args: plan (string, optional), allowedPrompts (array, optional).", true, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"plan\":{\"type\":\"string\",\"description\":\"Plan text\"},\"allowedPrompts\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"tool\":{\"type\":\"string\"},\"prompt\":{\"type\":\"string\"}}}}}}",
                requiresUserInteraction: true));
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(AgentTool.ToolName, "Delegate a subtask to a sub-agent with restricted tools. Args: description (string), prompt (string), allowedTools (string[]).", true, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"description\":{\"type\":\"string\"},\"prompt\":{\"type\":\"string\"},\"allowedTools\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"description\",\"prompt\"]}"));
            // Phase 0: AskUserQuestion — requires user interaction (Broker pauses, Panel shows UI, Panel responds)
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeAskUserQuestionName,
                "Ask the user multiple-choice questions to gather information, clarify ambiguity, or make decisions. Args: questions (array of {question, header, options[{label, description, previewImageId}]}, 1-4 items).",
                true, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"questions\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"question\":{\"type\":\"string\"},\"header\":{\"type\":\"string\"},\"options\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"label\":{\"type\":\"string\"},\"description\":{\"type\":\"string\"},\"preview\":{\"type\":\"string\",\"description\":\"Plain-text preview shown beside the options (single-select only).\"},\"previewImageId\":{\"type\":\"string\",\"pattern\":\"^[0-9]{8}-[0-9]{6}-[a-z0-9]{6}/[a-z][a-z0-9-]{0,23}(?:/[a-zA-Z0-9._~-]{0,64})?$\",\"description\":\"Local preview reference deckId/candidateName/imageVersion returned by ppt_style_preview; the Panel resolves it to an image URL.\"},\"previewImage\":{\"type\":\"string\",\"description\":\"Legacy image URL returned by a preview tool (https://nixer-decks.local/...). Shown inside the option.\"}}},\"minItems\":2,\"maxItems\":4},\"multiSelect\":{\"type\":\"boolean\",\"default\":false}},\"required\":[\"question\",\"header\",\"options\"]},\"minItems\":1,\"maxItems\":4}},\"required\":[\"questions\"]}",
                requiresUserInteraction: true));
            if (capabilities != null && capabilities.SupportsWriteEffectPreflight)
            {
                AddTool(toolCatalog, RuntimeToolDescriptors.Create(
                    RuntimeDeclareExecutionModeName,
                    "Declare the intended Excel result mode before any workbook write beyond registered scratch output. A single candidate is only a model preview; it does not run HostBridge preflight and is not a measured write effect. If the user asks to see the actual effect before approving, call the corresponding write tool to trigger host preflight; do not claim a measured result before it returns. Submit one candidate as a visible preview, or multiple candidates when the user should choose. Include up to two clarifications when data meaning or calculation scope needs the user's answer; they will be shown in the same interaction as the execution-mode choice. Modes: result_only, new_sheet_static, new_sheet_formula, modify_in_place. Include targets, sources and a concise summary.",
                    true,
                    false,
                    false,
                    "low",
                    "{\"type\":\"object\",\"properties\":{\"candidates\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":4,\"items\":{\"type\":\"object\",\"properties\":{\"mode\":{\"type\":\"string\",\"enum\":[\"result_only\",\"new_sheet_static\",\"new_sheet_formula\",\"modify_in_place\"]},\"targets\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"sources\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"summary\":{\"type\":\"string\"}},\"required\":[\"mode\",\"summary\"],\"additionalProperties\":false}},\"clarifications\":{\"type\":\"array\",\"maxItems\":2,\"items\":{\"type\":\"object\",\"properties\":{\"question\":{\"type\":\"string\",\"maxLength\":240},\"header\":{\"type\":\"string\",\"maxLength\":24},\"options\":{\"type\":\"array\",\"minItems\":2,\"maxItems\":4,\"items\":{\"type\":\"object\",\"properties\":{\"label\":{\"type\":\"string\",\"maxLength\":80},\"description\":{\"type\":\"string\",\"maxLength\":240}},\"required\":[\"label\"],\"additionalProperties\":false}}},\"required\":[\"question\",\"header\",\"options\"],\"additionalProperties\":false}},\"reason\":{\"type\":\"string\"}},\"required\":[\"candidates\"],\"additionalProperties\":false}"));
            }
            // Phase 2: TodoWrite — manage session task checklist
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeTodoWriteName,
                "Manage a structured task list for your current coding session. Use to track progress, organize complex tasks, and demonstrate thoroughness. Args: todos (array of {content, status}), where status is one of: pending, in_progress, completed.",
                false, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"todos\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"content\":{\"type\":\"string\",\"description\":\"Task description\"},\"status\":{\"type\":\"string\",\"enum\":[\"pending\",\"in_progress\",\"completed\"],\"description\":\"Task status\"}},\"required\":[\"content\",\"status\"]}}},\"required\":[\"todos\"]}"));
            // Phase 4: BriefTool — send a formatted message to the user
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeBriefName,
                "Send a formatted message to the user. Use for proactive notifications (task completion, blocker alerts) or structured replies. Args: message (string, required), attachments (string[], optional), status ('normal'|'proactive').",
                true, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"message\":{\"type\":\"string\",\"description\":\"Markdown message\"},\"attachments\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"status\":{\"type\":\"string\",\"enum\":[\"normal\",\"proactive\"]}},\"required\":[\"message\"]}"));
            // Phase 5: PushNotification + Monitor — background notifications
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimePushNotificationName,
                "Send an OS-level notification to the user. Use for task completion alerts, blocker notifications, or unsolicited status updates when the user is away. Args: message (string), notificationType ('info'|'warning'|'error').",
                true, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"message\":{\"type\":\"string\"},\"notificationType\":{\"type\":\"string\",\"enum\":[\"info\",\"warning\",\"error\"]}},\"required\":[\"message\"]}"));
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeMonitorName,
                "Report progress for a long-running background task. Use to keep the user informed of progress. Args: task (string), progress (integer 0-100), status ('running'|'completed'|'failed').",
                true, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"task\":{\"type\":\"string\"},\"progress\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":100},\"status\":{\"type\":\"string\",\"enum\":[\"running\",\"completed\",\"failed\"]}},\"required\":[\"task\"]}"));
            // Phase P1: SkillCreator — create/update SKILL.md files
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeCreateSkillName,
                "Create or update a skill file at %APPDATA%/NixerOfficeCopilot/skills/<name>/SKILL.md. Use after interviewing the user with AskUserQuestion to gather skill requirements. Args: name, description, body (markdown instructions), category, allowedTools, whenToUse, argumentHint, arguments, disableModelInvocation, userInvocable, version, context, effort, requiresConfirmation, maxToolRoundCount.",
                false, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"description\":{\"type\":\"string\"},\"body\":{\"type\":\"string\"},\"category\":{\"type\":\"string\"},\"allowedTools\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"whenToUse\":{\"type\":\"string\"},\"argumentHint\":{\"type\":\"string\"},\"arguments\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"disableModelInvocation\":{\"type\":\"boolean\"},\"userInvocable\":{\"type\":\"boolean\"},\"version\":{\"type\":\"string\"},\"context\":{\"type\":\"string\",\"enum\":[\"inline\",\"fork\"]},\"effort\":{\"type\":\"string\"},\"requiresConfirmation\":{\"type\":\"boolean\"},\"maxToolRoundCount\":{\"type\":\"integer\"}},\"required\":[\"name\",\"description\",\"body\"]}"));
            // Phase P4: PowerShellTool — execute PowerShell commands
            // critical（而非 high）：可执行任意代码，必须受技能的代码执行授权约束。
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimePowerShellName,
                PowerShellTool.GetDescription(),
                false, true, false, OfficeCopilot.Agent.Abstractions.ToolRiskLevels.Critical,
                PowerShellTool.GetSchemaJson()));

            // Record memory tool — user says "记住xx" → write to persistent memory
            AddTool(toolCatalog, RuntimeToolDescriptors.Create(RuntimeRecordMemoryName,
                "Record a memory to persistent storage. Use when the user says '记住', '记录下来', '写入记忆', or asks to remember something. Args: content (string, required), tags (string[], optional), scope ('user'|'workspace'|'session', optional), type (string, optional).",
                false, false, false, "low",
                "{\"type\":\"object\",\"properties\":{\"content\":{\"type\":\"string\",\"description\":\"Memory content to record\"},\"tags\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Tags for categorization\"},\"scope\":{\"type\":\"string\",\"enum\":[\"user\",\"workspace\",\"session\"],\"description\":\"Memory scope\"},\"type\":{\"type\":\"string\",\"description\":\"Memory type: user, feedback, project, reference\"}},\"required\":[\"content\"]}"));

            if (capabilities != null)
            {
                if (capabilities.Tools != null && capabilities.Tools.Count > 0)
                {
                    foreach (var hostTool in capabilities.Tools)
                    {
                        if (hostTool == null || string.IsNullOrWhiteSpace(hostTool.Name)) continue;
                        var descriptor = RuntimeToolDescriptors.Create(hostTool.Name.Trim(),
                            string.IsNullOrWhiteSpace(hostTool.Description) ? "Host bridge tool." : hostTool.Description,
                            hostTool.IsReadOnly, hostTool.RequiresConfirmation, hostTool.RequiresBackup,
                            string.IsNullOrWhiteSpace(hostTool.RiskLevel) ? (hostTool.IsReadOnly ? "low" : "high") : hostTool.RiskLevel,
                            string.IsNullOrWhiteSpace(hostTool.InputSchemaJson) ? GenericObjectSchemaJson : hostTool.InputSchemaJson,
                            hostTool.RequiresUserInteraction,
                            hostTool.MaxWriteEffect);
                        descriptor.RequiresWriteEffectPreflight = hostTool.RequiresWriteEffectPreflight;
                        AddTool(toolCatalog, descriptor);
                    }
                }
                else if (capabilities.ToolNames != null)
                {
                    foreach (var toolName in capabilities.ToolNames)
                    {
                        if (string.IsNullOrWhiteSpace(toolName)) continue;
                        AddTool(toolCatalog, RuntimeToolDescriptors.Create(toolName.Trim(), "Host bridge tool.", false, true, capabilities.CanCreateDocumentBackup, "high", GenericObjectSchemaJson));
                    }
                }
                if (capabilities.CanInsertText)
                    AddTool(toolCatalog, RuntimeToolDescriptors.Create(HostBridgeOperationNames.InsertText, "Insert text at current selection.", false, true, capabilities.CanCreateDocumentBackup, "high", GenericObjectSchemaJson));
                if (capabilities.CanExecuteCode)
                    AddTool(toolCatalog, RuntimeToolDescriptors.Create(HostBridgeOperationNames.ExecuteCode, "Execute code script in host.", false, true, capabilities.CanCreateDocumentBackup, "critical", GenericObjectSchemaJson));
            }

            return toolCatalog;
        }

        internal static void AddTool(IDictionary<string, HostBridgeToolDescriptor> catalog, HostBridgeToolDescriptor def)
        {
            if (catalog == null || def == null || string.IsNullOrWhiteSpace(def.Name)) return;
            if (!catalog.ContainsKey(def.Name)) catalog.Add(def.Name, def);
        }

        public static List<ModelClientTool> BuildModelTools(IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog, Planning.RuntimeSkillPlan skillPlan)
        {
            var tools = new List<ModelClientTool>();
            if (toolCatalog == null) return tools;
            foreach (var toolName in GetOrderedModelToolNames(toolCatalog, skillPlan))
            {
                if (!toolCatalog.TryGetValue(toolName, out var tool) || tool == null) continue;
                if (!ShouldExposeToolToModel(tool, skillPlan) || !IsToolPermittedBySkillPlan(tool, skillPlan)) continue;
                tools.Add(new ModelClientTool { Name = tool.Name ?? string.Empty, Description = tool.Description ?? string.Empty,
                    InputSchemaJson = NormalizeToolSchemaJson(tool.InputSchemaJson, EmptyObjectSchemaJson) });
            }
            return tools;
        }

        /// <summary>
        /// 取工具目录的只读子集（Ask 模式本轮可用的全部工具）。
        /// 同时剔除 PlanMode 管理工具：<c>exit_plan_mode</c> 会把权限门禁切到 AcceptEdits，
        /// 留着它等于给 Ask 模式开了一条自助提权的后门。
        /// </summary>
        public static IReadOnlyDictionary<string, HostBridgeToolDescriptor> FilterToReadOnlyTools(
            IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog)
        {
            var filtered = new Dictionary<string, HostBridgeToolDescriptor>(StringComparer.OrdinalIgnoreCase);
            if (toolCatalog == null) return filtered;

            foreach (var pair in toolCatalog)
            {
                if (pair.Value == null || !pair.Value.IsReadOnly) continue;
                if (IsPermissionEscalationTool(pair.Key)) continue;
                filtered[pair.Key] = pair.Value;
            }

            return filtered;
        }

        private static bool IsPermissionEscalationTool(string toolName)
            => string.Equals(toolName, RuntimeEnterPlanModeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, RuntimeExitPlanModeName, StringComparison.OrdinalIgnoreCase);

        /// <summary>按本轮交互模式设置权限门禁。</summary>
        internal void SetPermissionMode(PermissionMode mode)
        {
            _permissionGate.SetMode(mode);
        }

        /// <summary>
        /// 「始终放行」在本次调用中是否生效。Plan 模式（Ask 模式与 EnterPlanMode）始终保留只读阻止，不受放行影响。
        /// </summary>
        private bool IsTrustAlwaysAllowActive()
            => CurrentAlwaysAllowTools
                && (_permissionGate == null || _permissionGate.Mode != PermissionMode.Plan);

        internal static IEnumerable<string> GetOrderedModelToolNames(IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog, Planning.RuntimeSkillPlan skillPlan)
        {
            var orderedNames = new List<string>();
            if (toolCatalog == null || toolCatalog.Count == 0) return orderedNames;
            if (skillPlan != null)
            {
                var currentStep = skillPlan.GetCurrentExecutionStep();
                if (currentStep != null) { AddToolNames(orderedNames, currentStep.PreferredToolNames, toolCatalog); AddToolNames(orderedNames, currentStep.AllowedToolNames, toolCatalog); AddToolNames(orderedNames, currentStep.VerificationToolNames, toolCatalog); }
                if (skillPlan.ExecutionPlanSteps != null && skillPlan.ExecutionPlanSteps.Count > 0)
                {
                    AddToolNames(orderedNames, skillPlan.ExecutionPlanSteps.SelectMany(s => s?.PreferredToolNames ?? Enumerable.Empty<string>()), toolCatalog);
                    AddToolNames(orderedNames, skillPlan.ExecutionPlanSteps.SelectMany(s => s?.AllowedToolNames ?? Enumerable.Empty<string>()), toolCatalog);
                    AddToolNames(orderedNames, skillPlan.ExecutionPlanSteps.SelectMany(s => s?.VerificationToolNames ?? Enumerable.Empty<string>()), toolCatalog);
                }
                AddToolNames(orderedNames, skillPlan.PreferredToolNames, toolCatalog); AddToolNames(orderedNames, skillPlan.AllowedToolNames, toolCatalog); AddToolNames(orderedNames, skillPlan.VerificationToolNames, toolCatalog);
            }
            foreach (var toolName in toolCatalog.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                if (!orderedNames.Any(n => string.Equals(n, toolName, StringComparison.OrdinalIgnoreCase))) orderedNames.Add(toolName);
            return orderedNames;
        }

        internal static void AddToolNames(IList<string> orderedNames, IEnumerable<string> toolNames, IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog)
        {
            if (orderedNames == null || toolNames == null || toolCatalog == null) return;
            foreach (var tn in toolNames)
                if (!string.IsNullOrWhiteSpace(tn) && toolCatalog.ContainsKey(tn) && !orderedNames.Any(n => string.Equals(n, tn, StringComparison.OrdinalIgnoreCase)))
                    orderedNames.Add(tn);
        }

        internal static bool ShouldExposeToolToModel(HostBridgeToolDescriptor tool, Planning.RuntimeSkillPlan skillPlan)
            => SkillPlanSupport.ShouldExposeToolToModel(tool, skillPlan);
        internal static bool IsToolPermittedBySkillPlan(HostBridgeToolDescriptor tool, Planning.RuntimeSkillPlan skillPlan)
            => SkillPlanSupport.IsToolPermittedBySkillPlan(tool, skillPlan);

        public async Task MergeMcpToolsIntoCatalogAsync(Dictionary<string, HostBridgeToolDescriptor> toolCatalog, CancellationToken ct)
        {
            if (_mcpRegistry == null || toolCatalog == null) return;
            try
            {
                var mcpTools = await _mcpRegistry.ListAllToolsAsync(ct).ConfigureAwait(false);
                if (mcpTools == null) return;
                foreach (var t in mcpTools)
                {
                    if (string.IsNullOrWhiteSpace(t.Name)) continue;
                    AddTool(toolCatalog, CreateMcpToolDescriptor(t));
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 服务器声明 readOnlyHint=true 的工具视为只读：不弹确认、Ask 模式可用、风险 low。
        /// 其余工具按写入类处理：必须确认，风险等级只按工具名推导（保守取值）。备份默认关闭。
        /// readOnlyHint 来自服务器自报，可能不准确，这是已接受的风险。
        /// </summary>
        internal static HostBridgeToolDescriptor CreateMcpToolDescriptor(McpToolDescriptor t)
        {
            var description = t.Description ?? $"[MCP] {t.Name}";
            var schema = t.InputSchemaJson ?? GenericObjectSchemaJson;
            if (t.IsReadOnly)
            {
                return RuntimeToolDescriptors.Create(t.Name, description, isReadOnly: true, requiresConfirmation: false, requiresBackup: false, "low", schema);
            }
            return RuntimeToolDescriptors.Create(t.Name, description, isReadOnly: false, requiresConfirmation: true, requiresBackup: false, InferMcpToolRiskLevel(t.Name), schema);
        }

        /// <summary>
        /// 写入类 MCP 工具的风险等级只按工具名推导。
        /// </summary>
        internal static string InferMcpToolRiskLevel(string toolName)
            => ToolSafetyPolicy.InferRiskLevel(new ToolDefinition { Name = toolName ?? string.Empty, IsReadOnly = false });

        public async Task<ToolExecutionOutcome> DispatchToolCallAsync(
            string toolCallId, string toolName, string argumentsJson,
            HostContextSnapshotDto hostSnapshot,
            IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog,
            IEnumerable<KeyValuePair<string, string>> requestMetadata,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(toolName, RuntimeListToolsName, StringComparison.OrdinalIgnoreCase))
                return ExecuteListTools(toolCallId, toolName, toolCatalog, CurrentSkillPlan);
            if (string.Equals(toolName, RuntimeGetHostContextName, StringComparison.OrdinalIgnoreCase))
                return ExecuteGetHostContext(toolCallId, toolName, hostSnapshot);
            if (string.Equals(toolName, RuntimeCalculateName, StringComparison.OrdinalIgnoreCase))
                return new CalculateTool().Execute(
                    toolCallId,
                    toolName,
                    argumentsJson,
                    CurrentEvidenceLedger);
            if (string.Equals(toolName, RuntimeDeclareExecutionModeName, StringComparison.OrdinalIgnoreCase))
                return await DeclareExecutionModeAsync(
                    toolCallId,
                    toolName,
                    argumentsJson,
                    cancellationToken).ConfigureAwait(false);
            if (string.Equals(toolName, RuntimeGetCurrentTimeName, StringComparison.OrdinalIgnoreCase))
                return CurrentTimeTool.Execute(toolCallId, toolName);
            if (string.Equals(toolName, RuntimeMcpStatusName, StringComparison.OrdinalIgnoreCase))
                return ExecuteMcpStatus(toolCallId, toolName);
            if (string.Equals(toolName, RuntimeCompactName, StringComparison.OrdinalIgnoreCase))
                return await ExecuteCompactAsync(toolCallId, toolName, cancellationToken).ConfigureAwait(false);
            if (string.Equals(toolName, RuntimeInvokeSkillName, StringComparison.OrdinalIgnoreCase) && _skillTool != null)
                return _skillTool.Execute(toolCallId, toolName, argumentsJson);
            if (string.Equals(toolName, RuntimeEnterPlanModeName, StringComparison.OrdinalIgnoreCase))
                return _enterPlanModeTool.Execute(toolCallId, toolName);
            // Phase 3: ExitPlanMode is a user-interaction tool — handled by the block below
            if (string.Equals(toolName, RuntimeExitPlanModeName, StringComparison.OrdinalIgnoreCase))
                return await HandleUserInteractionToolAsync(toolCallId, toolName, argumentsJson, cancellationToken).ConfigureAwait(false);
            if (string.Equals(toolName, AgentTool.ToolName, StringComparison.OrdinalIgnoreCase) && _agentTool != null)
                return await _agentTool.ExecuteAsync(toolCallId, toolName, argumentsJson, CurrentModelOptions, toolCatalog, hostSnapshot, CurrentOnThinkingDelta, cancellationToken).ConfigureAwait(false);

            // Phase 0: Handle user-interaction tools (e.g. AskUserQuestion)
            if (toolCatalog != null && toolCatalog.TryGetValue(toolName, out var uiToolDef) && uiToolDef != null && uiToolDef.RequiresUserInteraction)
                return await HandleUserInteractionToolAsync(toolCallId, toolName, argumentsJson, cancellationToken).ConfigureAwait(false);

            // Phase 2: Handle TodoWrite
            if (string.Equals(toolName, RuntimeTodoWriteName, StringComparison.OrdinalIgnoreCase))
                return _todoWriteTool.Execute(toolCallId, toolName, argumentsJson, CurrentConversationId ?? string.Empty,
                    statusJson => { if (CurrentOnStatus != null) CurrentOnStatus(statusJson); });

            // Phase 4: Handle BriefTool — send formatted message to Panel
            if (string.Equals(toolName, RuntimeBriefName, StringComparison.OrdinalIgnoreCase))
                return _briefTool.Execute(toolCallId, toolName, argumentsJson);

            // Phase 5: Handle PushNotification + Monitor
            if (string.Equals(toolName, RuntimePushNotificationName, StringComparison.OrdinalIgnoreCase))
                return _pushNotificationTool.Execute(toolCallId, toolName, argumentsJson);
            if (string.Equals(toolName, RuntimeMonitorName, StringComparison.OrdinalIgnoreCase))
                return _monitorTool.Execute(toolCallId, toolName, argumentsJson);

            // Phase P1: Handle SkillCreator — write SKILL.md file
            if (string.Equals(toolName, RuntimeCreateSkillName, StringComparison.OrdinalIgnoreCase))
                return _skillCreatorTool.Execute(toolCallId, toolName, argumentsJson);

            // Phase M1: Handle RecordMemory — write to persistent memory
            if (string.Equals(toolName, RuntimeRecordMemoryName, StringComparison.OrdinalIgnoreCase) && _memoryWriteTool != null)
                return await _memoryWriteTool.ExecuteAsync(toolCallId, toolName, argumentsJson, cancellationToken).ConfigureAwait(false);

            // Phase P4: PowerShell permission gate — must run BEFORE the generic gate
            // because we need per-command read-only classification, not per-tool.
            if (string.Equals(toolName, RuntimePowerShellName, StringComparison.OrdinalIgnoreCase))
            {
                return await HandlePowerShellWithPermissionGateAsync(
                    toolCallId, toolName, argumentsJson, toolCatalog, cancellationToken).ConfigureAwait(false);
            }

            var confirmationGranted = false;
            var confirmationRequested = false;

            // 技能工具白名单的运行时拦截：模型仍可能凭历史上下文调用未授权的工具，
            // 这里返回一条可读的失败结果，让模型改用白名单内的工具。
            var skillPlanViolation = SkillPlanSupport.GetSkillPlanViolationMessage(toolName, toolCatalog, CurrentSkillPlan);
            if (!string.IsNullOrWhiteSpace(skillPlanViolation))
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    Content = skillPlanViolation,
                    ErrorCode = "skill.tool_not_allowed",
                    ErrorMessage = skillPlanViolation
                };
            }

            if (string.Equals(toolName, DeckBuildToolName, StringComparison.OrdinalIgnoreCase)
                && IsDeckBuildTargetIncomplete(argumentsJson))
            {
                var deckTarget = await ResolveDeckBuildTargetAsync(
                    toolCallId,
                    toolName,
                    argumentsJson,
                    cancellationToken).ConfigureAwait(false);
                if (deckTarget.Failure != null)
                {
                    return deckTarget.Failure;
                }

                argumentsJson = deckTarget.ResolvedArgumentsJson;
            }

            HostBridgeToolDescriptor hostToolDefinition = null;
            if (toolCatalog != null)
            {
                toolCatalog.TryGetValue(toolName, out hostToolDefinition);
            }

            PreflightToolCallResult writeEffectPreflight = null;
            if (hostToolDefinition != null && hostToolDefinition.RequiresWriteEffectPreflight)
            {
                if (!CurrentWriteEffectBaselineReady)
                {
                    return CreateWriteEffectPreflightFailure(
                        toolCallId,
                        toolName,
                        "excel.write_effect_baseline_unavailable",
                        "本轮工作簿基线不可用；可以继续读取，但本次写入已阻止。");
                }

                var preflightInvoker = _hostToolInvoker as IHostToolPreflightInvoker;
                if (preflightInvoker == null)
                {
                    return CreateWriteEffectPreflightFailure(
                        toolCallId,
                        toolName,
                        "excel.write_effect_preflight_unavailable",
                        "当前工具调用通道不支持写入影响预检；本次写入已阻止。");
                }

                writeEffectPreflight = await preflightInvoker.PreflightToolCallAsync(
                    new PreflightToolCallRequest
                    {
                        ConversationId = CurrentConversationId ?? string.Empty,
                        MessageId = CurrentTurnMessageId ?? string.Empty,
                        ToolCallId = toolCallId ?? string.Empty,
                        ToolName = toolName ?? string.Empty,
                        ArgumentsJson = NormalizeArgumentsJson(argumentsJson)
                    },
                    cancellationToken).ConfigureAwait(false);

                if (writeEffectPreflight == null || !writeEffectPreflight.Success)
                {
                    return CreateWriteEffectPreflightFailure(
                        toolCallId,
                        toolName,
                        writeEffectPreflight == null
                            ? "excel.write_effect_preflight_failed"
                            : writeEffectPreflight.ErrorCode,
                        writeEffectPreflight == null
                            ? "宿主没有返回写入影响预检结果；本次写入已阻止。"
                            : writeEffectPreflight.ErrorMessage);
                }

                if (!ToolWriteEffectLevels.IsValid(writeEffectPreflight.Effect)
                    || !ToolWriteEffectLevels.IsWithinLimit(
                        writeEffectPreflight.Effect,
                        hostToolDefinition.MaxWriteEffect)
                    || string.IsNullOrWhiteSpace(writeEffectPreflight.PreflightToken))
                {
                    return CreateWriteEffectPreflightFailure(
                        toolCallId,
                        toolName,
                        "excel.write_effect_limit_exceeded",
                        "预检结果无效或超出工具写入上限；本次写入已阻止。");
                }
            }

            var exceedsExecutionModePreview = false;
            if (writeEffectPreflight != null)
            {
                var executionModeFailure = ValidateExecutionMode(
                    toolCallId,
                    toolName,
                    writeEffectPreflight,
                    out exceedsExecutionModePreview);
                if (CurrentWriteEffectEventEmitter != null)
                {
                    var contract = CurrentExecutionContract;
                    var effectExceedsDeclaredMaximum = contract != null
                        && contract.IsDeclared
                        && !ToolWriteEffectLevels.IsWithinLimit(
                            writeEffectPreflight.Effect,
                            contract.MaxWriteEffect);
                    var targetOutsideSelectedContract = contract != null
                        && contract.IsUserSelected
                        && !contract.AllowsTarget(
                            writeEffectPreflight.TargetWorksheetName,
                            writeEffectPreflight.TargetAddress);
                    await CurrentWriteEffectEventEmitter(
                        new AgentBrokerEvent
                        {
                            EventType = AgentBrokerEventTypes.WriteEffectEvaluated,
                            ConversationId = CurrentConversationId ?? string.Empty,
                            MessageId = CurrentTurnMessageId ?? string.Empty,
                            ToolCallId = toolCallId ?? string.Empty,
                            ToolName = toolName ?? string.Empty,
                            ExpectedWriteEffect = contract?.MaxWriteEffect ?? string.Empty,
                            ActualWriteEffect = writeEffectPreflight.Effect ?? string.Empty,
                            ExecutionModeDeclared = contract != null && contract.IsDeclared,
                            ExecutionModeUserSelected = contract != null && contract.IsUserSelected,
                            EffectExceedsDeclaredMaximum = effectExceedsDeclaredMaximum,
                            TargetOutsideSelectedContract = targetOutsideSelectedContract,
                            ExceededExecutionModePreview = exceedsExecutionModePreview
                        },
                        cancellationToken).ConfigureAwait(false);
                }

                if (executionModeFailure != null)
                {
                    return AddWriteEffectOutcome(
                        executionModeFailure,
                        writeEffectPreflight,
                        false,
                        false);
                }
            }

            // 「始终放行」生效时，宿主侧也不得再因范围越界等原因强制弹窗（见 DefaultHostBridgeRequestHandler）。
            var trustAlwaysAllow = false;
            if (_permissionGate != null)
            {
                trustAlwaysAllow = IsTrustAlwaysAllowActive();
                var toolDef = hostToolDefinition;
                var isReadOnly = toolDef?.IsReadOnly ?? false;
                if (!_permissionGate.CanExecute(toolName, isReadOnly))
                {
                    var reason = _permissionGate.GetBlockReason(toolName, isReadOnly);
                    return new ToolExecutionOutcome { ToolCallId = toolCallId ?? string.Empty, ToolName = toolName ?? string.Empty, Success = false, ErrorCode = "permission.blocked_by_plan_mode", ErrorMessage = reason };
                }

                // AcceptEdits 下 high / critical 级工具（如 powershell、MCP 高危工具）仍需用户明确确认。
                var isElevatedRisk = toolDef != null
                    && (string.Equals(toolDef.RiskLevel, "high", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(toolDef.RiskLevel, "critical", StringComparison.OrdinalIgnoreCase));
                var modeSkipsConfirmation = trustAlwaysAllow
                    || _permissionGate.Mode == PermissionMode.BypassPermissions
                    || (_permissionGate.Mode == PermissionMode.AcceptEdits && !isElevatedRisk);

                // 参数级豁免：只写 _wip_ 中间表的 VBA 调用不触碰既有数据。
                // 必须与宿主侧 StagedWriteConfirmationPolicy 用同一份判定，否则 Broker 先弹窗、
                // 宿主的豁免形同虚设（把该策略上移到 HostBridge.Contracts 正是为此）。
                var stagedWriteExempt = StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite(toolName, argumentsJson);

                var shouldConfirm = toolDef != null
                    && toolDef.RequiresConfirmation
                    && !isReadOnly
                    && !modeSkipsConfirmation
                    && !stagedWriteExempt;
                var appendScope = string.Empty;
                if (writeEffectPreflight != null)
                {
                    appendScope = BuildAppendConfirmationScope(writeEffectPreflight, toolName, argumentsJson);
                    var effect = writeEffectPreflight.Effect;
                    if (_permissionGate.Mode == PermissionMode.BypassPermissions
                        || string.Equals(effect, ToolWriteEffectLevels.None, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(effect, ToolWriteEffectLevels.Scratch, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldConfirm = false;
                    }
                    else if (string.Equals(effect, ToolWriteEffectLevels.Append, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldConfirm = _permissionGate.Mode == PermissionMode.Default
                            && toolDef != null
                            && toolDef.RequiresConfirmation
                            && (CurrentConfirmedAppendScopes == null
                                || !CurrentConfirmedAppendScopes.Contains(appendScope));
                    }
                    else if (string.Equals(effect, ToolWriteEffectLevels.Modify, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldConfirm = _permissionGate.Mode != PermissionMode.BypassPermissions;
                    }

                    if (exceedsExecutionModePreview)
                    {
                        shouldConfirm = true;
                    }
                }

                // 「始终放行」：写入影响级别与执行模式预览的强制确认一并取消。
                if (trustAlwaysAllow)
                {
                    shouldConfirm = false;
                }

                if (shouldConfirm)
                {
                    confirmationRequested = true;
                    var operationSummary = writeEffectPreflight == null
                        ? null
                        : BuildWriteEffectConfirmationSummary(
                            writeEffectPreflight,
                            exceedsExecutionModePreview);
                    if (exceedsExecutionModePreview && CurrentExecutionModeEmitter != null)
                    {
                        await CurrentExecutionModeEmitter(
                            CurrentExecutionContract,
                            true,
                            cancellationToken).ConfigureAwait(false);
                    }

                    var confirmOutcome = await RequestToolConfirmationAsync(
                        toolCallId,
                        toolName,
                        toolDef,
                        argumentsJson,
                        cancellationToken,
                        null,
                        operationSummary).ConfigureAwait(false);
                    if (!confirmOutcome.Success)
                    {
                        return writeEffectPreflight == null
                            ? confirmOutcome
                            : AddWriteEffectOutcome(
                                confirmOutcome,
                                writeEffectPreflight,
                                confirmationRequested,
                                false);
                    }

                    if (writeEffectPreflight != null
                        && string.Equals(
                            writeEffectPreflight.Effect,
                            ToolWriteEffectLevels.Append,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (CurrentConfirmedAppendScopes == null)
                        {
                            CurrentConfirmedAppendScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        }

                        CurrentConfirmedAppendScopes.Add(appendScope);
                    }
                }

                // 本次调用的确认已在 Broker 侧完成（或因权限模式而豁免），
                // 通知宿主不要再弹一次确认框。参见 DefaultHostBridgeRequestHandler。
                confirmationGranted = true;
            }

            if (_mcpRegistry != null)
            {
                if (_mcpRegistry.TryRoute(toolName, out var mcpClient, out var realToolName) && mcpClient != null)
                    return await DispatchToMcpAsync(toolCallId, toolName, realToolName, argumentsJson, mcpClient, cancellationToken).ConfigureAwait(false);
            }

            HostBridgeToolDescriptor schemaToolDefinition;
            if (toolCatalog != null && toolCatalog.TryGetValue(toolName, out schemaToolDefinition) && schemaToolDefinition != null)
            {
                var schemaJson = string.IsNullOrWhiteSpace(schemaToolDefinition.InputSchemaJson)
                    ? EmptyObjectSchemaJson
                    : schemaToolDefinition.InputSchemaJson;
                var validation = ToolArgumentValidator.Validate(schemaJson, NormalizeArgumentsJson(argumentsJson));
                if (!validation.SchemaValid)
                {
                    RuntimeTrace.Write($"[ToolArgumentValidator] Skipped '{toolName}' because its schema is invalid or unsupported: {validation.ErrorMessage}");
                }
                else if (!validation.IsValid)
                {
                    RuntimeTrace.Write($"[ToolArgumentValidator] '{toolName}' arguments do not match the declared schema: {validation.ErrorMessage}; log-only mode, continuing.");
                }
            }

            var hostRequestMetadata = AppendConversationMetadata(
                requestMetadata,
                CurrentConversationId);
            if (writeEffectPreflight != null)
            {
                hostRequestMetadata = AppendWriteEffectPreflightMetadata(
                    hostRequestMetadata,
                    writeEffectPreflight);
            }

            var hostOutcome = await InvokeHostToolAsync(toolCallId, toolName, argumentsJson,
                AppendTrustModeMetadata(
                    confirmationGranted ? AppendConfirmationGrantedMetadata(hostRequestMetadata) : hostRequestMetadata,
                    trustAlwaysAllow),
                cancellationToken).ConfigureAwait(false);
            if (writeEffectPreflight != null)
            {
                hostOutcome = AddWriteEffectOutcome(
                    hostOutcome,
                    writeEffectPreflight,
                    confirmationRequested,
                    confirmationRequested);
            }

            return hostToolDefinition != null && hostToolDefinition.RequiresWriteEffectPreflight
                ? AddWriteGateRecoveryPath(hostOutcome)
                : hostOutcome;
        }

        private string BuildWriteEffectConfirmationSummary(
            PreflightToolCallResult preflight,
            bool exceedsPreview)
        {
            var target = string.IsNullOrWhiteSpace(preflight.TargetWorksheetName)
                ? preflight.TargetAddress ?? string.Empty
                : preflight.TargetWorksheetName
                    + (string.IsNullOrWhiteSpace(preflight.TargetAddress)
                        ? string.Empty
                        : "!" + preflight.TargetAddress);
            var operation = BuildExecutionModeConfirmationSummary(
                preflight.OperationSummary,
                exceedsPreview);
            return "宿主实测 write effect：`" + (preflight.Effect ?? string.Empty)
                + "`\n目标：" + target
                + (string.IsNullOrWhiteSpace(operation)
                    ? string.Empty
                    : "\n\n本次操作：\n" + operation);
        }

        private static ToolExecutionOutcome AddWriteEffectOutcome(
            ToolExecutionOutcome outcome,
            PreflightToolCallResult preflight,
            bool confirmationRequested,
            bool confirmationApproved)
        {
            if (outcome == null || preflight == null)
            {
                return outcome;
            }

            var confirmationStatus = !confirmationRequested
                ? "not_requested"
                : confirmationApproved
                    ? "approved"
                    : string.Equals(outcome.ErrorCode, "permission.denied_by_user", StringComparison.OrdinalIgnoreCase)
                        ? "denied"
                        : "failed";
            var measuredEffect = "宿主实测 write effect：`" + (preflight.Effect ?? string.Empty)
                + "`；目标：" + (string.IsNullOrWhiteSpace(preflight.TargetWorksheetName)
                    ? preflight.TargetAddress ?? string.Empty
                    : preflight.TargetWorksheetName
                        + (string.IsNullOrWhiteSpace(preflight.TargetAddress)
                            ? string.Empty
                            : "!" + preflight.TargetAddress))
                + "；确认状态：" + confirmationStatus + "。";

            outcome.Content = string.IsNullOrWhiteSpace(outcome.Content)
                ? measuredEffect
                : measuredEffect + Environment.NewLine + outcome.Content;
            if (!outcome.Success && !string.IsNullOrWhiteSpace(outcome.ErrorMessage))
            {
                outcome.ErrorMessage = measuredEffect + Environment.NewLine + outcome.ErrorMessage;
            }

            outcome.StructuredContentJson = MergeWriteEffectResultJson(
                outcome.StructuredContentJson,
                preflight,
                confirmationStatus,
                outcome.Success);
            return outcome;
        }

        private static string MergeWriteEffectResultJson(
            string structuredContentJson,
            PreflightToolCallResult preflight,
            string confirmationStatus,
            bool writeSucceeded)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    if (!string.IsNullOrWhiteSpace(structuredContentJson))
                    {
                        try
                        {
                            using (var document = JsonDocument.Parse(structuredContentJson))
                            {
                                if (document.RootElement.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (var property in document.RootElement.EnumerateObject())
                                    {
                                        if (!string.Equals(property.Name, "writeEffect", StringComparison.OrdinalIgnoreCase))
                                        {
                                            property.WriteTo(writer);
                                        }
                                    }
                                }
                                else
                                {
                                    writer.WritePropertyName("hostResult");
                                    document.RootElement.WriteTo(writer);
                                }
                            }
                        }
                        catch (JsonException)
                        {
                            writer.WriteString("hostResultRaw", structuredContentJson);
                        }
                    }

                    writer.WritePropertyName("writeEffect");
                    writer.WriteStartObject();
                    writer.WriteString("actualWriteEffect", preflight.Effect ?? string.Empty);
                    writer.WriteBoolean("writeSucceeded", writeSucceeded);
                    writer.WriteString("targetWorkbookFullName", preflight.TargetWorkbookFullName ?? string.Empty);
                    writer.WriteString("targetWorksheetName", preflight.TargetWorksheetName ?? string.Empty);
                    writer.WriteString("targetAddress", preflight.TargetAddress ?? string.Empty);
                    writer.WriteString("operationSummary", preflight.OperationSummary ?? string.Empty);
                    writer.WriteString("confirmationStatus", confirmationStatus ?? string.Empty);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static ToolExecutionOutcome CreateWriteEffectPreflightFailure(
            string toolCallId,
            string toolName,
            string errorCode,
            string errorMessage)
        {
            var normalizedErrorCode = string.IsNullOrWhiteSpace(errorCode)
                ? "excel.write_effect_preflight_failed"
                : errorCode;
            var normalizedErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? "写入影响预检未能完成；本次写入已阻止。"
                : errorMessage;
            normalizedErrorMessage = AppendWriteGateRecoveryPath(
                normalizedErrorCode,
                normalizedErrorMessage);

            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = false,
                Content = normalizedErrorMessage,
                ErrorCode = normalizedErrorCode,
                ErrorMessage = normalizedErrorMessage
            };
        }

        private static ToolExecutionOutcome AddWriteGateRecoveryPath(ToolExecutionOutcome outcome)
        {
            if (outcome == null
                || outcome.Success
                || !IsWriteGateErrorCode(outcome.ErrorCode))
            {
                return outcome;
            }

            var message = string.IsNullOrWhiteSpace(outcome.ErrorMessage)
                ? outcome.Content ?? string.Empty
                : outcome.ErrorMessage;
            outcome.ErrorMessage = AppendWriteGateRecoveryPath(outcome.ErrorCode, message);
            outcome.Content = string.IsNullOrWhiteSpace(outcome.Content)
                ? outcome.ErrorMessage
                : AppendWriteGateRecoveryPath(outcome.ErrorCode, outcome.Content);
            return outcome;
        }

        private static string AppendWriteGateRecoveryPath(string errorCode, string errorMessage)
        {
            var message = errorMessage ?? string.Empty;
            if (message.IndexOf("[代码]", StringComparison.OrdinalIgnoreCase) >= 0
                && message.IndexOf("[原因]", StringComparison.OrdinalIgnoreCase) >= 0
                && message.IndexOf("[约束]", StringComparison.OrdinalIgnoreCase) >= 0
                && message.IndexOf("[出路]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return message;
            }

            var existingRecoveryIndex = message.IndexOf("[出路]", StringComparison.OrdinalIgnoreCase);
            if (existingRecoveryIndex >= 0)
            {
                message = message.Substring(0, existingRecoveryIndex).TrimEnd();
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                message = "写入操作被安全门禁拦截。";
            }

            string recoveryPath;
            if (string.Equals(errorCode, "EXECUTION_MODE_UNDECLARED", StringComparison.OrdinalIgnoreCase))
            {
                recoveryPath = "先调用 runtime.declare_execution_mode 声明本轮允许的写入方式；"
                    + "如用户意图或目标不明确，再调用 runtime.ask_user_question 并等待回答。";
            }
            else if (string.Equals(errorCode, "EXECUTION_MODE_EXCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                recoveryPath = "改用本轮用户选择允许的目标；若用户确需扩大范围，先调用 runtime.ask_user_question 获取确认，"
                    + "再重新声明执行方式。";
            }
            else if (string.Equals(errorCode, "WRITE_EFFECT_ESCALATED", StringComparison.OrdinalIgnoreCase))
            {
                recoveryPath = "重新核对工作簿与目标并重新预检；若用户选择的范围不再适用，先调用 "
                    + "runtime.ask_user_question 获取确认，再声明新的执行方式。";
            }
            else
            {
                recoveryPath = "不要重试当前写入；先继续只读工作并报告预检阻止原因，宿主预检恢复后再重新声明执行方式。"
                    + "若需要扩大用户已选范围，先调用 runtime.ask_user_question 获取确认。";
            }

            return "[代码] " + errorCode
                + Environment.NewLine + "[原因] " + message
                + Environment.NewLine + "[约束] " + GetWriteGateConstraint(errorCode)
                + Environment.NewLine + "[出路] " + recoveryPath;
        }

        private static string GetWriteGateConstraint(string errorCode)
        {
            if (string.Equals(errorCode, "EXECUTION_MODE_UNDECLARED", StringComparison.OrdinalIgnoreCase))
            {
                return "工作簿写入前必须完成本轮执行方式声明。";
            }

            if (string.Equals(errorCode, "EXECUTION_MODE_EXCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                return "用户选择的目标与最大写入影响级别是硬上限，不能由模型自行放宽。";
            }

            if (string.Equals(errorCode, "WRITE_EFFECT_ESCALATED", StringComparison.OrdinalIgnoreCase))
            {
                return "执行前复核与预检不一致时必须阻止写入，不得绕过重新预检。";
            }

            return "必须先取得有效的宿主写入预检；预检失败时不得提交写入。";
        }

        private static bool IsWriteGateErrorCode(string errorCode)
        {
            return !string.IsNullOrWhiteSpace(errorCode)
                && (errorCode.StartsWith("excel.write_effect.", StringComparison.OrdinalIgnoreCase)
                    || errorCode.StartsWith("EXECUTION_MODE_", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(errorCode, "WRITE_EFFECT_ESCALATED", StringComparison.OrdinalIgnoreCase));
        }

        private ToolExecutionOutcome ValidateExecutionMode(
            string toolCallId,
            string toolName,
            PreflightToolCallResult preflight,
            out bool exceedsPreview)
        {
            exceedsPreview = false;
            var contract = CurrentExecutionContract;
            var atOrBelowScratch = ToolWriteEffectLevels.IsWithinLimit(
                preflight.Effect,
                ToolWriteEffectLevels.Scratch);
            if (atOrBelowScratch)
            {
                if (contract != null
                    && contract.IsUserSelected
                    && !contract.AllowsTarget(preflight.TargetWorksheetName, preflight.TargetAddress))
                {
                    return CreateWriteEffectPreflightFailure(
                        toolCallId,
                        toolName,
                        "EXECUTION_MODE_EXCEEDED",
                        "本轮用户选择了“" + TurnExecutionContract.ModeLabel(contract.Mode)
                            + "”，但本次写入目标不在用户选择范围内；已阻止。");
                }

                return null;
            }

            if (contract == null || !contract.IsDeclared)
            {
                return CreateWriteEffectPreflightFailure(
                    toolCallId,
                    toolName,
                    "EXECUTION_MODE_UNDECLARED",
                    "本轮尚未声明执行方式；请先调用 runtime.declare_execution_mode，再执行会写入工作簿的操作。");
            }

            var targetMatches = contract.AllowsTarget(
                preflight.TargetWorksheetName,
                preflight.TargetAddress);
            var effectWithinMode = ToolWriteEffectLevels.IsWithinLimit(
                preflight.Effect,
                contract.MaxWriteEffect);

            if (contract.IsUserSelected && (!effectWithinMode || !targetMatches))
            {
                return CreateWriteEffectPreflightFailure(
                    toolCallId,
                    toolName,
                    "EXECUTION_MODE_EXCEEDED",
                    "本轮用户选择了“" + TurnExecutionContract.ModeLabel(contract.Mode)
                        + "”，本次实际写入为 " + preflight.Effect + "，目标为 "
                        + (string.IsNullOrWhiteSpace(preflight.TargetWorksheetName)
                            ? "未能确定的工作表"
                            : preflight.TargetWorksheetName + "!" + (preflight.TargetAddress ?? string.Empty))
                        + "；超出用户选择范围，已阻止。若确需更改，请重新询问用户并声明新的执行方式。");
            }

            exceedsPreview = !contract.IsUserSelected && (!effectWithinMode || !targetMatches);
            return null;
        }

        private string BuildExecutionModeConfirmationSummary(
            string operationSummary,
            bool exceedsPreview)
        {
            if (!exceedsPreview || CurrentExecutionContract == null)
            {
                return operationSummary ?? string.Empty;
            }

            return "超出预告：本轮预告为“"
                + TurnExecutionContract.ModeLabel(CurrentExecutionContract.Mode)
                + "”；本次实际操作："
                + (operationSummary ?? string.Empty);
        }

        private string BuildAppendConfirmationScope(
            PreflightToolCallResult preflight,
            string toolName,
            string argumentsJson)
        {
            var workbook = (preflight.TargetWorkbookFullName ?? string.Empty).Trim();
            var worksheet = (preflight.TargetWorksheetName ?? string.Empty).Trim();
            var address = (preflight.TargetAddress ?? string.Empty).Trim();
            if (workbook.Length == 0 && worksheet.Length == 0 && address.Length == 0)
            {
                address = NormalizeArgumentsJson(argumentsJson);
            }

            return (CurrentTurnMessageId ?? string.Empty)
                + "|" + workbook
                + "|" + worksheet
                + "|" + address
                + "|" + (toolName ?? string.Empty);
        }

        private static IEnumerable<KeyValuePair<string, string>> AppendWriteEffectPreflightMetadata(
            IEnumerable<KeyValuePair<string, string>> requestMetadata,
            PreflightToolCallResult preflight)
        {
            var metadata = new List<KeyValuePair<string, string>>();
            if (requestMetadata != null)
            {
                metadata.AddRange(requestMetadata);
            }

            metadata.Add(new KeyValuePair<string, string>(
                HostBridgeMetadataKeys.WriteEffectPreflightToken,
                preflight.PreflightToken ?? string.Empty));
            metadata.Add(new KeyValuePair<string, string>(
                HostBridgeMetadataKeys.WriteEffectApprovedLevel,
                preflight.Effect ?? string.Empty));
            return metadata;
        }

        private static IEnumerable<KeyValuePair<string, string>> AppendConversationMetadata(
            IEnumerable<KeyValuePair<string, string>> requestMetadata,
            string conversationId)
        {
            var metadata = new List<KeyValuePair<string, string>>();
            if (requestMetadata != null)
            {
                metadata.AddRange(requestMetadata.Where(pair =>
                    !string.Equals(
                        pair.Key,
                        ToolMetadataKeys.ConversationId,
                        StringComparison.OrdinalIgnoreCase)));
            }

            metadata.Add(new KeyValuePair<string, string>(
                ToolMetadataKeys.ConversationId,
                conversationId ?? string.Empty));
            return metadata;
        }

        private static IEnumerable<KeyValuePair<string, string>> AppendConfirmationGrantedMetadata(
            IEnumerable<KeyValuePair<string, string>> requestMetadata)
        {
            var metadata = new List<KeyValuePair<string, string>>();
            if (requestMetadata != null) metadata.AddRange(requestMetadata);
            metadata.Add(new KeyValuePair<string, string>(
                HostBridge.Contracts.HostBridgeMetadataKeys.ConfirmationGranted,
                HostBridge.Contracts.HostBridgeMetadataKeys.TrueValue));
            return metadata;
        }

        /// <summary>
        /// 把本次调用生效的信任模式写入宿主请求，覆盖请求上游携带的同名键，避免宿主读到未经本次判定的值。
        /// </summary>
        private static IEnumerable<KeyValuePair<string, string>> AppendTrustModeMetadata(
            IEnumerable<KeyValuePair<string, string>> requestMetadata,
            bool alwaysAllow)
        {
            var metadata = new List<KeyValuePair<string, string>>();
            if (requestMetadata != null)
            {
                metadata.AddRange(requestMetadata.Where(pair =>
                    !string.Equals(pair.Key, ToolMetadataKeys.ToolTrustMode, StringComparison.OrdinalIgnoreCase)));
            }

            metadata.Add(new KeyValuePair<string, string>(
                ToolMetadataKeys.ToolTrustMode,
                alwaysAllow ? ToolMetadataKeys.ToolTrustModeAlwaysAllow : ToolMetadataKeys.ToolTrustModeNeedConfirm));
            return metadata;
        }

        private static async Task<ToolExecutionOutcome> DispatchToMcpAsync(string toolCallId, string fullToolName, string realToolName, string argumentsJson, IMcpClient mcpClient, CancellationToken ct)
        {
            try
            {
                JsonElement args;
                try { using var doc = JsonDocument.Parse(argumentsJson ?? "{}"); args = doc.RootElement.Clone(); }
                catch { args = JsonDocument.Parse("{}").RootElement.Clone(); }
                var result = await mcpClient.CallToolAsync(realToolName, args, ct).ConfigureAwait(false);
                return new ToolExecutionOutcome { ToolCallId = toolCallId ?? string.Empty, ToolName = fullToolName ?? string.Empty, Success = result.Success, Content = result.Content ?? string.Empty,
                    ErrorCode = result.Success ? string.Empty : "mcp.tool_call_failed", ErrorMessage = result.Success ? string.Empty : (result.ErrorMessage ?? "MCP call failed.") };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new ToolExecutionOutcome { ToolCallId = toolCallId ?? string.Empty, ToolName = fullToolName ?? string.Empty, Success = false, ErrorCode = "mcp.tool_call_exception", ErrorMessage = $"MCP call failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// 列表必须与 <see cref="BuildModelTools"/> 下发给模型的集合一致。否则模型会看到
        /// 被技能计划屏蔽的工具并反复调用它们，被驳回后转而自行降级到代码执行。
        /// </summary>
        internal static ToolExecutionOutcome ExecuteListTools(string toolCallId, string toolName, IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog, Planning.RuntimeSkillPlan skillPlan)
        {
            var lines = new List<string>();
            var visible = new Dictionary<string, HostBridgeToolDescriptor>(StringComparer.OrdinalIgnoreCase);
            if (toolCatalog != null)
                foreach (var pair in toolCatalog)
                {
                    var def = pair.Value; if (def == null || !ShouldExposeToolInList(def)) continue;
                    if (!ShouldExposeToolToModel(def, skillPlan) || !IsToolPermittedBySkillPlan(def, skillPlan)) continue;
                    visible[pair.Key] = def;
                    var line = new StringBuilder().Append("- ").Append(def.Name);
                    if (!string.IsNullOrWhiteSpace(def.Description)) line.Append(": ").Append(def.Description);
                    if (def.RequiresConfirmation) line.Append(" (requires confirmation)");
                    lines.Add(line.ToString());
                }
            if (lines.Count == 0) lines.Add("当前没有可用工具。请先接入 HostBridge 客户端或 MCP 服务层。");
            return new ToolExecutionOutcome { ToolCallId = toolCallId ?? string.Empty, ToolName = toolName ?? string.Empty, Success = true,
                Content = string.Join(Environment.NewLine, lines), StructuredContentJson = JsonSerializer.Serialize(visible) };
        }

        internal static ToolExecutionOutcome ExecuteGetHostContext(string toolCallId, string toolName, HostContextSnapshotDto hostSnapshot)
        {
            var s = hostSnapshot ?? new HostContextSnapshotDto();
            var sb = new StringBuilder().Append("Host: ").Append(string.IsNullOrWhiteSpace(s.HostApplicationName) ? "Unknown" : s.HostApplicationName);
            if (!string.IsNullOrWhiteSpace(s.DocumentName)) sb.Append(" | Doc: ").Append(s.DocumentName);
            if (!string.IsNullOrWhiteSpace(s.WorksheetName)) sb.Append(" | Sheet: ").Append(s.WorksheetName);
            if (!string.IsNullOrWhiteSpace(s.SelectionAddress)) sb.Append(" | Selection: ").Append(s.SelectionAddress);
            if (!string.IsNullOrWhiteSpace(s.Summary)) sb.Append(" | Summary: ").Append(s.Summary);
            var workbookMapSummary = !string.IsNullOrWhiteSpace(s.WorkbookMapSummary)
                ? s.WorkbookMapSummary
                : DefaultContextBuilder.BuildWorkbookMapSummary(s.WorkbookMap);
            workbookMapSummary = DefaultContextBuilder.LimitWorkbookMapSummary(workbookMapSummary);
            if (!string.IsNullOrWhiteSpace(workbookMapSummary))
            {
                sb.Append(" | Workbook map: ").Append(workbookMapSummary);
            }

            return new ToolExecutionOutcome { ToolCallId = toolCallId ?? string.Empty, ToolName = toolName ?? string.Empty, Success = true, Content = sb.ToString(), StructuredContentJson = JsonSerializer.Serialize(s) };
        }

        internal static bool ShouldExposeToolInList(HostBridgeToolDescriptor tool) => tool != null;

        internal ToolExecutionOutcome ExecuteMcpStatus(string toolCallId, string toolName)
        {
            var lines = new List<string>();
            if (_mcpRegistry == null)
            {
                lines.Add("MCP 未激活（McpRegistry 为 null）。请检查 Broker 启动日志。");
            }
            else
            {
                var states = _mcpRegistry.GetServerStates();
                if (states == null || states.Count == 0)
                {
                    lines.Add("MCP 已激活，但未加载任何服务器配置。");
                    lines.Add("请检查 %APPDATA%/NixerOfficeCopilot/settings.json 中 McpServers 配置是否存在且已启用。");
                }
                else
                {
                    lines.Add($"MCP 服务器总数: {states.Count}");
                    foreach (var s in states)
                    {
                        var stateIcon = s.State == McpServerConnectionState.Connected ? "✅"
                            : s.State == McpServerConnectionState.Connecting ? "🔄"
                            : s.State == McpServerConnectionState.Failed ? "❌"
                            : "⏸";
                        lines.Add($"- {stateIcon} {s.ServerName}: {s.State}" + (s.ToolCount > 0 ? $" ({s.ToolCount} tools)" : ""));
                        if (s.State == McpServerConnectionState.Failed && !string.IsNullOrWhiteSpace(s.ErrorMessage))
                            lines.Add($"  错误: {s.ErrorMessage}");
                    }
                }
            }

            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = string.Join(Environment.NewLine, lines)
            };
        }

        private async Task<ToolExecutionOutcome> ExecuteCompactAsync(string toolCallId, string toolName, CancellationToken cancellationToken)
        {
            if (CompactCallback == null)
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "compact.not_available",
                    ErrorMessage = "Context compaction is not available. The runtime may not have context management enabled."
                };
            }

            try
            {
                var result = await CompactCallback(cancellationToken).ConfigureAwait(false);
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = true,
                    Content = result
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "compact.failed",
                    ErrorMessage = $"Compact failed: {ex.Message}"
                };
            }
        }

        public async Task<ToolExecutionOutcome> InvokeHostToolAsync(string toolCallId, string toolName, string argumentsJson, IEnumerable<KeyValuePair<string, string>> requestMetadata, CancellationToken ct)
        {
            try
            {
                var req = new HostToolInvocationRequest { ToolCallId = toolCallId ?? string.Empty, ToolName = toolName ?? string.Empty, ArgumentsJson = NormalizeArgumentsJson(argumentsJson) };
                if (requestMetadata != null) foreach (var pair in requestMetadata) req.Metadata[pair.Key] = pair.Value;
                var result = await _hostToolInvoker.InvokeAsync(req, ct).ConfigureAwait(false) ?? new HostToolInvocationResult();
                return new ToolExecutionOutcome { ToolCallId = string.IsNullOrWhiteSpace(result.ToolCallId) ? (toolCallId ?? string.Empty) : result.ToolCallId,
                    ToolName = string.IsNullOrWhiteSpace(result.ToolName) ? (toolName ?? string.Empty) : result.ToolName, Success = result.Success, Content = result.Content,
                    BackupPath = GetResultBackupPath(result), StructuredContentJson = result.StructuredContentJson, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage,
                    ContentParts = result.ContentParts };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return new ToolExecutionOutcome { ToolCallId = toolCallId ?? string.Empty, ToolName = toolName ?? string.Empty, Success = false, ErrorCode = "runtime.tool_exception", ErrorMessage = ex.Message }; }
        }

        private static string GetResultBackupPath(HostToolInvocationResult result)
        {
            if (result?.Metadata == null) return string.Empty;
            return result.Metadata.TryGetValue(BackupPathMetadataKey, out var path) && !string.IsNullOrWhiteSpace(path) ? path.Trim() : string.Empty;
        }

        internal static string NormalizeArgumentsJson(string argumentsJson) => string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson.Trim();

        internal static string NormalizeToolSchemaJson(string inputSchemaJson, string fallback)
        {
            var fb = string.IsNullOrWhiteSpace(fallback) ? EmptyObjectSchemaJson : fallback;
            if (string.IsNullOrWhiteSpace(inputSchemaJson)) return fb;
            try { using (JsonDocument.Parse(inputSchemaJson)) { return inputSchemaJson; } } catch { return fb; }
        }

        /// <summary>
        /// Phase 3: 从 ExitPlanMode 的 arguments 中提取 allowedPrompts 数组。
        /// 返回 JSON 字符串或 "[]"。
        /// </summary>
        private static string ParseAllowedPrompts(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return "[]";

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                if (doc.RootElement.TryGetProperty("allowedPrompts", out var prompts) &&
                    prompts.ValueKind == JsonValueKind.Array)
                {
                    return prompts.GetRawText();
                }
            }
            catch { }

            return "[]";
        }

        /// <summary>
        /// Phase P4: Request user confirmation before executing a tool marked as RequiresConfirmation.
        /// Sends a <c>tool.confirmation</c> interaction to the Panel and waits for approval.
        /// </summary>
        private async Task<ToolExecutionOutcome> HandlePowerShellWithPermissionGateAsync(
            string toolCallId,
            string toolName,
            string argumentsJson,
            IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog,
            CancellationToken cancellationToken)
        {
            var mode = _permissionGate?.Mode ?? PermissionMode.Default;

            // Extract the actual PS command to determine read-only status per-command
            var psCommand = ExtractPowerShellCommand(argumentsJson);
            var psIsReadOnly = string.IsNullOrWhiteSpace(psCommand) ? false : PowerShellTool.IsReadOnly(psCommand);

            // Step 1: Plan mode check — block non-read-only commands
            if (_permissionGate != null && !_permissionGate.CanExecute(toolName, psIsReadOnly))
            {
                var reason = _permissionGate.GetBlockReason(toolName, psIsReadOnly);
                RuntimeTrace.Write($"[PowerShellTool] Blocked by permission gate: {reason}");
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "powershell.permission_blocked",
                    ErrorMessage = reason
                };
            }

            // Step 2: If command is NOT read-only and we're in Default mode,
            // show a user confirmation dialog via Panel. 「始终放行」下不再弹窗。
            if (!psIsReadOnly && mode == PermissionMode.Default && !IsTrustAlwaysAllowActive())
            {
                var toolDef = toolCatalog != null && toolCatalog.TryGetValue(toolName, out var def) ? def : null;
                // Build a command-specific description for the confirmation dialog
                var confirmDescription = BuildPowerShellConfirmationDescription(psCommand, toolDef);

                var confirmOutcome = await RequestToolConfirmationAsync(
                    toolCallId, toolName, toolDef, argumentsJson, cancellationToken,
                    confirmDescription).ConfigureAwait(false);
                if (!confirmOutcome.Success)
                    return confirmOutcome;
            }

            // Step 3: Execute
            RuntimeTrace.Write($"[PowerShellTool] Permission check passed: mode={mode}, isReadOnly={psIsReadOnly}, command={(psCommand.Length > 100 ? psCommand.Substring(0, 100) + "..." : psCommand)}");
            return await _powerShellTool.ExecuteAsync(toolCallId, toolName, argumentsJson, mode, cancellationToken).ConfigureAwait(false);
        }

        private static string ExtractPowerShellCommand(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson)) return string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(argumentsJson);
                if (doc.RootElement.TryGetProperty("command", out var cmdProp))
                    return cmdProp.GetString() ?? string.Empty;
                return string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string BuildPowerShellConfirmationDescription(string command, HostBridgeToolDescriptor toolDef)
        {
            if (string.IsNullOrWhiteSpace(command)) return toolDef?.Description ?? "Execute PowerShell";
            var preview = command.Length > 80 ? command.Substring(0, 80) + "..." : command;
            return "PowerShell: " + preview;
        }

        private async Task<ToolExecutionOutcome> DeclareExecutionModeAsync(
            string toolCallId,
            string toolName,
            string argumentsJson,
            CancellationToken cancellationToken)
        {
            IList<ExecutionModeCandidate> candidates;
            IList<ExecutionModeClarification> clarifications;
            string parseError;
            if (!TurnExecutionContract.TryParseCandidates(argumentsJson, out candidates, out parseError))
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "execution_mode.invalid_declaration",
                    ErrorMessage = parseError
                };
            }

            if (!TurnExecutionContract.TryParseClarifications(argumentsJson, out clarifications, out parseError))
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "execution_mode.invalid_declaration",
                    ErrorMessage = parseError
                };
            }

            if (candidates.Count == 1
                && CurrentExecutionContract != null
                && CurrentExecutionContract.IsDeclared
                && CurrentExecutionContract.IsUserSelected)
            {
                if (!string.Equals(
                    candidates[0].Mode,
                    CurrentExecutionContract.Mode,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return new ToolExecutionOutcome
                    {
                        ToolCallId = toolCallId ?? string.Empty,
                        ToolName = toolName ?? string.Empty,
                        Success = false,
                        ErrorCode = "execution_mode.user_choice_locked",
                        ErrorMessage = "当前执行方式来自用户选择，不能由模型的单候选声明扩大或替换。若确需更改，请再次提交多个执行方式候选，由用户确认。"
                    };
                }

                if (clarifications.Count > 0)
                {
                    return await AskExecutionModeClarificationsOnlyAsync(
                        toolCallId,
                        toolName,
                        CurrentExecutionContract,
                        clarifications,
                        cancellationToken).ConfigureAwait(false);
                }

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "execution_mode.user_choice_locked",
                    ErrorMessage = "当前执行方式来自用户选择，不能由模型的单候选声明扩大或替换。若确需更改，请再次提交多个执行方式候选，由用户确认。"
                };
            }

            if (candidates.Count == 1 && clarifications.Count == 0)
            {
                var contract = candidates[0].ToContract(userSelected: false, inherited: false);
                await ApplyExecutionContractAsync(contract, false, cancellationToken).ConfigureAwait(false);
                return CreateExecutionModeOutcome(
                    toolCallId,
                    toolName,
                    contract,
                    "执行方式预告已登记；这只是模型提案，尚未触发 HostBridge 写入预检，也未写入工作簿。实际 write effect 只能由后续写入工具的宿主预检确定。");
            }

            return await AskForExecutionModeAsync(
                toolCallId,
                toolName,
                candidates,
                clarifications,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<ToolExecutionOutcome> AskExecutionModeClarificationsOnlyAsync(
            string toolCallId,
            string toolName,
            TurnExecutionContract contract,
            IList<ExecutionModeClarification> clarifications,
            CancellationToken cancellationToken)
        {
            var request = new AgentBrokerToolUserInteractionRequest
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                InteractionDataJson = JsonSerializer.Serialize(new
                {
                    questions = clarifications.Select(clarification => new
                    {
                        question = clarification.Question,
                        header = clarification.Header,
                        options = clarification.Options.Select(option => new
                        {
                            label = option.Label,
                            description = option.Description
                        }).ToArray(),
                        multiSelect = false
                    }).ToArray()
                }),
                TimeoutSeconds = 120
            };

            AgentBrokerToolUserInteractionResponse response;
            try
            {
                if (CurrentUserInteractionEmitter == null)
                {
                    return CreateRequiredClarificationFailure(
                        toolCallId,
                        toolName,
                        "用户交互界面不可用，无法确认数据口径。");
                }

                var responseTask = _userInteractionHandler.RequestUserInteractionAsync(request, cancellationToken);
                await CurrentUserInteractionEmitter(request, cancellationToken).ConfigureAwait(false);
                response = await responseTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CreateRequiredClarificationFailure(
                    toolCallId,
                    toolName,
                    "等待用户澄清超时；本次写入未执行。");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CreateRequiredClarificationFailure(
                    toolCallId,
                    toolName,
                    "无法完成口径澄清：" + ex.Message);
            }

            if (response == null || response.Cancelled)
            {
                return CreateRequiredClarificationFailure(
                    toolCallId,
                    toolName,
                    "用户取消或未完成数据口径澄清；本次写入未执行。");
            }

            var answers = new List<KeyValuePair<string, string>>();
            foreach (var clarification in clarifications)
            {
                string answer;
                if (!TryReadInteractionAnswer(response.AnswersJson, clarification.Question, out answer)
                    || !clarification.Options.Any(option => string.Equals(
                        option.Label,
                        answer,
                        StringComparison.Ordinal)))
                {
                    return CreateRequiredClarificationFailure(
                        toolCallId,
                        toolName,
                        "没有收到完整且有效的数据口径答复；本次写入不会继续。");
                }

                answers.Add(new KeyValuePair<string, string>(clarification.Question, answer));
            }

            var outcome = CreateExecutionModeOutcome(
                toolCallId,
                toolName,
                contract,
                "执行方式沿用用户已选择的会话默认值。");
            AppendClarificationAnswers(outcome, answers);
            return outcome;
        }

        private async Task<ToolExecutionOutcome> AskForExecutionModeAsync(
            string toolCallId,
            string toolName,
            IList<ExecutionModeCandidate> candidates,
            IList<ExecutionModeClarification> clarifications,
            CancellationToken cancellationToken)
        {
            var optionLabels = CreateExecutionModeOptionLabels(candidates);
            const string rememberQuestion = "是否记住此方式？";
            const string cancelLabel = "取消本轮写入";
            var asksForMode = candidates.Count > 1 || clarifications.Count > 0;
            var modeQuestion = candidates.Count == 1
                ? "是否按建议的执行方式继续？"
                : "本次结果采用哪种执行方式？";
            var questions = new List<object>();
            if (asksForMode)
            {
                var modeOptions = candidates.Select((candidate, index) => (object)new
                {
                    label = optionLabels[index],
                    description = BuildCandidateDescription(candidate)
                }).ToList();
                if (candidates.Count == 1)
                {
                    modeOptions.Add(new
                    {
                        label = cancelLabel,
                        description = "取消本轮写入，不执行所提议的工作簿操作。"
                    });
                }

                questions.Add(new
                {
                    question = modeQuestion,
                    header = "执行方式",
                    options = modeOptions,
                    multiSelect = false
                });
            }

            foreach (var clarification in clarifications)
            {
                questions.Add(new
                {
                    question = clarification.Question,
                    header = clarification.Header,
                    options = clarification.Options.Select(option => new
                    {
                        label = option.Label,
                        description = option.Description
                    }).ToArray(),
                    multiSelect = false
                });
            }

            questions.Add(new
            {
                question = rememberQuestion,
                header = "应用范围",
                options = new[]
                {
                    new { label = "仅本轮", description = "下次任务重新判断。" },
                    new { label = "本会话都按这个方式", description = "后续任务默认沿用，可用明确指令覆盖。" }
                },
                multiSelect = false
            });
            var interactionData = JsonSerializer.Serialize(new
            {
                questions
            });
            var request = new AgentBrokerToolUserInteractionRequest
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                InteractionDataJson = interactionData,
                TimeoutSeconds = 120
            };

            AgentBrokerToolUserInteractionResponse response;
            try
            {
                if (CurrentUserInteractionEmitter == null)
                {
                    if (clarifications.Count > 0)
                    {
                        return CreateRequiredClarificationFailure(
                            toolCallId,
                            toolName,
                            "用户交互界面不可用，无法确认数据口径。");
                    }

                    return await ApplyConservativeExecutionModeAsync(
                        toolCallId,
                        toolName,
                        candidates,
                        "用户交互界面不可用").ConfigureAwait(false);
                }

                var responseTask = _userInteractionHandler.RequestUserInteractionAsync(request, cancellationToken);
                await CurrentUserInteractionEmitter(request, cancellationToken).ConfigureAwait(false);
                response = await responseTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (clarifications.Count > 0)
                {
                    return CreateRequiredClarificationFailure(
                        toolCallId,
                        toolName,
                        "等待用户澄清超时；本次写入未执行。");
                }

                return await ApplyConservativeExecutionModeAsync(
                    toolCallId,
                    toolName,
                    candidates,
                    "等待用户选择超时").ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (clarifications.Count > 0)
                {
                    return CreateRequiredClarificationFailure(
                        toolCallId,
                        toolName,
                        "无法完成执行方式或口径询问：" + ex.Message);
                }

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "execution_mode.interaction_failed",
                    ErrorMessage = "无法完成执行方式询问：" + ex.Message
                };
            }

            if (response == null || response.Cancelled)
            {
                if (clarifications.Count > 0)
                {
                    return CreateRequiredClarificationFailure(
                        toolCallId,
                        toolName,
                        "用户取消或未完成数据口径澄清；本次写入未执行。");
                }

                return await ApplyConservativeExecutionModeAsync(
                    toolCallId,
                    toolName,
                    candidates,
                    "用户取消或未完成选择").ConfigureAwait(false);
            }

            var selectedLabel = string.Empty;
            string rememberAnswer;
            if (asksForMode
                && !TryReadInteractionAnswer(response.AnswersJson, modeQuestion, out selectedLabel))
            {
                if (clarifications.Count > 0)
                {
                    return CreateRequiredClarificationFailure(
                        toolCallId,
                        toolName,
                        "没有收到完整的执行方式选择或口径答复；本次写入不会继续。");
                }

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "execution_mode.invalid_user_response",
                    ErrorMessage = "没有收到完整的执行方式选择；请重新声明候选并再次询问用户。"
                };
            }

            var selectedIndex = asksForMode ? optionLabels.IndexOf(selectedLabel) : 0;
            if (candidates.Count == 1 && string.Equals(selectedLabel, cancelLabel, StringComparison.Ordinal))
            {
                return CreateRequiredClarificationFailure(
                    toolCallId,
                    toolName,
                    "用户取消了本次工作簿写入；未执行写入操作。");
            }

            if (selectedIndex < 0 || selectedIndex >= candidates.Count)
            {
                if (clarifications.Count > 0)
                {
                    return CreateRequiredClarificationFailure(
                        toolCallId,
                        toolName,
                        "用户选择不属于本次执行方式候选；本次写入不会继续。");
                }

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "execution_mode.invalid_user_response",
                    ErrorMessage = "用户选择不属于本次执行方式候选；本次写入不会继续。"
                };
            }

            var clarificationAnswers = new List<KeyValuePair<string, string>>();
            foreach (var clarification in clarifications)
            {
                string answer;
                if (!TryReadInteractionAnswer(response.AnswersJson, clarification.Question, out answer)
                    || !clarification.Options.Any(option => string.Equals(
                        option.Label,
                        answer,
                        StringComparison.Ordinal)))
                {
                    return CreateRequiredClarificationFailure(
                        toolCallId,
                        toolName,
                        "没有收到完整且有效的数据口径答复；本次写入不会继续。");
                }

                clarificationAnswers.Add(new KeyValuePair<string, string>(clarification.Question, answer));
            }

            TryReadInteractionAnswer(response.AnswersJson, rememberQuestion, out rememberAnswer);

            var selectedContract = candidates[selectedIndex].ToContract(userSelected: true, inherited: false);
            var rememberForSession = string.Equals(
                rememberAnswer,
                "本会话都按这个方式",
                StringComparison.Ordinal);
            await ApplyExecutionContractAsync(selectedContract, rememberForSession, cancellationToken)
                .ConfigureAwait(false);
            var outcome = CreateExecutionModeOutcome(
                toolCallId,
                toolName,
                selectedContract,
                rememberForSession ? "用户已选择执行方式，并设为本会话默认。" : "用户已选择本轮执行方式。");
            if (clarificationAnswers.Count > 0)
            {
                AppendClarificationAnswers(outcome, clarificationAnswers);
            }

            return outcome;
        }

        private ToolExecutionOutcome CreateRequiredClarificationFailure(
            string toolCallId,
            string toolName,
            string message)
        {
            CurrentExecutionContract = new TurnExecutionContract();
            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = false,
                ErrorCode = "execution_mode.clarification_required",
                ErrorMessage = message
            };
        }

        private static void AppendClarificationAnswers(
            ToolExecutionOutcome outcome,
            IList<KeyValuePair<string, string>> answers)
        {
            if (outcome == null || answers == null || answers.Count == 0)
            {
                return;
            }

            outcome.Content += Environment.NewLine + Environment.NewLine
                + "用户口径澄清答复："
                + string.Concat(answers.Select(answer =>
                    Environment.NewLine + "- " + answer.Key + "：" + answer.Value));
        }

        private async Task<ToolExecutionOutcome> ApplyConservativeExecutionModeAsync(
            string toolCallId,
            string toolName,
            IList<ExecutionModeCandidate> candidates,
            string reason)
        {
            var conservative = candidates
                .OrderBy(candidate => ExecutionModeRiskOrder(candidate.Mode))
                .First();
            var contract = conservative.ToContract(userSelected: true, inherited: false);
            await ApplyExecutionContractAsync(contract, false, CancellationToken.None).ConfigureAwait(false);
            return CreateExecutionModeOutcome(
                toolCallId,
                toolName,
                contract,
                reason + "；已采用候选中最保守的方式。");
        }

        private async Task ApplyExecutionContractAsync(
            TurnExecutionContract contract,
            bool rememberForSession,
            CancellationToken cancellationToken)
        {
            CurrentExecutionContract = contract;
            if (CurrentConversationMetadata != null)
            {
                var serialized = TurnExecutionContract.Serialize(contract);
                CurrentConversationMetadata[TurnExecutionContract.LastContractMetadataKey] = serialized;
                if (rememberForSession)
                {
                    CurrentConversationMetadata[TurnExecutionContract.SessionPreferenceMetadataKey] = serialized;
                }
            }

            if (CurrentExecutionModeEmitter != null)
            {
                await CurrentExecutionModeEmitter(contract, false, cancellationToken).ConfigureAwait(false);
            }
        }

        private static ToolExecutionOutcome CreateExecutionModeOutcome(
            string toolCallId,
            string toolName,
            TurnExecutionContract contract,
            string message)
        {
            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = message + " " + contract.ToDisplayText(),
                StructuredContentJson = TurnExecutionContract.Serialize(contract)
            };
        }

        private static IList<string> CreateExecutionModeOptionLabels(IList<ExecutionModeCandidate> candidates)
        {
            var labels = new List<string>();
            var usedLabels = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var label = TurnExecutionContract.ModeLabel(candidate.Mode);
                if (candidate.Targets != null && candidate.Targets.Count > 0)
                {
                    label += "：" + string.Join("、", candidate.Targets);
                }

                if (!usedLabels.Add(label))
                {
                    label += "（方案 " + (index + 1) + "）";
                    usedLabels.Add(label);
                }

                labels.Add(label);
            }

            return labels;
        }

        private static string BuildCandidateDescription(ExecutionModeCandidate candidate)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(candidate.Summary))
            {
                parts.Add(candidate.Summary.Trim());
            }

            if (candidate.Sources != null && candidate.Sources.Count > 0)
            {
                parts.Add("数据源：" + string.Join("、", candidate.Sources));
            }

            return string.Join("；", parts);
        }

        private static int ExecutionModeRiskOrder(string mode)
        {
            switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "result_only": return 0;
                case "new_sheet_static":
                case "new_sheet_formula": return 1;
                case "modify_in_place": return 2;
                default: return int.MaxValue;
            }
        }

        private static bool TryReadInteractionAnswer(string answersJson, string question, out string answer)
        {
            answer = string.Empty;
            try
            {
                using (var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(answersJson) ? "{}" : answersJson))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (string.Equals(property.Name, question, StringComparison.Ordinal)
                            && property.Value.ValueKind == JsonValueKind.String)
                        {
                            answer = property.Value.GetString() ?? string.Empty;
                            return answer.Length > 0;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Phase P4: Request user confirmation before executing a tool marked as RequiresConfirmation.
        /// Sends a <c>tool.confirmation</c> interaction to the Panel and waits for approval.
        /// </summary>
        private async Task<ToolExecutionOutcome> RequestToolConfirmationAsync(
            string toolCallId,
            string toolName,
            HostBridgeToolDescriptor toolDef,
            string argumentsJson,
            CancellationToken cancellationToken,
            string descriptionOverride = null,
            string operationSummary = null)
        {
            if (string.Equals(toolName, "ppt_deck_build", StringComparison.OrdinalIgnoreCase))
            {
                operationSummary = BuildDeckBuildConfirmationSummary(argumentsJson);
            }

            try
            {
                var desc = descriptionOverride ?? toolDef?.Description ?? string.Empty;
                var interactionData = JsonSerializer.Serialize(new
                {
                    type = "tool.confirmation",
                    toolName = toolName ?? string.Empty,
                    toolDescription = desc,
                    riskLevel = toolDef?.RiskLevel ?? "high",
                    argumentsSummary = TruncateArgumentsForDisplay(argumentsJson),
                    argumentsJson = argumentsJson ?? string.Empty,
                    operationSummary = operationSummary ?? string.Empty
                });

                var request = new AgentBrokerToolUserInteractionRequest
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    InteractionDataJson = interactionData,
                    TimeoutSeconds = 120
                };
                var responseTask = _userInteractionHandler.RequestUserInteractionAsync(
                    request,
                    cancellationToken);

                if (CurrentUserInteractionEmitter != null)
                {
                    try
                    {
                        await CurrentUserInteractionEmitter(request, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                    }
                }

                var response = await responseTask.ConfigureAwait(false);

                if (response == null || response.Cancelled)
                {
                    return new ToolExecutionOutcome
                    {
                        ToolCallId = toolCallId ?? string.Empty,
                        ToolName = toolName ?? string.Empty,
                        Success = false,
                        ErrorCode = "permission.denied_by_user",
                        ErrorMessage = $"Tool '{toolName}' was denied by the user."
                    };
                }

                // User approved — tool will proceed to execution
                return new ToolExecutionOutcome { ToolCallId = toolCallId ?? string.Empty, ToolName = toolName ?? string.Empty, Success = true };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "permission.confirmation_error",
                    ErrorMessage = $"Tool confirmation failed: {ex.Message}"
                };
            }
        }

        private const string DeckBuildToolName = "ppt_deck_build";

        /// <summary>
        /// ppt_deck_build 允许省略 deckId / workspaceTitle / slideCount；任一缺失时由宿主按会话绑定补全。
        /// </summary>
        internal static bool IsDeckBuildTargetIncomplete(string argumentsJson)
        {
            try
            {
                using (var document = JsonDocument.Parse(NormalizeArgumentsJson(argumentsJson)))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return true;
                    return !root.TryGetProperty("deckId", out var deckId)
                        || deckId.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(deckId.GetString())
                        || !root.TryGetProperty("workspaceTitle", out var title)
                        || title.ValueKind != JsonValueKind.String
                        || !root.TryGetProperty("slideCount", out var slideCount)
                        || slideCount.ValueKind != JsonValueKind.Number;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private async Task<(string ResolvedArgumentsJson, ToolExecutionOutcome Failure)> ResolveDeckBuildTargetAsync(
            string toolCallId,
            string toolName,
            string argumentsJson,
            CancellationToken cancellationToken)
        {
            ToolExecutionOutcome Fail(string code, string message) => new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = false,
                Content = message,
                ErrorCode = code,
                ErrorMessage = message
            };

            var preflightInvoker = _hostToolInvoker as IHostToolPreflightInvoker;
            if (preflightInvoker == null)
            {
                return (null, Fail(
                    "ppt.deck_build_target_required",
                    "当前通道无法按会话补全构建目标；请提供 deckId、workspaceTitle 和 slideCount。"));
            }

            var resolved = await preflightInvoker.PreflightToolCallAsync(
                new PreflightToolCallRequest
                {
                    ConversationId = CurrentConversationId ?? string.Empty,
                    MessageId = CurrentTurnMessageId ?? string.Empty,
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    ArgumentsJson = NormalizeArgumentsJson(argumentsJson)
                },
                cancellationToken).ConfigureAwait(false);
            if (resolved == null || !resolved.Success || string.IsNullOrWhiteSpace(resolved.ResolvedArgumentsJson))
            {
                return (null, Fail(
                    resolved == null || string.IsNullOrWhiteSpace(resolved.ErrorCode)
                        ? "ppt.deck_build_target_unbound"
                        : resolved.ErrorCode,
                    resolved == null || string.IsNullOrWhiteSpace(resolved.ErrorMessage)
                        ? "无法确定构建目标；请先对目标工作区运行 ppt_deck_preview。"
                        : resolved.ErrorMessage));
            }

            RuntimeTrace.Write("[DeckBuildTarget] resolved from session binding: " + resolved.ResolvedArgumentsJson);
            return (resolved.ResolvedArgumentsJson, null);
        }

        private static string BuildDeckBuildConfirmationSummary(string argumentsJson)
        {
            try
            {
                using (var document = JsonDocument.Parse(argumentsJson ?? "{}"))
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("workspaceTitle", out var titleElement)
                        || !root.TryGetProperty("deckId", out var deckIdElement)
                        || !root.TryGetProperty("slideCount", out var slideCountElement)
                        || titleElement.ValueKind != JsonValueKind.String
                        || deckIdElement.ValueKind != JsonValueKind.String
                        || !slideCountElement.TryGetInt32(out var slideCount))
                    {
                        return "构建目标信息不完整；执行端将阻止未绑定的工作区构建。";
                    }

                    var title = titleElement.GetString() ?? string.Empty;
                    var deckId = deckIdElement.GetString() ?? string.Empty;
                    return "构建工作区「" + (string.IsNullOrWhiteSpace(title) ? "无标题" : title)
                        + "」；ID " + deckId + "；共 " + slideCount + " 页。";
                }
            }
            catch (JsonException)
            {
                return "构建目标参数无法解析；执行端将阻止未绑定的工作区构建。";
            }
        }

        private static string TruncateArgumentsForDisplay(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return string.Empty;

            if (argumentsJson.Length <= 200)
                return argumentsJson;

            return argumentsJson.Substring(0, 197) + "...";
        }

        /// <summary>
        /// Phase 0+3: 处理需要用户交互的工具（AskUserQuestion, ExitPlanMode）。
        /// 1. 如果是 ExitPlanMode：先调用 tool 写入 plan 文件
        /// 2. 构造交互请求并先通过 IUserInteractionHandler 注册响应等待项
        /// 3. 通过 CurrentUserInteractionEmitter 发送给 Panel，再等待响应
        /// 4. 将响应转换为 ToolExecutionOutcome
        /// </summary>
        private async Task<ToolExecutionOutcome> HandleUserInteractionToolAsync(
            string toolCallId,
            string toolName,
            string argumentsJson,
            CancellationToken cancellationToken)
        {
            try
            {
                // Phase 3: ExitPlanMode — write plan file before asking for approval
                string planFilePath = null;
                string interactionData = argumentsJson;

                if (string.Equals(toolName, RuntimeExitPlanModeName, StringComparison.OrdinalIgnoreCase))
                {
                    // Parse plan text and write to file
                    string planText = null;
                    try
                    {
                        using var doc = JsonDocument.Parse(argumentsJson ?? "{}");
                        if (doc.RootElement.TryGetProperty("plan", out var planProp))
                            planText = planProp.GetString();
                    }
                    catch { }

                    if (!string.IsNullOrWhiteSpace(planText))
                    {
                        try
                        {
                            planFilePath = PlanFileManager.WritePlan(planText);
                        }
                        catch (Exception)
                        {
                        }
                    }

                    // Enrich interaction data with plan file path
                    interactionData = JsonSerializer.Serialize(new
                    {
                        type = "plan.approval",
                        plan = planText ?? string.Empty,
                        planFilePath = planFilePath ?? string.Empty,
                        allowedPrompts = ParseAllowedPrompts(argumentsJson)
                    });
                }

                var request = new AgentBrokerToolUserInteractionRequest
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    InteractionDataJson = interactionData,
                    TimeoutSeconds = 300
                };
                var responseTask = _userInteractionHandler.RequestUserInteractionAsync(
                    request,
                    cancellationToken);

                RuntimeTrace.Write($"[DefaultToolDispatcher] Requesting user interaction for '{toolName}' (callId={toolCallId}), data len={interactionData?.Length ?? 0}");

                // Emit event to Panel
                if (CurrentUserInteractionEmitter != null)
                {
                    try
                    {
                        await CurrentUserInteractionEmitter(request, cancellationToken).ConfigureAwait(false);
                        RuntimeTrace.Write($"[DefaultToolDispatcher] Emit to Panel succeeded for '{toolName}' (callId={toolCallId})");
                    }
                    catch (Exception ex)
                    {
                        RuntimeTrace.Write($"[DefaultToolDispatcher] Emit to Panel FAILED for '{toolName}': {ex.Message}");
                    }
                }
                else
                {
                    RuntimeTrace.Write($"[DefaultToolDispatcher] WARNING: CurrentUserInteractionEmitter is NULL");
                }

                RuntimeTrace.Write($"[DefaultToolDispatcher] Waiting for user response for '{toolName}' (callId={toolCallId})...");
                var response = await responseTask.ConfigureAwait(false);
                RuntimeTrace.Write($"[DefaultToolDispatcher] Received user response for '{toolName}': cancelled={response?.Cancelled}, errorCode={response?.ErrorCode}, errorMsg={response?.ErrorMessage}");

                if (response == null)
                {
                    return new ToolExecutionOutcome
                    {
                        ToolCallId = toolCallId ?? string.Empty,
                        ToolName = toolName ?? string.Empty,
                        Success = false,
                        ErrorCode = "user_interaction.null_response",
                        ErrorMessage = "User interaction returned null response."
                    };
                }

                if (response.Cancelled)
                {
                    return new ToolExecutionOutcome
                    {
                        ToolCallId = toolCallId ?? string.Empty,
                        ToolName = toolName ?? string.Empty,
                        Success = false,
                        ErrorCode = response.ErrorCode ?? "user_interaction.cancelled",
                        ErrorMessage = string.IsNullOrWhiteSpace(response.ErrorMessage)
                            ? "User cancelled the interaction."
                            : response.ErrorMessage
                    };
                }

                // Phase 3: ExitPlanMode — on approval, switch permission mode
                if (string.Equals(toolName, RuntimeExitPlanModeName, StringComparison.OrdinalIgnoreCase))
                {
                    _permissionGate.SetMode(PermissionMode.AcceptEdits);
                    RuntimeTrace.Write($"[DefaultToolDispatcher] Plan approved! Switched to AcceptEdits mode. CallId={toolCallId}");

                    var planSb = new StringBuilder()
                        .AppendLine("Plan approved. Write operations are now allowed.")
                        .AppendLine()
                        .AppendLine("Execute the approved plan step by step, verifying each modification.")
                        .AppendLine("When finished, summarize in Chinese.");

                    return new ToolExecutionOutcome
                    {
                        ToolCallId = toolCallId ?? string.Empty,
                        ToolName = toolName ?? string.Empty,
                        Success = true,
                        Content = planSb.ToString()
                    };
                }

                // Build result content from answers
                var content = new StringBuilder();
                content.Append("User has answered your questions.");
                if (!string.IsNullOrWhiteSpace(response.AnswersJson))
                {
                    try
                    {
                        using var answersDoc = JsonDocument.Parse(response.AnswersJson);
                        foreach (var prop in answersDoc.RootElement.EnumerateObject())
                        {
                            content.AppendLine();
                            content.Append("- \"").Append(prop.Name).Append("\" → \"").Append(prop.Value.GetString() ?? string.Empty).Append("\"");
                        }
                    }
                    catch
                    {
                        content.Append(" Answers: ").Append(response.AnswersJson);
                    }
                }

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = true,
                    Content = content.ToString(),
                    StructuredContentJson = JsonSerializer.Serialize(new
                    {
                        answers = response.AnswersJson,
                        annotations = response.AnnotationsJson
                    })
                };
            }
            catch (OperationCanceledException)
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "user_interaction.timeout",
                    ErrorMessage = "User interaction timed out or was cancelled."
                };
            }
            catch (Exception ex)
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "user_interaction.error",
                    ErrorMessage = $"User interaction failed: {ex.Message}"
                };
            }
        }
    }
}
