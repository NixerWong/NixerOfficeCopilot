using System;
using System.Text;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 进入 Plan 模式工具：模型调用此工具表示需要先探索和设计，再执行修改。
    /// 对应 cc-haha <c>EnterPlanModeTool.ts</c>。Phase 3 增强：完整 prompt。
    /// </summary>
    internal sealed class EnterPlanModeTool
    {
        public const string ToolName = "runtime.enter_plan_mode";

        private readonly IPermissionGate _gate;

        public EnterPlanModeTool(IPermissionGate gate)
        {
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public ToolExecutionOutcome Execute(string toolCallId, string toolName)
        {
            _gate.SetMode(PermissionMode.Plan);

            var content = BuildEnterPlanModePrompt();

            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = content
            };
        }

        /// <summary>
        /// Phase 3: 完整 Plan Mode 提示，移植自 cc-haha EnterPlanModeTool prompt.ts。
        /// </summary>
        internal static string BuildEnterPlanModePrompt()
        {
            return new StringBuilder()
                .AppendLine("Entered Plan Mode.")
                .AppendLine()
                .AppendLine("## What Happens in Plan Mode")
                .AppendLine()
                .AppendLine("In plan mode, you will:")
                .AppendLine("1. Thoroughly explore the context using runtime.list_tools and runtime.get_host_context to understand the current state")
                .AppendLine("2. Understand existing data, structures, and constraints")
                .AppendLine("3. Design an implementation approach")
                .AppendLine("4. Present your plan to the user for approval via runtime.exit_plan_mode")
                .AppendLine("5. Use runtime.ask_user_question if you need to clarify approaches before finalizing")
                .AppendLine()
                .AppendLine("## When to Use This Tool")
                .AppendLine()
                .AppendLine("Prefer using EnterPlanMode for implementation tasks unless they are simple. Use it when ANY of these apply:")
                .AppendLine()
                .AppendLine("1. **New Feature**: Adding meaningful new functionality (e.g. 'Add a chart to this sheet')")
                .AppendLine("2. **Multiple Approaches**: The task can be solved in several different ways")
                .AppendLine("3. **Data Modifications**: Changes that affect existing data or structure")
                .AppendLine("4. **Multi-Step Changes**: The task will likely require multiple tool calls across different operations")
                .AppendLine("5. **Unclear Requirements**: You need to explore before understanding the full scope")
                .AppendLine("6. **User Preference Matters**: The implementation could reasonably go multiple ways")
                .AppendLine()
                .AppendLine("## When NOT to Use")
                .AppendLine()
                .AppendLine("Skip EnterPlanMode for simple tasks:")
                .AppendLine("- Reading or inspecting data without modifications")
                .AppendLine("- Single-cell or single-range simple updates")
                .AppendLine("- Tasks where the user has given very specific, detailed instructions")
                .AppendLine("- Pure exploration/inspection tasks")
                .AppendLine()
                .AppendLine("## Examples")
                .AppendLine()
                .AppendLine("GOOD - Use EnterPlanMode:")
                .AppendLine("- User: 'Help me analyze this sales data and create a summary report' (multi-step, needs design)")
                .AppendLine("- User: 'Reformat this entire worksheet with consistent styling' (affects many cells)")
                .AppendLine("- User: 'Add formulas to calculate quarterly projections' (architectural decision)")
                .AppendLine()
                .AppendLine("BAD - Don't use EnterPlanMode:")
                .AppendLine("- User: 'What is the value in cell B5?' (simple read)")
                .AppendLine("- User: 'Make this cell bold' (single-cell formatting)")
                .AppendLine("- User: 'What worksheets are in this workbook?' (exploration)")
                .AppendLine()
                .AppendLine("IMPORTANT: In Plan Mode, ONLY read-only tools are allowed.")
                .AppendLine("Do NOT attempt to modify data, write to cells, format ranges, or execute code.")
                .AppendLine("Focus on understanding and designing — implementation will follow after plan approval.")
                .ToString();
        }
    }

    /// <summary>
    /// 退出 Plan 模式工具：模型展示计划并请求用户批准后退出只读模式。
    /// 对应 cc-haha <c>ExitPlanModeV2Tool.ts</c>。Phase 3 增强：写入 plan 文件 + 用户审批。
    /// </summary>
    internal sealed class ExitPlanModeTool
    {
        public const string ToolName = "runtime.exit_plan_mode";

        private readonly IPermissionGate _gate;
        internal Action<string> OnPlanWritten { get; set; }

        public ExitPlanModeTool(IPermissionGate gate)
        {
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        /// <summary>
        /// Phase 3: 执行退出 Plan 模式。支持写入 plan 文件。
        /// 返回 (outcome, planFilePath) — planFilePath 非空表示写入了文件，Panel 应该展示审批 UI。
        /// </summary>
        public ToolExecutionOutcome Execute(string toolCallId, string toolName, string argumentsJson)
        {
            var planText = string.Empty;
            var allowedPrompts = string.Empty;

            if (!string.IsNullOrWhiteSpace(argumentsJson))
            {
                try
                {
                    using (var doc = JsonDocument.Parse(argumentsJson))
                    {
                        if (doc.RootElement.TryGetProperty("plan", out var planProp))
                            planText = planProp.GetString() ?? string.Empty;

                        if (doc.RootElement.TryGetProperty("allowedPrompts", out var promptsProp) &&
                            promptsProp.ValueKind == JsonValueKind.Array)
                            allowedPrompts = promptsProp.GetRawText();
                    }
                }
                catch
                {
                    // Non-critical
                }
            }

            string planFilePath = null;

            // Write plan to file if plan text is provided
            if (!string.IsNullOrWhiteSpace(planText))
            {
                try
                {
                    planFilePath = PlanFileManager.WritePlan(planText);
                    OnPlanWritten?.Invoke(planFilePath);
                }
                catch (Exception)
                {
                }
            }

            // Switch to AcceptEdits mode (user approved plan)
            _gate.SetMode(PermissionMode.AcceptEdits);

            var content = new StringBuilder()
                .AppendLine("Exited Plan Mode. Write operations are now allowed.")
                .AppendLine();

            if (!string.IsNullOrWhiteSpace(planFilePath))
            {
                content.AppendLine("Plan file written to: " + planFilePath);
                content.AppendLine();
            }

            content.AppendLine("Now execute the approved plan step by step.");
            content.AppendLine("Use the tools you identified during planning to implement the changes.");
            content.AppendLine("After each modification step, verify the result.");

            if (!string.IsNullOrWhiteSpace(planText))
            {
                content.AppendLine();
                content.AppendLine("=== Approved Plan ===");
                content.AppendLine(planText);
            }

            if (!string.IsNullOrWhiteSpace(allowedPrompts))
            {
                content.AppendLine();
                content.AppendLine("=== Allowed Prompts ===");
                content.AppendLine(allowedPrompts);
            }

            content.AppendLine();
            content.Append("When finished, summarize the outcome.");

            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = content.ToString()
            };
        }
    }
}
