using System.Collections.Generic;

namespace OfficeCopilot.Agent.Abstractions
{
    public interface IToolRegistry
    {
        IReadOnlyList<IAgentTool> GetTools();

        IReadOnlyList<ToolDefinition> GetDefinitions();

        bool TryGetTool(string toolName, out IAgentTool tool);
    }
}