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
    internal sealed class HttpModelClient : IModelClient
    {
        private static readonly IModelProviderAdapter s_openAiCompatible = new OpenAiCompatibleProviderAdapter();
        private static readonly IModelProviderAdapter s_ollama = new OllamaProviderAdapter();
        private static readonly IModelProviderAdapter s_anthropic = new AnthropicProviderAdapter();

        public Task<ModelClientCompletion> CompleteAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (modelOptions == null)
            {
                throw new InvalidOperationException("Model options are required.");
            }

            var adapter = ResolveAdapter(modelOptions.ProviderKind);
            return adapter.CompleteAsync(modelOptions, messages, tools, streamingCallbacks, cancellationToken);
        }

        private static IModelProviderAdapter ResolveAdapter(string providerKind)
        {
            switch (NormalizeProviderKind(providerKind))
            {
                case "ollama":
                    return s_ollama;
                case "anthropic":
                    return s_anthropic;
                default:
                    return s_openAiCompatible;
            }
        }

        private static string NormalizeProviderKind(string providerKind)
        {
            return string.IsNullOrWhiteSpace(providerKind)
                ? "custom"
                : providerKind.Trim().Replace(" ", string.Empty).ToLowerInvariant();
        }

    }
}
