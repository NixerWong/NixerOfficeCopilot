using OfficeCopilot.Agent.Client;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime
{
    internal interface IModelClient
    {
        Task<ModelClientCompletion> CompleteAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken);
    }
}
