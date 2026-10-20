using System.Collections.Generic;

namespace OfficeCopilot.Agent.Abstractions
{
    /// <summary>
    /// Exposes runtime (Broker-level) built-in tool definitions for the Settings UI.
    /// These tools are registered in <c>DefaultToolDispatcher.BuildToolCatalog()</c>
    /// and run inside the Broker process, not the VSTO host.
    /// </summary>
    public static class RuntimeBuiltInTools
    {
        /// <summary>
        /// Returns all runtime built-in tool definitions for display in the Settings panel.
        /// This is read-only metadata — actual tool execution goes through the Broker.
        /// </summary>
        public static ToolDefinition[] GetAllDefinitions()
        {
            return new[]
            {
                // ---- Shell Group ----
                CreateDefinition(
                    "runtime.powershell",
                    "PowerShell",
                    "Execute a PowerShell command in the project environment. Use for terminal operations: git, npm, docker, build tools, and PS cmdlets. Do NOT use for file read/write/edit/search.",
                    RuntimeToolGroups.Shell,
                    inputSchemaJson: PsSchema,
                    riskLevel: "high",
                    requiresConfirmation: true),

                // ---- Interaction Group ----
                CreateDefinition(
                    "runtime.ask_user_question",
                    "Ask User Question",
                    "Ask the user multiple-choice questions to gather information, clarify ambiguity, or make decisions.",
                    RuntimeToolGroups.Interaction,
                    inputSchemaJson: AskUserQuestionSchema,
                    isReadOnly: true),

                CreateDefinition(
                    "runtime.todo_write",
                    "Todo Write",
                    "Manage a structured task list for the current coding session. Use to track progress and organize complex tasks.",
                    RuntimeToolGroups.Interaction,
                    inputSchemaJson: TodoWriteSchema),

                // ---- Planning Group ----
                CreateDefinition(
                    "runtime.enter_plan_mode",
                    "Enter Plan Mode",
                    "Enter plan mode — only read-only tools are allowed. The model can only output a plan for user approval.",
                    RuntimeToolGroups.Planning,
                    inputSchemaJson: EmptyObjSchema,
                    isReadOnly: true),

                CreateDefinition(
                    "runtime.exit_plan_mode",
                    "Exit Plan Mode",
                    "Exit plan mode and get user approval for the plan.",
                    RuntimeToolGroups.Planning,
                    inputSchemaJson: ExitPlanModeSchema,
                    requiresConfirmation: true),

                CreateDefinition(
                    "runtime.run_agent",
                    "Run Agent",
                    "Delegate a subtask to a sub-agent with restricted tools.",
                    RuntimeToolGroups.Planning,
                    inputSchemaJson: RunAgentSchema,
                    riskLevel: "high"),

                CreateDefinition(
                    "runtime.create_skill",
                    "Create Skill",
                    "Create or update a skill file (SKILL.md). Use after interviewing the user to gather skill requirements.",
                    RuntimeToolGroups.Planning,
                    inputSchemaJson: CreateSkillSchema),

                // ---- Notification Group ----
                CreateDefinition(
                    "runtime.brief",
                    "Brief",
                    "Send a formatted message to the user. Use for proactive notifications or structured replies.",
                    RuntimeToolGroups.Notification,
                    inputSchemaJson: BriefSchema,
                    isReadOnly: true),

                CreateDefinition(
                    "runtime.push_notification",
                    "Push Notification",
                    "Send an OS-level notification to the user. Use for task completion alerts or blocker notifications.",
                    RuntimeToolGroups.Notification,
                    inputSchemaJson: PushNotificationSchema,
                    isReadOnly: true),

                CreateDefinition(
                    "runtime.monitor",
                    "Monitor",
                    "Report progress for a long-running background task.",
                    RuntimeToolGroups.Notification,
                    inputSchemaJson: MonitorSchema,
                    isReadOnly: true),

                // ---- Context Group ----
                CreateDefinition(
                    "runtime.compact",
                    "Context Compact",
                    "Manually trigger context compaction. Prunes old messages and truncates large tool results to free up context window space. Use when context usage is high (>70%).",
                    RuntimeToolGroups.Context,
                    inputSchemaJson: EmptyObjSchema,
                    isReadOnly: true),
            };
        }

        private static ToolDefinition CreateDefinition(
            string name,
            string displayName,
            string description,
            string groupName,
            string inputSchemaJson = null,
            ToolSourceKind sourceKind = ToolSourceKind.BuiltIn,
            string riskLevel = "low",
            bool isReadOnly = false,
            bool requiresConfirmation = false)
        {
            return new ToolDefinition
            {
                Name = name ?? string.Empty,
                DisplayName = displayName ?? string.Empty,
                Description = description ?? string.Empty,
                SourceKind = sourceKind,
                RiskLevel = riskLevel ?? "low",
                IsReadOnly = isReadOnly,
                RequiresConfirmation = requiresConfirmation,
                IsEnabled = true,
                InputSchemaJson = inputSchemaJson ?? EmptyObjSchema,
                Metadata = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    { ToolMetadataKeys.GroupName, groupName ?? string.Empty }
                }
            };
        }

        // ------------------------------------------------------------------
        // Schema constants — keep these in sync with the actual tool implementations
        // in OfficeCopilot.Agent.Runtime/Turn/
        // ------------------------------------------------------------------

        private const string EmptyObjSchema = "{\"type\":\"object\",\"properties\":{}}";

        private const string PsSchema = @"{""type"":""object"",""properties"":{""command"":{""type"":""string"",""description"":""The PowerShell command to execute.""},""timeout"":{""type"":""integer"",""description"":""Optional timeout in milliseconds (max 600000).""},""description"":{""type"":""string"",""description"":""Clear, concise description of what this command does in active voice.""}},""required"":[""command""]}";

        private const string AskUserQuestionSchema = @"{""type"":""object"",""properties"":{""questions"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{""question"":{""type"":""string""},""header"":{""type"":""string""},""options"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{""label"":{""type"":""string""},""description"":{""type"":""string""},""preview"":{""type"":""string"",""description"":""Plain-text preview shown beside the options (single-select only).""},""previewImageId"":{""type"":""string"",""pattern"":""^[0-9]{8}-[0-9]{6}-[a-z0-9]{6}/[a-z][a-z0-9-]{0,23}(?:/[a-zA-Z0-9._~-]{0,64})?$"",""description"":""Local preview reference deckId/candidateName/imageVersion returned by ppt_style_preview; the Panel resolves it to an image URL.""},""previewImage"":{""type"":""string"",""description"":""Legacy image URL returned by a preview tool (https://nixer-decks.local/...). Shown inside the option.""}}},""minItems"":2,""maxItems"":4},""multiSelect"":{""type"":""boolean"",""default"":false}},""required"":[""question"",""header"",""options""]},""minItems"":1,""maxItems"":4}},""required"":[""questions""]}";

        private const string TodoWriteSchema = @"{""type"":""object"",""properties"":{""todos"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{""content"":{""type"":""string"",""description"":""Task description""},""status"":{""type"":""string"",""enum"":[""pending"",""in_progress"",""completed""],""description"":""Task status""}},""required"":[""content"",""status""]}}},""required"":[""todos""]}";

        private const string ExitPlanModeSchema = @"{""type"":""object"",""properties"":{""plan"":{""type"":""string"",""description"":""Plan text""},""allowedPrompts"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{""tool"":{""type"":""string""},""prompt"":{""type"":""string""}}}}}}";

        private const string RunAgentSchema = @"{""type"":""object"",""properties"":{""description"":{""type"":""string""},""prompt"":{""type"":""string""},""allowedTools"":{""type"":""array"",""items"":{""type"":""string""}}},""required"":[""description"",""prompt""]}";

        private const string CreateSkillSchema = @"{""type"":""object"",""properties"":{""name"":{""type"":""string""},""description"":{""type"":""string""},""body"":{""type"":""string""},""category"":{""type"":""string""},""allowedTools"":{""type"":""array"",""items"":{""type"":""string""}},""whenToUse"":{""type"":""string""},""argumentHint"":{""type"":""string""},""arguments"":{""type"":""array"",""items"":{""type"":""string""}},""disableModelInvocation"":{""type"":""boolean""},""userInvocable"":{""type"":""boolean""},""version"":{""type"":""string""},""context"":{""type"":""string"",""enum"":[""inline"",""fork""]},""effort"":{""type"":""string""},""requiresConfirmation"":{""type"":""boolean""},""maxToolRoundCount"":{""type"":""integer""}},""required"":[""name"",""description"",""body""]}";

        private const string BriefSchema = @"{""type"":""object"",""properties"":{""message"":{""type"":""string"",""description"":""Markdown message""},""attachments"":{""type"":""array"",""items"":{""type"":""string""}},""status"":{""type"":""string"",""enum"":[""normal"",""proactive""]}},""required"":[""message""]}";

        private const string PushNotificationSchema = @"{""type"":""object"",""properties"":{""message"":{""type"":""string""},""notificationType"":{""type"":""string"",""enum"":[""info"",""warning"",""error""]}},""required"":[""message""]}";

        private const string MonitorSchema = @"{""type"":""object"",""properties"":{""task"":{""type"":""string""},""progress"":{""type"":""integer"",""minimum"":0,""maximum"":100},""status"":{""type"":""string"",""enum"":[""running"",""completed"",""failed""]}},""required"":[""task""]}";
    }
}
