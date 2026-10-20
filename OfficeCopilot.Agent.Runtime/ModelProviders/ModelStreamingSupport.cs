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
using static OfficeCopilot.Agent.Runtime.ModelHttpTransport;
using static OfficeCopilot.Agent.Runtime.ModelJsonHelpers;
using static OfficeCopilot.Agent.Runtime.ModelRequestParameters;
using static OfficeCopilot.Agent.Runtime.ModelStreamingSupport;

namespace OfficeCopilot.Agent.Runtime
{
    internal static class ModelStreamingSupport
    {
        internal static async Task DispatchContentDeltaAsync(
            ModelClientStreamingCallbacks streamingCallbacks,
            StringBuilder contentBuilder,
            string delta,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            contentBuilder.Append(delta);

            if (streamingCallbacks != null && streamingCallbacks.OnContentDeltaAsync != null)
            {
                await streamingCallbacks.OnContentDeltaAsync(delta, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static async Task DispatchReasoningDeltaAsync(
            ModelClientStreamingCallbacks streamingCallbacks,
            StringBuilder reasoningBuilder,
            string delta,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            reasoningBuilder.Append(delta);

            if (streamingCallbacks != null && streamingCallbacks.OnReasoningDeltaAsync != null)
            {
                await streamingCallbacks.OnReasoningDeltaAsync(delta, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static StreamingToolCallState GetOrCreateToolCallState(List<StreamingToolCallState> toolCallStates, int index)
        {
            foreach (var state in toolCallStates)
            {
                if (state.Index == index)
                {
                    return state;
                }
            }

            var newState = new StreamingToolCallState(index);
            toolCallStates.Add(newState);
            return newState;
        }

        internal static void AppendToolCallArgumentsFragment(StringBuilder builder, JsonNode node)
        {
            if (builder == null || node == null)
            {
                return;
            }

            if (node is JsonValue)
            {
                builder.Append(GetString(node));
                return;
            }

            builder.Append(node.ToJsonString());
        }

        internal static IList<ModelClientToolCall> BuildStreamedToolCalls(List<StreamingToolCallState> toolCallStates)
        {
            var result = new List<ModelClientToolCall>();
            if (toolCallStates == null || toolCallStates.Count == 0)
            {
                return result;
            }

            foreach (var state in toolCallStates.OrderBy(s => s.Index))
            {
                var functionName = state.FunctionName.ToString();
                var toolCallId = state.Id;
                if (string.IsNullOrWhiteSpace(toolCallId))
                {
                    toolCallId = BuildSyntheticToolCallId(functionName, state.Index + 1);
                }

                result.Add(new ModelClientToolCall
                {
                    Id = toolCallId,
                    Name = functionName ?? string.Empty,
                    ArgumentsJson = NormalizeStreamedArgumentsJson(state.Arguments.ToString())
                });
            }

            return result;
        }

        internal sealed class StreamingToolCallState
        {
            public StreamingToolCallState(int index)
            {
                Index = index;
                Id = string.Empty;
                Type = "function";
                FunctionName = new StringBuilder();
                Arguments = new StringBuilder();
            }

            public int Index { get; private set; }

            public string Id { get; set; }

            public string Type { get; set; }

            public StringBuilder FunctionName { get; private set; }

            public StringBuilder Arguments { get; private set; }
        }
    }
}
