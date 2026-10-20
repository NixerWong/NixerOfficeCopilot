using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Planning
{
    public sealed class SkillSelectionResult
    {
        public SkillSelectionResult()
        {
            SkillName = string.Empty;
            Reason = string.Empty;
            CandidateSkillNames = new List<string>();
            CandidateSkillSummaries = new List<string>();
            AllowedToolNames = new List<string>();
            PreferredToolNames = new List<string>();
            VerificationToolNames = new List<string>();
            CompanionSkillNames = new List<string>();
            ExecutionSteps = new List<string>();
            ExecutionPlanSteps = new List<SkillExecutionStep>();
            FallbackSkillNames = new List<string>();
            MaxToolRoundCount = 0;
        }

        public string SkillName { get; set; }

        public string Reason { get; set; }

        public string DisabledRequestedSkillName { get; set; }

        public double Confidence { get; set; }

        public IList<string> CandidateSkillNames { get; set; }

        public IList<string> CandidateSkillSummaries { get; set; }

        public IList<string> AllowedToolNames { get; set; }

        public IList<string> PreferredToolNames { get; set; }

        public IList<string> VerificationToolNames { get; set; }

        public IList<string> CompanionSkillNames { get; set; }

        public IList<string> ExecutionSteps { get; set; }

        public IList<SkillExecutionStep> ExecutionPlanSteps { get; set; }

        public IList<string> FallbackSkillNames { get; set; }

        public int MaxToolRoundCount { get; set; }

        public bool IsFallback { get; set; }

        public bool RequireVerification { get; set; }

        public bool RequiresConfirmation { get; set; }

        public bool RequiresBackup { get; set; }

        public bool AllowHighRiskTools { get; set; }

        public bool AllowCodeExecution { get; set; }

        public bool HasSelection
        {
            get { return !string.IsNullOrWhiteSpace(SkillName); }
        }
    }
}
