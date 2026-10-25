using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentBrokerEvent
    {
        public AgentBrokerEvent()
        {
            EventType = string.Empty;
            ConversationId = string.Empty;
            MessageId = string.Empty;
            Text = string.Empty;
            ToolCallId = string.Empty;
            ToolName = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
            BackupPath = string.Empty;
            ExpectedWriteEffect = string.Empty;
            ActualWriteEffect = string.Empty;
            ToolDisplayName = string.Empty;
            ErrorParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string EventType { get; set; }

        public string ConversationId { get; set; }

        public string MessageId { get; set; }

        public int Round { get; set; }

        public string Text { get; set; }

        public string ToolCallId { get; set; }

        public string ToolName { get; set; }

        public string ToolDisplayName { get; set; }

        public bool Success { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public IDictionary<string, string> ErrorParameters { get; set; }

        public bool RequiresBackup { get; set; }

        public string BackupPath { get; set; }

        public string ExpectedWriteEffect { get; set; }

        public string ActualWriteEffect { get; set; }

        public bool ExecutionModeDeclared { get; set; }

        public bool ExecutionModeUserSelected { get; set; }

        public bool EffectExceedsDeclaredMaximum { get; set; }

        public bool TargetOutsideSelectedContract { get; set; }

        public bool ExceededExecutionModePreview { get; set; }

        public AgentBrokerTurnQualitySummary QualitySummary { get; set; }
    }

    public sealed class AgentBrokerTurnQualitySummary
    {
        public AgentBrokerTurnQualitySummary()
        {
            TrustTier = string.Empty;
            SelectedSkillName = string.Empty;
            SkillSelectionReason = string.Empty;
        }

        public bool HasExcelDataToolCalls { get; set; }

        public int OutputItemCount { get; set; }

        public int CoveredOutputItemCount { get; set; }

        public int UncoveredOutputItemCount { get; set; }

        public int VerificationCount { get; set; }

        public int AssertionCount { get; set; }

        public long CoveredRows { get; set; }

        public string TrustTier { get; set; }

        public string SelectedSkillName { get; set; }

        public string SkillSelectionReason { get; set; }

        public double SkillSelectionConfidence { get; set; }

        public bool HasNumberMatchReport { get; set; }

        public int MatchedAnswerNumberCount { get; set; }

        public int UnmatchedAnswerNumberCount { get; set; }

        public bool NumberCorrectionAttempted { get; set; }

        public bool NumberCorrectionSucceeded { get; set; }
    }
}
