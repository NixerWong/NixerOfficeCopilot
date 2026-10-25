using System;
using System.Linq;
using System.Text;

namespace OfficeCopilot.Agent.Planning
{
    public static class SkillExecutionPromptBuilder
    {
        public static string Build(SkillDefinition skill, SkillSelectionResult selection, string userInput = null)
        {
            if (skill == null && (selection == null || !selection.HasSelection))
            {
                return string.Empty;
            }

            var isExcelSelection = HasExcelTools(skill, selection);
            var builder = new StringBuilder();
            builder.AppendLine("Follow the active Office skill policy for this turn.");

            if (skill != null)
            {
                builder.Append("- Active skill: ")
                    .Append(skill.Name ?? string.Empty);

                if (!string.IsNullOrWhiteSpace(skill.Description))
                {
                    builder.Append(". ")
                        .Append(skill.Description.Trim());
                }

                builder.AppendLine();
            }

            if (selection != null && selection.AllowedToolNames != null && selection.AllowedToolNames.Count > 0)
            {
                builder.Append(isExcelSelection
                        ? "- Recommended tools for this turn (not an access-control list): "
                        : "- Suggested tools for this turn: ")
                    .AppendLine(string.Join(", ", selection.AllowedToolNames.Where(name => !string.IsNullOrWhiteSpace(name))));
            }

            if (isExcelSelection)
            {
                builder.AppendLine("- Excel skill tool lists are recommendations, not permissions; do not infer tool availability from a selected skill.");
                builder.AppendLine("- Only a worksheet registered as this session's WIP is staging scratch; the `_wip_` name prefix alone grants no exemption.");
            }

            if (selection != null && selection.CompanionSkillNames != null && selection.CompanionSkillNames.Count > 0)
            {
                builder.Append("- Companion skills selected for this turn: ")
                    .AppendLine(string.Join(", ", selection.CompanionSkillNames.Where(name => !string.IsNullOrWhiteSpace(name))));
                builder.AppendLine("- You may combine read or analysis steps with the companion skill tools when they are required to finish the same user request safely.");
            }

            if (selection != null && selection.ExecutionPlanSteps != null && selection.ExecutionPlanSteps.Count > 0)
            {
                builder.AppendLine("- Execute in this order:");
                if (selection.ExecutionPlanSteps.Any(step => step != null
                    && !string.IsNullOrWhiteSpace(step.VerifiesStepId)))
                {
                    builder.AppendLine("- A verify step with a target stage is a hard barrier between write stages: finish all writes belonging to that stage, verify them, and do not start a later write stage until verification passes.");
                }

                var stepIndex = 1;
                foreach (var step in selection.ExecutionPlanSteps.Where(step => step != null))
                {
                    builder.Append("  ")
                        .Append(stepIndex)
                        .Append(". ")
                        .Append(string.IsNullOrWhiteSpace(step.Title) ? "Step" : step.Title.Trim());

                    if (!string.IsNullOrWhiteSpace(step.SkillName))
                    {
                        builder.Append(" [")
                            .Append(step.SkillName.Trim())
                            .Append("]");
                    }

                    builder.Append(": ")
                        .AppendLine((step.Instruction ?? string.Empty).Trim());
                    if (string.Equals(step.StepKind, "verify", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(step.VerifiesStepId))
                    {
                        builder.Append("     Verifies stage: ")
                            .AppendLine(step.VerifiesStepId);
                    }

                    if (step.PreferredToolNames != null && step.PreferredToolNames.Count > 0)
                    {
                        builder.Append("     Preferred tools: ")
                            .AppendLine(string.Join(", ", step.PreferredToolNames.Where(name => !string.IsNullOrWhiteSpace(name))));
                    }

                    if (step.AllowedToolNames != null && step.AllowedToolNames.Count > 0)
                    {
                        builder.Append("     Useful tools for this step: ")
                            .AppendLine(string.Join(", ", step.AllowedToolNames.Where(name => !string.IsNullOrWhiteSpace(name))));
                    }

                    if (step.RequireVerification && step.VerificationToolNames != null && step.VerificationToolNames.Count > 0)
                    {
                        builder.Append("     Verify with: ")
                            .AppendLine(string.Join(", ", step.VerificationToolNames.Where(name => !string.IsNullOrWhiteSpace(name))));
                    }

                    stepIndex++;
                }
            }
            else if (selection != null && selection.ExecutionSteps != null && selection.ExecutionSteps.Count > 0)
            {
                builder.AppendLine("- Execute in this order:");

                var stepIndex = 1;
                foreach (var step in selection.ExecutionSteps.Where(step => !string.IsNullOrWhiteSpace(step)))
                {
                    builder.Append("  ")
                        .Append(stepIndex)
                        .Append(". ")
                        .AppendLine(step.Trim());
                    stepIndex++;
                }
            }

            if (selection != null && selection.PreferredToolNames != null && selection.PreferredToolNames.Count > 0)
            {
                builder.Append("- Prefer these tools first: ")
                    .AppendLine(string.Join(", ", selection.PreferredToolNames.Where(name => !string.IsNullOrWhiteSpace(name))));
            }

            if (selection != null
                && selection.AllowedToolNames != null
                && selection.AllowedToolNames.Any(name =>
                    string.Equals(name, "excel_aggregate", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "excel_execute_vba", System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "excel_set_range_formulas", System.StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("- Before calculating or transforming data, establish the basis from the user's request and actual workbook headers/profile: source, date field, filters/exclusions, group keys, measures and aggregation.");
                builder.AppendLine("- If a required column/date field or filter has multiple plausible meanings, do not guess: call runtime.ask_user_question with 2-4 concrete choices based on the workbook and wait for the answer. Do not ask again when the user has already stated the basis clearly.");
                builder.AppendLine("- Before reading or transforming data, also ask with runtime.ask_user_question when the requested operation has materially different interpretations, such as duplicate keys or which record to keep, join keys or join type, output shape or destination, or what 'clean up'/'optimize' means. Do not choose a default or substitute a text-only question; ask only for the decision needed and wait for the user's answer.");
                builder.AppendLine("- For a request to remove or delete duplicates, if the user has not specified both the duplicate key and which row to retain, make runtime.ask_user_question the first tool call. Do not inspect the workbook to infer the rule or defer the question to the final response.");
                builder.AppendLine("- Keep the confirmed basis identical in calculation and verification; declare derived groups with source column, transform and alias in excel_assert.basis.derivedGroupBy or excel_execute_vba.expect.basis.derivedGroupBy. The excel_aggregate request itself is its basis.");
                builder.AppendLine("- In excel_assert, source.column and target.column/columns are side-specific physical headers. basis.measures defines the source metric; it does not name the target column. When validating a labeled output table, include its header row in target.address, set basis.headerMode explicitly, and name the target's actual header (for example source Amount -> target Value). Specify target columns for nonBlank assertions too.");
                builder.AppendLine("- Use runtime.calculate for any derived number you present, including shares, growth rates, differences and arithmetic cross-checks; do not calculate them mentally. Pass exact numeric operands from tool results without rounding or rewriting them; round only the final displayed result. For percentage shares, calculate the 0-to-1 ratio (numerator / denominator), not the ratio multiplied by 100, then present it as a percentage. Every operand must appear in user/tool evidence, except integer constants from 0 to 100.");
                builder.AppendLine("- In the final answer, report only the results the user requested. Do not add unrequested row counts, percentages, backend comparison values, derived counts/totals, arithmetic reconciliations or example formulas.");
            }

            if (selection != null
                && selection.AllowedToolNames != null
                && selection.AllowedToolNames.Any(name => string.Equals(name, "excel_aggregate", System.StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("- Start from table structure, not raw rows: call excel_profile_range once to learn columns, types, distinct counts and value distribution.");
                builder.AppendLine("- For whole-table totals, call excel_profile_range with the explicit worksheetName and no address to resolve that worksheet's complete UsedRange, then aggregate over the returned range. Do not substitute the active selection, preview, or a guessed partial range.");
                builder.AppendLine("- For grouping, subtotals, distinct counts, ranking or share-of-total, call excel_aggregate. Never page through the whole table and compute in your head.");
                builder.AppendLine("- When a group key is a simple derived value, use excel_aggregate.derivedGroupBy first; it is in-memory and never writes a helper column. Text: { column: \"Name\", transform: \"firstCharacter\", alias: \"Initial\" }. Date parts: transform year, quarter, month, monthName, yearMonth, day or dayOfWeek on a date-formatted column — average salary by birth month is { column: \"Birthday\", transform: \"month\" } plus an average measure on the salary column. Numeric ranges: { transform: \"numberBucket\", size: 1000 }.");
                builder.AppendLine("- Do not conclude that a derivation is impossible before checking the transform list above. Extracting a month, quarter or year from a date needs no code execution and no helper column.");
                builder.AppendLine("- To verify a summary against details, call excel_reconcile (total + group count + sampling). Never compare row by row.");
                builder.AppendLine("- The cost of excel_profile_range and excel_aggregate does not grow with row count, so a 1,000-row or 100,000-row table takes the same number of calls.");
                builder.AppendLine("- For a column containing Excel errors or mixed native numbers and numeric text, avoid native excel_analyze_range SUM. Use excel_aggregate with isNativeNumber / isTextNumber filters to keep the two numeric categories separate; its sum ignores errors and includes parseable numeric text consistently.");
                builder.AppendLine("- For data-quality counts, use the profile's exact type counts and excel_aggregate errorCount for the independently checked error count. Do not use countDistinct as a substitute for a type or error count; use runtime.calculate for explicitly requested derived counts.");
            }

            if (selection != null
                && selection.AllowedToolNames != null
                && selection.AllowedToolNames.Any(name => string.Equals(name, "excel_compare_key_sets", System.StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("- To compare key membership across two tables or sheets, use excel_compare_key_sets; it returns both unmatched counts and bounded deterministic samples. Do not enumerate keys or build a large in-filter. Use excel_assert keySet only when the required conclusion is whether two sets are equal.");
            }

            if (selection != null
                && selection.AllowedToolNames != null
                && selection.AllowedToolNames.Any(name => string.Equals(
                    name,
                    "excel_get_range_info",
                    System.StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("- If asked to report hidden rows, first resolve the complete source range with excel_profile_range, then call excel_get_range_info for that worksheet/address with includeHiddenRows:true. Report row numbers only when hiddenRowsScanComplete is true; the complete scan covers manually hidden and filter-hidden worksheet rows. A complete successful scan is the verification; do not read row contents or perform a second verification unless requested. Report only the requested hidden-row count and absolute row numbers. Do not infer or report visible-row counts, remaining-row counts, record ordinals, or other unrequested numeric summaries.");
            }

            if (selection != null
                && selection.AllowedToolNames != null
                && selection.AllowedToolNames.Any(name => string.Equals(name, "excel_set_range_format", System.StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("- A successful excel_set_range_format or VBA call only proves the call did not throw; it does not prove the cells look right. After any formatting change, read it back with excel_get_range_style and compare the actual fillColor / fontColor / bold / numberFormat against what you intended.");
                builder.AppendLine("- excel_get_range_style returns \"mixed\" for a property whose value differs across the range. \"mixed\" means the readback is inconclusive, never that the property is unset — narrow the address to a single cell and read again before drawing a conclusion.");
            }

            if (HasToolName(skill, selection, "excel_execute_vba"))
            {
                builder.AppendLine("- Prefer dedicated Excel analysis, formula, and write tools when they fully meet the request. Use excel_execute_vba when the requested bulk or structural operation is not adequately served by those tools, or when the user explicitly asks for VBA.");
                builder.AppendLine("- Before VBA writes, inspect the workbook map and source profile, declare the actual output worksheet/range and source in the execution contract, and do not write outside declared targets. For multi-stage work, prefer a new _wip_NN_description sheet and include expect with the expected columns and row bounds; the _wip_ name alone does not make a sheet scratch.");
                builder.AppendLine("- Row counts and column names do not prove the numbers are right. Whenever a stage moves or summarizes amounts, add expect.conserve comparing the stage total back to the source detail, e.g. { function: \"sum\", column: \"Total Amount\", tolerance: 0.01, compareTo: { worksheetName: \"Sales Detail\", column: \"Amount\" } }. A stage whose expect fails is reported as a failed tool call, so do not treat it as done.");
                builder.AppendLine("- On a table with more than a few hundred rows, set probe.rows on the first stage that touches it. The code then runs on a copied sample first; only after expect passes on the sample does it run on the full data. This turns a wrong column name into a cheap sample failure instead of a damaged sheet.");
                builder.AppendLine("- Do not use .Select, .Activate, Selection, or ActiveCell in the generated VBA; they throw error 1004 whenever the target worksheet is not the active sheet. Operate on Worksheet and Range objects directly.");
                builder.AppendLine("- First profile the source worksheet by name. Then run one _wip_ stage, inspect its returned profile and NixerLog output, and only then aggregate or reconcile the derived table. Never use an unrelated current selection as the source.");
                builder.AppendLine("- Keep _wip_ sheets while reporting the result so the calculation remains inspectable. Use excel_cleanup_workspace only after the user confirms cleanup or after explicitly reporting which intermediate sheets will be removed.");
                builder.AppendLine("- State the verification conclusion and the intermediate sheets used. Do not claim the source workbook was unchanged when a derived worksheet was created.");
            }

            if (isExcelSelection && TraceabilityIntentPolicy.RequiresTraceableSteps(userInput))
            {
                builder.AppendLine("- The user asked for verifiable, traceable, step-by-step results: materialize key intermediate results into _wip_NN_ tables even when in-memory aggregation would be enough, and keep them for inspection instead of cleaning up early.");
            }

            if (selection != null
                && selection.AllowedToolNames != null
                && selection.AllowedToolNames.Any(name => string.Equals(name, "excel_analyze_range", System.StringComparison.OrdinalIgnoreCase)))
            {
                builder.AppendLine("- For max/min/average/sum/count/rank/top and matching a related row by an extreme value, prefer excel_analyze_range as soon as the target range is known.");
                builder.AppendLine("- If a full target range has already been read, or excel_analyze_range already returned the needed result, stop calling tools and give the final answer immediately.");
                builder.AppendLine("- Do not keep probing with repeated search calls such as excel_find_in_range just to prove there is no larger or smaller value.");
                builder.AppendLine("- If the current evidence is sufficient for a best-effort conclusion, answer directly and state any remaining limitation briefly instead of continuing to search.");
            }

            if (selection != null && selection.RequireVerification)
            {
                if (selection.VerificationToolNames != null && selection.VerificationToolNames.Count > 0)
                {
                    builder.Append("- Verify write operations with: ")
                        .AppendLine(string.Join(", ", selection.VerificationToolNames.Where(name => !string.IsNullOrWhiteSpace(name))));
                }
                else
                {
                    builder.AppendLine("- Verify every write operation before reporting success.");
                }
            }

            if (selection != null && selection.RequiresConfirmation)
            {
                builder.AppendLine("- Ask for confirmation before destructive or wide-scope changes.");
            }

            if (selection != null && selection.RequiresBackup)
            {
                builder.AppendLine("- Prefer creating or preserving a backup before risky changes.");
            }

            if (selection != null && !selection.AllowHighRiskTools)
            {
                builder.AppendLine("- Use high-risk or destructive tools only when they are clearly necessary and the user intent is explicit.");
            }

            if (selection != null && !selection.AllowCodeExecution && !isExcelSelection)
            {
                builder.AppendLine("- This skill has no code-execution authority. Express the work with native Office tools; do not plan around excel_execute_vba.");
            }

            if (selection != null && selection.MaxToolRoundCount > 0)
            {
                builder.Append("- Finish within ")
                    .Append(selection.MaxToolRoundCount)
                    .AppendLine(" tool rounds, and end the turn early once the answer is supported.");
            }

            builder.AppendLine("- If the available tools cannot safely complete the request, explain the limitation instead of claiming success.");

            return builder.ToString().Trim();
        }

        private static bool HasExcelTools(SkillDefinition skill, SkillSelectionResult selection)
        {
            return HasToolName(skill, selection, null, true);
        }

        private static bool HasToolName(
            SkillDefinition skill,
            SkillSelectionResult selection,
            string toolName,
            bool excelToolsOnly = false)
        {
            bool Matches(string candidate)
                => excelToolsOnly
                    ? !string.IsNullOrWhiteSpace(candidate)
                        && candidate.StartsWith("excel_", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(candidate, toolName, StringComparison.OrdinalIgnoreCase);

            return (skill != null
                    && ((skill.AllowedToolNames != null && skill.AllowedToolNames.Any(Matches))
                        || (skill.PreferredToolNames != null && skill.PreferredToolNames.Any(Matches))))
                || (selection != null
                    && ((selection.AllowedToolNames != null && selection.AllowedToolNames.Any(Matches))
                        || (selection.PreferredToolNames != null && selection.PreferredToolNames.Any(Matches))));
        }
    }
}
