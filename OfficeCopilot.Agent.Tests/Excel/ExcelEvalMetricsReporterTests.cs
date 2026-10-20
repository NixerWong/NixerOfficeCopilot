using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Eval.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelEvalMetricsReporterTests
    {
        [TestMethod]
        public void CalculateComputesCohortRatesMediansAndFailureRates()
        {
            var metrics = ExcelEvalMetricsReporter.Calculate(CreateSummary());

            Assert.AreEqual(1, metrics.PassK.Numerator);
            Assert.AreEqual(2, metrics.PassK.Denominator);
            Assert.AreEqual(2, metrics.PassAtK.Numerator);
            Assert.AreEqual(2, metrics.PassAtK.Denominator);
            Assert.AreEqual(3.0, metrics.MedianObservedRounds.Value, 0.0001);
            Assert.AreEqual(140.0, metrics.MedianContextUsageTokens.Value, 0.0001);
            Assert.AreEqual(2, metrics.ForbiddenToolInvocationRate.Numerator);
            Assert.AreEqual(6, metrics.ForbiddenToolInvocationRate.Denominator);
            Assert.AreEqual(1, metrics.TrustAFailureRate.Numerator);
            Assert.AreEqual(4, metrics.TrustAFailureRate.Denominator);
            Assert.AreEqual(1, metrics.SilentFailureRate.Numerator);
            Assert.AreEqual(5, metrics.SilentFailureRate.Denominator);
            Assert.AreEqual(4, metrics.TrustTierCounts["A"]);
            Assert.AreEqual(1, metrics.TrustTierCounts["B"]);
            Assert.AreEqual(1, metrics.TrustTierCounts["未报告"]);
        }

        [TestMethod]
        public void BuildMarkdownMarksUnavailableMetricsAndShowsBaselineComparison()
        {
            var summary = CreateSummary();
            var report = ExcelEvalMetricsReporter.BuildMarkdown(summary, summary);

            StringAssert.Contains(report, "0.0 pp");
            StringAssert.Contains(report, "N/A");
            StringAssert.Contains(report, "验证覆盖率");
            StringAssert.Contains(report, "溯源误报率");
            StringAssert.Contains(report, "已提交写入超出用户预期范围率");
            StringAssert.Contains(report, "非账单 token");
        }

        [TestMethod]
        public void CalculateComputesCoverageAndAnswerNumberMatchRatesFromQualityTelemetry()
        {
            var summary = CreateSummary();
            summary.Runs[0].QualitySummary = new AgentBrokerTurnQualitySummary
            {
                HasExcelDataToolCalls = true,
                OutputItemCount = 2,
                CoveredOutputItemCount = 2,
                HasNumberMatchReport = true,
                MatchedAnswerNumberCount = 2,
                UnmatchedAnswerNumberCount = 1
            };
            summary.Runs[1].QualitySummary = new AgentBrokerTurnQualitySummary
            {
                HasExcelDataToolCalls = true,
                OutputItemCount = 1,
                UncoveredOutputItemCount = 1,
                HasNumberMatchReport = true,
                MatchedAnswerNumberCount = 1,
                UnmatchedAnswerNumberCount = 1
            };
            summary.Runs[2].QualitySummary = new AgentBrokerTurnQualitySummary
            {
                HasExcelDataToolCalls = true,
                HasNumberMatchReport = true
            };

            var metrics = ExcelEvalMetricsReporter.Calculate(summary);

            Assert.AreEqual(1, metrics.VerificationCoverage.Numerator);
            Assert.AreEqual(2, metrics.VerificationCoverage.Denominator);
            Assert.AreEqual(3, metrics.AnswerNumberEvidenceMatchRate.Numerator);
            Assert.AreEqual(5, metrics.AnswerNumberEvidenceMatchRate.Denominator);
            StringAssert.Contains(
                ExcelEvalMetricsReporter.BuildMarkdown(summary, null),
                "60.0% (3/5)");
        }

        [TestMethod]
        public void CalculateReportsRealSkillRoutingAccuracyAndCountsMissingSelectionAsFailure()
        {
            var summary = CreateSummary();
            summary.Runs[0].SkillRoutingMode = "real";
            summary.Runs[0].ExpectedSkill = "read-analysis";
            summary.Runs[0].QualitySummary = new AgentBrokerTurnQualitySummary
            {
                SelectedSkillName = "read-analysis"
            };
            summary.Runs[1].SkillRoutingMode = "real";
            summary.Runs[1].ExpectedSkill = "read-analysis";
            summary.Runs[1].QualitySummary = null;

            var metrics = ExcelEvalMetricsReporter.Calculate(summary);

            Assert.AreEqual(1, metrics.RealSkillRoutingAccuracy.Numerator);
            Assert.AreEqual(2, metrics.RealSkillRoutingAccuracy.Denominator);
            StringAssert.Contains(
                ExcelEvalMetricsReporter.BuildMarkdown(summary, null),
                "50.0% (1/2)");
        }

        [TestMethod]
        public void CalculateReportsMutationInquiryAndOverPreviewRatesWithObservedDenominators()
        {
            var summary = CreateSummary();
            summary.Runs[0].ExecutionMode = "automatic-read-only";
            summary.Runs[0].Checks = new System.Collections.Generic.List<ExcelEvalCheck>
            {
                new ExcelEvalCheck
                {
                    Name = "workbook-unchanged",
                    Expected = bool.TrueString,
                    Passed = false
                }
            };
            summary.Runs[1].ExecutionMode = "automatic-read-only";
            summary.Runs[1].Checks = new System.Collections.Generic.List<ExcelEvalCheck>
            {
                new ExcelEvalCheck
                {
                    Name = "workbook-unchanged",
                    Expected = bool.TrueString,
                    Passed = true
                }
            };
            summary.Runs[2].ExecutionMode = "automatic-read-only";
            summary.Runs[2].Checks = new System.Collections.Generic.List<ExcelEvalCheck>();

            summary.Runs[0].InquiryCohort = "clear";
            summary.Runs[0].ObservedRounds = 1;
            summary.Runs[0].ClarificationRequestCount = 0;
            summary.Runs[1].InquiryCohort = "clear";
            summary.Runs[1].ObservedRounds = 2;
            summary.Runs[1].ClarificationRequestCount = 1;
            summary.Runs[2].InquiryCohort = "casual-routing";
            summary.Runs[2].ObservedRounds = 3;
            summary.Runs[2].ClarificationRequestCount = 1;
            summary.Runs[3].InquiryCohort = "casual-routing";
            summary.Runs[3].ObservedRounds = 4;
            summary.Runs[3].ClarificationRequestCount = 0;
            summary.Runs[4].InquiryCohort = "clarification-required";
            summary.Runs[4].ObservedRounds = 2;
            summary.Runs[4].ClarificationRequestCount = 1;
            summary.Runs[0].DeclaredWriteCallCount = 1;
            summary.Runs[0].PreviewWriteCallCount = 1;
            summary.Runs[1].DeclaredWriteCallCount = 1;
            summary.Runs[1].PreviewWriteCallCount = 1;
            summary.Runs[1].ExceededPreviewWriteCallCount = 1;
            summary.Runs[2].DeclaredWriteCallCount = 1;
            summary.Runs[2].UserSelectedWriteCallCount = 1;
            summary.Runs[2].UserSelectedWriteViolationCount = 1;

            var metrics = ExcelEvalMetricsReporter.Calculate(summary);

            Assert.AreEqual(1, metrics.ReadOnlyMutationRate.Numerator);
            Assert.AreEqual(2, metrics.ReadOnlyMutationRate.Denominator);
            Assert.AreEqual(2, metrics.ReadOnlyMutationAuditCoverage.Numerator);
            Assert.AreEqual(3, metrics.ReadOnlyMutationAuditCoverage.Denominator);
            Assert.AreEqual(1, metrics.ExplicitTaskInquiryRate.Numerator);
            Assert.AreEqual(2, metrics.ExplicitTaskInquiryRate.Denominator);
            Assert.AreEqual(1, metrics.CasualRoutingInquiryRate.Numerator);
            Assert.AreEqual(2, metrics.CasualRoutingInquiryRate.Denominator);
            Assert.AreEqual(1, metrics.ClarificationRequiredInquiryRate.Numerator);
            Assert.AreEqual(1, metrics.ClarificationRequiredInquiryRate.Denominator);
            Assert.AreEqual(1, metrics.OverPreviewWriteCallRate.Numerator);
            Assert.AreEqual(2, metrics.OverPreviewWriteCallRate.Denominator);
            Assert.AreEqual(1, metrics.UserSelectedWriteViolationRate.Numerator);
            Assert.AreEqual(1, metrics.UserSelectedWriteViolationRate.Denominator);

            var report = ExcelEvalMetricsReporter.BuildMarkdown(summary, null);
            StringAssert.Contains(report, "只读回合意外改写率");
            StringAssert.Contains(report, "明确任务询问率（目标 ≤ 5%）");
            StringAssert.Contains(report, "口语化路由任务询问率（不代表业务歧义）");
            StringAssert.Contains(report, "真实歧义任务询问率（目标 ≥ 80%）");
            StringAssert.Contains(report, "模型执行方式预告超出率（按写入预检调用）");
            StringAssert.Contains(report, "用户选择执行方式越界尝试率（按写入预检调用）");
        }

        [TestMethod]
        public void CalculateReportsOutOfScopeSubmittedWriteRateOnlyForAuditedWrites()
        {
            var summary = CreateSummary();
            summary.Runs[0].ExecutionMode = "manual-confirmation";
            summary.Runs[0].ChangeScopeAuditCompleted = true;
            summary.Runs[0].ChangedCellCount = 3;
            summary.Runs[0].OutOfScopeCellCount = 1;
            summary.Runs[0].ChangedWorksheetCount = 1;
            summary.Runs[0].OutOfScopeWorksheetCount = 0;
            summary.Runs[1].ExecutionMode = "manual-confirmation";
            summary.Runs[1].ChangeScopeAuditCompleted = true;
            summary.Runs[1].ChangedCellCount = 5;
            summary.Runs[1].OutOfScopeCellCount = 0;
            summary.Runs[1].ChangedWorksheetCount = 0;
            summary.Runs[1].OutOfScopeWorksheetCount = 0;
            summary.Runs[2].ExecutionMode = "manual-confirmation";
            summary.Runs[2].ChangeScopeAuditCompleted = false;

            var metrics = ExcelEvalMetricsReporter.Calculate(summary);

            Assert.AreEqual(1, metrics.SubmittedChangeScopeViolationRate.Numerator);
            Assert.AreEqual(2, metrics.SubmittedChangeScopeViolationRate.Denominator);
            StringAssert.Contains(
                ExcelEvalMetricsReporter.BuildMarkdown(summary, null),
                "50.0% (1/2)");
        }

        [TestMethod]
        public void CalculateRejectsInconsistentChangeScopeAuditTelemetry()
        {
            var summary = CreateSummary();
            summary.Runs[0].ExecutionMode = "manual-confirmation";
            summary.Runs[0].ChangeScopeAuditCompleted = true;
            summary.Runs[0].ChangedCellCount = 1;
            summary.Runs[0].OutOfScopeCellCount = 2;
            summary.Runs[0].ChangedWorksheetCount = 0;
            summary.Runs[0].OutOfScopeWorksheetCount = 0;

            Assert.ThrowsException<InvalidDataException>(
                () => ExcelEvalMetricsReporter.Calculate(summary));
        }

        [TestMethod]
        public async Task EventSinkCapturesWriteEffectEvaluationsAndModeOverruns()
        {
            var tracePath = Path.Combine(
                Path.GetTempPath(),
                "excel-eval-write-effect-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                var task = new ExcelEvalTaskDefinition
                {
                    Expect = new Dictionary<string, object>()
                };
                using (var eventSink = new ExcelEvalEventSink(
                    tracePath,
                    new NullAgentBrokerClient(),
                    task,
                    "write-effect-task",
                    1,
                    false,
                    null))
                {
                    await eventSink.OnEventAsync(
                        new AgentBrokerEvent
                        {
                            EventType = AgentBrokerEventTypes.ExecutionMode,
                            Success = true
                        },
                        CancellationToken.None);
                    await eventSink.OnEventAsync(
                        new AgentBrokerEvent
                        {
                            EventType = AgentBrokerEventTypes.WriteEffectEvaluated,
                            ActualWriteEffect = "append",
                            ExpectedWriteEffect = "scratch",
                            ExecutionModeDeclared = true,
                            ExceededExecutionModePreview = true
                        },
                        CancellationToken.None);
                    await eventSink.OnEventAsync(
                        new AgentBrokerEvent
                        {
                            EventType = AgentBrokerEventTypes.ExecutionMode,
                            Success = false
                        },
                        CancellationToken.None);
                    await eventSink.OnEventAsync(
                        new AgentBrokerEvent
                        {
                            EventType = AgentBrokerEventTypes.WriteEffectEvaluated,
                            ActualWriteEffect = "modify",
                            ExpectedWriteEffect = "append",
                            ExecutionModeDeclared = true,
                            ExecutionModeUserSelected = true,
                            EffectExceedsDeclaredMaximum = true
                        },
                        CancellationToken.None);

                    Assert.AreEqual(2, eventSink.DeclaredWriteCallCount);
                    Assert.AreEqual(1, eventSink.PreviewWriteCallCount);
                    Assert.AreEqual(1, eventSink.ExceededPreviewWriteCallCount);
                    Assert.AreEqual(1, eventSink.UserSelectedWriteCallCount);
                    Assert.AreEqual(1, eventSink.UserSelectedWriteViolationCount);
                    Assert.IsTrue(eventSink.ExecutionModeDeclared);
                    Assert.IsTrue(eventSink.ExceededPreview);
                }
            }
            finally
            {
                if (File.Exists(tracePath))
                {
                    File.Delete(tracePath);
                }
            }
        }

        [TestMethod]
        public void CalculateKeepsMissingTelemetryUnavailableInsteadOfZero()
        {
            var summary = CreateSummary();
            foreach (var run in summary.Runs)
            {
                run.ObservedRounds = null;
                run.ContextUsageTokens = null;
                run.ToolCallCount = 0;
                run.ForbiddenToolCallCount = 0;
            }

            var metrics = ExcelEvalMetricsReporter.Calculate(summary);

            Assert.IsFalse(metrics.MedianObservedRounds.HasValue);
            Assert.IsFalse(metrics.MedianContextUsageTokens.HasValue);
            Assert.IsFalse(metrics.ForbiddenToolInvocationRate.Value.HasValue);
            Assert.IsFalse(metrics.VerificationCoverage.Value.HasValue);
            Assert.IsFalse(metrics.AnswerNumberEvidenceMatchRate.Value.HasValue);
            Assert.IsFalse(metrics.ClarificationRequiredInquiryRate.Value.HasValue);
        }

        [TestMethod]
        public void CalculateRejectsInconsistentWriteEffectCounters()
        {
            var summary = CreateSummary();
            summary.Runs[0].DeclaredWriteCallCount = 1;
            summary.Runs[0].PreviewWriteCallCount = 2;

            Assert.ThrowsException<InvalidDataException>(
                () => ExcelEvalMetricsReporter.Calculate(summary));
        }

        [TestMethod]
        public void CalculateExcludesIncompleteTasksFromPassKDenominator()
        {
            var summary = CreateSummary();
            summary.Runs.RemoveAt(summary.Runs.Count - 1);

            var metrics = ExcelEvalMetricsReporter.Calculate(summary);

            Assert.AreEqual(5, metrics.RecordedRuns);
            Assert.AreEqual(6, metrics.ExpectedRuns);
            Assert.AreEqual(1, metrics.CompleteTasks);
            Assert.AreEqual(1, metrics.PassK.Numerator);
            Assert.AreEqual(1, metrics.PassK.Denominator);
        }

        [TestMethod]
        public void WriteReportCreatesMarkdownFile()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "excel-eval-report-" + Guid.NewGuid().ToString("N") + ".md");
            try
            {
                ExcelEvalMetricsReporter.WriteReport(path, CreateSummary(), null);

                Assert.IsTrue(File.Exists(path));
                StringAssert.Contains(File.ReadAllText(path), "# Excel Eval 报告");
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [TestMethod]
        public void EnsureComparableRejectsDifferentDatasetFingerprints()
        {
            var current = CreateSummary();
            var baseline = CreateSummary();
            baseline.DatasetFingerprint = new string('b', 64);

            Assert.ThrowsException<InvalidDataException>(
                () => ExcelEvalMetricsReporter.EnsureComparable(current, baseline));
        }

        private static ExcelEvalRunSummary CreateSummary()
        {
            return new ExcelEvalRunSummary
            {
                TaskSetPath = "tasks.json",
                DatasetFingerprint = new string('a', 64),
                TaskIds = new System.Collections.Generic.List<string>
                {
                    "task-a",
                    "task-b"
                },
                RunsPerTask = 3,
                RequestedRuns = 6,
                Runs = new System.Collections.Generic.List<ExcelEvalRunResult>
                {
                    CreateRun("task-a", 1, true, true, "A", 1, 100, 1, 0),
                    CreateRun("task-a", 2, true, true, "A", 2, 120, 2, 1),
                    CreateRun("task-a", 3, true, true, "A", 4, 140, 0, 0),
                    CreateRun("task-b", 1, false, true, "A", 3, 200, 1, 0),
                    CreateRun("task-b", 2, true, true, "B", null, null, 0, 0),
                    CreateRun("task-b", 3, false, false, string.Empty, 5, 300, 2, 1)
                }
            };
        }

        private static ExcelEvalRunResult CreateRun(
            string taskId,
            int runNumber,
            bool passed,
            bool turnSucceeded,
            string trustTier,
            int? rounds,
            int? contextTokens,
            int toolCalls,
            int forbiddenToolCalls)
        {
            return new ExcelEvalRunResult
            {
                TaskId = taskId,
                RunNumber = runNumber,
                Passed = passed,
                TurnSucceeded = turnSucceeded,
                TrustTier = trustTier,
                ObservedRounds = rounds,
                ContextUsageTokens = contextTokens,
                ToolCallCount = toolCalls,
                ForbiddenToolCallCount = forbiddenToolCalls
            };
        }
    }
}
