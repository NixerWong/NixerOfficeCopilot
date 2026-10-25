using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime.Planning;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Skill plan support utilities: formatting, filtering, verification prompts.
    /// Extracted from <see cref="BrokerTurnExecutor"/> (Phase 7B). All methods are pure static.
    /// </summary>
    internal static class SkillPlanSupport
    {
        public static IDictionary<string, string> BuildSkillPlannerMetadata(
            Agent.Client.AgentBrokerTurnRequest request,
            HostBridge.Contracts.HostContextSnapshotDto snapshot)
        {
            var metadata = request != null && request.Metadata != null
                ? new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (snapshot == null) return metadata;

            SetMetadataValue(metadata, SkillPlannerRequestMetadataKeys.DocumentName, snapshot.DocumentName);
            SetMetadataValue(metadata, SkillPlannerRequestMetadataKeys.WorksheetName, snapshot.WorksheetName);
            SetMetadataValue(metadata, SkillPlannerRequestMetadataKeys.SelectionAddress, snapshot.SelectionAddress);
            SetMetadataValue(metadata, SkillPlannerRequestMetadataKeys.SelectionPreview, snapshot.SelectionPreview);
            SetMetadataValue(metadata, SkillPlannerRequestMetadataKeys.Summary, snapshot.Summary);
            return metadata;
        }

        public static void SetMetadataValue(IDictionary<string, string> metadata, string key, string value)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
            metadata[key] = value.Trim();
        }

        public static string BuildSkillPlanStatusText(RuntimeSkillPlan skillPlan)
        {
            if (skillPlan == null || !skillPlan.HasSkill) return string.Empty;
            var b = new StringBuilder();
            b.Append("Skill selected: ").Append(skillPlan.SkillName);
            if (!string.IsNullOrWhiteSpace(skillPlan.Reason)) b.Append(". ").Append(skillPlan.Reason.Trim());
            if (skillPlan.AllowedToolNames != null && skillPlan.AllowedToolNames.Count > 0)
                b.Append(" Suggested tools: ").Append(string.Join(", ", skillPlan.AllowedToolNames));
            var cs = skillPlan.GetCurrentExecutionStep();
            if (cs != null && !string.IsNullOrWhiteSpace(cs.Title)) b.Append(" Current step: ").Append(cs.Title.Trim());
            AppendExecutionPlanState(b, skillPlan);
            b.Append(" Confidence: ").Append((skillPlan.Selection == null ? 0d : skillPlan.Selection.Confidence).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            return b.ToString();
        }

        public static void AppendExecutionPlanState(StringBuilder builder, RuntimeSkillPlan skillPlan)
        {
            if (builder == null || skillPlan == null || !skillPlan.HasStructuredExecutionPlan) return;
            builder.Append(" Execution plan state: current=").Append(FormatStepLabel(skillPlan.GetCurrentExecutionStep()));
            var pm = skillPlan.GetPendingMutationStep();
            builder.Append(", pendingMutation=").Append(pm == null ? "none" : FormatStepLabel(pm));
            var pv = skillPlan.GetPendingVerificationStep();
            builder.Append(", pendingVerification=").Append(pv == null ? "none" : FormatStepLabel(pv));
            builder.Append(", completed=").Append(skillPlan.CompletedExecutionStepCount).Append('/')
                .Append(skillPlan.ExecutionPlanSteps == null ? 0 : skillPlan.ExecutionPlanSteps.Count)
                .Append(", verifyRetry=").Append(skillPlan.VerificationRetryCount).Append('/').Append(skillPlan.MaxVerificationRetryCount);
        }

        public static string FormatStepLabel(SkillExecutionStep step)
        {
            if (step == null) return "none";
            var title = string.IsNullOrWhiteSpace(step.Title) ? (step.StepKind ?? string.Empty) : step.Title.Trim();
            if (string.IsNullOrWhiteSpace(step.SkillName)) return title;
            return title + " [" + step.SkillName.Trim() + "]";
        }

        public static string BuildSkillPlanCandidateTraceText(RuntimeSkillPlan skillPlan)
        {
            if (skillPlan?.Selection?.CandidateSkillSummaries == null || skillPlan.Selection.CandidateSkillSummaries.Count == 0)
                return string.Empty;
            var b = new StringBuilder();
            b.AppendLine("Skill scoring trace:");
            for (var i = 0; i < skillPlan.Selection.CandidateSkillSummaries.Count; i++)
            {
                var s = skillPlan.Selection.CandidateSkillSummaries[i];
                if (string.IsNullOrWhiteSpace(s)) continue;
                b.Append(i + 1).Append(". ").AppendLine(s.Trim());
            }
            return b.ToString().Trim();
        }

        public static IEnumerable<string> GetOrderedModelToolNames(
            IReadOnlyDictionary<string, HostBridgeToolDescriptor> toolCatalog, RuntimeSkillPlan skillPlan)
        {
            var ordered = new List<string>();
            if (toolCatalog == null || toolCatalog.Count == 0) return ordered;
            if (skillPlan != null)
            {
                var cs = skillPlan.GetCurrentExecutionStep();
                if (cs != null) { AddToolNames(ordered, cs.PreferredToolNames, toolCatalog); AddToolNames(ordered, cs.AllowedToolNames, toolCatalog); AddToolNames(ordered, cs.VerificationToolNames, toolCatalog); }
                if (skillPlan.ExecutionPlanSteps != null && skillPlan.ExecutionPlanSteps.Count > 0)
                {
                    AddToolNames(ordered, skillPlan.ExecutionPlanSteps.SelectMany(s => s?.PreferredToolNames ?? Enumerable.Empty<string>()), toolCatalog);
                    AddToolNames(ordered, skillPlan.ExecutionPlanSteps.SelectMany(s => s?.AllowedToolNames ?? Enumerable.Empty<string>()), toolCatalog);
                    AddToolNames(ordered, skillPlan.ExecutionPlanSteps.SelectMany(s => s?.VerificationToolNames ?? Enumerable.Empty<string>()), toolCatalog);
                }
                AddToolNames(ordered, skillPlan.PreferredToolNames, toolCatalog);
                AddToolNames(ordered, skillPlan.AllowedToolNames, toolCatalog);
                AddToolNames(ordered, skillPlan.VerificationToolNames, toolCatalog);
            }
            foreach (var n in toolCatalog.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                if (!ordered.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) ordered.Add(n);
            return ordered;
        }

        public static void AddToolNames(IList<string> ordered, IEnumerable<string> names, IReadOnlyDictionary<string, HostBridgeToolDescriptor> catalog)
        {
            if (ordered == null || names == null || catalog == null) return;
            foreach (var n in names)
                if (!string.IsNullOrWhiteSpace(n) && catalog.ContainsKey(n) && !ordered.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                    ordered.Add(n);
        }

        /// <summary>
        /// 技能计划声明的推荐工具集合（<c>SKILL.md</c> 的 <c>allowedTools</c> 及各执行步骤的
        /// preferred / allowed / verification 工具名合集）。
        /// </summary>
        public static ISet<string> CollectSkillPlanToolNames(RuntimeSkillPlan skillPlan)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (skillPlan == null) return names;

            AddNonEmpty(names, skillPlan.AllowedToolNames);
            AddNonEmpty(names, skillPlan.PreferredToolNames);
            AddNonEmpty(names, skillPlan.VerificationToolNames);

            if (skillPlan.ExecutionPlanSteps != null)
            {
                foreach (var step in skillPlan.ExecutionPlanSteps)
                {
                    if (step == null) continue;
                    AddNonEmpty(names, step.AllowedToolNames);
                    AddNonEmpty(names, step.PreferredToolNames);
                    AddNonEmpty(names, step.VerificationToolNames);
                }
            }

            return names;
        }

        private static void AddNonEmpty(ISet<string> target, IEnumerable<string> names)
        {
            if (target == null || names == null) return;
            foreach (var n in names)
                if (!string.IsNullOrWhiteSpace(n)) target.Add(n.Trim());
        }

        /// <summary>
        /// 判断工具是否应当出现在发给模型的工具列表中。Excel 工具始终由工作簿级写入闸门
        /// 控制可执行范围；技能列表只影响推荐顺序，不作为权限白名单。
        /// </summary>
        public static bool ShouldExposeToolToModel(HostBridgeToolDescriptor tool, RuntimeSkillPlan skillPlan)
            => IsToolPermittedBySkillPlan(tool, skillPlan);

        public static bool IsInfrastructureTool(string toolName)
            => string.IsNullOrWhiteSpace(toolName) || toolName.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase) || toolName.StartsWith("host.", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// MCP 工具由用户自行配置，内置技能的 <c>allowedTools</c> 不可能预先列举，
        /// 不豁免就会在命中任意技能时整体消失。它们仍受 critical 硬门约束。
        /// </summary>
        public static bool IsWhitelistExemptTool(string toolName)
            => IsInfrastructureTool(toolName) || OfficeCopilot.Mcp.McpToolNaming.IsMcpTool(toolName);

        /// <summary>
        /// 非 Excel 宿主的技能是否获得任意代码执行授权。只看单一标志：旧实现要求
        /// <c>AllowHighRiskTools</c> 与 <c>AllowVbaFallback</c> 同时成立，两个语义重叠的标志
        /// 漏配任意一个都会让白名单静默失效且无任何告警（D9 就是这么来的）。
        /// </summary>
        public static bool AllowsCodeExecution(RuntimeSkillPlan skillPlan)
            => skillPlan != null && skillPlan.AllowCodeExecution;

        /// <summary>
        /// 技能工具白名单的唯一判定入口，同时用于「暴露给模型」与「运行时拦截」两处。
        /// </summary>
        public static bool IsToolPermittedBySkillPlan(HostBridgeToolDescriptor tool, RuntimeSkillPlan skillPlan)
        {
            if (tool == null) return false;
            if (skillPlan == null || !skillPlan.HasSkill) return true;

            if (IsExcelTool(tool.Name)) return true;

            // critical 级工具（VBA / 宏 / 任意代码执行等）是硬门：
            // 技能必须声明 allow-code-execution 才放行，且不受基础设施 / MCP 豁免影响。
            if (!AllowsCodeExecution(skillPlan) && IsCodeExecutionGated(tool)) return false;

            if (IsWhitelistExemptTool(tool.Name)) return true;

            var allowed = CollectSkillPlanToolNames(skillPlan);
            if (allowed.Count == 0) return true;

            return allowed.Contains(tool.Name ?? string.Empty);
        }

        /// <summary>
        /// 是否为会执行任意代码的 critical 级工具。风险等级由
        /// <c>ToolSafetyPolicy.InferRiskLevel</c> 在宿主侧推导后随工具目录一并下发。
        /// </summary>
        public static bool IsCriticalTool(HostBridgeToolDescriptor tool)
            => tool != null
                && !tool.IsReadOnly
                && string.Equals(tool.RiskLevel, "critical", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 是否受技能 <c>allow-code-execution</c> 硬门约束。<c>runtime.powershell</c> 豁免：
        /// 它由 <see cref="PowerShellPermissionChecker"/> 的只读自动放行与确认机制控制，
        /// 所有技能都可用于与文档无关的本地查询（如查询 IP、进程、文件）。
        /// </summary>
        public static bool IsCodeExecutionGated(HostBridgeToolDescriptor tool)
            => IsCriticalTool(tool) && !IsRuntimePowerShellTool(tool.Name);

        public static bool IsRuntimePowerShellTool(string toolName)
            => string.Equals(toolName, PowerShellTool.ToolName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 工具调用违反技能白名单时返回给模型的说明文字；未违反时返回空串。
        /// </summary>
        public static string GetSkillPlanViolationMessage(PlannedToolCall call, IReadOnlyDictionary<string, HostBridgeToolDescriptor> catalog, RuntimeSkillPlan plan)
            => GetSkillPlanViolationMessage(call?.ToolName, catalog, plan);

        public static string GetSkillPlanViolationMessage(string toolName, IReadOnlyDictionary<string, HostBridgeToolDescriptor> catalog, RuntimeSkillPlan plan)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return string.Empty;
            if (plan == null || !plan.HasSkill) return string.Empty;
            if (IsExcelTool(toolName)) return string.Empty;

            HostBridgeToolDescriptor definition = null;
            if (catalog != null) catalog.TryGetValue(toolName, out definition);

            if (!AllowsCodeExecution(plan) && IsCodeExecutionGated(definition))
            {
                return "Tool '" + toolName + "' executes arbitrary code and is blocked because the active skill '"
                    + (plan.SkillName ?? string.Empty)
                    + "' does not grant code-execution authority. Do one of the following, in order: "
                    + "(1) re-express the work with a native Office tool \u2014 excel_aggregate supports in-memory derived group keys "
                    + "(year/quarter/month/monthName/yearMonth/day/dayOfWeek/numberBucket/prefix) so date and bucket grouping needs no helper column and no code; "
                    + "runtime.invoke_skill only returns guidance and does not change the active tool policy. "
                    + "Use a suitable tool already permitted by the current plan; if none can perform the requested operation, tell the user what is blocked and why.";
            }

            if (IsWhitelistExemptTool(toolName)) return string.Empty;

            var allowed = CollectSkillPlanToolNames(plan);
            if (allowed.Count == 0 || allowed.Contains(toolName)) return string.Empty;

            // 白名单里的名字如果一个都不在当前宿主的工具目录中（例如在 Word 里命中了一个
            // 面向 Excel 的技能），说明白名单对本次会话没有意义，此时不做拦截。
            if (catalog != null && catalog.Count > 0 && !allowed.Any(catalog.ContainsKey)) return string.Empty;

            var b = new StringBuilder();
            b.Append("Tool '").Append(toolName).Append("' is not allowed by the active skill '")
                .Append(plan.SkillName ?? string.Empty).Append("'.");

            var usable = catalog == null
                ? allowed.ToList()
                : allowed.Where(catalog.ContainsKey).ToList();
            if (usable.Count > 0)
                b.Append(" Allowed tools: ").Append(string.Join(", ", usable.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))).Append('.');

            b.Append(" Choose one of them, or explain why the request cannot be completed.");
            return b.ToString();
        }

        private static bool IsExcelTool(string toolName)
            => !string.IsNullOrWhiteSpace(toolName)
                && toolName.StartsWith("excel_", StringComparison.OrdinalIgnoreCase);

        public static bool IsHighRiskTool(HostBridgeToolDescriptor tool)
            => tool != null && !tool.IsReadOnly && (string.Equals(tool.RiskLevel, "high", StringComparison.OrdinalIgnoreCase) || string.Equals(tool.RiskLevel, "critical", StringComparison.OrdinalIgnoreCase));
        public static bool IsVbaFallbackTool(string toolName)
            => !string.IsNullOrWhiteSpace(toolName) && (string.Equals(toolName, HostBridge.Contracts.HostBridgeOperationNames.ExecuteCode, StringComparison.OrdinalIgnoreCase) || toolName.IndexOf("vba", StringComparison.OrdinalIgnoreCase) >= 0 || toolName.IndexOf("macro", StringComparison.OrdinalIgnoreCase) >= 0 || toolName.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0);
        public static bool ShouldContinueForPendingVerification(RuntimeSkillPlan p)
        {
            if (p == null || !p.HasStructuredExecutionPlan)
                return false;
            if (p.HasStartedMutationAwaitingVerification())
            {
                return true;
            }

            var currentStep = p.GetCurrentExecutionStep();
            // 只催促当前步骤，不能因为计划中有后续 verify 就跳过前面的写入阶段。
            return p.CompletedExecutionStepCount > 0
                && currentStep != null
                && string.Equals(currentStep.StepKind, "verify", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldContinueForPendingMutation(
            RuntimeSkillPlan p,
            TurnExecutionContract executionContract)
        {
            if (p == null
                || !p.HasStructuredExecutionPlan
                || executionContract == null
                || !executionContract.IsDeclared
                || !ToolWriteEffectLevels.IsWithinLimit(
                    ToolWriteEffectLevels.Append,
                    executionContract.MaxWriteEffect))
            {
                return false;
            }

            var currentStep = p.GetCurrentExecutionStep();
            return p.CompletedExecutionStepCount > 0
                && currentStep != null
                && !string.Equals(currentStep.StepKind, "inspect", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentStep.StepKind, "verify", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(currentStep.StepKind, "general", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldBlockWriteForPendingVerification(RuntimeSkillPlan p, bool isWriteTool)
        {
            if (p == null || !isWriteTool || !p.HasStructuredExecutionPlan)
            {
                return false;
            }

            var currentStep = p.GetCurrentExecutionStep();
            return currentStep != null
                && string.Equals(currentStep.StepKind, "verify", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(currentStep.VerifiesStepId);
        }

        public static bool CanRecoverFromVerificationFailure(RuntimeSkillPlan p, string tn) => p != null && p.HasStructuredExecutionPlan && p.IsVerificationTool(tn) && p.CanRetryVerificationFailure();

        /// <summary>
        /// 工具自带的 expect/守恒校验没过（如 <c>excel.execute_vba_expectation_failed</c>）。
        /// 这是工具在自证失败，语义等同于一次验证失败，必须走同一条回退重做路径，
        /// 否则「执行工具自己发现结果不对」反而拿不到重试机会。
        /// </summary>
        public static bool IsSelfVerificationFailureCode(string errorCode)
            => !string.IsNullOrWhiteSpace(errorCode)
                && (errorCode.IndexOf("expectation_failed", StringComparison.OrdinalIgnoreCase) >= 0
                    || errorCode.IndexOf("assertion_failed", StringComparison.OrdinalIgnoreCase) >= 0);

        public static bool CanRecoverFromVerificationFailure(RuntimeSkillPlan p, string toolName, string errorCode)
            => p != null
                && p.HasStructuredExecutionPlan
                && (p.IsVerificationTool(toolName) || IsSelfVerificationFailureCode(errorCode))
                && p.CanRetryVerificationFailure();

        /// <summary>重试预算已经用尽（用过至少一次且不能再用），用于区分"从未允许重试"和"重试到头了"。</summary>
        public static bool HasExhaustedVerificationRetries(RuntimeSkillPlan p, string toolName, string errorCode)
            => p != null
                && p.HasStructuredExecutionPlan
                && (p.IsVerificationTool(toolName) || IsSelfVerificationFailureCode(errorCode))
                && p.VerificationRetryCount > 0
                && !p.CanRetryVerificationFailure();

        public static string BuildPendingMutationSystemPrompt(SkillExecutionStep step)
        {
            var b = new StringBuilder();
            b.AppendLine("Do not provide the final answer yet.");
            b.AppendLine("A mutation step is still pending. Complete it before summarizing.");
            if (step != null && !string.IsNullOrWhiteSpace(step.Instruction)) b.Append("Pending: ").AppendLine(step.Instruction.Trim());
            if (step?.PreferredToolNames?.Count > 0) b.Append("Tools: ").AppendLine(string.Join(", ", step.PreferredToolNames.Where(n => !string.IsNullOrWhiteSpace(n))));
            else if (step?.AllowedToolNames?.Count > 0) b.Append("Tools: ").AppendLine(string.Join(", ", step.AllowedToolNames.Where(n => !string.IsNullOrWhiteSpace(n))));
            return b.ToString().Trim();
        }

        public static string AppendPendingMutationNotice(string content, SkillExecutionStep step)
        {
            var b = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(content)) { b.Append(content.Trim()); b.AppendLine(); b.AppendLine(); }
            b.Append("A mutation step is still pending");
            if (step != null && !string.IsNullOrWhiteSpace(step.Title)) b.Append(": ").Append(step.Title.Trim());
            b.Append(". Complete the change before finishing.");
            return b.ToString().Trim();
        }

        public static string BuildVerificationFailureSystemPrompt(RuntimeSkillPlan plan, string toolName, string msg)
        {
            var b = new StringBuilder();
            b.AppendLine("Verification did not succeed. Adjust and retry before final answer.");
            if (!string.IsNullOrWhiteSpace(toolName)) b.Append("Verifier: ").AppendLine(toolName.Trim());
            if (!string.IsNullOrWhiteSpace(msg)) b.Append("Result: ").AppendLine(msg.Trim());
            if (plan != null) b.Append("Retry: ").Append(plan.VerificationRetryCount).Append("/").AppendLine(plan.MaxVerificationRetryCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var cs = plan?.GetCurrentExecutionStep();
            if (cs?.PreferredToolNames?.Count > 0) b.Append("Retry with: ").Append(string.Join(", ", cs.PreferredToolNames.Where(n => !string.IsNullOrWhiteSpace(n))));
            var alts = plan?.GetRetryCandidateMutationSteps().ToList();
            if (alts != null && alts.Count > 0) { b.AppendLine(); b.Append("Or switch to: ").Append(string.Join(", ", alts.Select(s => string.IsNullOrWhiteSpace(s.SkillName) ? s.Title ?? string.Empty : s.SkillName).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))); }
            return b.ToString().Trim();
        }

        public static string BuildVerificationRetryLimitSystemPrompt(RuntimeSkillPlan plan, string toolName, string msg)
        {
            var b = new StringBuilder();
            b.AppendLine("Verification retry limit reached. Do not retry. Explain the limitation.");
            if (!string.IsNullOrWhiteSpace(toolName)) b.Append("Verifier: ").AppendLine(toolName.Trim());
            if (!string.IsNullOrWhiteSpace(msg)) b.Append("Result: ").AppendLine(msg.Trim());
            if (plan != null) b.Append("Attempts: ").Append(plan.VerificationRetryCount).Append("/").Append(plan.MaxVerificationRetryCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return b.ToString().Trim();
        }

        public static string BuildExecutionPlanStateText(RuntimeSkillPlan plan)
        {
            if (plan == null || !plan.HasStructuredExecutionPlan) return "Execution plan state: none";
            var b = new StringBuilder(); AppendExecutionPlanState(b, plan); return b.ToString().Trim();
        }

        public static string BuildPendingVerificationSystemPrompt(SkillExecutionStep step)
        {
            var b = new StringBuilder();
            b.AppendLine("Do not provide the final answer yet. Complete verification first.");
            if (step?.VerificationToolNames?.Count > 0) b.Append("Use: ").AppendLine(string.Join(", ", step.VerificationToolNames.Where(n => !string.IsNullOrWhiteSpace(n))));
            if (step != null && !string.IsNullOrWhiteSpace(step.Instruction)) b.Append("Instruction: ").Append(step.Instruction.Trim());
            return b.ToString().Trim();
        }

        public static string AppendVerificationPendingNotice(string content, SkillExecutionStep step)
        {
            var b = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(content)) { b.Append(content.Trim()); b.AppendLine(); b.AppendLine(); }
            b.Append("验证未完成");
            if (step != null && !string.IsNullOrWhiteSpace(step.Title)) b.Append("（").Append(step.Title.Trim()).Append("）");
            b.Append("：请在结束前完成对结果的验证。");
            return b.ToString().Trim();
        }

        public static string AppendVerificationSummary(string content, string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) return content ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content)) return summary;
            return content + "\n\n" + summary;
        }

        public static void WriteDiagnosticLog(string category, string conversationId, string messageId, string text)
        {
            Diagnostics.TurnTraceWriter.Write(conversationId, messageId, "diagnostic", new Dictionary<string, object>
            {
                ["category"] = category ?? string.Empty,
                ["text"] = Diagnostics.TurnTraceWriter.Truncate(text, 20000)
            });
        }
    }
}
