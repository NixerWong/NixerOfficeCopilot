using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Abstractions
{
    public interface IAgentTool
    {
        ToolDefinition Definition { get; }

        Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request);
    }
}