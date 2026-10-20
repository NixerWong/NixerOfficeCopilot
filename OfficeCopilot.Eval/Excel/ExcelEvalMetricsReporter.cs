using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace OfficeCopilot.Eval.Excel
{
    internal sealed class ExcelEvalMetricRate
    {
        public int Numerator { get; set; }

        public int Denominator { get; set; }

        public double? Value
        {
            get
            {
                return Denominator == 0
                    ? (double?)null
                    : (double)Numerator / Denominator;
            }
        }
    }

    internal sealed class ExcelEvalMetricsSnapshot
    {
        public int ExpectedRuns { get; set; }

        public int RecordedRuns { get; set; }

        public int CompleteTasks { get; set; }

        public int TotalTasks { get; set; }

        public ExcelEvalMetricRate PassK { get; set; }

        public ExcelEvalMetricRate PassAtK { get; set; }

        public double? MedianObservedRounds { get; set; }

        public double? MedianContextUsageTokens { get; set; }

        public ExcelEvalMetricRate ForbiddenToolInvocationRate { get; set; }

        public ExcelEvalMetricRate TrustAFailureRate { get; set; }

        public ExcelEvalMetricRate SilentFailureRate { get; set; }

        public ExcelEvalMetricRate VerificationCoverage { get; set; }

        public ExcelEvalMetricRate AnswerNumberEvidenceMatchRate { get; set; }

        public ExcelEvalMetricRate RealSkillRoutingAccuracy { get; set; }

        public ExcelEvalMetricRate ReadOnlyMutationRate { get; set; }

        public ExcelEvalMetricRate ReadOnlyMutationAuditCoverage { get; set; }

        public ExcelEvalMetricRate ExplicitTaskInquiryRate { get; set; }

        public ExcelEvalMetricRate CasualRoutingInquiryRate { get; set; }

        public ExcelEvalMetricRate ClarificationRequiredInquiryRate { get; set; }

        public ExcelEvalMetricRate UserSelectedWriteViolationRate { get; set; }

        public ExcelEvalMetricRate OverPreviewWriteCallRate { get; set; }

        public ExcelEvalMetricRate SubmittedChangeScopeViolationRate { get; set; }

        public IDictionary<string, int> TrustTierCounts { get; set; }
    }

    internal static class ExcelEvalMetricsReporter
    {
        private static readonly string[] _trustTierNames =
        {
            "A",
            "B",
            "C",
            "未报告"
        };

        public static ExcelEvalMetricsSnapshot Calculate(ExcelEvalRunSummary summary)
        {
            ValidateSummary(summary);

            var taskIds = new HashSet<string>(summary.TaskIds, StringComparer.Ordinal);
            var runsByTask = summary.TaskIds.ToDictionary(
                taskId => taskId,
                _ => new List<ExcelEvalRunResult>(),
                StringComparer.Ordinal);
            var seenRunNumbers = new HashSet<string>(StringComparer.Ordinal);

            foreach (var run in summary.Runs)
            {
                if (run == null || !taskIds.Contains(run.TaskId ?? string.Empty))
                {
                    throw new InvalidDataException("Eval summary contains a run for an unknown task.");
                }

                if (run.RunNumber < 1 || run.RunNumber > summary.RunsPerTask)
                {
                    throw new InvalidDataException(
                        "Eval summary contains an invalid run number for task '" + run.TaskId + "'.");
                }

                var runKey = run.TaskId + "\n" + run.RunNumber.ToString(CultureInfo.InvariantCulture);
                if (!seenRunNumbers.Add(runKey))
                {
                    throw new InvalidDataException(
                        "Eval summary contains a duplicate run number for task '" + run.TaskId + "'.");
                }

                ValidateRunTelemetry(run);
                runsByTask[run.TaskId].Add(run);
            }

            var completeTaskRuns = runsByTask.Values
                .Where(runs => runs.Count == summary.RunsPerTask)
                .ToList();
            var recordedRuns = summary.Runs;
            var successfulRuns = recordedRuns.Where(run => run.TurnSucceeded).ToList();
            var toolCallCount = recordedRuns.Sum(run => run.ToolCallCount);
            var forbiddenToolCallCount = recordedRuns.Sum(run => run.ForbiddenToolCallCount);
            var runsWithOutputArtifacts = recordedRuns
                .Where(run => run.QualitySummary != null
                    && run.QualitySummary.HasExcelDataToolCalls
                    && run.QualitySummary.OutputItemCount > 0)
                .ToList();
            var runsWithNumberMatchReports = recordedRuns
                .Where(run => run.QualitySummary != null
                    && run.QualitySummary.HasNumberMatchReport)
                .ToList();
            var realRoutingRuns = recordedRuns
                .Where(run => string.Equals(
                    run.SkillRoutingMode,
                    "real",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            var readOnlyRuns = recordedRuns
                .Where(IsAutomaticReadOnlyRun)
                .ToList();
            var auditedWriteRuns = recordedRuns
                .Where(run => IsManualConfirmationRun(run) && run.ChangeScopeAuditCompleted)
                .ToList();
            var auditedReadOnlyRuns = readOnlyRuns
                .Where(HasReadOnlyMutationAudit)
                .ToList();
            var explicitInquiryRuns = recordedRuns
                .Where(run => HasObservedModelRound(run)
                    && string.Equals(run.InquiryCohort, "clear", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var casualRoutingInquiryRuns = recordedRuns
                .Where(run => HasObservedModelRound(run)
                    && string.Equals(run.InquiryCohort, "casual-routing", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var clarificationRequiredInquiryRuns = recordedRuns
                .Where(run => HasObservedModelRound(run)
                    && string.Equals(
                        run.InquiryCohort,
                        "clarification-required",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
            var matchedAnswerNumbers = runsWithNumberMatchReports.Sum(
                run => run.QualitySummary.MatchedAnswerNumberCount);
            var totalAnswerNumbers = runsWithNumberMatchReports.Sum(
                run => run.QualitySummary.MatchedAnswerNumberCount
                    + run.QualitySummary.UnmatchedAnswerNumberCount);
            var tierCounts = _trustTierNames.ToDictionary(
                tier => tier,
                _ => 0,
                StringComparer.Ordinal);

            foreach (var run in recordedRuns)
            {
                var tier = string.IsNullOrWhiteSpace(run.TrustTier)
                    ? "未报告"
                    : run.TrustTier;
                tierCounts[tier]++;
            }

            return new ExcelEvalMetricsSnapshot
            {
                ExpectedRuns = summary.RequestedRuns,
                RecordedRuns = recordedRuns.Count,
                CompleteTasks = completeTaskRuns.Count,
                TotalTasks = summary.TaskIds.Count,
                PassK = new ExcelEvalMetricRate
                {
                    Numerator = completeTaskRuns.Count(runs => runs.All(run => run.Passed)),
                    Denominator = completeTaskRuns.Count
                },
                PassAtK = new ExcelEvalMetricRate
                {
                    Numerator = completeTaskRuns.Count(runs => runs.Any(run => run.Passed)),
                    Denominator = completeTaskRuns.Count
                },
                MedianObservedRounds = Median(recordedRuns
                    .Where(run => run.ObservedRounds.HasValue)
                    .Select(run => (double)run.ObservedRounds.Value)),
                MedianContextUsageTokens = Median(recordedRuns
                    .Where(run => run.ContextUsageTokens.HasValue)
                    .Select(run => (double)run.ContextUsageTokens.Value)),
                ForbiddenToolInvocationRate = new ExcelEvalMetricRate
                {
                    Numerator = forbiddenToolCallCount,
                    Denominator = toolCallCount
                },
                TrustAFailureRate = new ExcelEvalMetricRate
                {
                    Numerator = recordedRuns.Count(run =>
                        string.Equals(run.TrustTier, "A", StringComparison.Ordinal)
                        && !run.Passed),
                    Denominator = tierCounts["A"]
                },
                SilentFailureRate = new ExcelEvalMetricRate
                {
                    Numerator = successfulRuns.Count(run => !run.Passed),
                    Denominator = successfulRuns.Count
                },
                VerificationCoverage = new ExcelEvalMetricRate
                {
                    Numerator = runsWithOutputArtifacts.Count(run =>
                        run.QualitySummary.CoveredOutputItemCount
                        == run.QualitySummary.OutputItemCount),
                    Denominator = runsWithOutputArtifacts.Count
                },
                AnswerNumberEvidenceMatchRate = new ExcelEvalMetricRate
                {
                    Numerator = matchedAnswerNumbers,
                    Denominator = totalAnswerNumbers
                },
                RealSkillRoutingAccuracy = new ExcelEvalMetricRate
                {
                    Numerator = realRoutingRuns.Count(run =>
                        run.QualitySummary != null
                        && !string.IsNullOrWhiteSpace(run.ExpectedSkill)
                        && !string.IsNullOrWhiteSpace(run.QualitySummary.SelectedSkillName)
                        && string.Equals(
                            run.ExpectedSkill,
                            run.QualitySummary.SelectedSkillName,
                            StringComparison.OrdinalIgnoreCase)),
                    Denominator = realRoutingRuns.Count
                },
                ReadOnlyMutationRate = new ExcelEvalMetricRate
                {
                    Numerator = auditedReadOnlyRuns.Count(HasUnexpectedReadOnlyMutation),
                    Denominator = auditedReadOnlyRuns.Count
                },
                ReadOnlyMutationAuditCoverage = new ExcelEvalMetricRate
                {
                    Numerator = auditedReadOnlyRuns.Count,
                    Denominator = readOnlyRuns.Count
                },
                ExplicitTaskInquiryRate = new ExcelEvalMetricRate
                {
                    Numerator = explicitInquiryRuns.Count(run => run.ClarificationRequestCount > 0),
                    Denominator = explicitInquiryRuns.Count
                },
                CasualRoutingInquiryRate = new ExcelEvalMetricRate
                {
                    Numerator = casualRoutingInquiryRuns.Count(run => run.ClarificationRequestCount > 0),
                    Denominator = casualRoutingInquiryRuns.Count
                },
                ClarificationRequiredInquiryRate = new ExcelEvalMetricRate
                {
                    Numerator = clarificationRequiredInquiryRuns.Count(run => run.ClarificationRequestCount > 0),
                    Denominator = clarificationRequiredInquiryRuns.Count
                },
                UserSelectedWriteViolationRate = new ExcelEvalMetricRate
                {
                    Numerator = recordedRuns.Sum(run => run.UserSelectedWriteViolationCount),
                    Denominator = recordedRuns.Sum(run => run.UserSelectedWriteCallCount)
                },
                OverPreviewWriteCallRate = new ExcelEvalMetricRate
                {
                    Numerator = recordedRuns.Sum(run => run.ExceededPreviewWriteCallCount),
                    Denominator = recordedRuns.Sum(run => run.PreviewWriteCallCount)
                },
                SubmittedChangeScopeViolationRate = new ExcelEvalMetricRate
                {
                    Numerator = auditedWriteRuns.Count(run =>
                        run.OutOfScopeCellCount.Value > 0
                        || run.OutOfScopeWorksheetCount.Value > 0),
                    Denominator = auditedWriteRuns.Count
                },
                TrustTierCounts = tierCounts
            };
        }

        public static void EnsureComparable(
            ExcelEvalRunSummary current,
            ExcelEvalRunSummary baseline)
        {
            Calculate(current);
            Calculate(baseline);

            if (!string.Equals(
                    current.DatasetFingerprint,
                    baseline.DatasetFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Baseline comparison requires an identical task set, workbook fixtures, and answer files.");
            }

            var currentTaskIds = current.TaskIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var baselineTaskIds = baseline.TaskIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            if (current.RunsPerTask != baseline.RunsPerTask
                || !currentTaskIds.SequenceEqual(baselineTaskIds, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Baseline comparison requires the same task cohort and runsPerTask value.");
            }
        }

        public static string BuildMarkdown(
            ExcelEvalRunSummary current,
            ExcelEvalRunSummary baseline)
        {
            var metrics = Calculate(current);
            ExcelEvalMetricsSnapshot baselineMetrics = null;
            if (baseline != null)
            {
                EnsureComparable(current, baseline);
                baselineMetrics = Calculate(baseline);
            }

            var report = new StringBuilder();
            report.AppendLine("# Excel Eval 报告");
            report.AppendLine();
            report.AppendLine("- 任务集：`" + EscapeMarkdown(current.TaskSetPath) + "`");
            report.AppendLine("- 任务 cohort：`"
                + EscapeMarkdown(string.Join(", ", current.TaskIds)) + "`");
            report.AppendLine("- 数据指纹：`" + current.DatasetFingerprint + "`");
            report.AppendLine("- 人工确认任务跳过："
                + current.SkippedManualTasks.ToString(CultureInfo.InvariantCulture));
            if (current.SkippedManualTaskIds != null && current.SkippedManualTaskIds.Count > 0)
            {
                report.AppendLine("- 未运行任务："
                    + EscapeMarkdown(string.Join(", ", current.SkippedManualTaskIds)));
            }
            report.AppendLine("- 回合：已记录 " + metrics.RecordedRuns.ToString(CultureInfo.InvariantCulture)
                + " / 计划 " + metrics.ExpectedRuns.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- 完整任务："
                + metrics.CompleteTasks.ToString(CultureInfo.InvariantCulture)
                + " / " + metrics.TotalTasks.ToString(CultureInfo.InvariantCulture)
                + "（每任务 k = " + current.RunsPerTask.ToString(CultureInfo.InvariantCulture) + "）");
            report.AppendLine("- 基线："
                + (baseline == null
                    ? "未指定；可在下一次运行时传入 `--baseline-summary <summary.json>`"
                    : "`" + EscapeMarkdown(current.BaselineSummaryPath ?? "compatible summary") + "`"));
            report.AppendLine();
            report.AppendLine("| 指标 | 本次 | 基线 | 变化 |");
            report.AppendLine("|---|---:|---:|---:|");
            AppendRateRow(report, "pass^k（全部 k 次通过的任务比例）", metrics.PassK,
                baselineMetrics == null ? null : baselineMetrics.PassK);
            AppendRateRow(report, "pass@k（至少一次通过的任务比例）", metrics.PassAtK,
                baselineMetrics == null ? null : baselineMetrics.PassAtK);
            AppendMedianRow(report, "模型轮次中位数", metrics.MedianObservedRounds,
                baselineMetrics == null ? null : baselineMetrics.MedianObservedRounds, "0.0");
            AppendMedianRow(report, "上下文 token 估算中位数（非账单 token）",
                metrics.MedianContextUsageTokens,
                baselineMetrics == null ? null : baselineMetrics.MedianContextUsageTokens,
                "0");
            AppendRateRow(report, "命中任务禁用清单的工具尝试率", metrics.ForbiddenToolInvocationRate,
                baselineMetrics == null ? null : baselineMetrics.ForbiddenToolInvocationRate);
            AppendRateRow(report, "A 档 rubric 失败率", metrics.TrustAFailureRate,
                baselineMetrics == null ? null : baselineMetrics.TrustAFailureRate);
            AppendRateRow(report, "静默失败率（回合成功但 rubric 失败）",
                metrics.SilentFailureRate,
                baselineMetrics == null ? null : baselineMetrics.SilentFailureRate);
            AppendRateRow(report, "产出项验证覆盖率（有产出的 Excel 回合）",
                metrics.VerificationCoverage,
                baselineMetrics == null ? null : baselineMetrics.VerificationCoverage);
            AppendRateRow(report, "回答数字证据匹配率（不是误报率）",
                metrics.AnswerNumberEvidenceMatchRate,
                baselineMetrics == null ? null : baselineMetrics.AnswerNumberEvidenceMatchRate);
            AppendRateRow(report, "真实路由技能准确率",
                metrics.RealSkillRoutingAccuracy,
                baselineMetrics == null ? null : baselineMetrics.RealSkillRoutingAccuracy);
            AppendRateRow(report, "只读回合意外改写率",
                metrics.ReadOnlyMutationRate,
                baselineMetrics == null ? null : baselineMetrics.ReadOnlyMutationRate);
            AppendRateRow(report, "只读工作簿审计覆盖率",
                metrics.ReadOnlyMutationAuditCoverage,
                baselineMetrics == null ? null : baselineMetrics.ReadOnlyMutationAuditCoverage);
            AppendRateRow(report, "明确任务询问率（目标 ≤ 5%）",
                metrics.ExplicitTaskInquiryRate,
                baselineMetrics == null ? null : baselineMetrics.ExplicitTaskInquiryRate);
            AppendRateRow(report, "口语化路由任务询问率（不代表业务歧义）",
                metrics.CasualRoutingInquiryRate,
                baselineMetrics == null ? null : baselineMetrics.CasualRoutingInquiryRate);
            AppendRateRow(report, "真实歧义任务询问率（目标 ≥ 80%）",
                metrics.ClarificationRequiredInquiryRate,
                baselineMetrics == null ? null : baselineMetrics.ClarificationRequiredInquiryRate);
            AppendRateRow(report, "用户选择执行方式越界尝试率（按写入预检调用）",
                metrics.UserSelectedWriteViolationRate,
                baselineMetrics == null ? null : baselineMetrics.UserSelectedWriteViolationRate);
            AppendRateRow(report, "模型执行方式预告超出率（按写入预检调用）",
                metrics.OverPreviewWriteCallRate,
                baselineMetrics == null ? null : baselineMetrics.OverPreviewWriteCallRate);
            AppendRateRow(report, "已提交写入超出用户预期范围率（按完成范围审计的写入回合）",
                metrics.SubmittedChangeScopeViolationRate,
                baselineMetrics == null ? null : baselineMetrics.SubmittedChangeScopeViolationRate);
            report.AppendLine();
            report.AppendLine("## 可信档位分布");
            report.AppendLine();
            report.AppendLine("| 档位 | 本次 | 基线 |");
            report.AppendLine("|---|---:|---:|");
            foreach (var tier in _trustTierNames)
            {
                var currentCount = metrics.TrustTierCounts[tier];
                var baselineCount = baselineMetrics == null
                    ? (int?)null
                    : baselineMetrics.TrustTierCounts[tier];
                report.AppendLine("| " + tier + " | "
                    + FormatCount(currentCount, metrics.RecordedRuns)
                    + " | " + (baselineCount.HasValue
                        ? FormatCount(baselineCount.Value, baselineMetrics.RecordedRuns)
                        : "—")
                    + " |");
            }

            report.AppendLine();
            report.AppendLine("## 尚未接入的指标");
            report.AppendLine();
            report.AppendLine("| 指标 | 状态 | 原因 |");
            report.AppendLine("|---|---|---|");
            report.AppendLine("| 溯源误报率 | N/A | 运行时已记录数字匹配计数；误报率仍需人工标注哪些未匹配数字实际有正确来源，不能用匹配率替代。 |");
            report.AppendLine();
            report.AppendLine("上下文 token 数来自 Broker 的上下文预算估算，不代表模型供应商实际计费 token。");
            return report.ToString();
        }

        public static void WriteReport(
            string path,
            ExcelEvalRunSummary current,
            ExcelEvalRunSummary baseline)
        {
            File.WriteAllText(path, BuildMarkdown(current, baseline), new UTF8Encoding(false));
        }

        private static void ValidateSummary(ExcelEvalRunSummary summary)
        {
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            if (summary.RunsPerTask < 1
                || summary.TaskIds == null
                || summary.TaskIds.Count == 0
                || summary.Runs == null)
            {
                throw new InvalidDataException("Eval summary is missing its task cohort or run data.");
            }

            if (summary.TaskIds.Any(string.IsNullOrWhiteSpace)
                || summary.TaskIds.Distinct(StringComparer.Ordinal).Count() != summary.TaskIds.Count)
            {
                throw new InvalidDataException("Eval summary contains an empty or duplicate task ID.");
            }

            if (string.IsNullOrWhiteSpace(summary.DatasetFingerprint))
            {
                throw new InvalidDataException("Eval summary is missing the dataset fingerprint.");
            }

            var expectedRuns = checked(summary.TaskIds.Count * summary.RunsPerTask);
            if (summary.RequestedRuns != expectedRuns || summary.Runs.Count > expectedRuns)
            {
                throw new InvalidDataException("Eval summary run counts do not match its task cohort.");
            }
        }

        private static bool IsAutomaticReadOnlyRun(ExcelEvalRunResult run)
        {
            return string.Equals(
                run.ExecutionMode,
                "automatic-read-only",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManualConfirmationRun(ExcelEvalRunResult run)
        {
            return string.Equals(
                run.ExecutionMode,
                "manual-confirmation",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasObservedModelRound(ExcelEvalRunResult run)
        {
            return run.ObservedRounds.HasValue && run.ObservedRounds.Value > 0;
        }

        private static bool HasReadOnlyMutationAudit(ExcelEvalRunResult run)
        {
            return run.Checks != null && run.Checks.Any(check =>
                check != null
                && string.Equals(check.Name, "workbook-unchanged", StringComparison.OrdinalIgnoreCase)
                && string.Equals(check.Expected, bool.TrueString, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasUnexpectedReadOnlyMutation(ExcelEvalRunResult run)
        {
            return run.Checks.Any(check =>
                check != null
                && string.Equals(check.Name, "workbook-unchanged", StringComparison.OrdinalIgnoreCase)
                && string.Equals(check.Expected, bool.TrueString, StringComparison.OrdinalIgnoreCase)
                && !check.Passed);
        }

        private static void ValidateRunTelemetry(ExcelEvalRunResult run)
        {
            if (run.ToolCallCount < 0
                || run.ForbiddenToolCallCount < 0
                || run.ForbiddenToolCallCount > run.ToolCallCount
                || run.ClarificationRequestCount < 0
                || run.DeclaredWriteCallCount < 0
                || run.PreviewWriteCallCount < 0
                || run.ExceededPreviewWriteCallCount < 0
                || run.ExceededPreviewWriteCallCount > run.PreviewWriteCallCount
                || run.UserSelectedWriteCallCount < 0
                || run.UserSelectedWriteViolationCount < 0
                || run.UserSelectedWriteViolationCount > run.UserSelectedWriteCallCount
                || (long)run.PreviewWriteCallCount + run.UserSelectedWriteCallCount
                    > run.DeclaredWriteCallCount
                || (run.ObservedRounds.HasValue && run.ObservedRounds.Value < 1)
                || (run.ContextUsageTokens.HasValue && run.ContextUsageTokens.Value < 0))
            {
                throw new InvalidDataException(
                    "Eval summary contains invalid telemetry for task '" + run.TaskId + "'.");
            }

            var hasChangeScopeCounts = run.ChangedCellCount.HasValue
                || run.OutOfScopeCellCount.HasValue
                || run.ChangedWorksheetCount.HasValue
                || run.OutOfScopeWorksheetCount.HasValue;
            if (run.ChangeScopeAuditCompleted
                ? !IsManualConfirmationRun(run)
                    || !run.ChangedCellCount.HasValue
                    || !run.OutOfScopeCellCount.HasValue
                    || !run.ChangedWorksheetCount.HasValue
                    || !run.OutOfScopeWorksheetCount.HasValue
                    || run.ChangedCellCount.Value < 0
                    || run.OutOfScopeCellCount.Value < 0
                    || run.OutOfScopeCellCount.Value > run.ChangedCellCount.Value
                    || run.ChangedWorksheetCount.Value < 0
                    || run.OutOfScopeWorksheetCount.Value < 0
                    || run.OutOfScopeWorksheetCount.Value > run.ChangedWorksheetCount.Value
                : hasChangeScopeCounts)
            {
                throw new InvalidDataException(
                    "Eval summary contains invalid change-scope audit telemetry for task '" + run.TaskId + "'.");
            }

            var quality = run.QualitySummary;
            if (quality != null
                && (quality.OutputItemCount < 0
                    || quality.CoveredOutputItemCount < 0
                    || quality.CoveredOutputItemCount > quality.OutputItemCount
                    || quality.UncoveredOutputItemCount != quality.OutputItemCount - quality.CoveredOutputItemCount
                    || quality.VerificationCount < 0
                    || quality.AssertionCount < 0
                    || quality.CoveredRows < 0
                    || quality.MatchedAnswerNumberCount < 0
                    || quality.UnmatchedAnswerNumberCount < 0
                    || quality.NumberCorrectionSucceeded && !quality.NumberCorrectionAttempted
                    || quality.NumberCorrectionAttempted && !quality.HasNumberMatchReport
                    || !quality.HasNumberMatchReport
                        && (quality.MatchedAnswerNumberCount != 0
                            || quality.UnmatchedAnswerNumberCount != 0)
                    || !quality.HasExcelDataToolCalls
                        && (quality.OutputItemCount != 0
                            || quality.CoveredOutputItemCount != 0
                            || quality.UncoveredOutputItemCount != 0
                            || quality.VerificationCount != 0
                            || quality.AssertionCount != 0
                            || quality.CoveredRows != 0)))
            {
                throw new InvalidDataException(
                    "Eval summary contains invalid quality telemetry for task '" + run.TaskId + "'.");
            }

            if (!string.IsNullOrWhiteSpace(run.TrustTier)
                && !_trustTierNames.Take(3).Contains(run.TrustTier, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    "Eval summary contains an unknown trust tier for task '" + run.TaskId + "'.");
            }
        }

        private static double? Median(IEnumerable<double> values)
        {
            var ordered = values.OrderBy(value => value).ToArray();
            if (ordered.Length == 0)
            {
                return null;
            }

            var middle = ordered.Length / 2;
            return ordered.Length % 2 == 0
                ? (ordered[middle - 1] + ordered[middle]) / 2.0
                : ordered[middle];
        }

        private static void AppendRateRow(
            StringBuilder report,
            string name,
            ExcelEvalMetricRate current,
            ExcelEvalMetricRate baseline)
        {
            report.AppendLine("| " + name + " | " + FormatRate(current)
                + " | " + (baseline == null ? "—" : FormatRate(baseline))
                + " | " + (baseline == null ? "—" : FormatRateDelta(current, baseline)) + " |");
        }

        private static void AppendMedianRow(
            StringBuilder report,
            string name,
            double? current,
            double? baseline,
            string format)
        {
            report.AppendLine("| " + name + " | " + FormatNumber(current, format)
                + " | " + (baseline.HasValue ? FormatNumber(baseline, format) : "—")
                + " | " + (baseline.HasValue ? FormatNumberDelta(current, baseline, format) : "—")
                + " |");
        }

        private static string FormatRate(ExcelEvalMetricRate rate)
        {
            return rate.Value.HasValue
                ? (rate.Value.Value * 100.0).ToString("0.0", CultureInfo.InvariantCulture)
                    + "% (" + rate.Numerator.ToString(CultureInfo.InvariantCulture)
                    + "/" + rate.Denominator.ToString(CultureInfo.InvariantCulture) + ")"
                : "N/A (0/0)";
        }

        private static string FormatRateDelta(
            ExcelEvalMetricRate current,
            ExcelEvalMetricRate baseline)
        {
            if (!current.Value.HasValue || !baseline.Value.HasValue)
            {
                return "N/A";
            }

            var delta = (current.Value.Value - baseline.Value.Value) * 100.0;
            return delta.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " pp";
        }

        private static string FormatNumber(double? value, string format)
        {
            return value.HasValue
                ? value.Value.ToString(format, CultureInfo.InvariantCulture)
                : "N/A";
        }

        private static string FormatNumberDelta(
            double? current,
            double? baseline,
            string format)
        {
            if (!current.HasValue || !baseline.HasValue)
            {
                return "N/A";
            }

            return (current.Value - baseline.Value).ToString(
                "+" + format + ";-" + format + ";" + format,
                CultureInfo.InvariantCulture);
        }

        private static string FormatCount(int count, int denominator)
        {
            return count.ToString(CultureInfo.InvariantCulture)
                + " (" + (denominator == 0
                    ? "N/A"
                    : (count * 100.0 / denominator).ToString("0.0", CultureInfo.InvariantCulture) + "%")
                + ")";
        }

        private static string EscapeMarkdown(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|");
        }
    }
}
