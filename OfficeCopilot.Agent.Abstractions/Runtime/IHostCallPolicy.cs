using System;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Abstractions
{
    public interface IHostCallPolicy
    {
        Task<ToolExecutionResult> ExecuteAsync(
            ToolDefinition definition,
            ToolExecutionContext context,
            ToolExecutionRequest request,
            Func<Task<ToolExecutionResult>> execute);
    }
}
