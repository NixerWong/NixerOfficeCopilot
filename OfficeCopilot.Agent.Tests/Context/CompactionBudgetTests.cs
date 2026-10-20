using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime.Context;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Context
{
    [TestClass]
    public class CompactionBudgetTests
    {
        private sealed class FixedTokenEstimator : ITokenEstimator
        {
            private readonly int _conversationTokens;

            public FixedTokenEstimator(int conversationTokens)
            {
                _conversationTokens = conversationTokens;
            }

            public int EstimateTokens(string text)
            {
                return _conversationTokens;
            }

            public int EstimateMessageTokens(BrokerConversationMessage message)
            {
                return _conversationTokens;
            }

            public int EstimateConversationTokens(BrokerConversationState conversation)
            {
                return _conversationTokens;
            }
        }

        // ---- ContextWindowThresholds 的对数缩放 ----

        [TestMethod]
        public void LogScale_IsZeroAtFiveKAndOneAtOneMillion()
        {
            Assert.AreEqual(0.0, ContextWindowThresholds.LogScale(5_000), 1e-9);
            Assert.AreEqual(0.0, ContextWindowThresholds.LogScale(1_000), 1e-9);
            Assert.AreEqual(1.0, ContextWindowThresholds.LogScale(1_000_000), 1e-9);
            Assert.AreEqual(1.0, ContextWindowThresholds.LogScale(4_000_000), 1e-9);
        }

        [TestMethod]
        public void LogScale_IsMonotonicallyIncreasing()
        {
            var small = ContextWindowThresholds.LogScale(8_000);
            var medium = ContextWindowThresholds.LogScale(128_000);
            var large = ContextWindowThresholds.LogScale(500_000);

            Assert.IsTrue(small < medium, "8K 应小于 128K");
            Assert.IsTrue(medium < large, "128K 应小于 500K");
        }

        [TestMethod]
        public void GetCompactRatio_IsClampedBetweenMinAndMax()
        {
            Assert.AreEqual(ContextWindowThresholds.CompactRatioMin, ContextWindowThresholds.GetCompactRatio(4_000), 1e-9);

            // 公式为 0.6 + 0.2 * logScale，logScale 上限 1 → 实际最大 0.8，未触及 CompactRatioMax(0.85)。
            Assert.AreEqual(0.8, ContextWindowThresholds.GetCompactRatio(1_000_000), 1e-9);

            var mid = ContextWindowThresholds.GetCompactRatio(200_000);
            Assert.IsTrue(mid > ContextWindowThresholds.CompactRatioMin && mid < ContextWindowThresholds.CompactRatioMax);
        }

        [TestMethod]
        public void GetCompactThresholdRatio_MirrorsGetCompactRatio()
        {
            Assert.AreEqual(
                ContextWindowThresholds.GetCompactRatio(128_000),
                ContextWindowThresholds.GetCompactThresholdRatio(128_000),
                1e-9);
        }

        [TestMethod]
        public void GetEffectiveCap_IsBoundedByCostCeiling()
        {
            Assert.AreEqual(200_000, ContextWindowThresholds.GetEffectiveCap(200_000));
            Assert.AreEqual(
                ContextWindowThresholds.DefaultGlobalCostCeiling,
                ContextWindowThresholds.GetEffectiveCap(1_000_000));
            Assert.AreEqual(50_000, ContextWindowThresholds.GetEffectiveCap(200_000, 50_000));
        }

        [TestMethod]
        public void GetEffectiveCap_FallsBackToDefaultForNonPositiveCeiling()
        {
            Assert.AreEqual(
                ContextWindowThresholds.DefaultGlobalCostCeiling,
                ContextWindowThresholds.GetEffectiveCap(1_000_000, 0));
        }

        [TestMethod]
        public void ReservedForResponseAndSummaryBudget_AreClamped()
        {
            Assert.AreEqual(ContextWindowThresholds.ReservedForResponseFloor, ContextWindowThresholds.GetReservedForResponse(4_096));
            Assert.AreEqual(ContextWindowThresholds.ReservedForResponseCeiling, ContextWindowThresholds.GetReservedForResponse(400_000));
            Assert.AreEqual(ContextWindowThresholds.SummaryBudgetFloor, ContextWindowThresholds.GetSummaryBudget(4_096));
            Assert.AreEqual(ContextWindowThresholds.SummaryBudgetCeiling, ContextWindowThresholds.GetSummaryBudget(400_000));
        }

        [TestMethod]
        public void KeepTurns_AreClampedToConfiguredRange()
        {
            Assert.AreEqual(ContextWindowThresholds.MinKeepTurnsFloor, ContextWindowThresholds.GetMinKeepTurns(5_000));
            Assert.AreEqual(ContextWindowThresholds.MinKeepTurnsCeiling, ContextWindowThresholds.GetMinKeepTurns(1_000_000));
            Assert.AreEqual(ContextWindowThresholds.MaxKeepTurnsFloor, ContextWindowThresholds.GetMaxKeepTurns(1_000));
            Assert.AreEqual(ContextWindowThresholds.MaxKeepTurnsCeiling, ContextWindowThresholds.GetMaxKeepTurns(200_000));
            Assert.AreEqual(ContextWindowThresholds.MaxKeepTurnsCeiling, ContextWindowThresholds.GetMaxKeepTurns(200_000, 0));
        }

        [TestMethod]
        public void GetSingleMessageCap_IsClamped()
        {
            Assert.AreEqual(ContextWindowThresholds.SingleMessageCapFloor, ContextWindowThresholds.GetSingleMessageCap(4_096));
            Assert.AreEqual(ContextWindowThresholds.SingleMessageCapCeiling, ContextWindowThresholds.GetSingleMessageCap(400_000));
        }

        [TestMethod]
        public void DerivedThresholds_AreOrderedWarningBeforeAutoCompactBeforeBlocking()
        {
            var warning = ContextWindowThresholds.GetWarningThreshold(200_000);
            var autoCompact = ContextWindowThresholds.GetAutoCompactThreshold(200_000);
            var blocking = ContextWindowThresholds.GetBlockingLimit(200_000);

            Assert.IsTrue(warning < autoCompact, "警告阈值应早于自动压缩阈值");
            Assert.IsTrue(autoCompact < blocking, "自动压缩阈值应早于阻塞阈值");
            Assert.IsTrue(blocking < 200_000);
        }

        [TestMethod]
        public void GetEffectiveContextWindowSize_ReservesSummaryOutput()
        {
            // 小窗口按 10% 预留；大窗口按 MaxOutputTokensForSummary 封顶。
            Assert.AreEqual(90_000, ContextWindowThresholds.GetEffectiveContextWindowSize(100_000));
            Assert.AreEqual(
                1_000_000 - ContextWindowThresholds.MaxOutputTokensForSummary,
                ContextWindowThresholds.GetEffectiveContextWindowSize(1_000_000));
        }

        // ---- CompactionBudget.Compute ----

        [TestMethod]
        public void Compute_AppliesFloorToTinyWindow()
        {
            var budget = CompactionBudget.Compute(1_000, 100, 800);

            Assert.AreEqual(4096, budget.MaxContextTokens, "窗口下限应被抬到 4096");
            Assert.AreEqual(4096, budget.EffectiveCap);
            Assert.AreEqual(ContextWindowThresholds.CompactRatioMin, budget.CompactRatio, 1e-9);
        }

        [TestMethod]
        public void Compute_UsesDefaultCostCeilingWhenNotSpecified()
        {
            var budget = CompactionBudget.Compute(1_000_000, 2_000, 800);

            Assert.AreEqual(ContextWindowThresholds.DefaultGlobalCostCeiling, budget.CostCeiling);
            Assert.AreEqual(ContextWindowThresholds.DefaultGlobalCostCeiling, budget.EffectiveCap);
            Assert.AreEqual(1_000_000, budget.MaxContextTokens);
        }

        [TestMethod]
        public void Compute_HonoursExplicitCostCeiling()
        {
            var budget = CompactionBudget.Compute(200_000, 2_000, 800, costCeiling: 50_000);

            Assert.AreEqual(50_000, budget.CostCeiling);
            Assert.AreEqual(50_000, budget.EffectiveCap);
        }

        [TestMethod]
        public void Compute_TriggerThresholdIsCapTimesRatio()
        {
            var budget = CompactionBudget.Compute(200_000, 2_000, 800);
            var expected = (int)(budget.EffectiveCap * budget.CompactRatio);

            Assert.AreEqual(expected, budget.TriggerThreshold);
            Assert.IsTrue(budget.TriggerThreshold < budget.EffectiveCap);
        }

        [TestMethod]
        public void Compute_TriggerThresholdIsAtLeastOne()
        {
            Assert.IsTrue(CompactionBudget.Compute(0, 0, 0).TriggerThreshold >= 1);
        }

        [TestMethod]
        public void Compute_ClampsNegativeSystemTokensToZero()
        {
            Assert.AreEqual(0, CompactionBudget.Compute(200_000, -500, 800).SystemTokens);
        }

        [TestMethod]
        public void Compute_RecentBudgetIsRemainderForTypicalWindow()
        {
            var budget = CompactionBudget.Compute(200_000, 3_000, 800);
            var expected = budget.EffectiveCap - budget.SystemTokens - budget.ReservedForResponse - budget.SummaryBudget;

            Assert.AreEqual(expected, budget.RecentBudget);
            Assert.IsTrue(budget.RecentBudget > 0);
        }

        [TestMethod]
        public void Compute_RecentBudgetNeverCollapsesToZero()
        {
            // System prompt 比整个窗口还大的极端场景：Recent 仍需保住绝对下限。
            var budget = CompactionBudget.Compute(4_096, 100_000, 800);

            Assert.IsTrue(budget.RecentBudget >= 256, "实际值: " + budget.RecentBudget);
            Assert.IsTrue(budget.RecentBudget >= budget.MinKeepTurns * 256);
        }

        [TestMethod]
        public void Compute_KeepTurnsAreConsistentWithThresholds()
        {
            var budget = CompactionBudget.Compute(200_000, 2_000, 800);

            Assert.AreEqual(ContextWindowThresholds.GetMinKeepTurns(200_000), budget.MinKeepTurns);
            Assert.AreEqual(ContextWindowThresholds.GetMaxKeepTurns(200_000, 800), budget.MaxKeepTurns);
            Assert.IsTrue(budget.MinKeepTurns <= budget.MaxKeepTurns);
        }

        // ---- AutoCompactPolicy ----

        [TestMethod]
        public void AutoCompact_ReturnsNoActionForNullConversation()
        {
            var policy = new AutoCompactPolicy(new FixedTokenEstimator(999_999));

            Assert.AreEqual(ContextWindowAction.None, policy.Evaluate(null).RecommendedAction);
        }

        [TestMethod]
        public void AutoCompact_TriggersAtOrAboveThreshold()
        {
            var trigger = TriggerThresholdFor(200_000);

            Assert.AreEqual(
                ContextWindowAction.AutoCompact,
                EvaluateWith(trigger).RecommendedAction);
            Assert.AreEqual(
                ContextWindowAction.None,
                EvaluateWith(trigger - 1).RecommendedAction);
        }

        [TestMethod]
        public void AutoCompact_ReportsEstimatedTokensBelowThreshold()
        {
            var evaluation = EvaluateWith(1_000);

            Assert.AreEqual(ContextWindowAction.None, evaluation.RecommendedAction);
            Assert.AreEqual(1_000, evaluation.EstimatedPromptTokens);
            Assert.AreEqual(string.Empty, evaluation.Reason);
        }

        [TestMethod]
        public void AutoCompact_EmitsWarningReasonNearThreshold()
        {
            var warning = Math.Max(1, (int)(TriggerThresholdFor(200_000) * 0.70));
            var evaluation = EvaluateWith(warning);

            Assert.AreEqual(ContextWindowAction.None, evaluation.RecommendedAction);
            StringAssert.Contains(evaluation.Reason, "warning threshold");
        }

        [TestMethod]
        public void AutoCompact_CircuitBreakerOpensAfterThreeFailuresAndCanReset()
        {
            var policy = new AutoCompactPolicy(new FixedTokenEstimator(999_999));
            var conversation = new BrokerConversationState();

            Assert.AreEqual(ContextWindowAction.AutoCompact, policy.Evaluate(conversation).RecommendedAction);

            for (var i = 0; i < ContextWindowThresholds.MaxConsecutiveAutoCompactFailures; i++)
            {
                policy.RecordCompactResult(false);
            }

            Assert.IsTrue(policy.IsCircuitBreakerOpen);
            Assert.AreEqual(ContextWindowThresholds.MaxConsecutiveAutoCompactFailures, policy.ConsecutiveFailures);
            var blocked = policy.Evaluate(conversation);
            Assert.AreEqual(ContextWindowAction.None, blocked.RecommendedAction);
            StringAssert.Contains(blocked.Reason, "circuit breaker");

            policy.ResetCircuitBreaker();

            Assert.IsFalse(policy.IsCircuitBreakerOpen);
            Assert.AreEqual(ContextWindowAction.AutoCompact, policy.Evaluate(conversation).RecommendedAction);
        }

        [TestMethod]
        public void AutoCompact_SuccessResetsFailureCounter()
        {
            var policy = new AutoCompactPolicy(new FixedTokenEstimator(10));
            policy.RecordCompactResult(false);
            policy.RecordCompactResult(false);
            policy.RecordCompactResult(true);

            Assert.AreEqual(0, policy.ConsecutiveFailures);
            Assert.IsFalse(policy.IsCircuitBreakerOpen);
        }

        [TestMethod]
        public void AutoCompact_UpdateContextWindowSizeChangesThreshold()
        {
            var tokens = TriggerThresholdFor(200_000) + 1;
            var policy = new AutoCompactPolicy(new FixedTokenEstimator(tokens), 200_000);
            var conversation = new BrokerConversationState();

            Assert.AreEqual(ContextWindowAction.AutoCompact, policy.Evaluate(conversation).RecommendedAction);

            policy.UpdateContextWindowSize(1_000_000);

            Assert.AreEqual(ContextWindowAction.None, policy.Evaluate(conversation).RecommendedAction);
        }

        [TestMethod]
        public void ApplyPrune_ReturnsSameInstanceForShortConversation()
        {
            var policy = new AutoCompactPolicy(new FixedTokenEstimator(10));
            var conversation = new BrokerConversationState();
            conversation.Messages.Add(new BrokerConversationMessage { Role = "user", Content = "hi" });

            Assert.AreSame(conversation, policy.ApplyPrune(conversation));
            Assert.IsNull(policy.ApplyPrune(null));
        }

        [TestMethod]
        public void ApplyPrune_KeepsSystemMessageAndTrimsHistory()
        {
            var policy = new AutoCompactPolicy(new FixedTokenEstimator(999_999));
            var conversation = new BrokerConversationState();
            conversation.Messages.Add(new BrokerConversationMessage { Role = "system", Content = "system prompt" });
            for (var i = 0; i < 20; i++)
            {
                conversation.Messages.Add(new BrokerConversationMessage
                {
                    Role = i % 2 == 0 ? "user" : "assistant",
                    Content = "message " + i
                });
            }

            var pruned = policy.ApplyPrune(conversation, maxMessagesAfterCompact: 6);

            // system prompt + compact_boundary + 最近 6 条
            Assert.AreEqual(8, pruned.Messages.Count);
            Assert.AreEqual("system prompt", pruned.Messages[0].Content);
            Assert.AreEqual("compact_boundary", pruned.Messages[1].MessageType);
            Assert.AreEqual("message 19", pruned.Messages[pruned.Messages.Count - 1].Content);
        }

        private static int TriggerThresholdFor(int contextWindowSize)
        {
            var cap = ContextWindowThresholds.GetEffectiveCap(contextWindowSize);
            return Math.Max(1, (int)(cap * ContextWindowThresholds.GetCompactRatio(contextWindowSize)));
        }

        private static ContextWindowEvaluation EvaluateWith(int estimatedTokens)
        {
            return new AutoCompactPolicy(new FixedTokenEstimator(estimatedTokens), 200_000)
                .Evaluate(new BrokerConversationState());
        }
    }
}
