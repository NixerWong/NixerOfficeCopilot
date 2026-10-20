using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Com;

namespace OfficeCopilot.Host.Excel.Verification
{
    public sealed class VerificationAssertionExecution
    {
        public VerificationAssertionPlan Plan { get; set; }

        public CalcResult TargetResult { get; set; }

        public CalcResult SourceResult { get; set; }

        public Exception TargetException { get; set; }

        public Exception SourceException { get; set; }
    }

    public sealed class Comparator
    {
        private const int MaximumDiffs = 20;
        private const double DefaultTolerance = 1e-6d;

        public ExcelVerificationReport Compare(
            IList<VerificationAssertionExecution> executions)
        {
            if (executions == null)
            {
                throw new ArgumentNullException(nameof(executions));
            }

            var report = new ExcelVerificationReport();
            if (executions.Count == 0)
            {
                report.Notes.Add("没有可比对的断言。");
                return report;
            }

            foreach (var execution in executions)
            {
                if (execution == null || execution.Plan == null)
                {
                    throw new ArgumentException(
                        "比对输入及其 VerificationAssertionPlan 不能为空。",
                        nameof(executions));
                }

                var outcome = CompareOne(execution, report);
                report.Items.Add(outcome);
                report.DiffCount += outcome.DiffCount;
            }

            if (report.DiffCount > report.Diffs.Count)
            {
                report.Notes.Add(
                    "差异明细仅展示前 " + MaximumDiffs + " 条，共 "
                    + report.DiffCount + " 项差异。");
            }

            report.Passed = report.Items.All(item => item.Passed);
            report.Tier = GetWorstTier(report.Items);
            return report;
        }

        private static ExcelAssertionOutcome CompareOne(
            VerificationAssertionExecution execution,
            ExcelVerificationReport report)
        {
            var plan = execution.Plan;
            var targetPlan = FindPlannedSide(plan, "target");
            var sourcePlan = FindPlannedSide(plan, "source", false);
            var kind = Normalize(plan.Kind);
            bool sourceRequired;
            if (kind == "groupmatch" || kind == "keyset" || kind == "keysetdifference")
            {
                sourceRequired = true;
            }
            else if (kind == "scalar" || kind == "rowcount" || kind == "nonblank")
            {
                sourceRequired = !plan.ExpectedValue.HasValue;
            }
            else
            {
                sourceRequired = false;
            }

            if (sourceRequired != (sourcePlan != null))
            {
                throw new InvalidOperationException(
                    "比对计划的 source 查询与断言类型/expectedValue 不一致。");
            }

            if (sourcePlan == null
                && (execution.SourceResult != null || execution.SourceException != null))
            {
                throw new InvalidOperationException(
                    "比对输入包含 source 结果，但断言没有 source 查询。");
            }

            var outcome = new ExcelAssertionOutcome
            {
                Id = plan.AssertionId ?? string.Empty,
                Kind = plan.Kind ?? string.Empty,
                Independent = targetPlan.Independent
                    && (sourcePlan == null || sourcePlan.Independent),
                TargetBackend = GetBackendName(targetPlan),
                SourceBackend = sourcePlan == null ? null : GetBackendName(sourcePlan),
                TargetCoverage = execution.TargetResult == null
                    ? null
                    : execution.TargetResult.Coverage,
                SourceCoverage = execution.SourceResult == null
                    ? null
                    : execution.SourceResult.Coverage,
                Tier = targetPlan.Independent
                    && (sourcePlan == null || sourcePlan.Independent)
                    ? "A"
                    : "B"
            };

            AddResultNotes(report, outcome.Id, "target", execution.TargetResult);
            AddResultNotes(report, outcome.Id, "source", execution.SourceResult);
            AddFallbackReason(report, outcome.Id, targetPlan);
            AddFallbackReason(report, outcome.Id, sourcePlan);

            var hasExecutionFailure = false;
            if (execution.TargetException != null)
            {
                MarkUnverifiable(
                    report,
                    outcome,
                    FormatFailure("target", execution.TargetException));
                hasExecutionFailure = true;
            }

            if (sourcePlan != null && execution.SourceException != null)
            {
                MarkUnverifiable(
                    report,
                    outcome,
                    FormatFailure("source", execution.SourceException));
                hasExecutionFailure = true;
            }

            if (hasExecutionFailure)
            {
                return outcome;
            }

            string targetReason;
            var targetComplete = IsCompleteResult(
                execution.TargetResult,
                "target",
                out targetReason);
            var sourceComplete = true;
            var sourceReason = string.Empty;
            if (sourcePlan != null)
            {
                sourceComplete = IsCompleteResult(
                    execution.SourceResult,
                    "source",
                    out sourceReason);
            }

            if (!targetComplete)
            {
                MarkUnverifiable(report, outcome, targetReason);
            }

            if (!sourceComplete)
            {
                MarkUnverifiable(report, outcome, sourceReason);
            }

            if (!targetComplete || !sourceComplete)
            {
                return outcome;
            }

            if (plan.ExpectedValue.HasValue && !IsFinite(plan.ExpectedValue.Value))
            {
                MarkUnverifiable(report, outcome, "expectedValue 不是有限数值。");
                return outcome;
            }

            double? tolerance = null;
            if (kind == "scalar"
                || kind == "rowcount"
                || kind == "nonblank"
                || kind == "groupmatch")
            {
                tolerance = ResolveTolerance(plan.Tolerance);
                outcome.Tolerance = tolerance;
            }

            switch (kind)
            {
                case "scalar":
                case "rowcount":
                case "nonblank":
                    CompareScalar(plan, execution, outcome, report, tolerance.Value);
                    break;
                case "groupmatch":
                    CompareGroups(
                        execution.TargetResult,
                        execution.SourceResult,
                        outcome,
                        report,
                        tolerance.Value);
                    break;
                case "keyset":
                    CompareKeySets(
                        execution.TargetResult,
                        execution.SourceResult,
                        outcome,
                        report);
                    break;
                case "keysetdifference":
                    CompareKeySetDifferences(
                        execution.TargetResult,
                        execution.SourceResult,
                        plan.SampleCount,
                        outcome,
                        report);
                    break;
                case "sorted":
                    CompareSorted(
                        execution.TargetResult,
                        outcome,
                        report);
                    break;
                default:
                    MarkUnverifiable(
                        report,
                        outcome,
                        "不支持的断言类型：" + (plan.Kind ?? string.Empty));
                    break;
            }

            return outcome;
        }

        private static void CompareScalar(
            VerificationAssertionPlan plan,
            VerificationAssertionExecution execution,
            ExcelAssertionOutcome outcome,
            ExcelVerificationReport report,
            double tolerance)
        {
            if (!execution.TargetResult.Scalar.HasValue)
            {
                MarkUnverifiable(report, outcome, "target 没有返回数值结果。");
                return;
            }

            double expected;
            if (plan.ExpectedValue.HasValue)
            {
                expected = plan.ExpectedValue.Value;
            }
            else if (execution.SourceResult != null
                && execution.SourceResult.Scalar.HasValue)
            {
                expected = execution.SourceResult.Scalar.Value;
            }
            else
            {
                MarkUnverifiable(report, outcome, "source 没有返回数值结果。");
                return;
            }

            var actual = execution.TargetResult.Scalar.Value;
            if (!IsFinite(expected) || !IsFinite(actual))
            {
                MarkUnverifiable(report, outcome, "比较值不是有限数值。");
                return;
            }

            outcome.ExpectedValue = expected;
            outcome.ActualValue = actual;
            var difference = Math.Abs(actual - expected);
            if (IsFinite(difference))
            {
                outcome.Difference = difference;
            }
            else
            {
                report.Notes.Add(
                    Prefix(outcome.Id) + "差值超出 double 可表示范围，按不匹配处理。");
            }

            outcome.Passed = difference <= tolerance;
            if (!outcome.Passed)
            {
                AddDiff(
                    report,
                    outcome,
                    Prefix(outcome.Id) + "target=" + FormatNumber(actual)
                    + ", expected=" + FormatNumber(expected)
                    + ", difference=" + FormatNumber(difference)
                    + ", tolerance=" + FormatNumber(tolerance) + ".");
            }
        }

        private static void CompareGroups(
            CalcResult target,
            CalcResult source,
            ExcelAssertionOutcome outcome,
            ExcelVerificationReport report,
            double tolerance)
        {
            if (target.Groups == null || source.Groups == null)
            {
                MarkUnverifiable(report, outcome, "groupMatch 结果缺少 groups。");
                return;
            }

            var comparedCount = target.Groups.Count;
            foreach (var key in source.Groups.Keys)
            {
                if (!target.Groups.ContainsKey(key))
                {
                    comparedCount++;
                }
            }

            outcome.ExpectedCount = source.Groups.Count;
            outcome.ActualCount = target.Groups.Count;
            outcome.ComparedCount = comparedCount;

            foreach (var targetGroup in target.Groups)
            {
                double expected;
                if (!source.Groups.TryGetValue(targetGroup.Key, out expected))
                {
                    AddDiff(
                        report,
                        outcome,
                        Prefix(outcome.Id) + "group '" + targetGroup.Key
                        + "' 多出于 target。");
                    continue;
                }

                if (!IsFinite(targetGroup.Value) || !IsFinite(expected))
                {
                    MarkUnverifiable(
                        report,
                        outcome,
                        "group '" + targetGroup.Key + "' 包含非有限数值。");
                    return;
                }

                var difference = Math.Abs(targetGroup.Value - expected);
                if (difference > tolerance)
                {
                    AddDiff(
                        report,
                        outcome,
                        Prefix(outcome.Id) + "group '" + targetGroup.Key + "': target="
                        + FormatNumber(targetGroup.Value) + ", source=" + FormatNumber(expected)
                        + ", difference=" + FormatNumber(difference) + ".");
                }
            }

            foreach (var sourceGroup in source.Groups)
            {
                if (!target.Groups.ContainsKey(sourceGroup.Key))
                {
                    AddDiff(
                        report,
                        outcome,
                        Prefix(outcome.Id) + "group '" + sourceGroup.Key
                        + "' 缺失于 target。");
                }
            }

            outcome.Passed = outcome.DiffCount == 0;
        }

        private static void CompareKeySets(
            CalcResult target,
            CalcResult source,
            ExcelAssertionOutcome outcome,
            ExcelVerificationReport report)
        {
            HashSet<string> targetKeys;
            HashSet<string> sourceKeys;
            string reason;
            if (!TryCreateKeySet(target.Keys, "target", out targetKeys, out reason)
                || !TryCreateKeySet(source.Keys, "source", out sourceKeys, out reason))
            {
                MarkUnverifiable(report, outcome, reason);
                return;
            }

            var comparedCount = targetKeys.Count;
            foreach (var key in sourceKeys)
            {
                if (!targetKeys.Contains(key))
                {
                    comparedCount++;
                }
            }

            outcome.ExpectedCount = sourceKeys.Count;
            outcome.ActualCount = targetKeys.Count;
            outcome.ComparedCount = comparedCount;

            foreach (var key in target.Keys)
            {
                var inTarget = targetKeys.Contains(key);
                var inSource = sourceKeys.Contains(key);
                if (inTarget && !inSource)
                {
                    AddDiff(
                        report,
                        outcome,
                        Prefix(outcome.Id) + "key '" + key + "' 多出于 target。");
                }
            }

            foreach (var key in source.Keys)
            {
                if (!targetKeys.Contains(key))
                {
                    AddDiff(
                        report,
                        outcome,
                        Prefix(outcome.Id) + "key '" + key + "' 缺失于 target。");
                }
            }

            outcome.Passed = outcome.DiffCount == 0;
        }

        private static void CompareKeySetDifferences(
            CalcResult target,
            CalcResult source,
            int sampleCount,
            ExcelAssertionOutcome outcome,
            ExcelVerificationReport report)
        {
            HashSet<string> targetKeys;
            HashSet<string> sourceKeys;
            string reason;
            if (!TryCreateKeySet(target.Keys, "target", out targetKeys, out reason)
                || !TryCreateKeySet(source.Keys, "source", out sourceKeys, out reason))
            {
                MarkUnverifiable(report, outcome, reason);
                return;
            }

            var targetOnly = targetKeys
                .Where(key => !sourceKeys.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
            var sourceOnly = sourceKeys
                .Where(key => !targetKeys.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            outcome.ExpectedCount = sourceKeys.Count;
            outcome.ActualCount = targetKeys.Count;
            outcome.ComparedCount = targetKeys.Count + sourceOnly.Count;
            outcome.TargetOnlyCount = targetOnly.Count;
            outcome.SourceOnlyCount = sourceOnly.Count;
            outcome.TargetOnlySamples = SelectBoundedSamples(targetOnly, sampleCount);
            outcome.SourceOnlySamples = SelectBoundedSamples(sourceOnly, sampleCount);
            outcome.Passed = true;
        }

        private static List<string> SelectBoundedSamples(
            IList<string> sortedKeys,
            int sampleCount)
        {
            var samples = new List<string>();
            if (sortedKeys == null || sortedKeys.Count == 0 || sampleCount <= 0)
            {
                return samples;
            }

            var count = Math.Min(sortedKeys.Count, sampleCount);
            if (count == 1)
            {
                samples.Add(sortedKeys[(sortedKeys.Count - 1) / 2]);
                return samples;
            }

            for (var sampleIndex = 0; sampleIndex < count; sampleIndex++)
            {
                var keyIndex = (int)Math.Floor(
                    (double)sampleIndex * (sortedKeys.Count - 1) / (count - 1));
                samples.Add(sortedKeys[keyIndex]);
            }

            return samples;
        }

        private static void CompareSorted(
            CalcResult target,
            ExcelAssertionOutcome outcome,
            ExcelVerificationReport report)
        {
            if (!target.IsSorted.HasValue)
            {
                MarkUnverifiable(report, outcome, "sorted 查询无法确定排序结果。");
                return;
            }

            outcome.IsSorted = target.IsSorted;
            outcome.Passed = target.IsSorted.Value;
            if (!outcome.Passed)
            {
                report.Notes.Add(Prefix(outcome.Id) + "target 列未按指定方向排序。");
            }
        }

        private static bool IsCompleteResult(
            CalcResult result,
            string side,
            out string reason)
        {
            reason = string.Empty;
            if (result == null)
            {
                reason = side + " 没有返回计算结果。";
                return false;
            }

            var coverage = result.Coverage;
            if (coverage == null)
            {
                reason = side + " 缺少覆盖报告。";
                return false;
            }

            if (coverage.TotalRows < 0
                || coverage.ScannedRows < 0
                || coverage.ScannedRows > coverage.TotalRows)
            {
                reason = side + " 覆盖报告中的行数无效。";
                return false;
            }

            if (coverage.ScannedRows < coverage.TotalRows)
            {
                reason = side + " 覆盖不完整：" + coverage.ScannedRows
                    + " / " + coverage.TotalRows + " 行。";
                return false;
            }

            return true;
        }

        private static bool TryCreateKeySet(
            IList<string> keys,
            string side,
            out HashSet<string> keySet,
            out string reason)
        {
            keySet = new HashSet<string>(StringComparer.Ordinal);
            reason = string.Empty;
            if (keys == null)
            {
                reason = side + " keySet 结果缺少 keys。";
                return false;
            }

            foreach (var key in keys)
            {
                if (key == null || !keySet.Add(key))
                {
                    reason = side + " keySet 结果包含 null 或重复 key。";
                    return false;
                }
            }

            return true;
        }

        private static PlannedCalcSide FindPlannedSide(
            VerificationAssertionPlan plan,
            string side,
            bool required = true)
        {
            if (plan.Sides == null)
            {
                throw new InvalidOperationException("验证计划缺少查询侧列表。");
            }

            PlannedCalcSide match = null;
            foreach (var plannedSide in plan.Sides)
            {
                if (plannedSide != null
                    && string.Equals(
                        plannedSide.Side,
                        side,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (match != null)
                    {
                        throw new InvalidOperationException(
                            "验证计划包含重复的 " + side + " 查询侧。");
                    }

                    match = plannedSide;
                }
            }

            if (match == null && required)
            {
                throw new InvalidOperationException(
                    "验证计划缺少 " + side + " 查询侧。");
            }

            if (match != null
                && match.Backend == null
                && string.IsNullOrWhiteSpace(match.BackendName))
            {
                throw new InvalidOperationException(
                    "验证计划的 " + side + " 查询侧缺少后端。");
            }

            return match;
        }

        private static string GetBackendName(PlannedCalcSide side)
        {
            return side.Backend == null
                ? (side.BackendName ?? string.Empty)
                : (side.Backend.Name ?? string.Empty);
        }

        private static double ResolveTolerance(double? requestedTolerance)
        {
            if (!requestedTolerance.HasValue)
            {
                return DefaultTolerance;
            }

            if (!IsFinite(requestedTolerance.Value))
            {
                throw new InvalidOperationException("tolerance 必须是有限数值。");
            }

            return requestedTolerance.Value <= 0d
                ? DefaultTolerance
                : requestedTolerance.Value;
        }

        private static void AddResultNotes(
            ExcelVerificationReport report,
            string assertionId,
            string side,
            CalcResult result)
        {
            if (result == null || result.Notes == null)
            {
                return;
            }

            foreach (var note in result.Notes)
            {
                if (!string.IsNullOrWhiteSpace(note))
                {
                    report.Notes.Add(Prefix(assertionId) + side + ": " + note);
                }
            }
        }

        private static void AddFallbackReason(
            ExcelVerificationReport report,
            string assertionId,
            PlannedCalcSide side)
        {
            if (side != null
                && !side.Independent
                && !string.IsNullOrWhiteSpace(side.Reason))
            {
                report.Notes.Add(
                    Prefix(assertionId) + side.Side + " 路由：" + side.Reason);
            }
        }

        private static void MarkUnverifiable(
            ExcelVerificationReport report,
            ExcelAssertionOutcome outcome,
            string reason)
        {
            outcome.Passed = false;
            outcome.Tier = "C";
            report.Notes.Add(Prefix(outcome.Id) + reason);
        }

        private static void AddDiff(
            ExcelVerificationReport report,
            ExcelAssertionOutcome outcome,
            string difference)
        {
            outcome.DiffCount++;
            if (report.Diffs.Count < MaximumDiffs)
            {
                report.Diffs.Add(difference);
            }
        }

        private static string FormatFailure(string side, Exception exception)
        {
            var detail = string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message;

            if (ExcelComErrorClassifier.IsBusyException(exception))
            {
                return side + " 遇到 Excel COM 忙碌，验证未完成：" + detail;
            }

            if (ContainsTimeout(exception))
            {
                return side + " 后端执行超时，验证未完成：" + detail;
            }

            var incompleteCoverage = exception as ExcelCoverageIncompleteException;
            if (incompleteCoverage != null)
            {
                return side + " 覆盖不完整，验证未完成：" + detail;
            }

            return side + " 后端执行失败，验证未完成：" + detail;
        }

        private static bool ContainsTimeout(Exception exception)
        {
            var pending = new Stack<Exception>();
            pending.Push(exception);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (current is TimeoutException)
                {
                    return true;
                }

                var aggregate = current as AggregateException;
                if (aggregate != null)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        if (inner != null)
                        {
                            pending.Push(inner);
                        }
                    }
                }

                if (current.InnerException != null)
                {
                    pending.Push(current.InnerException);
                }
            }

            return false;
        }

        private static string GetWorstTier(IEnumerable<ExcelAssertionOutcome> outcomes)
        {
            if (outcomes.Any(outcome => outcome.Tier == "C"))
            {
                return "C";
            }

            return outcomes.Any(outcome => outcome.Tier == "B") ? "B" : "A";
        }

        private static string Prefix(string assertionId)
        {
            return string.IsNullOrWhiteSpace(assertionId)
                ? string.Empty
                : "[" + assertionId + "] ";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("G", CultureInfo.InvariantCulture);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
