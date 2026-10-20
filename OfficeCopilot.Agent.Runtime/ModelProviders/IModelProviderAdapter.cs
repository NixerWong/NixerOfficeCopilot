using OfficeCopilot.Agent.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime
{
    internal interface IModelProviderAdapter
    {
        Task<ModelClientCompletion> CompleteAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken);
    }
}
