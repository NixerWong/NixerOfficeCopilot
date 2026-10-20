using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Planning
{
    public interface ISkillPlanner
    {
        Task<SkillSelectionResult> SelectSkillAsync(
            SkillPlannerRequest request,
            ISkillRegistry skillRegistry,
            CancellationToken cancellationToken);
    }
}
