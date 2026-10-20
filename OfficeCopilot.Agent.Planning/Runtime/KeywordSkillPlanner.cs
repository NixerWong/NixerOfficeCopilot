using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Planning
{
    public sealed class KeywordSkillPlanner : ISkillPlanner
    {
        public Task<SkillSelectionResult> SelectSkillAsync(
            SkillPlannerRequest request,
            ISkillRegistry skillRegistry,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var skills = skillRegistry == null
                ? new List<SkillDefinition>()
                : skillRegistry.GetSkills().Where(skill => skill != null && skill.IsEnabled).ToList();

            var requestedName = request == null ? null : request.RequestedSkillName;
            if (!string.IsNullOrWhiteSpace(requestedName) && skillRegistry != null)
            {
                var requestedSkill = skillRegistry.FindByName(requestedName);
                if (requestedSkill != null && !requestedSkill.IsEnabled)
                {
                    return Task.FromResult(new SkillSelectionResult
                    {
                        Reason = $"Requested skill '{requestedSkill.Name}' is disabled.",
                        DisabledRequestedSkillName = requestedSkill.Name ?? requestedName
                    });
                }
            }

            if (skills.Count == 0)
            {
                return Task.FromResult(new SkillSelectionResult());
            }

            var hostSkills = skills
                .Where(skill => MatchesHost(skill, request) && !SkillInvocationPolicy.IsModelInvocationDisabled(skill))
                .ToList();
            var scoredSkills = hostSkills
                .Select(skill => new SkillScoreCandidate
                {
                    Skill = skill,
                    Score = HybridSkillScorer.Score(skill, request)
                })
                .Where(item => !RequiresKeywordMatch(item.Skill) || (item.Score != null && item.Score.KeywordScore > 0))
                .OrderByDescending(item => item.Score.TotalScore)
                .ThenBy(item => item.Skill.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var explicitlyRequestedSkill = TryFindRequestedSkill(skills, request == null ? null : request.RequestedSkillName);
            if (explicitlyRequestedSkill != null && SkillInvocationPolicy.IsUserInvocationDisabled(explicitlyRequestedSkill))
            {
                // user-invocable: false，用户点名无效，回落到自动规划
                explicitlyRequestedSkill = null;
            }
            if (explicitlyRequestedSkill != null)
            {
                var explicitResult = BuildSelectionResult(
                    explicitlyRequestedSkill,
                    request,
                    100,
                    "Selected the explicitly requested skill.",
                    0.99d,
                    scoredSkills);
                explicitResult.CandidateSkillNames.Add(explicitlyRequestedSkill.Name ?? string.Empty);
                explicitResult.CandidateSkillSummaries.Add("Selected the explicitly requested skill.");
                return Task.FromResult(explicitResult);
            }

            var structuredIntent = StructuredExcelIntentClassifier.Classify(request);
            var structuredSkill = structuredIntent == null
                ? null
                : TryFindRequestedSkill(hostSkills, structuredIntent.SkillName);
            if (structuredSkill != null)
            {
                var structuredResult = BuildSelectionResult(
                    structuredSkill,
                    request,
                    100,
                    structuredIntent.Reason,
                    structuredIntent.Confidence,
                    scoredSkills);
                structuredResult.CandidateSkillNames.Add(structuredSkill.Name ?? string.Empty);
                structuredResult.CandidateSkillSummaries.Add(structuredIntent.Reason);
                return Task.FromResult(structuredResult);
            }

            var best = scoredSkills.FirstOrDefault();
            if (best == null)
            {
                return Task.FromResult(new SkillSelectionResult());
            }

            if (ShouldSkipSkillSelection(best.Score))
            {
                if (string.Equals(
                    SkillHostMatcher.Normalize(SkillHostMatcher.ResolveActual(request)),
                    "excel",
                    StringComparison.Ordinal))
                {
                    var excelDefaultSkill = TryFindRequestedSkill(
                        hostSkills,
                        request == null || string.IsNullOrWhiteSpace(request.DefaultSkillName)
                            ? "read-analysis"
                            : request.DefaultSkillName);
                    if (excelDefaultSkill != null)
                    {
                        var defaultResult = BuildSelectionResult(
                            excelDefaultSkill,
                            request,
                            1,
                            "Selected the default Excel strategy because the request did not clearly match another strategy.",
                            0.25d,
                            scoredSkills);
                        defaultResult.CandidateSkillNames.Add(excelDefaultSkill.Name ?? string.Empty);
                        defaultResult.CandidateSkillSummaries.Add(defaultResult.Reason);
                        return Task.FromResult(defaultResult);
                    }
                }

                var noSkillResult = new SkillSelectionResult
                {
                    Reason = "No Office skill selected because the request did not show a clear Office task intent.",
                    Confidence = 0.1d
                };

                foreach (var candidate in scoredSkills.Take(3))
                {
                    noSkillResult.CandidateSkillNames.Add(candidate.Skill.Name ?? string.Empty);
                    noSkillResult.CandidateSkillSummaries.Add(candidate.Score == null
                        ? (candidate.Skill == null ? string.Empty : candidate.Skill.Name ?? string.Empty)
                        : candidate.Score.Reason ?? string.Empty);
                }

                return Task.FromResult(noSkillResult);
            }

            if (best.Score == null || best.Score.TotalScore <= 0)
            {
                var defaultSkill = TryFindRequestedSkill(hostSkills, request == null ? null : request.DefaultSkillName);
                if (defaultSkill != null)
                {
                    var defaultResult = BuildSelectionResult(
                        defaultSkill,
                        request,
                        1,
                        "Selected the configured default skill because no stronger match was found.",
                        0.35d,
                        scoredSkills);
                    defaultResult.CandidateSkillNames.Add(defaultSkill.Name ?? string.Empty);
                    defaultResult.CandidateSkillSummaries.Add("Selected the configured default skill because no stronger match was found.");
                    return Task.FromResult(defaultResult);
                }
            }

            var second = scoredSkills.Skip(1).FirstOrDefault();
            var confidence = CalculateConfidence(best.Score, second == null ? null : second.Score);

            var result = BuildSelectionResult(
                best.Skill,
                request,
                best.Score == null ? 0 : best.Score.TotalScore,
                best.Score == null ? BuildReason(best.Skill, 0, SkillToolFilter.FilterToolNames(best.Skill, request == null ? null : request.AvailableToolNames).Count) : best.Score.Reason,
                confidence,
                scoredSkills);

            foreach (var candidate in scoredSkills.Take(3))
            {
                result.CandidateSkillNames.Add(candidate.Skill.Name ?? string.Empty);
                result.CandidateSkillSummaries.Add(candidate.Score == null
                    ? (candidate.Skill == null ? string.Empty : candidate.Skill.Name ?? string.Empty)
                    : candidate.Score.Reason ?? string.Empty);
            }

            return Task.FromResult(result);
        }

        // 技能白名单会隐藏其余工具，宿主不符的技能一旦命中会让当前宿主的工具全部不可见。
        private static bool MatchesHost(SkillDefinition skill, SkillPlannerRequest request)
        {
            string expected;
            if (skill.Metadata == null
                || !skill.Metadata.TryGetValue(SkillMetadataKeys.HostKind, out expected)
                || string.IsNullOrWhiteSpace(expected)
                || request == null)
            {
                return true;
            }

            var actual = SkillHostMatcher.ResolveActual(request);
            return SkillHostMatcher.Matches(expected, actual);
        }

        private static bool RequiresKeywordMatch(SkillDefinition skill)
        {
            string value;
            return skill != null
                && skill.Metadata != null
                && skill.Metadata.TryGetValue(SkillMetadataKeys.RequireKeywordMatch, out value)
                && string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        private static SkillSelectionResult BuildSelectionResult(
            SkillDefinition skill,
            SkillPlannerRequest request,
            int score,
            string reason,
            double confidence,
            IEnumerable<SkillScoreCandidate> scoredSkills)
        {
            var activeSkills = BuildActiveSkills(skill, scoredSkills);
            var filteredTools = SkillToolFilter.FilterToolNames(activeSkills, request == null ? null : request.AvailableToolNames);
            var companionSkillNames = activeSkills
                .Skip(1)
                .Select(activeSkill => activeSkill.Name ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = new SkillSelectionResult
            {
                SkillName = skill == null ? string.Empty : skill.Name ?? string.Empty,
                Reason = BuildCompositeReason(reason, companionSkillNames),
                Confidence = confidence,
                AllowedToolNames = filteredTools.ToList(),
                PreferredToolNames = MergeNames(activeSkills.SelectMany(activeSkill => activeSkill.PreferredToolNames), filteredTools),
                VerificationToolNames = MergeNames(activeSkills.SelectMany(activeSkill => activeSkill.VerificationToolNames), filteredTools),
                CompanionSkillNames = companionSkillNames,
                ExecutionPlanSteps = BuildExecutionPlanSteps(request, activeSkills, request == null ? null : request.AvailableToolNames),
                FallbackSkillNames = MergeNames(activeSkills.SelectMany(activeSkill => activeSkill.FallbackSkillNames), null),
                MaxToolRoundCount = activeSkills.Count == 0 ? 0 : activeSkills.Max(activeSkill => activeSkill.MaxToolRoundCount),
                IsFallback = string.Equals(skill == null ? null : skill.Category, "fallback", StringComparison.OrdinalIgnoreCase),
                RequireVerification = activeSkills.Any(activeSkill => activeSkill.RequireVerification),
                RequiresConfirmation = activeSkills.Any(activeSkill => activeSkill.RequiresConfirmation),
                RequiresBackup = activeSkills.Any(activeSkill => activeSkill.RequiresBackup),
                // 注意：上面两个是“收紧”标志，任一伴生技能要求则全局要求，.Any() 方向正确。
                // 下面两个是“放权”标志，若也用 .Any()，一个伴生技能就能把权限注入整个计划
                // （如 read-analysis 携带 high-risk-mutation 伴生后整体获得高风险权限），因此只取主技能。
                AllowHighRiskTools = skill != null && skill.AllowHighRiskTools,
                AllowCodeExecution = skill != null && skill.AllowCodeExecution
            };

            result.ExecutionSteps = result.ExecutionPlanSteps
                .Where(step => step != null && !string.IsNullOrWhiteSpace(step.Instruction))
                .Select(step => step.Instruction)
                .ToList();

            return result;
        }

        private static IReadOnlyList<SkillDefinition> BuildActiveSkills(
            SkillDefinition primarySkill,
            IEnumerable<SkillScoreCandidate> scoredSkills)
        {
            var activeSkills = new List<SkillDefinition>();
            if (primarySkill == null)
            {
                return activeSkills;
            }

            activeSkills.Add(primarySkill);
            if (primarySkill.CompanionSkillNames == null || primarySkill.CompanionSkillNames.Count == 0)
            {
                return activeSkills;
            }

            var companionNames = new HashSet<string>(
                primarySkill.CompanionSkillNames.Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in scoredSkills ?? Enumerable.Empty<SkillScoreCandidate>())
            {
                if (candidate == null
                    || candidate.Skill == null
                    || string.Equals(candidate.Skill.Name, primarySkill.Name, StringComparison.OrdinalIgnoreCase)
                    || !companionNames.Contains(candidate.Skill.Name ?? string.Empty)
                    || !ShouldIncludeCompanion(candidate.Score))
                {
                    continue;
                }

                activeSkills.Add(candidate.Skill);
            }

            return activeSkills;
        }

        private static bool ShouldIncludeCompanion(HybridSkillScorer.HybridSkillScore score)
        {
            return score != null
                && score.TotalScore > 0
                && (score.KeywordScore > 0 || score.IntentScore > 0 || score.SimilarityScore > 0 || score.ContextScore > 0);
        }

        private static List<string> MergeNames(IEnumerable<string> names, IEnumerable<string> visibleNames)
        {
            var visible = visibleNames == null
                ? null
                : new HashSet<string>(visibleNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);

            var merged = new List<string>();
            foreach (var name in names ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (visible != null && !visible.Contains(name))
                {
                    continue;
                }

                if (!merged.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(name);
                }
            }

            return merged;
        }

        private static List<SkillExecutionStep> BuildExecutionPlanSteps(
            SkillPlannerRequest request,
            IReadOnlyList<SkillDefinition> activeSkills,
            IEnumerable<string> availableToolNames)
        {
            var steps = new List<SkillExecutionStep>();
            var skills = activeSkills ?? new List<SkillDefinition>();

            foreach (var readSkill in skills.Where(activeSkill => string.Equals(activeSkill.Category, "read", StringComparison.OrdinalIgnoreCase)))
            {
                var inspectStep = CreateExecutionStep(
                    "inspect",
                    "Inspect and locate target",
                    readSkill,
                    availableToolNames,
                    "Inspect the current workbook, worksheet, selection, and target range first. Identify the exact row, cell, or range before making changes.");
                inspectStep.StepId = BuildExecutionStepId("inspect", readSkill);
                steps.Add(inspectStep);
            }

            foreach (var writeSkill in skills.Where(activeSkill => string.Equals(activeSkill.Category, "write", StringComparison.OrdinalIgnoreCase)))
            {
                var mutationStep = CreateExecutionStep(
                    GetMutationStepKind(writeSkill),
                    GetMutationStepTitle(writeSkill),
                    writeSkill,
                    availableToolNames,
                    BuildMutationInstruction(writeSkill));
                if (IsExcelSkill(writeSkill))
                {
                    mutationStep.StepId = BuildExecutionStepId("mutation", writeSkill);
                }

                steps.Add(mutationStep);

                if (IsExcelSkill(writeSkill) && writeSkill.RequireVerification)
                {
                    steps.Add(CreateVerificationStep(
                        writeSkill,
                        "verify:" + (writeSkill.Name ?? string.Empty),
                        mutationStep.StepId,
                            "Verify the output from this write stage before starting another write stage.",
                            availableToolNames: availableToolNames));
                }
            }

            var verificationSkills = skills
                .Where(activeSkill => activeSkill.RequireVerification
                    && !(IsExcelSkill(activeSkill)
                        && string.Equals(activeSkill.Category, "write", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (verificationSkills.Count > 0)
            {
                steps.Add(CreateVerificationStep(
                    verificationSkills[0],
                    "verify:final",
                    string.Empty,
                    "Verify the final result with the verification tools before reporting success.",
                    verificationSkills,
                    availableToolNames));
            }

            if (steps.Count == 0 && request != null && !string.IsNullOrWhiteSpace(request.UserInput))
            {
                steps.Add(new SkillExecutionStep
                {
                    StepKind = "general",
                    StepId = "general:request",
                    Title = "Complete request safely",
                    SkillName = string.Empty,
                    Instruction = "Use the available tools conservatively and stop as soon as the user request is fully supported.",
                    AllowedToolNames = MergeNames(availableToolNames, availableToolNames),
                    PreferredToolNames = new List<string>(),
                    VerificationToolNames = new List<string>()
                });
            }

            return steps;
        }

        private static SkillExecutionStep CreateVerificationStep(
            SkillDefinition skill,
            string stepId,
            string verifiesStepId,
            string instruction,
            IEnumerable<SkillDefinition> verificationSkills = null,
            IEnumerable<string> availableToolNames = null)
        {
            var skills = (verificationSkills ?? new[] { skill })
                .Where(item => item != null)
                .ToList();
            var name = string.Join(
                ", ",
                skills.Select(item => item.Name ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item)));
            var tools = MergeNames(
                skills.SelectMany(item => item.VerificationToolNames ?? new List<string>()),
                availableToolNames);
            if (!string.IsNullOrWhiteSpace(verifiesStepId))
            {
                tools = tools
                    .Where(toolName => IsSupportedStageVerificationTool(skills, toolName))
                    .ToList();
            }

            var verificationInstruction = instruction ?? string.Empty;
            if (skills.Any(skill => string.Equals(
                    skill.Name,
                    "structural-transform",
                    StringComparison.OrdinalIgnoreCase))
                && tools.Any(toolName => string.Equals(
                    toolName,
                    "excel_cleanup_workspace",
                    StringComparison.OrdinalIgnoreCase)))
            {
                verificationInstruction += " For WIP registration checks, use excel_cleanup_workspace with dryRun=true only. After an authorized cleanup, use excel_list_worksheets to confirm the removed WIP worksheets are absent. Do not delete worksheets as part of verification.";
            }

            return new SkillExecutionStep
            {
                StepKind = "verify",
                StepId = stepId ?? string.Empty,
                VerifiesStepId = verifiesStepId ?? string.Empty,
                Title = string.IsNullOrWhiteSpace(name) ? "Verify result" : "Verify " + name,
                SkillName = name,
                Instruction = verificationInstruction,
                AllowedToolNames = tools.ToList(),
                PreferredToolNames = tools.ToList(),
                VerificationToolNames = tools.ToList(),
                RequireVerification = true,
                RequiresConfirmation = false,
                RequiresBackup = false
            };
        }

        private static bool IsSupportedStageVerificationTool(
            IEnumerable<SkillDefinition> skills,
            string toolName)
        {
            if (string.Equals(toolName, "excel_assert", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, "excel_reconcile", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var skill in skills ?? Enumerable.Empty<SkillDefinition>())
            {
                if (string.Equals(skill == null ? null : skill.Name, "bulk-format", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(toolName, "excel_get_range_style", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(skill == null ? null : skill.Name, "live-result", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(toolName, "excel_validate_range_formulas", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(skill == null ? null : skill.Name, "structural-transform", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(toolName, "excel_cleanup_workspace", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(toolName, "excel_list_worksheets", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildExecutionStepId(string stepKind, SkillDefinition skill)
        {
            var skillName = skill == null ? string.Empty : skill.Name ?? string.Empty;
            return (stepKind ?? "step") + ":" + skillName.Trim().ToLowerInvariant();
        }

        private static bool IsExcelSkill(SkillDefinition skill)
        {
            return skill != null
                && skill.Metadata != null
                && skill.Metadata.TryGetValue(SkillMetadataKeys.HostKind, out var hostKind)
                && string.Equals(hostKind, "excel", StringComparison.OrdinalIgnoreCase);
        }

        private static SkillExecutionStep CreateExecutionStep(
            string stepKind,
            string title,
            SkillDefinition skill,
            IEnumerable<string> availableToolNames,
            string instruction)
        {
            return new SkillExecutionStep
            {
                StepKind = stepKind ?? string.Empty,
                Title = title ?? string.Empty,
                SkillName = skill == null ? string.Empty : skill.Name ?? string.Empty,
                Instruction = instruction ?? string.Empty,
                AllowedToolNames = SkillToolFilter.FilterToolNames(skill, availableToolNames).ToList(),
                PreferredToolNames = MergeNames(skill == null ? null : skill.PreferredToolNames, availableToolNames),
                VerificationToolNames = MergeNames(skill == null ? null : skill.VerificationToolNames, availableToolNames),
                RequireVerification = skill != null && skill.RequireVerification,
                RequiresConfirmation = skill != null && skill.RequiresConfirmation,
                RequiresBackup = skill != null && skill.RequiresBackup
            };
        }

        private static string GetMutationStepKind(SkillDefinition skill)
        {
            var skillName = skill == null ? string.Empty : skill.Name ?? string.Empty;
            if (string.Equals(skillName, "live-result", StringComparison.OrdinalIgnoreCase))
            {
                return "formula";
            }

            if (string.Equals(skillName, "bulk-format", StringComparison.OrdinalIgnoreCase))
            {
                return "format";
            }

            if (string.Equals(skillName, "structural-transform", StringComparison.OrdinalIgnoreCase))
            {
                return "structural";
            }

            return "write";
        }

        private static string GetMutationStepTitle(SkillDefinition skill)
        {
            var skillName = skill == null ? string.Empty : skill.Name ?? string.Empty;
            if (string.Equals(skillName, "live-result", StringComparison.OrdinalIgnoreCase))
            {
                return "Write formulas";
            }

            if (string.Equals(skillName, "bulk-format", StringComparison.OrdinalIgnoreCase))
            {
                return "Apply formatting";
            }

            if (string.Equals(skillName, "structural-transform", StringComparison.OrdinalIgnoreCase))
            {
                return "Restructure data";
            }

            return "Apply write operation";
        }

        private static string BuildMutationInstruction(SkillDefinition skill)
        {
            var skillName = skill == null ? string.Empty : skill.Name ?? string.Empty;
            if (string.Equals(skillName, "live-result", StringComparison.OrdinalIgnoreCase))
            {
                return "After the target range is confirmed, write formulas with the formula tools instead of raw value writes, so the result recalculates when the source changes.";
            }

            if (string.Equals(skillName, "bulk-format", StringComparison.OrdinalIgnoreCase))
            {
                return "After the target range is confirmed, apply the requested formatting change to that exact range only, and leave every cell value untouched.";
            }

            if (string.Equals(skillName, "structural-transform", StringComparison.OrdinalIgnoreCase))
            {
                return "Stage the reshaping on a _wip_ worksheet with a conservation expectation first, then commit the verified result to the target sheet.";
            }

            return "After the target range is confirmed, apply the requested write operation to the intended cells only.";
        }

        private static string BuildCompositeReason(string reason, IList<string> companionSkillNames)
        {
            if (companionSkillNames == null || companionSkillNames.Count == 0)
            {
                return reason ?? string.Empty;
            }

            return (reason ?? string.Empty)
                + " Companion skills enabled for the same turn: "
                + string.Join(", ", companionSkillNames)
                + ".";
        }

        private sealed class SkillScoreCandidate
        {
            public SkillDefinition Skill { get; set; }

            public HybridSkillScorer.HybridSkillScore Score { get; set; }
        }

        private static double CalculateConfidence(
            HybridSkillScorer.HybridSkillScore best,
            HybridSkillScorer.HybridSkillScore second)
        {
            if (best == null)
            {
                return 0.15d;
            }

            var baseConfidence = best.TotalScore <= 0
                ? 0.2d
                : Math.Min(0.7d, 0.25d + (best.TotalScore / 80d));
            var gap = best.TotalScore - (second == null ? 0 : second.TotalScore);
            var gapConfidence = gap <= 0 ? 0d : Math.Min(0.25d, gap / 40d);
            return Math.Max(0.15d, Math.Min(0.98d, baseConfidence + gapConfidence));
        }

        private static bool ShouldSkipSkillSelection(HybridSkillScorer.HybridSkillScore score)
        {
            return score != null
                && score.KeywordScore == 0
                && score.IntentScore == 0
                && score.SimilarityScore <= 1;
        }

        private static SkillDefinition TryFindRequestedSkill(
            IEnumerable<SkillDefinition> skills,
            string requestedSkillName)
        {
            if (skills == null || string.IsNullOrWhiteSpace(requestedSkillName))
            {
                return null;
            }

            return skills.FirstOrDefault(skill => string.Equals(skill.Name, requestedSkillName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildReason(SkillDefinition skill, int score, int toolCount)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Selected skill '{0}' with score {1} and {2} visible tools.",
                skill == null ? string.Empty : skill.Name ?? string.Empty,
                score,
                toolCount);
        }
    }
}
