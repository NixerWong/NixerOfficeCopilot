using OfficeCopilot.Agent.Planning;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Agent.Runtime.Planning
{
    internal sealed class RuntimeSkillPlan
    {
        private readonly HashSet<int> _completedExecutionStepIndexes;
        private readonly HashSet<int> _startedMutationStepIndexes;
        private int _dynamicMutationStageCount;

        public RuntimeSkillPlan()
        {
            SkillName = string.Empty;
            Reason = string.Empty;
            Prompt = string.Empty;
            AllowedToolNames = new List<string>();
            PreferredToolNames = new List<string>();
            VerificationToolNames = new List<string>();
            CompanionSkillNames = new List<string>();
            ExecutionSteps = new List<string>();
            ExecutionPlanSteps = new List<SkillExecutionStep>();
            FallbackSkillNames = new List<string>();
            MaxToolRoundCount = 0;
            MaxVerificationRetryCount = 2;
            VerificationRetryCount = 0;
            _completedExecutionStepIndexes = new HashSet<int>();
            _startedMutationStepIndexes = new HashSet<int>();
            _dynamicMutationStageCount = 0;
        }

        public string SkillName { get; set; }

        public string Reason { get; set; }

        public string Prompt { get; set; }

        public SkillSelectionResult Selection { get; set; }

        public IList<string> AllowedToolNames { get; set; }

        public IList<string> PreferredToolNames { get; set; }

        public IList<string> VerificationToolNames { get; set; }

        public IList<string> CompanionSkillNames { get; set; }

        public IList<string> ExecutionSteps { get; set; }

        public IList<SkillExecutionStep> ExecutionPlanSteps { get; set; }

        public int VerificationRetryCount { get; private set; }

        public int MaxVerificationRetryCount { get; set; }

        public IList<string> FallbackSkillNames { get; set; }

        public int MaxToolRoundCount { get; set; }

        public bool RequireVerification { get; set; }

        public bool RequiresConfirmation { get; set; }

        public bool RequiresBackup { get; set; }

        public bool AllowHighRiskTools { get; set; }

        public bool AllowCodeExecution { get; set; }

        public bool HasSkill
        {
            get { return !string.IsNullOrWhiteSpace(SkillName); }
        }

        public bool HasStructuredExecutionPlan
        {
            get { return ExecutionPlanSteps != null && ExecutionPlanSteps.Count > 0; }
        }

        public int CompletedExecutionStepCount
        {
            get { return _completedExecutionStepIndexes.Count; }
        }

        public void InitializeExecutionPlanProgress()
        {
            _completedExecutionStepIndexes.Clear();
            _startedMutationStepIndexes.Clear();
            VerificationRetryCount = 0;
        }

        public IEnumerable<SkillExecutionStep> GetExecutionPlanSteps()
        {
            if (!HasStructuredExecutionPlan)
            {
                return Enumerable.Empty<SkillExecutionStep>();
            }

            return ExecutionPlanSteps.Where(step => step != null);
        }

        public bool HasPendingMutationStep()
        {
            if (!HasStructuredExecutionPlan)
            {
                return false;
            }

            for (var i = 0; i < ExecutionPlanSteps.Count; i++)
            {
                var step = ExecutionPlanSteps[i];
                if (IsMutationStep(step) && !_completedExecutionStepIndexes.Contains(i))
                {
                    return true;
                }
            }

            return false;
        }

        public SkillExecutionStep GetPendingMutationStep()
        {
            if (!HasStructuredExecutionPlan)
            {
                return null;
            }

            for (var i = 0; i < ExecutionPlanSteps.Count; i++)
            {
                var step = ExecutionPlanSteps[i];
                if (IsMutationStep(step) && !_completedExecutionStepIndexes.Contains(i))
                {
                    return step;
                }
            }

            return null;
        }

        public bool HasStartedMutationAwaitingVerification()
        {
            var currentIndex = FindFirstIncompleteExecutionStepIndex();
            return currentIndex >= 0
                && IsMutationStep(ExecutionPlanSteps[currentIndex])
                && _startedMutationStepIndexes.Contains(currentIndex)
                && FindLinkedVerificationStepIndex(currentIndex, null) >= 0;
        }

        public bool IsVerificationTool(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            var currentStep = GetCurrentExecutionStep();
            if (currentStep != null
                && string.Equals(currentStep.StepKind, "verify", System.StringComparison.OrdinalIgnoreCase))
            {
                return ContainsToolName(currentStep.VerificationToolNames, toolName);
            }

            if (VerificationToolNames != null
                && VerificationToolNames.Any(name => string.Equals(name, toolName, System.StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return ExecutionPlanSteps != null
                && ExecutionPlanSteps.Any(step => step != null
                    && step.VerificationToolNames != null
                    && step.VerificationToolNames.Any(name => string.Equals(name, toolName, System.StringComparison.OrdinalIgnoreCase)));
        }

        public bool RewindForVerificationFailure()
        {
            if (!HasStructuredExecutionPlan)
            {
                return false;
            }

            var verificationIndex = FindFirstIncompleteExecutionStepIndex(
                step => step != null && string.Equals(step.StepKind, "verify", System.StringComparison.OrdinalIgnoreCase));

            if (verificationIndex <= 0)
            {
                return false;
            }

            var previousMutationIndex = -1;
            for (var i = verificationIndex - 1; i >= 0; i--)
            {
                var step = ExecutionPlanSteps[i];
                if (IsMutationStep(step))
                {
                    previousMutationIndex = i;
                    break;
                }
            }

            if (previousMutationIndex < 0)
            {
                return false;
            }

            _completedExecutionStepIndexes.Remove(verificationIndex);
            _completedExecutionStepIndexes.Remove(previousMutationIndex);
            return true;
        }

        public bool CanRetryVerificationFailure()
        {
            return MaxVerificationRetryCount > 0 && VerificationRetryCount < MaxVerificationRetryCount;
        }

        public bool RegisterVerificationFailure()
        {
            if (!CanRetryVerificationFailure())
            {
                return false;
            }

            VerificationRetryCount++;
            return true;
        }

        public IEnumerable<SkillExecutionStep> GetRetryCandidateMutationSteps()
        {
            if (!HasStructuredExecutionPlan)
            {
                return Enumerable.Empty<SkillExecutionStep>();
            }

            var currentStep = GetCurrentExecutionStep();
            return GetExecutionPlanSteps()
                .Where(IsMutationStep)
                .Where(step => currentStep == null
                    || !string.Equals(step.SkillName, currentStep.SkillName, System.StringComparison.OrdinalIgnoreCase));
        }

        public bool RegisterSuccessfulToolCall(string toolName)
        {
            return RegisterSuccessfulToolCall(toolName, false, false);
        }

        public bool RegisterSuccessfulToolCall(
            string toolName,
            bool isWriteTool,
            bool hasPassingVerification)
        {
            if (!HasStructuredExecutionPlan || string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            var currentIndex = FindFirstIncompleteExecutionStepIndex();
            if (currentIndex < 0)
            {
                return false;
            }

            var currentStep = ExecutionPlanSteps[currentIndex];
            if (IsMutationStep(currentStep))
            {
                if (isWriteTool && StepMatchesTool(currentStep, toolName))
                {
                    if (FindLinkedVerificationStepIndex(currentIndex, null) >= 0)
                    {
                        _startedMutationStepIndexes.Add(currentIndex);
                        return false;
                    }

                    _completedExecutionStepIndexes.Add(currentIndex);
                    return FindFirstIncompleteExecutionStepIndex() != currentIndex;
                }

                if (!isWriteTool
                    && hasPassingVerification
                    && _startedMutationStepIndexes.Contains(currentIndex))
                {
                    var verificationIndex = FindLinkedVerificationStepIndex(currentIndex, toolName);
                    if (verificationIndex >= 0)
                    {
                        _startedMutationStepIndexes.Remove(currentIndex);
                        _completedExecutionStepIndexes.Add(currentIndex);
                        _completedExecutionStepIndexes.Add(verificationIndex);
                        return FindFirstIncompleteExecutionStepIndex() != currentIndex;
                    }
                }

                return false;
            }

            var matchedIndex = FindMatchingIncompleteStepIndex(
                toolName,
                isWriteTool,
                hasPassingVerification);
            if (matchedIndex < 0)
            {
                return false;
            }

            var completedStep = ExecutionPlanSteps[matchedIndex];
            _completedExecutionStepIndexes.Add(matchedIndex);
            if (hasPassingVerification
                && string.Equals(
                    completedStep.StepKind,
                    "inspect",
                    System.StringComparison.OrdinalIgnoreCase)
                && !HasPendingMutationStep())
            {
                var nextIndex = FindFirstIncompleteExecutionStepIndex();
                var finalVerificationIndex = FindFirstIncompleteExecutionStepIndex(step =>
                    step != null
                    && string.Equals(
                        step.StepKind,
                        "verify",
                        System.StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(step.VerifiesStepId)
                    && ContainsToolName(step.VerificationToolNames, toolName));
                if (nextIndex >= 0 && nextIndex == finalVerificationIndex)
                {
                    _completedExecutionStepIndexes.Add(finalVerificationIndex);
                }
            }

            return currentIndex >= 0 && FindFirstIncompleteExecutionStepIndex() != currentIndex;
        }

        private int FindLinkedVerificationStepIndex(int mutationIndex, string toolName)
        {
            if (mutationIndex < 0
                || mutationIndex >= ExecutionPlanSteps.Count
                || !IsMutationStep(ExecutionPlanSteps[mutationIndex]))
            {
                return -1;
            }

            var mutationStepId = ExecutionPlanSteps[mutationIndex].StepId;
            if (string.IsNullOrWhiteSpace(mutationStepId))
            {
                return -1;
            }

            for (var i = mutationIndex + 1; i < ExecutionPlanSteps.Count; i++)
            {
                var step = ExecutionPlanSteps[i];
                if (step == null
                    || !string.Equals(step.StepKind, "verify", System.StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(step.VerifiesStepId, mutationStepId, System.StringComparison.OrdinalIgnoreCase)
                    || _completedExecutionStepIndexes.Contains(i))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(toolName)
                    || ContainsToolName(step.VerificationToolNames, toolName))
                {
                    return i;
                }
            }

            return -1;
        }

        public bool TryAppendFollowupMutationStage(string toolName)
        {
            if (!HasStructuredExecutionPlan
                || !RequireVerification
                || string.IsNullOrWhiteSpace(toolName)
                || !AllowsTool(toolName)
                || GetCurrentExecutionStep() != null)
            {
                return false;
            }

            var verificationTemplate = GetExecutionPlanSteps()
                .Where(step => string.Equals(step.StepKind, "verify", System.StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(step.VerifiesStepId))
                .LastOrDefault();
            if (verificationTemplate == null)
            {
                return false;
            }

            var mutationTemplate = GetExecutionPlanSteps()
                .FirstOrDefault(step => IsMutationStep(step)
                    && string.Equals(
                        step.StepId,
                        verificationTemplate.VerifiesStepId,
                        System.StringComparison.OrdinalIgnoreCase));
            if (mutationTemplate == null)
            {
                return false;
            }

            var stageNumber = ++_dynamicMutationStageCount;
            var mutationStepId = "mutation:runtime:" + stageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var verificationStepId = "verify:runtime:" + stageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ExecutionPlanSteps.Add(new SkillExecutionStep
            {
                StepKind = mutationTemplate.StepKind,
                StepId = mutationStepId,
                Title = mutationTemplate.Title + " (additional stage)",
                SkillName = mutationTemplate.SkillName,
                Instruction = "Perform this additional write as its own stage and verify it before making another change.",
                AllowedToolNames = new List<string> { toolName },
                PreferredToolNames = new List<string> { toolName },
                VerificationToolNames = new List<string>(),
                RequireVerification = true,
                RequiresConfirmation = mutationTemplate.RequiresConfirmation,
                RequiresBackup = mutationTemplate.RequiresBackup
            });
            ExecutionPlanSteps.Add(new SkillExecutionStep
            {
                StepKind = "verify",
                StepId = verificationStepId,
                VerifiesStepId = mutationStepId,
                Title = "Verify additional write stage",
                SkillName = mutationTemplate.SkillName,
                Instruction = "Verify the output from this additional write stage before any further write.",
                AllowedToolNames = verificationTemplate.AllowedToolNames == null
                    ? new List<string>()
                    : verificationTemplate.AllowedToolNames.ToList(),
                PreferredToolNames = verificationTemplate.PreferredToolNames == null
                    ? new List<string>()
                    : verificationTemplate.PreferredToolNames.ToList(),
                VerificationToolNames = verificationTemplate.VerificationToolNames == null
                    ? new List<string>()
                    : verificationTemplate.VerificationToolNames.ToList(),
                RequireVerification = true,
                RequiresConfirmation = false,
                RequiresBackup = false
            });
            return true;
        }

        public bool HasCompletedExecutionStep(string stepKind)
        {
            if (!HasStructuredExecutionPlan || string.IsNullOrWhiteSpace(stepKind))
            {
                return false;
            }

            return _completedExecutionStepIndexes.Any(index => index >= 0
                && index < ExecutionPlanSteps.Count
                && string.Equals(ExecutionPlanSteps[index].StepKind, stepKind, System.StringComparison.OrdinalIgnoreCase));
        }

        public bool HasPendingVerificationStep()
        {
            if (!HasStructuredExecutionPlan)
            {
                return false;
            }

            for (var i = 0; i < ExecutionPlanSteps.Count; i++)
            {
                var step = ExecutionPlanSteps[i];
                if (step != null
                    && string.Equals(step.StepKind, "verify", System.StringComparison.OrdinalIgnoreCase)
                    && !_completedExecutionStepIndexes.Contains(i))
                {
                    return true;
                }
            }

            return false;
        }

        public SkillExecutionStep GetPendingVerificationStep()
        {
            if (!HasStructuredExecutionPlan)
            {
                return null;
            }

            for (var i = 0; i < ExecutionPlanSteps.Count; i++)
            {
                var step = ExecutionPlanSteps[i];
                if (step != null
                    && string.Equals(step.StepKind, "verify", System.StringComparison.OrdinalIgnoreCase)
                    && !_completedExecutionStepIndexes.Contains(i))
                {
                    return step;
                }
            }

            return null;
        }

        public SkillExecutionStep GetCurrentExecutionStep()
        {
            if (!HasStructuredExecutionPlan)
            {
                return null;
            }

            var index = FindFirstIncompleteExecutionStepIndex();
            if (index < 0 || index >= ExecutionPlanSteps.Count)
            {
                return null;
            }

            return ExecutionPlanSteps[index];
        }

        public bool IsPreferredTool(string toolName)
        {
            if (PreferredToolNames == null || PreferredToolNames.Count == 0 || string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            return PreferredToolNames.Any(name => string.Equals(name, toolName, System.StringComparison.OrdinalIgnoreCase));
        }

        public bool AllowsTool(string toolName)
        {
            if (AllowedToolNames == null || AllowedToolNames.Count == 0)
            {
                return true;
            }

            return AllowedToolNames.Any(name => string.Equals(name, toolName, System.StringComparison.OrdinalIgnoreCase));
        }

        private static bool StepMatchesTool(SkillExecutionStep step, string toolName)
        {
            if (step == null || string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            return ContainsToolName(step.PreferredToolNames, toolName)
                || ContainsToolName(step.AllowedToolNames, toolName);
        }

        private static bool ContainsToolName(IList<string> toolNames, string toolName)
        {
            return toolNames != null
                && toolNames.Any(name => string.Equals(name, toolName, System.StringComparison.OrdinalIgnoreCase));
        }

        private int FindMatchingIncompleteStepIndex(
            string toolName,
            bool isWriteTool,
            bool hasPassingVerification)
        {
            var currentIndex = FindFirstIncompleteExecutionStepIndex();
            if (currentIndex < 0)
            {
                return -1;
            }

            var step = ExecutionPlanSteps[currentIndex];
            if (IsMutationStep(step))
            {
                return isWriteTool && StepMatchesTool(step, toolName)
                    ? currentIndex
                    : -1;
            }

            if (string.Equals(step.StepKind, "verify", System.StringComparison.OrdinalIgnoreCase))
            {
                return hasPassingVerification
                    && ContainsToolName(step.VerificationToolNames, toolName)
                    ? currentIndex
                    : -1;
            }

            return !isWriteTool && StepMatchesTool(step, toolName)
                ? currentIndex
                : -1;
        }

        private int FindFirstIncompleteExecutionStepIndex()
        {
            return FindFirstIncompleteExecutionStepIndex(null);
        }

        private int FindFirstIncompleteExecutionStepIndex(System.Func<SkillExecutionStep, bool> predicate)
        {
            if (!HasStructuredExecutionPlan)
            {
                return -1;
            }

            for (var i = 0; i < ExecutionPlanSteps.Count; i++)
            {
                var step = ExecutionPlanSteps[i];
                if (step == null || _completedExecutionStepIndexes.Contains(i))
                {
                    continue;
                }

                if (predicate == null || predicate(step))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsMutationStep(SkillExecutionStep step)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.StepKind))
            {
                return false;
            }

            return !string.Equals(step.StepKind, "inspect", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(step.StepKind, "verify", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(step.StepKind, "general", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
