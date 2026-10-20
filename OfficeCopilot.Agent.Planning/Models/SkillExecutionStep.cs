using System.Collections.Generic;

namespace OfficeCopilot.Agent.Planning
{
    public sealed class SkillExecutionStep
    {
        public SkillExecutionStep()
        {
            StepKind = string.Empty;
            StepId = string.Empty;
            VerifiesStepId = string.Empty;
            Title = string.Empty;
            SkillName = string.Empty;
            Instruction = string.Empty;
            AllowedToolNames = new List<string>();
            PreferredToolNames = new List<string>();
            VerificationToolNames = new List<string>();
        }

        public string StepKind { get; set; }

        public string StepId { get; set; }

        public string VerifiesStepId { get; set; }

        public string Title { get; set; }

        public string SkillName { get; set; }

        public string Instruction { get; set; }

        public IList<string> AllowedToolNames { get; set; }

        public IList<string> PreferredToolNames { get; set; }

        public IList<string> VerificationToolNames { get; set; }

        public bool RequireVerification { get; set; }

        public bool RequiresConfirmation { get; set; }

        public bool RequiresBackup { get; set; }
    }
}
