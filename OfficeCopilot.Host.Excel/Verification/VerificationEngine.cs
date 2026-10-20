using System;
using System.Collections.Generic;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Verification.Backends;

namespace OfficeCopilot.Host.Excel.Verification
{
    internal sealed class VerificationEngine
    {
        private readonly ExcelAggregationService _sourceService;
        private readonly VerificationPlanner _planner;
        private readonly Comparator _comparator;

        public VerificationEngine(
            ExcelAggregationService sourceService,
            IExcelCalcBackend comBackend,
            IExcelCalcBackend managedBackend)
        {
            _sourceService = sourceService ?? throw new ArgumentNullException(nameof(sourceService));
            _planner = new VerificationPlanner(comBackend, managedBackend);
            _comparator = new Comparator();
        }

        public ExcelVerificationReport Verify(
            ExcelVerificationPlan verificationPlan,
            bool targetProducedByManaged = false)
        {
            if (verificationPlan == null)
            {
                throw new ArgumentNullException(nameof(verificationPlan));
            }

            if (verificationPlan.Basis == null)
            {
                throw new InvalidOperationException("验证计划缺少 basis。");
            }

            if (verificationPlan.Assertions == null || verificationPlan.Assertions.Count == 0)
            {
                throw new InvalidOperationException("验证计划至少需要一条断言。");
            }

            var assertionPlans = new List<VerificationAssertionPlan>();
            foreach (var assertion in verificationPlan.Assertions)
            {
                if (assertion == null)
                {
                    throw new InvalidOperationException("验证计划不能包含 null 断言。");
                }

                var kind = (assertion.Kind ?? string.Empty).Trim().ToLowerInvariant();
                VerificationPlanner.ValidateAssertionShape(
                    assertion,
                    kind,
                    verificationPlan.Basis);

                var targetQuery = VerificationPlanner.BuildQuery(
                    verificationPlan.Basis,
                    assertion,
                    assertion.Target,
                    kind,
                    true);
                var targetCost = _sourceService.EstimateCalcCost(targetQuery);

                CalcQuery sourceQuery = null;
                CalcCostEstimate sourceCost = null;
                if (assertion.Source != null)
                {
                    sourceQuery = VerificationPlanner.BuildQuery(
                        verificationPlan.Basis,
                        assertion,
                        assertion.Source,
                        kind,
                        false);
                    sourceCost = _sourceService.EstimateCalcCost(sourceQuery);
                }

                var assertionPlan = _planner.PlanAssertion(
                    verificationPlan,
                    assertion,
                    targetCost,
                    sourceCost,
                    targetProducedByManaged,
                    false);
                assertionPlans.Add(assertionPlan);
            }

            var executions = new List<VerificationAssertionExecution>(assertionPlans.Count);
            foreach (var assertionPlan in assertionPlans)
            {
                executions.Add(ExecuteAssertion(assertionPlan));
            }

            return _comparator.Compare(executions);
        }

        private VerificationAssertionExecution ExecuteAssertion(
            VerificationAssertionPlan assertionPlan)
        {
            var execution = new VerificationAssertionExecution
            {
                Plan = assertionPlan
            };

            foreach (var side in assertionPlan.Sides)
            {
                try
                {
                    var result = side.Backend.Evaluate(side.Query);
                    if (string.Equals(side.Side, "target", StringComparison.OrdinalIgnoreCase))
                    {
                        execution.TargetResult = result;
                    }
                    else if (string.Equals(side.Side, "source", StringComparison.OrdinalIgnoreCase))
                    {
                        execution.SourceResult = result;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "验证计划包含未知查询侧：" + (side.Side ?? string.Empty));
                    }
                }
                catch (Exception exception)
                {
                    if (ExcelComErrorClassifier.IsBusyException(exception))
                    {
                        throw;
                    }

                    if (string.Equals(side.Side, "target", StringComparison.OrdinalIgnoreCase))
                    {
                        execution.TargetException = exception;
                    }
                    else if (string.Equals(side.Side, "source", StringComparison.OrdinalIgnoreCase))
                    {
                        execution.SourceException = exception;
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return execution;
        }
    }
}
