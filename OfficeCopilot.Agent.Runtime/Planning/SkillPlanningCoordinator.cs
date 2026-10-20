using OfficeCopilot.Agent.Planning;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.Planning
{
    internal sealed class SkillPlanningCoordinator
    {
        private readonly ISkillPlanner _skillPlanner;
        private readonly ISkillRegistry _skillRegistry;

        public SkillPlanningCoordinator()
            : this(
                new KeywordSkillPlanner(),
                new InMemorySkillRegistry(OfficeSkillCatalog.CreateDefaultSkills()))
        {
        }

        public SkillPlanningCoordinator(ISkillPlanner skillPlanner, ISkillRegistry skillRegistry)
        {
            _skillPlanner = skillPlanner;
            _skillRegistry = skillRegistry;
        }

        public async Task<RuntimeSkillPlan> PlanAsync(
            string userInput,
            string hostApplicationName,
            IEnumerable<string> availableToolNames,
            IDictionary<string, string> metadata,
            string requestedSkillName,
            string defaultSkillName,
            CancellationToken cancellationToken)
        {
            if (_skillPlanner == null || _skillRegistry == null)
            {
                return new RuntimeSkillPlan();
            }

            var selection = await _skillPlanner.SelectSkillAsync(
                new SkillPlannerRequest
                {
                    UserInput = userInput ?? string.Empty,
                    HostApplicationName = hostApplicationName ?? string.Empty,
                    HostKind = hostApplicationName ?? string.Empty,
                    RequestedSkillName = requestedSkillName ?? string.Empty,
                    DefaultSkillName = defaultSkillName ?? string.Empty,
                    AvailableToolNames = (availableToolNames ?? Enumerable.Empty<string>()).ToList(),
                    Metadata = metadata == null
                        ? new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(metadata, System.StringComparer.OrdinalIgnoreCase)
                },
                _skillRegistry,
                cancellationToken).ConfigureAwait(false);

            var selectedSkill = selection == null || string.IsNullOrWhiteSpace(selection.SkillName)
                ? null
                : _skillRegistry.FindByName(selection.SkillName);

            var plan = new RuntimeSkillPlan
            {
                SkillName = selection == null ? string.Empty : selection.SkillName ?? string.Empty,
                Reason = selection == null ? string.Empty : selection.Reason ?? string.Empty,
                Prompt = selection != null && !string.IsNullOrEmpty(selection.DisabledRequestedSkillName)
                    ? "The user explicitly requested skill '" + selection.DisabledRequestedSkillName + "', but it is disabled in settings. Do not use or imitate it. Tell the user that this skill is disabled and that they can enable it in settings, then answer without it."
                    : SkillExecutionPromptBuilder.Build(selectedSkill, selection, userInput),
                Selection = selection,
                AllowedToolNames = selection == null ? new List<string>() : selection.AllowedToolNames.ToList(),
                PreferredToolNames = selection == null ? new List<string>() : selection.PreferredToolNames.ToList(),
                VerificationToolNames = selection == null ? new List<string>() : selection.VerificationToolNames.ToList(),
                CompanionSkillNames = selection == null ? new List<string>() : selection.CompanionSkillNames.ToList(),
                ExecutionSteps = selection == null ? new List<string>() : selection.ExecutionSteps.ToList(),
                ExecutionPlanSteps = selection == null || selection.ExecutionPlanSteps == null
                    ? new List<SkillExecutionStep>()
                    : selection.ExecutionPlanSteps
                        .Where(step => step != null)
                        .Select(step => new SkillExecutionStep
                        {
                            StepKind = step.StepKind ?? string.Empty,
                            StepId = step.StepId ?? string.Empty,
                            VerifiesStepId = step.VerifiesStepId ?? string.Empty,
                            Title = step.Title ?? string.Empty,
                            SkillName = step.SkillName ?? string.Empty,
                            Instruction = step.Instruction ?? string.Empty,
                            AllowedToolNames = step.AllowedToolNames == null ? new List<string>() : step.AllowedToolNames.ToList(),
                            PreferredToolNames = step.PreferredToolNames == null ? new List<string>() : step.PreferredToolNames.ToList(),
                            VerificationToolNames = step.VerificationToolNames == null ? new List<string>() : step.VerificationToolNames.ToList(),
                            RequireVerification = step.RequireVerification,
                            RequiresConfirmation = step.RequiresConfirmation,
                            RequiresBackup = step.RequiresBackup
                        })
                        .ToList(),
                FallbackSkillNames = selection == null ? new List<string>() : selection.FallbackSkillNames.ToList(),
                MaxToolRoundCount = selection == null ? 0 : selection.MaxToolRoundCount,
                RequireVerification = selection != null && selection.RequireVerification,
                RequiresConfirmation = selection != null && selection.RequiresConfirmation,
                RequiresBackup = selection != null && selection.RequiresBackup,
                AllowHighRiskTools = selection != null && selection.AllowHighRiskTools,
                AllowCodeExecution = selection != null && selection.AllowCodeExecution
            };

            plan.InitializeExecutionPlanProgress();
            return plan;
        }
    }
}
