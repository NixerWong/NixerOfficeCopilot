using OfficeCopilot.Agent.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OfficeCopilot.Eval.Excel
{
    internal sealed class ExcelEvalRunnerOptions
    {
        public LoadedExcelEvalTaskSet TaskSet { get; set; }

        public string TaskId { get; set; }

        public bool RunAllReadOnlyTasks { get; set; }

        public bool RunAllManualConfirmationTasks { get; set; }

        public bool AllowWorkbookWriteTests { get; set; }

        public string OutputDirectory { get; set; }

        public string BrokerPipeName { get; set; }

        public string BaselineSummaryPath { get; set; }

        public int TimeoutSeconds { get; set; }
    }

    internal sealed class ExcelEvalCheck
    {
        public string Name { get; set; }

        public bool Passed { get; set; }

        public string Expected { get; set; }

        public string Actual { get; set; }
    }

    internal sealed class ExcelEvalRunResult
    {
        public string TaskId { get; set; }

        public int RunNumber { get; set; }

        public string SkillRoutingMode { get; set; }

        public string ExpectedSkill { get; set; }

        public string ExecutionMode { get; set; }

        public string InquiryCohort { get; set; }

        public string OutputDirectory { get; set; }

        public bool TurnSucceeded { get; set; }

        public bool Passed { get; set; }

        public string Error { get; set; }

        public string Answer { get; set; }

        public string TrustTier { get; set; }

        public AgentBrokerTurnQualitySummary QualitySummary { get; set; }

        public int? ObservedRounds { get; set; }

        public int? ContextUsageTokens { get; set; }

        public int ToolCallCount { get; set; }

        public int ForbiddenToolCallCount { get; set; }

        public int AutoApprovedWriteConfirmations { get; set; }

        public int ClarificationRequestCount { get; set; }

        public bool ExecutionModeDeclared { get; set; }

        public bool ExceededPreview { get; set; }

        public int DeclaredWriteCallCount { get; set; }

        public int PreviewWriteCallCount { get; set; }

        public int ExceededPreviewWriteCallCount { get; set; }

        public int UserSelectedWriteCallCount { get; set; }

        public int UserSelectedWriteViolationCount { get; set; }

        public bool ChangeScopeAuditCompleted { get; set; }

        public int? ChangedCellCount { get; set; }

        public int? OutOfScopeCellCount { get; set; }

        public int? ChangedWorksheetCount { get; set; }

        public int? OutOfScopeWorksheetCount { get; set; }

        public List<ExcelEvalCheck> Checks { get; set; }
    }

    internal sealed class ExcelEvalRunSummary
    {
        public string StartedAtUtc { get; set; }

        public string CompletedAtUtc { get; set; }

        public string TaskSetPath { get; set; }

        public string DatasetFingerprint { get; set; }

        public string ExcelProcessId { get; set; }

        public List<string> ExcelProcessIds { get; set; }

        public List<string> TaskIds { get; set; }

        public int RunsPerTask { get; set; }

        public int RequestedRuns { get; set; }

        public int PassedRuns { get; set; }

        public int FailedRuns { get; set; }

        public int SkippedManualTasks { get; set; }

        public List<string> SkippedManualTaskIds { get; set; }

        public string BaselineSummaryPath { get; set; }

        public string ReportFileName { get; set; }

        public List<ExcelEvalRunResult> Runs { get; set; }
    }

    internal sealed class ExcelEvalRunner
    {
        private static readonly Regex _numberPattern = new Regex(
            @"(?<![\p{L}\d])(?<number>[-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)(?<percent>\s*[%％])?",
            RegexOptions.Compiled);

        private static readonly Regex _trustTierPattern = new Regex(
            @"校验\s*[:：]\s*(?:(?<tier>[ABC])(?:级)?(?![A-Za-z])|(?<phrase>全量独立复核通过|已复核，仍有局限|尚未完成有效验证))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly JavaScriptSerializer _serializer =
            ExcelEvalTaskSetLoader.CreateSerializer();

        public ExcelEvalRunSummary Run(ExcelEvalRunnerOptions options)
        {
            ValidateOptions(options);
            var modelConfiguration = ExcelEvalModelOptionsProvider.LoadDefault();
            Directory.CreateDirectory(options.OutputDirectory);

            var selectedTasks = SelectTasks(options);
            var skippedManualTaskIds = options.RunAllReadOnlyTasks
                ? options.TaskSet.Document.Tasks
                    .Where(IsManualConfirmationTask)
                    .Select(task => task.Id)
                    .ToList()
                : options.RunAllManualConfirmationTasks
                    ? options.TaskSet.Document.Tasks
                        .Where(IsManualConfirmationTask)
                        .Where(HasUnsupportedFaultInjection)
                        .Select(task => task.Id)
                        .ToList()
                    : new List<string>();
            var datasetFingerprint = ComputeDatasetFingerprint(options.TaskSet, selectedTasks);
            var baselineSummary = options.AllowWorkbookWriteTests
                ? null
                : LoadBaselineSummary(options.BaselineSummaryPath);
            var summary = new ExcelEvalRunSummary
            {
                StartedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                TaskSetPath = options.TaskSet.TaskSetPath,
                DatasetFingerprint = datasetFingerprint,
                TaskIds = selectedTasks.Select(task => task.Id).ToList(),
                RunsPerTask = options.TaskSet.Document.RunsPerTask,
                RequestedRuns = selectedTasks.Sum(_ => options.TaskSet.Document.RunsPerTask),
                SkippedManualTasks = skippedManualTaskIds.Count,
                SkippedManualTaskIds = skippedManualTaskIds,
                BaselineSummaryPath = options.BaselineSummaryPath,
                ReportFileName = "report.md",
                ExcelProcessIds = new List<string>(),
                Runs = new List<ExcelEvalRunResult>()
            };
            if (baselineSummary != null)
            {
                ExcelEvalMetricsReporter.EnsureComparable(summary, baselineSummary);
            }

            var runRoot = Path.Combine(
                options.OutputDirectory,
                "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(runRoot);

            foreach (var task in selectedTasks)
            {
                RunTaskGroup(
                    task,
                    modelConfiguration,
                    options,
                    runRoot,
                    summary);
            }

            summary.CompletedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            WriteJson(Path.Combine(runRoot, "summary.json"), summary);
            ExcelEvalMetricsReporter.WriteReport(
                Path.Combine(runRoot, summary.ReportFileName),
                summary,
                baselineSummary);
            Console.WriteLine(
                "Excel Eval completed: " + summary.PassedRuns.ToString(CultureInfo.InvariantCulture)
                + " passed, " + summary.FailedRuns.ToString(CultureInfo.InvariantCulture)
                + " failed, " + summary.SkippedManualTasks.ToString(CultureInfo.InvariantCulture)
                + " tasks not run.");
            if (summary.SkippedManualTaskIds.Count > 0)
            {
                Console.WriteLine(
                    "Tasks not run (unsupported fault injection or outside selected cohort): "
                    + string.Join(", ", summary.SkippedManualTaskIds));
            }
            Console.WriteLine("Artifacts: " + runRoot);
            Console.WriteLine("Report: " + Path.Combine(runRoot, summary.ReportFileName));
            return summary;
        }

        private static ExcelEvalRunResult RunTaskOnce(
            ExcelAutomationSession excel,
            IAgentBrokerClient client,
            ExcelEvalModelConfiguration modelConfiguration,
            ExcelEvalRunnerOptions options,
            ExcelEvalTaskDefinition task,
            int runNumber,
            string runRoot)
        {
            var taskOutputDirectory = Path.Combine(
                runRoot,
                task.Id,
                "run-" + runNumber.ToString("D2", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(taskOutputDirectory);

            var inputPath = Path.Combine(taskOutputDirectory, "input.xlsx");
            var baselinePath = Path.Combine(taskOutputDirectory, "baseline.xlsx");
            var resultPath = Path.Combine(taskOutputDirectory, "workbook.xlsx");
            var answerPath = Path.Combine(taskOutputDirectory, "answer.txt");
            var tracePath = Path.Combine(taskOutputDirectory, "trace.jsonl");
            var resultJsonPath = Path.Combine(taskOutputDirectory, "result.json");
            var fixturePath = Path.Combine(options.TaskSet.FixtureDirectory, task.Fixture + ".xlsx");
            File.Copy(fixturePath, inputPath, false);

            var runResult = new ExcelEvalRunResult
            {
                TaskId = task.Id,
                RunNumber = runNumber,
                SkillRoutingMode = string.Equals(
                    (task.RoutingMode ?? string.Empty).Trim(),
                    "real",
                    StringComparison.OrdinalIgnoreCase)
                        ? "real"
                        : "pinned",
                ExpectedSkill = task.Skill ?? string.Empty,
                ExecutionMode = task.ExecutionMode ?? string.Empty,
                InquiryCohort = GetInquiryCohort(task),
                OutputDirectory = taskOutputDirectory,
                Checks = new List<ExcelEvalCheck>()
            };
            ExcelEvalEventSink runEventSink = null;
            var retainBaselineForDiagnostics = false;
            var isManualConfirmationTask = IsManualConfirmationTask(task);

            try
            {
                using (var workbook = excel.OpenWorkbook(inputPath, !isManualConfirmationTask))
                using (var eventSink = new ExcelEvalEventSink(
                    tracePath,
                    client,
                    task,
                    task.Id,
                    runNumber,
                    isManualConfirmationTask && options.AllowWorkbookWriteTests,
                    GetFaultInjection(task)))
                {
                    runEventSink = eventSink;
                    workbook.SaveCopyAs(baselinePath);
                    var baselineFingerprint = WorkbookPackageFingerprint.Compute(baselinePath);
                    var cellValuesFingerprint = isManualConfirmationTask
                        && GetBooleanExpectation(task, "cellValuesUnchanged")
                            ? ExcelWorkbookAssertions.ComputeCellValuesFingerprint(workbook)
                            : null;
                    using (var cancellationSource = new CancellationTokenSource(
                        TimeSpan.FromSeconds(options.TimeoutSeconds)))
                    {
                        var requestId = Guid.NewGuid().ToString("N");
                        var turnRequest = new AgentBrokerTurnRequest
                        {
                            ConversationId = Guid.NewGuid().ToString("N"),
                            MessageId = Guid.NewGuid().ToString("N"),
                            UserInput = task.Prompt,
                            InteractiveMode = isManualConfirmationTask ? "agent" : "ask",
                            SkillId = string.Equals(
                                (task.RoutingMode ?? string.Empty).Trim(),
                                "real",
                                StringComparison.OrdinalIgnoreCase)
                                    ? string.Empty
                                    : task.Skill ?? string.Empty,
                            ModelProviderId = modelConfiguration.ModelProviderId,
                            ModelOptions = modelConfiguration.ModelOptions,
                            SessionId = Guid.NewGuid().ToString("N")
                        };
                        turnRequest.Metadata["brokerRequestId"] = requestId;
                        turnRequest.Metadata["runtime.turnTimeoutSeconds"] =
                            options.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
                        eventSink.SetTurnRequest(turnRequest, requestId);

                        var turnTask = client.SendTurnAsync(
                            turnRequest,
                            eventSink,
                            cancellationSource.Token);
                        AgentBrokerTurnResult turnResult;
                        try
                        {
                            turnResult = PumpStaUntilCompleted(turnTask, eventSink, workbook);
                        }
                        catch (OperationCanceledException)
                        {
                            throw new TimeoutException(
                                "Broker turn exceeded the configured timeout of "
                                + options.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)
                                + " seconds.");
                        }

                        runResult.TurnSucceeded = turnResult != null && turnResult.Success;
                        runResult.Answer = turnResult == null ? string.Empty : turnResult.Content ?? string.Empty;
                        runResult.ContextUsageTokens = turnResult == null
                            ? (int?)null
                            : turnResult.ContextUsageTokens;
                        File.WriteAllText(answerPath, runResult.Answer, new UTF8Encoding(false));

                        workbook.SaveCopyAs(resultPath);
                        var outputFingerprint = WorkbookPackageFingerprint.Compute(resultPath);
                        var workbookUnchanged = baselineFingerprint == outputFingerprint;
                        retainBaselineForDiagnostics = !workbookUnchanged;
                        if (isManualConfirmationTask)
                        {
                            object expectedChangeScope;
                            if (!task.Expect.TryGetValue("expectedChangeScope", out expectedChangeScope)
                                || !(expectedChangeScope is IList))
                            {
                                throw new InvalidDataException(
                                    "Manual task '" + task.Id + "' is missing expectedChangeScope.");
                            }

                            var scopeAudit = WorkbookChangeScopeAuditor.Compare(
                                baselinePath,
                                resultPath,
                                (IList)expectedChangeScope);
                            runResult.ChangeScopeAuditCompleted = true;
                            runResult.ChangedCellCount = scopeAudit.ChangedCellCount;
                            runResult.OutOfScopeCellCount = scopeAudit.OutOfScopeCellCount;
                            runResult.ChangedWorksheetCount = scopeAudit.ChangedWorksheetCount;
                            runResult.OutOfScopeWorksheetCount = scopeAudit.OutOfScopeWorksheetCount;
                            runResult.Checks.Add(new ExcelEvalCheck
                            {
                                Name = "submitted-change-scope",
                                Passed = scopeAudit.IsWithinScope,
                                Expected = "all changed cells are within expectedChangeScope; "
                                    + "worksheet additions must be explicitly declared",
                                Actual = scopeAudit.ChangedCellCount.ToString(CultureInfo.InvariantCulture)
                                    + " changed cell(s), "
                                    + scopeAudit.OutOfScopeCellCount.ToString(CultureInfo.InvariantCulture)
                                    + " out-of-scope cell(s), "
                                    + scopeAudit.ChangedWorksheetCount.ToString(CultureInfo.InvariantCulture)
                                    + " added/removed worksheet(s), "
                                    + scopeAudit.OutOfScopeWorksheetCount.ToString(CultureInfo.InvariantCulture)
                                    + " out-of-scope worksheet(s)"
                            });
                        }

                        runResult.Checks.Add(new ExcelEvalCheck
                        {
                            Name = GetBooleanExpectation(task, "clarificationOnly")
                                ? "expected-clarification"
                                : "turn-success",
                            Passed = GetBooleanExpectation(task, "clarificationOnly")
                                ? eventSink.ClarificationRequestCount > 0 && !eventSink.UnexpectedInteraction
                                : runResult.TurnSucceeded,
                            Expected = GetBooleanExpectation(task, "clarificationOnly")
                                ? "at least one expected clarifying question"
                                : "successful broker turn",
                            Actual = GetBooleanExpectation(task, "clarificationOnly")
                                ? eventSink.ClarificationRequestCount.ToString(CultureInfo.InvariantCulture)
                                    + " clarifying question(s)"
                                : turnResult == null
                                    ? "no turn result"
                                    : (turnResult.Success ? "success" : turnResult.ErrorCode + ": " + turnResult.ErrorMessage)
                        });
                        runResult.Checks.Add(new ExcelEvalCheck
                        {
                            Name = "model-invocation",
                            Passed = eventSink.MaxRound > 0,
                            Expected = "at least one model round",
                            Actual = eventSink.MaxRound > 0
                                ? eventSink.MaxRound.ToString(CultureInfo.InvariantCulture)
                                : "no model round observed"
                        });
                        runResult.Checks.AddRange(EvaluateRubric(
                            task,
                            options.TaskSet.AnswersByFixture[task.Fixture],
                            runResult.Answer,
                            workbookUnchanged,
                            eventSink,
                            workbook,
                            cellValuesFingerprint));
                    }
                }

                runResult.Passed = runResult.Checks.All(check => check.Passed);
            }
            catch (Exception ex)
            {
                runResult.Error = ex.ToString();
                runResult.Checks.Add(new ExcelEvalCheck
                {
                    Name = "runner-execution",
                    Passed = false,
                    Expected = "task completes and artifacts are captured",
                    Actual = ex.Message
                });
                runResult.Passed = false;
            }
            finally
            {
                if (!retainBaselineForDiagnostics && File.Exists(baselinePath))
                {
                    File.Delete(baselinePath);
                }

                if (runEventSink != null)
                {
                    runResult.ObservedRounds = runEventSink.MaxRound > 0
                        ? (int?)runEventSink.MaxRound
                        : null;
                    runResult.ToolCallCount = runEventSink.ToolCallCount;
                    runResult.ForbiddenToolCallCount = runEventSink.ForbiddenToolCallCount;
                    runResult.AutoApprovedWriteConfirmations =
                        runEventSink.AutoApprovedWriteConfirmations;
                    runResult.ClarificationRequestCount =
                        runEventSink.ClarificationRequestCount;
                    runResult.ExecutionModeDeclared =
                        runEventSink.ExecutionModeDeclared;
                    runResult.ExceededPreview =
                        runEventSink.ExceededPreview;
                    runResult.DeclaredWriteCallCount =
                        runEventSink.DeclaredWriteCallCount;
                    runResult.PreviewWriteCallCount =
                        runEventSink.PreviewWriteCallCount;
                    runResult.ExceededPreviewWriteCallCount =
                        runEventSink.ExceededPreviewWriteCallCount;
                    runResult.UserSelectedWriteCallCount =
                        runEventSink.UserSelectedWriteCallCount;
                    runResult.UserSelectedWriteViolationCount =
                        runEventSink.UserSelectedWriteViolationCount;
                    runResult.QualitySummary = runEventSink.QualitySummary;
                }

                runResult.TrustTier = runResult.QualitySummary != null
                    && runResult.QualitySummary.HasExcelDataToolCalls
                    && !string.IsNullOrWhiteSpace(runResult.QualitySummary.TrustTier)
                    ? runResult.QualitySummary.TrustTier
                    : ExtractTrustTier(runResult.Answer);
            }

            WriteJson(resultJsonPath, runResult);
            return runResult;
        }

        private static IList<ExcelEvalCheck> EvaluateRubric(
            ExcelEvalTaskDefinition task,
            IDictionary<string, object> answerRoot,
            string answer,
            bool workbookUnchanged,
            ExcelEvalEventSink eventSink,
            ExcelWorkbookSession workbook,
            string cellValuesFingerprint)
        {
            var checks = new List<ExcelEvalCheck>();
            var expect = task.Expect;

            if (string.Equals(
                (task.RoutingMode ?? string.Empty).Trim(),
                "real",
                StringComparison.OrdinalIgnoreCase))
            {
                var selectedSkill = eventSink.QualitySummary == null
                    ? string.Empty
                    : eventSink.QualitySummary.SelectedSkillName ?? string.Empty;
                checks.Add(new ExcelEvalCheck
                {
                    Name = "skill-routing",
                    Passed = !string.IsNullOrWhiteSpace(task.Skill)
                        && !string.IsNullOrWhiteSpace(selectedSkill)
                        && string.Equals(selectedSkill, task.Skill, StringComparison.OrdinalIgnoreCase),
                    Expected = task.Skill,
                    Actual = selectedSkill
                });
            }

            object answerNumbers;
            if (expect.TryGetValue("answerNumbers", out answerNumbers))
            {
                checks.Add(EvaluateAnswerNumbers(answer, answerRoot, AsList(answerNumbers)));
            }

            object requiredTerms;
            if (expect.TryGetValue("answerContainsAll", out requiredTerms))
            {
                foreach (var term in AsList(requiredTerms).Select(
                    value => Convert.ToString(value, CultureInfo.InvariantCulture)))
                {
                    checks.Add(new ExcelEvalCheck
                    {
                        Name = "answer-contains",
                        Passed = answer.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0,
                        Expected = term,
                        Actual = answer
                    });
                }
            }

            object optionalTerms;
            if (expect.TryGetValue("answerMentionsAny", out optionalTerms))
            {
                var terms = AsList(optionalTerms)
                    .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))
                    .ToList();
                checks.Add(new ExcelEvalCheck
                {
                    Name = "answer-mentions-any",
                    Passed = terms.Any(term => answer.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0),
                    Expected = string.Join(" | ", terms),
                    Actual = answer
                });
            }

            object expectUnchanged;
            if (expect.TryGetValue("workbookUnchanged", out expectUnchanged))
            {
                checks.Add(new ExcelEvalCheck
                {
                    Name = "workbook-unchanged",
                    Passed = Equals(expectUnchanged, workbookUnchanged),
                    Expected = Convert.ToString(expectUnchanged, CultureInfo.InvariantCulture),
                    Actual = workbookUnchanged.ToString()
                });
            }

            object traceValue;
            if (expect.TryGetValue("trace", out traceValue))
            {
                var trace = traceValue as Dictionary<string, object>;
                if (trace != null)
                {
                    object minimumVerificationRetries;
                    if (trace.TryGetValue("minVerificationRetries", out minimumVerificationRetries))
                    {
                        var minimumRetries = Convert.ToInt32(
                            minimumVerificationRetries,
                            CultureInfo.InvariantCulture);
                        checks.Add(new ExcelEvalCheck
                        {
                            Name = "trace-min-verification-retries",
                            Passed = eventSink.VerificationFailureCount >= minimumRetries,
                            Expected = "at least " + minimumRetries.ToString(CultureInfo.InvariantCulture)
                                + " failed verification after injected fault",
                            Actual = eventSink.VerificationFailureCount.ToString(CultureInfo.InvariantCulture)
                        });
                        checks.Add(new ExcelEvalCheck
                        {
                            Name = "trace-verification-recovery",
                            Passed = eventSink.SuccessfulVerificationRetryCount > 0,
                            Expected = "a successful excel_assert after a failed verification",
                            Actual = eventSink.SuccessfulVerificationRetryCount.ToString(
                                CultureInfo.InvariantCulture)
                        });
                    }

                    object forbidden;
                    if (trace.TryGetValue("forbidTools", out forbidden))
                    {
                        var forbiddenTools = AsList(forbidden).Select(Convert.ToString).ToList();
                        var observed = eventSink.ToolNames;
                        var violations = observed
                            .Where(name => forbiddenTools.Contains(name, StringComparer.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        checks.Add(new ExcelEvalCheck
                        {
                            Name = "trace-forbidden-tools",
                            Passed = violations.Length == 0,
                            Expected = "none of: " + string.Join(", ", forbiddenTools),
                            Actual = violations.Length == 0 ? "none" : string.Join(", ", violations)
                        });
                    }

                    object maximumRounds;
                    if (trace.TryGetValue("maxRounds", out maximumRounds))
                    {
                        var maxRounds = Convert.ToInt32(maximumRounds, CultureInfo.InvariantCulture);
                        var observedRounds = eventSink.MaxRound;
                        checks.Add(new ExcelEvalCheck
                        {
                            Name = "trace-max-rounds",
                            Passed = observedRounds > 0 && observedRounds <= maxRounds,
                            Expected = "<= " + maxRounds.ToString(CultureInfo.InvariantCulture),
                            Actual = observedRounds.ToString(CultureInfo.InvariantCulture)
                        });
                    }

                    object requiredToolsAny;
                    if (trace.TryGetValue("requiredToolsAny", out requiredToolsAny))
                    {
                        var requiredTools = AsList(requiredToolsAny).Select(Convert.ToString).ToList();
                        var observedTools = eventSink.ToolNames;
                        var matchedTools = observedTools
                            .Where(name => requiredTools.Contains(name, StringComparer.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        checks.Add(new ExcelEvalCheck
                        {
                            Name = "trace-required-tools",
                            Passed = matchedTools.Length > 0,
                            Expected = "at least one of: " + string.Join(", ", requiredTools),
                            Actual = matchedTools.Length == 0 ? "none observed" : string.Join(", ", matchedTools)
                        });
                    }

                    checks.Add(new ExcelEvalCheck
                    {
                        Name = "trace-no-interaction",
                        Passed = GetBooleanExpectation(task, "clarificationOnly")
                            ? eventSink.ClarificationRequestCount > 0 && !eventSink.UnexpectedInteraction
                            : !eventSink.UnexpectedInteraction,
                        Expected = GetBooleanExpectation(task, "clarificationOnly")
                            ? "expected clarification only"
                            : "no undeclared user interaction",
                        Actual = eventSink.UnexpectedInteraction
                            ? "unexpected interaction"
                            : eventSink.ClarificationRequestCount > 0
                                ? eventSink.ClarificationRequestCount.ToString(CultureInfo.InvariantCulture)
                                    + " clarification request(s)"
                                : "none"
                    });
                }
            }

            if (eventSink.FaultInjection != null)
            {
                checks.Add(new ExcelEvalCheck
                {
                    Name = "fault-injection-applied-once",
                    Passed = eventSink.FaultInjectionAppliedCount == 1,
                    Expected = "1 application of " + eventSink.FaultInjection,
                    Actual = eventSink.FaultInjectionAppliedCount.ToString(CultureInfo.InvariantCulture)
                });
            }

            object workbookAssertions;
            if (expect.TryGetValue("workbookAssertions", out workbookAssertions))
            {
                checks.AddRange(ExcelWorkbookAssertions.Evaluate(
                    workbook,
                    answerRoot,
                    AsList(workbookAssertions)));
            }

            object expectCellValuesUnchanged;
            if (expect.TryGetValue("cellValuesUnchanged", out expectCellValuesUnchanged))
            {
                var actualFingerprint = ExcelWorkbookAssertions.ComputeCellValuesFingerprint(workbook);
                checks.Add(new ExcelEvalCheck
                {
                    Name = "cell-values-unchanged",
                    Passed = Equals(expectCellValuesUnchanged, true)
                        && string.Equals(
                            cellValuesFingerprint,
                            actualFingerprint,
                            StringComparison.Ordinal),
                    Expected = "cell values unchanged",
                    Actual = string.Equals(
                        cellValuesFingerprint,
                        actualFingerprint,
                        StringComparison.Ordinal)
                        ? "unchanged"
                        : "changed"
                });
            }

            object trustValue;
            if (expect.TryGetValue("trustTier", out trustValue))
            {
                var trust = trustValue as Dictionary<string, object>;
                object minimum;
                var minimumTier = trust != null && trust.TryGetValue("min", out minimum)
                    ? Convert.ToString(minimum, CultureInfo.InvariantCulture)
                    : string.Empty;
                var actualTier = ExtractTrustTier(answer);
                checks.Add(new ExcelEvalCheck
                {
                    Name = "trust-tier",
                    Passed = TierRank(actualTier) >= TierRank(minimumTier),
                    Expected = "at least " + minimumTier,
                    Actual = string.IsNullOrEmpty(actualTier) ? "no runtime trust footer" : actualTier
                });
            }

            return checks;
        }

        private static string ExtractTrustTier(string answer)
        {
            var tierMatch = _trustTierPattern.Match(answer ?? string.Empty);
            if (!tierMatch.Success)
            {
                return string.Empty;
            }

            var tier = tierMatch.Groups["tier"].Value;
            if (!string.IsNullOrWhiteSpace(tier))
            {
                return tier.ToUpperInvariant();
            }

            switch (tierMatch.Groups["phrase"].Value)
            {
                case "全量独立复核通过":
                    return "A";
                case "已复核，仍有局限":
                    return "B";
                case "尚未完成有效验证":
                    return "C";
                default:
                    return string.Empty;
            }
        }

        private static ExcelEvalCheck EvaluateAnswerNumbers(
            string answer,
            IDictionary<string, object> answerRoot,
            IList expectedNumberDefinitions)
        {
            var actualNumbers = ParseNumbers(answer);
            var consumed = new HashSet<int>();
            var missing = new List<string>();
            var definitionCount = 0;

            foreach (var definitionValue in expectedNumberDefinitions)
            {
                var definition = definitionValue as Dictionary<string, object>;
                if (definition == null)
                {
                    throw new InvalidDataException("answerNumbers entries must be objects.");
                }

                object fromValue;
                if (!definition.TryGetValue("from", out fromValue))
                {
                    throw new InvalidDataException("answerNumbers entry is missing 'from'.");
                }

                var reference = Convert.ToString(fromValue, CultureInfo.InvariantCulture);
                var expectedValues = ExcelEvalTaskSetLoader.ResolveAnswerPath(answerRoot, reference);
                object toleranceValue;
                var tolerance = definition.TryGetValue("tolerance", out toleranceValue)
                    ? Convert.ToDouble(toleranceValue, CultureInfo.InvariantCulture)
                    : 0d;
                object decimalsValue;
                var percentDecimals = definition.TryGetValue("percentDecimals", out decimalsValue)
                    ? Convert.ToInt32(decimalsValue, CultureInfo.InvariantCulture)
                    : -1;

                foreach (var expectedValue in expectedValues)
                {
                    definitionCount++;
                    double expected;
                    if (!TryConvertNumber(expectedValue, out expected))
                    {
                        throw new InvalidDataException(
                            "answerNumbers reference did not resolve to a number: " + reference);
                    }

                    if (percentDecimals >= 0)
                    {
                        expected = Math.Round(expected, percentDecimals, MidpointRounding.AwayFromZero);
                    }

                    var matchIndex = -1;
                    for (var i = 0; i < actualNumbers.Count; i++)
                    {
                        if (consumed.Contains(i))
                        {
                            continue;
                        }

                        var candidate = actualNumbers[i];
                        if (percentDecimals >= 0
                            && (!candidate.IsPercent || candidate.DecimalDigits != percentDecimals))
                        {
                            continue;
                        }

                        if (Math.Abs(candidate.Value - expected) <= tolerance)
                        {
                            matchIndex = i;
                            break;
                        }
                    }

                    if (matchIndex >= 0)
                    {
                        consumed.Add(matchIndex);
                    }
                    else
                    {
                        missing.Add(expected.ToString("G17", CultureInfo.InvariantCulture)
                            + (percentDecimals >= 0 ? "%" : string.Empty));
                    }
                }
            }

            return new ExcelEvalCheck
            {
                Name = "answer-numbers",
                Passed = missing.Count == 0 && definitionCount > 0,
                Expected = definitionCount.ToString(CultureInfo.InvariantCulture) + " expected values",
                Actual = missing.Count == 0
                    ? "all expected values found"
                    : "missing: " + string.Join(", ", missing)
            };
        }

        private static List<ParsedNumber> ParseNumbers(string answer)
        {
            var values = new List<ParsedNumber>();
            foreach (Match match in _numberPattern.Matches(answer ?? string.Empty))
            {
                double value;
                if (!double.TryParse(
                    match.Groups["number"].Value.Replace(",", string.Empty),
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out value))
                {
                    continue;
                }

                var decimalPoint = match.Groups["number"].Value.IndexOf('.');
                values.Add(new ParsedNumber
                {
                    Value = value,
                    IsPercent = match.Groups["percent"].Success,
                    DecimalDigits = decimalPoint < 0
                        ? 0
                        : match.Groups["number"].Value.Length - decimalPoint - 1
                });
            }

            return values;
        }

        private static bool TryConvertNumber(object value, out double number)
        {
            if (value == null)
            {
                number = 0;
                return false;
            }

            return double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out number);
        }

        private static List<object> AsList(object value)
        {
            var list = value as IList;
            if (list == null)
            {
                throw new InvalidDataException("Expected a JSON array in the task rubric.");
            }

            var result = new List<object>();
            foreach (var item in list)
            {
                result.Add(item);
            }

            return result;
        }

        private static int TierRank(string tier)
        {
            switch ((tier ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "A":
                    return 3;
                case "B":
                    return 2;
                case "C":
                    return 1;
                default:
                    return 0;
            }
        }

        private static IList<ExcelEvalTaskDefinition> SelectTasks(ExcelEvalRunnerOptions options)
        {
            if (options.RunAllReadOnlyTasks)
            {
                return options.TaskSet.Document.Tasks
                    .Where(task => string.Equals(
                        task.ExecutionMode,
                        "automatic-read-only",
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (options.RunAllManualConfirmationTasks)
            {
                if (!options.AllowWorkbookWriteTests)
                {
                    throw new InvalidOperationException(
                        "Manual-confirmation tasks require explicit --allow-workbook-write-tests opt-in.");
                }

                var selectedTasks = options.TaskSet.Document.Tasks
                    .Where(IsManualConfirmationTask)
                    .Where(task => !HasUnsupportedFaultInjection(task))
                    .ToList();
                if (selectedTasks.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No manual-confirmation tasks are runnable; all require unsupported fault injection.");
                }

                return selectedTasks;
            }

            var selectedTask = options.TaskSet.FindTask(options.TaskId);
            if (selectedTask == null)
            {
                throw new InvalidDataException("Excel Eval task id not found: " + options.TaskId);
            }

            if (IsManualConfirmationTask(selectedTask))
            {
                if (!options.AllowWorkbookWriteTests)
                {
                    throw new InvalidOperationException(
                        "Task '" + selectedTask.Id + "' modifies its workbook. "
                        + "Use --allow-workbook-write-tests to run only on an isolated copy.");
                }

                if (HasUnsupportedFaultInjection(selectedTask))
                {
                    throw new InvalidOperationException(
                        "Task '" + selectedTask.Id
                        + "' requires an unsupported fault injector: "
                        + GetConfiguredFaultInjectionName(selectedTask) + ".");
                }
            }
            else if (!string.Equals(
                selectedTask.ExecutionMode,
                "automatic-read-only",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Task '" + selectedTask.Id + "' has an unsupported execution mode.");
            }

            return new[] { selectedTask };
        }

        private static bool IsManualConfirmationTask(ExcelEvalTaskDefinition task)
        {
            return string.Equals(
                task.ExecutionMode,
                "manual-confirmation",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasUnsupportedFaultInjection(ExcelEvalTaskDefinition task)
        {
            object faultInjection;
            if (task.Scenario == null || !task.Scenario.TryGetValue("faultInjection", out faultInjection))
            {
                return false;
            }

            var name = Convert.ToString(faultInjection, CultureInfo.InvariantCulture);
            return !string.Equals(name, "corrupt-first-staged-total", StringComparison.Ordinal)
                && !string.Equals(name, "omit-one-unmatched-order", StringComparison.Ordinal);
        }

        private static string GetFaultInjection(ExcelEvalTaskDefinition task)
        {
            object faultInjection;
            if (task.Scenario == null || !task.Scenario.TryGetValue("faultInjection", out faultInjection))
            {
                return null;
            }

            var name = Convert.ToString(faultInjection, CultureInfo.InvariantCulture);
            if (HasUnsupportedFaultInjection(task))
            {
                throw new InvalidOperationException(
                    "Task '" + task.Id + "' requires an unsupported fault injector: "
                    + (name ?? string.Empty) + ".");
            }

            return name;
        }

        private static string GetConfiguredFaultInjectionName(ExcelEvalTaskDefinition task)
        {
            object faultInjection;
            return task.Scenario != null && task.Scenario.TryGetValue("faultInjection", out faultInjection)
                ? Convert.ToString(faultInjection, CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static bool GetBooleanExpectation(ExcelEvalTaskDefinition task, string name)
        {
            object value;
            return task.Expect != null
                && task.Expect.TryGetValue(name, out value)
                && Equals(value, true);
        }

        private static string GetInquiryCohort(ExcelEvalTaskDefinition task)
        {
            if (task.Tags != null && task.Tags.Any(tag =>
                string.Equals(tag, "clarification-required", StringComparison.OrdinalIgnoreCase)))
            {
                return "clarification-required";
            }

            if (task.Tags != null && task.Tags.Any(tag =>
                string.Equals(tag, "casual-routing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "fuzzy-routing", StringComparison.OrdinalIgnoreCase)))
            {
                return "casual-routing";
            }

            return "clear";
        }

        private static AgentBrokerEndpointInfo ResolveEndpoint(int processId, string pipeNameOverride)
        {
            if (!string.IsNullOrWhiteSpace(pipeNameOverride))
            {
                return new AgentBrokerEndpointInfo
                {
                    ProcessId = processId,
                    ServerName = ".",
                    PipeName = pipeNameOverride
                };
            }

            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                var endpoints = AgentBrokerEndpointRegistry.GetForProcess(processId);
                if (endpoints.Count == 1)
                {
                    return endpoints[0];
                }

                if (endpoints.Count > 1)
                {
                    throw new InvalidOperationException(
                        "More than one Broker endpoint is registered for Excel process "
                        + processId.ToString(CultureInfo.InvariantCulture)
                        + ". Supply --broker-pipe to select the intended window.");
                }

                Application.DoEvents();
                Thread.Sleep(200);
            }

            throw new TimeoutException(
                "No Broker endpoint was published for Excel process "
                + processId.ToString(CultureInfo.InvariantCulture)
                + " within 60 seconds. Confirm that the add-in and Copilot pane are loaded.");
        }

        private static IAgentBrokerClient CreateClient(AgentBrokerEndpointInfo endpoint, int timeoutSeconds)
        {
            var timeoutMilliseconds = checked(timeoutSeconds * 1000);
            var transport = new NamedPipeAgentBrokerTransport(
                new NamedPipeAgentBrokerTransportOptions
                {
                    ServerName = endpoint.ServerName,
                    PipeName = endpoint.PipeName,
                    ConnectTimeoutMilliseconds = 5000,
                    ReadTimeoutMilliseconds = timeoutMilliseconds,
                    WriteTimeoutMilliseconds = timeoutMilliseconds
                });

            return new TransportAgentBrokerClient(
                new AgentBrokerConnectionOptions
                {
                    Endpoint = endpoint.PipeName,
                    ClientId = "excel-eval-" + Guid.NewGuid().ToString("N"),
                    RequestTimeoutMilliseconds = timeoutMilliseconds
                },
                transport);
        }

        private void RunTaskGroup(
            ExcelEvalTaskDefinition task,
            ExcelEvalModelConfiguration modelConfiguration,
            ExcelEvalRunnerOptions options,
            string runRoot,
            ExcelEvalRunSummary summary)
        {
            using (var excel = new ExcelAutomationSession())
            {
                var processId = excel.ProcessId.ToString(CultureInfo.InvariantCulture);
                summary.ExcelProcessIds.Add(processId);
                if (string.IsNullOrWhiteSpace(summary.ExcelProcessId))
                {
                    summary.ExcelProcessId = processId;
                }

                var bootstrapPath = Path.Combine(runRoot, "endpoint-bootstrap.xlsx");
                File.Copy(
                    Path.Combine(options.TaskSet.FixtureDirectory, task.Fixture + ".xlsx"),
                    bootstrapPath,
                    false);

                AgentBrokerEndpointInfo endpoint;
                try
                {
                    using (excel.OpenReadOnly(bootstrapPath))
                    {
                        endpoint = ResolveEndpoint(excel.ProcessId, options.BrokerPipeName);
                    }
                }
                finally
                {
                    if (File.Exists(bootstrapPath))
                    {
                        File.Delete(bootstrapPath);
                    }
                }

                var client = CreateClient(endpoint, options.TimeoutSeconds);
                for (var runNumber = 1;
                    runNumber <= options.TaskSet.Document.RunsPerTask;
                    runNumber++)
                {
                    var result = RunTaskOnce(
                        excel,
                        client,
                        modelConfiguration,
                        options,
                        task,
                        runNumber,
                        runRoot);
                    summary.Runs.Add(result);
                    if (result.Passed)
                    {
                        summary.PassedRuns++;
                    }
                    else
                    {
                        summary.FailedRuns++;
                    }

                    Console.WriteLine(
                        (result.Passed ? "PASS " : "FAIL ")
                        + task.Id + " run " + runNumber.ToString(CultureInfo.InvariantCulture)
                        + " -> " + result.OutputDirectory);
                }
            }
        }

        private static void ValidateOptions(ExcelEvalRunnerOptions options)
        {
            if (options == null || options.TaskSet == null)
            {
                throw new ArgumentNullException(nameof(options), "A loaded task set is required.");
            }

            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                throw new ArgumentException("An output directory is required.", nameof(options));
            }

            if (options.TimeoutSeconds < 1 || options.TimeoutSeconds > 3600)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options.TimeoutSeconds),
                    "Timeout must be between 1 and 3600 seconds.");
            }

            if (options.RunAllManualConfirmationTasks && !options.AllowWorkbookWriteTests)
            {
                throw new InvalidOperationException(
                    "Manual-confirmation tasks require explicit write-test opt-in.");
            }

            if (options.AllowWorkbookWriteTests
                && !string.IsNullOrWhiteSpace(options.BaselineSummaryPath))
            {
                throw new InvalidOperationException(
                    "Write-test runs cannot be compared to an automatic read-only baseline.");
            }

            options.OutputDirectory = Path.GetFullPath(options.OutputDirectory);
            if (!string.IsNullOrWhiteSpace(options.BaselineSummaryPath))
            {
                options.BaselineSummaryPath = Path.GetFullPath(options.BaselineSummaryPath);
                if (!File.Exists(options.BaselineSummaryPath))
                {
                    throw new FileNotFoundException(
                        "Excel Eval baseline summary was not found.",
                        options.BaselineSummaryPath);
                }
            }
        }

        private static ExcelEvalRunSummary LoadBaselineSummary(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var summary = _serializer.Deserialize<ExcelEvalRunSummary>(File.ReadAllText(path));
            if (summary == null)
            {
                throw new InvalidDataException("Excel Eval baseline summary is empty or invalid: " + path);
            }

            return summary;
        }

        private static string ComputeDatasetFingerprint(
            LoadedExcelEvalTaskSet taskSet,
            IList<ExcelEvalTaskDefinition> selectedTasks)
        {
            var components = new List<string>
            {
                "excel-eval-dataset-v1",
                ComputeFileHash(taskSet.TaskSetPath)
            };

            foreach (var fixture in selectedTasks
                .Select(task => task.Fixture)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                components.Add(fixture);
                components.Add(ComputeFileHash(
                    Path.Combine(taskSet.FixtureDirectory, fixture + ".xlsx")));
                components.Add(ComputeFileHash(
                    Path.Combine(taskSet.FixtureDirectory, fixture + ".answers.json")));
            }

            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", components)));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static string ComputeFileHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static T PumpStaUntilCompleted<T>(
            Task<T> task,
            ExcelEvalEventSink eventSink,
            ExcelWorkbookSession workbook)
        {
            while (!task.IsCompleted)
            {
                Application.DoEvents();
                string faultInjection;
                TaskCompletionSource<bool> completion;
                if (eventSink.TryTakePendingFaultInjection(out faultInjection, out completion))
                {
                    try
                    {
                        workbook.ApplyFaultInjection(faultInjection);
                        eventSink.CompleteFaultInjection(completion, null);
                    }
                    catch (Exception exception)
                    {
                        eventSink.CompleteFaultInjection(completion, exception);
                        throw;
                    }
                }

                Thread.Sleep(20);
            }

            return task.GetAwaiter().GetResult();
        }

        private static void WriteJson(string path, object value)
        {
            File.WriteAllText(
                path,
                _serializer.Serialize(value),
                new UTF8Encoding(false));
        }

        private sealed class ParsedNumber
        {
            public double Value { get; set; }

            public bool IsPercent { get; set; }

            public int DecimalDigits { get; set; }
        }
    }

    internal sealed class ExcelEvalEventSink : IAgentBrokerEventSink, IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly StreamWriter _writer;
        private readonly IAgentBrokerClient _client;
        private readonly AgentBrokerCancelTurnRequest _cancelRequest;
        private readonly HashSet<string> _forbiddenTools;
        private readonly HashSet<string> _allowedConfirmationTools;
        private readonly bool _allowExpectedWriteConfirmations;
        private readonly bool _expectClarificationOnly;
        private readonly string _faultInjection;
        private readonly Dictionary<string, string> _vbaOutputSheets =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly JavaScriptSerializer _serializer =
            ExcelEvalTaskSetLoader.CreateSerializer();
        private readonly List<string> _toolNames = new List<string>();
        private bool _disposed;
        private bool _unexpectedInteraction;
        private int _maxRound;
        private int _toolCallCount;
        private int _forbiddenToolCallCount;
        private int _autoApprovedWriteConfirmations;
        private int _faultInjectionAppliedCount;
        private int _verificationFailureCount;
        private int _successfulVerificationRetryCount;
        private int _clarificationRequestCount;
        private int _executionModeDeclared;
        private int _exceededPreview;
        private int _declaredWriteCallCount;
        private int _previewWriteCallCount;
        private int _exceededPreviewWriteCallCount;
        private int _userSelectedWriteCallCount;
        private int _userSelectedWriteViolationCount;
        private bool _faultInjectionScheduled;
        private bool _verificationFailedAfterFaultInjection;
        private TaskCompletionSource<bool> _pendingFaultInjection;
        private AgentBrokerTurnQualitySummary _qualitySummary;

        public ExcelEvalEventSink(
            string tracePath,
            IAgentBrokerClient client,
            ExcelEvalTaskDefinition task,
            string taskId,
            int runNumber,
            bool allowExpectedWriteConfirmations,
            string faultInjection)
        {
            _writer = new StreamWriter(
                new FileStream(tracePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _forbiddenTools = ReadForbiddenTools(task.Expect);
            _allowedConfirmationTools = ReadAllowedConfirmationTools(task.Expect);
            _allowExpectedWriteConfirmations = allowExpectedWriteConfirmations;
            _expectClarificationOnly = task.Expect != null
                && task.Expect.TryGetValue("clarificationOnly", out var clarificationOnly)
                && Equals(clarificationOnly, true);
            _faultInjection = faultInjection;
            _cancelRequest = new AgentBrokerCancelTurnRequest();
            _taskId = taskId;
            _runNumber = runNumber;
        }

        private readonly string _taskId;
        private readonly int _runNumber;

        public IList<string> ToolNames
        {
            get
            {
                lock (_syncRoot)
                {
                    return _toolNames.ToArray();
                }
            }
        }

        public int MaxRound
        {
            get { return Volatile.Read(ref _maxRound); }
        }

        public int ToolCallCount
        {
            get { return Volatile.Read(ref _toolCallCount); }
        }

        public int ForbiddenToolCallCount
        {
            get { return Volatile.Read(ref _forbiddenToolCallCount); }
        }

        public int AutoApprovedWriteConfirmations
        {
            get { return Volatile.Read(ref _autoApprovedWriteConfirmations); }
        }

        public int ClarificationRequestCount
        {
            get { return Volatile.Read(ref _clarificationRequestCount); }
        }

        public bool ExecutionModeDeclared
        {
            get { return Volatile.Read(ref _executionModeDeclared) != 0; }
        }

        public bool ExceededPreview
        {
            get { return Volatile.Read(ref _exceededPreview) != 0; }
        }

        public int DeclaredWriteCallCount
        {
            get { return Volatile.Read(ref _declaredWriteCallCount); }
        }

        public int PreviewWriteCallCount
        {
            get { return Volatile.Read(ref _previewWriteCallCount); }
        }

        public int ExceededPreviewWriteCallCount
        {
            get { return Volatile.Read(ref _exceededPreviewWriteCallCount); }
        }

        public int UserSelectedWriteCallCount
        {
            get { return Volatile.Read(ref _userSelectedWriteCallCount); }
        }

        public int UserSelectedWriteViolationCount
        {
            get { return Volatile.Read(ref _userSelectedWriteViolationCount); }
        }

        public string FaultInjection
        {
            get { return _faultInjection; }
        }

        public int FaultInjectionAppliedCount
        {
            get { return Volatile.Read(ref _faultInjectionAppliedCount); }
        }

        public int VerificationFailureCount
        {
            get { return Volatile.Read(ref _verificationFailureCount); }
        }

        public int SuccessfulVerificationRetryCount
        {
            get { return Volatile.Read(ref _successfulVerificationRetryCount); }
        }

        public AgentBrokerTurnQualitySummary QualitySummary
        {
            get
            {
                lock (_syncRoot)
                {
                    return _qualitySummary;
                }
            }
        }

        public bool UnexpectedInteraction
        {
            get { return Volatile.Read(ref _unexpectedInteraction); }
        }

        public void SetTurnRequest(AgentBrokerTurnRequest request, string brokerRequestId)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            _cancelRequest.TargetRequestId = brokerRequestId ?? string.Empty;
            _cancelRequest.ConversationId = request.ConversationId ?? string.Empty;
            _cancelRequest.MessageId = request.MessageId ?? string.Empty;
            _cancelRequest.SessionId = request.SessionId ?? string.Empty;
            _cancelRequest.Reason = "Excel Eval rejected an undeclared interaction or forbidden tool.";
        }

        public async Task OnEventAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken)
        {
            if (brokerEvent == null)
            {
                return;
            }

            TaskCompletionSource<bool> faultInjectionCompletion = null;
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                var record = new Dictionary<string, object>
                {
                    { "receivedAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                    { "taskId", _taskId },
                    { "runNumber", _runNumber },
                    { "event", brokerEvent }
                };
                _writer.WriteLine(_serializer.Serialize(record));
                _writer.Flush();

                if (string.Equals(
                        brokerEvent.EventType,
                        AgentBrokerEventTypes.TurnCompleted,
                        StringComparison.OrdinalIgnoreCase)
                    && brokerEvent.QualitySummary != null)
                {
                    _qualitySummary = brokerEvent.QualitySummary;
                }

                if (string.Equals(
                    brokerEvent.EventType,
                    AgentBrokerEventTypes.ExecutionMode,
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (brokerEvent.Success)
                    {
                        Interlocked.Exchange(ref _executionModeDeclared, 1);
                    }
                    else
                    {
                        Interlocked.Exchange(ref _exceededPreview, 1);
                    }
                }

                if (string.Equals(
                    brokerEvent.EventType,
                    AgentBrokerEventTypes.WriteEffectEvaluated,
                    StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        brokerEvent.ActualWriteEffect,
                        "none",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (brokerEvent.ExecutionModeDeclared)
                    {
                        Interlocked.Increment(ref _declaredWriteCallCount);
                        if (brokerEvent.ExecutionModeUserSelected)
                        {
                            Interlocked.Increment(ref _userSelectedWriteCallCount);
                            if (brokerEvent.EffectExceedsDeclaredMaximum
                                || brokerEvent.TargetOutsideSelectedContract)
                            {
                                Interlocked.Increment(ref _userSelectedWriteViolationCount);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref _previewWriteCallCount);
                            if (brokerEvent.ExceededExecutionModePreview)
                            {
                                Interlocked.Increment(ref _exceededPreviewWriteCallCount);
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(brokerEvent.ToolName))
                {
                    _toolNames.Add(brokerEvent.ToolName);
                }

                if (IsToolCallStarted(brokerEvent))
                {
                    Interlocked.Increment(ref _toolCallCount);
                    if (IsForbiddenTool(brokerEvent))
                    {
                        Interlocked.Increment(ref _forbiddenToolCallCount);
                    }

                    if (string.Equals(
                            brokerEvent.ToolName,
                            "excel_execute_vba",
                            StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(brokerEvent.ToolCallId))
                    {
                        _vbaOutputSheets[brokerEvent.ToolCallId] =
                            ReadOutputSheetFromArguments(brokerEvent.Text);
                    }
                }

                if (IsToolCallCompleted(brokerEvent))
                {
                    RecordVerificationOutcome(brokerEvent);
                    faultInjectionCompletion = TryScheduleFaultInjection(brokerEvent);
                }

                if (brokerEvent.Round > _maxRound)
                {
                    Volatile.Write(ref _maxRound, brokerEvent.Round);
                }
            }

            if (faultInjectionCompletion != null)
            {
                await faultInjectionCompletion.Task.ConfigureAwait(false);
            }

            if (IsUnexpectedInteraction(brokerEvent))
            {
                var interaction = _serializer.Deserialize<AgentBrokerToolUserInteractionRequest>(
                    brokerEvent.Text ?? "{}");
                if (interaction == null || string.IsNullOrWhiteSpace(interaction.ToolCallId))
                {
                    throw new InvalidDataException(
                        "Broker emitted a user interaction event without a toolCallId.");
                }

                var isToolConfirmation = IsToolConfirmationInteraction(interaction);
                var expectedClarification = _expectClarificationOnly && !isToolConfirmation;
                if (!isToolConfirmation)
                {
                    Interlocked.Increment(ref _clarificationRequestCount);
                }

                var approvedExpectedWrite = _allowExpectedWriteConfirmations
                    && IsExpectedWriteConfirmation(interaction);
                if (approvedExpectedWrite)
                {
                    var approvalResponse = await _client.SendUserInteractionResponseAsync(
                        new AgentBrokerToolUserInteractionResponse
                        {
                            ToolCallId = interaction.ToolCallId,
                            AnswersJson = "{\"approved\":true,\"decision\":\"allow-once\"}",
                            Cancelled = false
                        },
                        cancellationToken).ConfigureAwait(false);
                    if (approvalResponse == null || !string.IsNullOrWhiteSpace(approvalResponse.ErrorCode))
                    {
                        throw new InvalidOperationException(
                            "Could not approve declared write-test tool confirmation: "
                            + (approvalResponse == null
                                ? "no response"
                                : approvalResponse.ErrorCode + " " + approvalResponse.ErrorMessage));
                    }

                    Interlocked.Increment(ref _autoApprovedWriteConfirmations);
                    return;
                }

                if (!expectedClarification)
                {
                    Volatile.Write(ref _unexpectedInteraction, true);
                }

                var response = await _client.SendUserInteractionResponseAsync(
                    new AgentBrokerToolUserInteractionResponse
                    {
                        ToolCallId = interaction.ToolCallId,
                        Cancelled = true,
                        ErrorCode = expectedClarification
                            ? "excel_eval.expected_clarification_observed"
                            : "excel_eval.undeclared_interaction",
                        ErrorMessage = expectedClarification
                            ? "Expected clarification observed; ending this clarification-only task."
                            : "This task does not declare user interactions."
                    },
                    cancellationToken).ConfigureAwait(false);
                var cancellationErrorCode = expectedClarification
                    ? "excel_eval.expected_clarification_observed"
                    : "excel_eval.undeclared_interaction";
                if (response == null
                    || !response.Cancelled
                    || !string.Equals(
                        response.ToolCallId,
                        interaction.ToolCallId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        response.ErrorCode,
                        cancellationErrorCode,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Broker did not acknowledge interaction cancellation: "
                        + (response == null ? "no response" : response.ErrorCode + " " + response.ErrorMessage));
                }

                return;
            }

            if (IsForbiddenTool(brokerEvent))
            {
                var cancellation = await _client.CancelTurnAsync(
                    _cancelRequest,
                    CancellationToken.None).ConfigureAwait(false);
                if (cancellation == null || !cancellation.WasCancelled)
                {
                    throw new InvalidOperationException(
                        "The Broker did not cancel a forbidden tool call for task '" + _taskId + "'.");
                }
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _writer.Dispose();
            }
        }

        public bool TryTakePendingFaultInjection(
            out string faultInjection,
            out TaskCompletionSource<bool> completion)
        {
            lock (_syncRoot)
            {
                completion = _pendingFaultInjection;
                if (completion == null)
                {
                    faultInjection = null;
                    return false;
                }

                _pendingFaultInjection = null;
                faultInjection = _faultInjection;
                return true;
            }
        }

        public void CompleteFaultInjection(
            TaskCompletionSource<bool> completion,
            Exception exception)
        {
            if (completion == null)
            {
                throw new ArgumentNullException(nameof(completion));
            }

            if (exception == null)
            {
                Interlocked.Increment(ref _faultInjectionAppliedCount);
                completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetException(exception);
            }
        }

        private TaskCompletionSource<bool> TryScheduleFaultInjection(AgentBrokerEvent brokerEvent)
        {
            if (_faultInjection == null || _faultInjectionScheduled
                || !brokerEvent.Success
                || !string.Equals(
                    brokerEvent.ToolName,
                    "excel_execute_vba",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string outputSheet;
            if (string.IsNullOrWhiteSpace(brokerEvent.ToolCallId)
                || !_vbaOutputSheets.TryGetValue(brokerEvent.ToolCallId, out outputSheet))
            {
                return null;
            }

            _vbaOutputSheets.Remove(brokerEvent.ToolCallId);
            if (!string.Equals(outputSheet, GetFaultInjectionOutputSheet(_faultInjection), StringComparison.Ordinal))
            {
                return null;
            }

            _faultInjectionScheduled = true;
            _pendingFaultInjection = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return _pendingFaultInjection;
        }

        private void RecordVerificationOutcome(AgentBrokerEvent brokerEvent)
        {
            if (FaultInjectionAppliedCount == 0
                || !string.Equals(brokerEvent.ToolName, "excel_assert", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!brokerEvent.Success
                && (brokerEvent.ErrorCode ?? string.Empty).IndexOf(
                    "assertion_failed",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Interlocked.Increment(ref _verificationFailureCount);
                _verificationFailedAfterFaultInjection = true;
            }
            else if (brokerEvent.Success && _verificationFailedAfterFaultInjection)
            {
                Interlocked.Increment(ref _successfulVerificationRetryCount);
            }
        }

        private string ReadOutputSheetFromArguments(string argumentsJson)
        {
            var arguments = _serializer.Deserialize<Dictionary<string, object>>(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            object outputSheet;
            return arguments != null && arguments.TryGetValue("outputSheet", out outputSheet)
                ? Convert.ToString(outputSheet, CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static string GetFaultInjectionOutputSheet(string faultInjection)
        {
            switch (faultInjection)
            {
                case "corrupt-first-staged-total":
                    return "_wip_01_summary";
                case "omit-one-unmatched-order":
                    return "_wip_01_unmatched";
                default:
                    throw new InvalidOperationException(
                        "Unsupported Excel Eval fault injection: " + (faultInjection ?? string.Empty));
            }
        }

        private bool IsForbiddenTool(AgentBrokerEvent brokerEvent)
        {
            return string.Equals(
                    brokerEvent.EventType,
                    AgentBrokerEventTypes.ToolCallStarted,
                    StringComparison.OrdinalIgnoreCase)
                && _forbiddenTools.Contains(brokerEvent.ToolName ?? string.Empty);
        }

        private static bool IsToolCallStarted(AgentBrokerEvent brokerEvent)
        {
            return string.Equals(
                    brokerEvent.EventType,
                    AgentBrokerEventTypes.ToolCallStarted,
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(brokerEvent.ToolName);
        }

        private static bool IsToolCallCompleted(AgentBrokerEvent brokerEvent)
        {
            return string.Equals(
                brokerEvent.EventType,
                AgentBrokerEventTypes.ToolCallCompleted,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnexpectedInteraction(AgentBrokerEvent brokerEvent)
        {
            if (!string.Equals(
                brokerEvent.EventType,
                AgentBrokerEventTypes.Status,
                StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(brokerEvent.Text)
                || !brokerEvent.Text.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return false;
            }

            var request = ExcelEvalTaskSetLoader.CreateSerializer()
                .Deserialize<AgentBrokerToolUserInteractionRequest>(brokerEvent.Text);
            return request != null
                && !string.IsNullOrWhiteSpace(request.ToolCallId)
                && !string.IsNullOrWhiteSpace(request.ToolName);
        }

        private static HashSet<string> ReadForbiddenTools(IDictionary<string, object> expect)
        {
            var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object traceValue;
            var trace = expect != null && expect.TryGetValue("trace", out traceValue)
                ? traceValue as Dictionary<string, object>
                : null;
            object forbiddenValue;
            if (trace != null && trace.TryGetValue("forbidTools", out forbiddenValue))
            {
                foreach (var tool in (IList)forbiddenValue)
                {
                    tools.Add(Convert.ToString(tool, CultureInfo.InvariantCulture));
                }
            }

            return tools;
        }

        private static HashSet<string> ReadAllowedConfirmationTools(IDictionary<string, object> expect)
        {
            var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object confirmationTools;
            if (expect != null && expect.TryGetValue("confirmationTools", out confirmationTools))
            {
                foreach (var tool in (IList)confirmationTools)
                {
                    tools.Add(Convert.ToString(tool, CultureInfo.InvariantCulture));
                }
            }

            return tools;
        }

        private bool IsExpectedWriteConfirmation(AgentBrokerToolUserInteractionRequest request)
        {
            if (request == null
                || !_allowedConfirmationTools.Contains(request.ToolName ?? string.Empty))
            {
                return false;
            }

            var interactionData = _serializer.Deserialize<Dictionary<string, object>>(
                request.InteractionDataJson ?? "{}");
            object type;
            object toolName;
            return interactionData != null
                && interactionData.TryGetValue("type", out type)
                && interactionData.TryGetValue("toolName", out toolName)
                && string.Equals(
                    Convert.ToString(type, CultureInfo.InvariantCulture),
                    "tool.confirmation",
                    StringComparison.Ordinal)
                && string.Equals(
                    Convert.ToString(toolName, CultureInfo.InvariantCulture),
                    request.ToolName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private bool IsToolConfirmationInteraction(
            AgentBrokerToolUserInteractionRequest request)
        {
            if (request == null)
            {
                return false;
            }

            var interactionData = _serializer.Deserialize<Dictionary<string, object>>(
                request.InteractionDataJson ?? "{}");
            object type;
            return interactionData != null
                && interactionData.TryGetValue("type", out type)
                && string.Equals(
                    Convert.ToString(type, CultureInfo.InvariantCulture),
                    "tool.confirmation",
                    StringComparison.Ordinal);
        }
    }
}
