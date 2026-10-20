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
    internal sealed class OllamaProviderAdapter : IModelProviderAdapter
    {
        public async Task<ModelClientCompletion> CompleteAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken)
        {
            if (streamingCallbacks != null)
            {
                return await StreamOllamaAsync(modelOptions, messages, tools, streamingCallbacks, cancellationToken).ConfigureAwait(false);
            }

            var endpoint = ResolveOllamaEndpoint(modelOptions);
            var requestBody = BuildOllamaRequest(modelOptions, messages, tools);
            var requestJson = SerializeRequestJson(requestBody);

            var jsonText = await SendJsonAsync(
                "agent.turn",
                endpoint,
                requestJson,
                modelOptions,
                cancellationToken,
                "Ollama model request failed.").ConfigureAwait(false);

            return ExtractOllamaCompletion(jsonText);
        }

        private async Task<ModelClientCompletion> StreamOllamaAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken)
        {
            var endpoint = ResolveOllamaEndpoint(modelOptions);
            var requestBody = BuildOllamaRequest(modelOptions, messages, tools);
            requestBody["stream"] = true;
            var requestJson = SerializeRequestJson(requestBody);

            s_transcriptLogger.WriteRequest(modelOptions, "agent.turn.stream", endpoint, "application/json", requestJson);

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            using (var requestTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                request.Headers.TryAddWithoutValidation("Accept", "application/x-ndjson, application/json");
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                ApplyProviderHeaders(request, modelOptions);

                requestTimeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(15, modelOptions.GetRequestTimeoutSeconds())));

                try
                {
                    using (var response = await s_httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestTimeoutCts.Token).ConfigureAwait(false))
                    {
                        var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using (var reader = new StreamReader(responseStream, Encoding.UTF8))
                        using (var streamCancelRegistration = cancellationToken.Register(() =>
                        {
                            try { responseStream.Dispose(); } catch { }
                        }))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                var errorText = await reader.ReadToEndAsync().ConfigureAwait(false);
                                s_transcriptLogger.WriteResponse(modelOptions, "agent.turn.stream.error", endpoint, "text/plain", errorText);
                                throw CreateModelRequestException("Ollama model request failed.", response.StatusCode, errorText);
                            }

                            var completion = new ModelClientCompletion();
                            var contentBuilder = new StringBuilder();
                            var reasoningBuilder = new StringBuilder();
                            var responseRaw = new StringBuilder();
                            var toolCallStates = new List<StreamingToolCallState>();

                            while (!reader.EndOfStream)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                string line;
                                try
                                {
                                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                                }
                                catch (ObjectDisposedException)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    throw;
                                }
                                catch (IOException)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    throw;
                                }
                                if (!string.IsNullOrEmpty(line))
                                {
                                    responseRaw.AppendLine(line);
                                }

                                if (string.IsNullOrWhiteSpace(line))
                                {
                                    continue;
                                }

                                var json = JsonNode.Parse(line) as JsonObject ?? new JsonObject();
                                var errorMessage = GetString(json["error"]);
                                if (!string.IsNullOrWhiteSpace(errorMessage))
                                {
                                    throw new InvalidOperationException("Ollama model request failed: " + errorMessage);
                                }

                                var finishReason = GetOllamaFinishReason(json);
                                if (!string.IsNullOrWhiteSpace(finishReason))
                                {
                                    completion.FinishReason = finishReason;
                                }

                                await DispatchContentDeltaAsync(
                                    streamingCallbacks,
                                    contentBuilder,
                                    ExtractOllamaStreamContentDelta(json),
                                    cancellationToken).ConfigureAwait(false);

                                await DispatchReasoningDeltaAsync(
                                    streamingCallbacks,
                                    reasoningBuilder,
                                    ExtractOllamaStreamReasoningDelta(json),
                                    cancellationToken).ConfigureAwait(false);

                                ApplyOllamaStreamToolCalls(json, toolCallStates);

                                if (GetBoolean(json["done"]))
                                {
                                    break;
                                }
                            }

                            s_transcriptLogger.WriteResponse(modelOptions, "agent.turn.stream", endpoint, "application/x-ndjson", responseRaw.ToString());

                            completion.Content = contentBuilder.ToString();
                            completion.ReasoningContent = reasoningBuilder.ToString();
                            completion.ToolCalls = BuildStreamedToolCalls(toolCallStates);

                            return completion;
                        }
                    }
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    s_transcriptLogger.WriteException(modelOptions, "agent.turn.stream.error", endpoint, ex);
                    throw new TimeoutException("Ollama model request failed. Request timed out.", ex);
                }
                catch (HttpRequestException ex)
                {
                    s_transcriptLogger.WriteException(modelOptions, "agent.turn.stream.error", endpoint, ex);
                    throw new InvalidOperationException("Ollama model request failed.", ex);
                }
            }
        }

        private static JsonObject BuildOllamaRequest(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools)
        {
            var normalizedMessages = NormalizeMessagesForRequest(messages);
            bool deepThinkingEnabled;
            var hasExplicitDeepThinking = TryReadBooleanParameter(
                modelOptions == null ? null : modelOptions.AdditionalParameters,
                out deepThinkingEnabled,
                AgentModelAdditionalParameterNames.SupportsDeepThinking,
                "supports_deep_thinking",
                "supportsDeepThinking",
                "enable_thinking",
                "EnableThinking",
                "think");
            var requestBody = new JsonObject
            {
                ["model"] = modelOptions.ModelId ?? string.Empty,
                ["stream"] = false,
                ["messages"] = new JsonArray(normalizedMessages.Select(BuildOllamaMessage).ToArray())
            };

            if (hasExplicitDeepThinking)
            {
                requestBody["think"] = deepThinkingEnabled;
            }

            double temperature;
            if (TryReadDoubleParameter(
                modelOptions == null ? null : modelOptions.AdditionalParameters,
                out temperature,
                AgentModelAdditionalParameterNames.Temperature,
                "temperature"))
            {
                requestBody["temperature"] = temperature;
            }

            double topP;
            if (TryReadDoubleParameter(
                modelOptions == null ? null : modelOptions.AdditionalParameters,
                out topP,
                AgentModelAdditionalParameterNames.TopP,
                "top_p",
                "topP"))
            {
                requestBody["top_p"] = topP;
            }

            int maxTokens;
            if (TryReadIntParameter(
                modelOptions == null ? null : modelOptions.AdditionalParameters,
                out maxTokens,
                AgentModelAdditionalParameterNames.MaxTokens,
                "max_tokens",
                "maxTokens"))
            {
                requestBody["max_tokens"] = maxTokens;
            }

            if (tools != null && tools.Count > 0 && modelOptions.GetSupportsFunctionCalling())
            {
                requestBody["tools"] = new JsonArray(tools.Select(BuildOpenAiCompatibleTool).ToArray());
            }

            ApplyAdditionalRequestParameters(
                requestBody,
                modelOptions == null ? null : modelOptions.AdditionalParameters,
                "messages",
                "model",
                "stream",
                "tools",
                "think",
                AgentModelAdditionalParameterNames.SupportsFunctionCalling,
                AgentModelAdditionalParameterNames.RequestTimeoutSeconds,
                AgentModelAdditionalParameterNames.Temperature,
                AgentModelAdditionalParameterNames.TopP,
                AgentModelAdditionalParameterNames.MaxTokens,
                AgentModelAdditionalParameterNames.ModelTranscriptEnabled,
                AgentModelAdditionalParameterNames.ModelTranscriptMode,
                AgentModelAdditionalParameterNames.ModelTranscriptFilePath,
                "supports_function_calling",
                "supportsFunctionCalling",
                "request_timeout_seconds",
                "requestTimeoutSeconds",
                "temperature",
                "top_p",
                "topP",
                "max_tokens",
                "maxTokens");

            return requestBody;
        }

        private static JsonObject BuildOllamaMessage(ModelClientMessage message)
        {
            var json = new JsonObject
            {
                ["role"] = message == null ? string.Empty : message.Role ?? string.Empty,
                ["content"] = message != null && message.ContentParts != null && message.ContentParts.Count > 0
                    ? ExtractTextContentFromParts(message.ContentParts)
                    : message == null ? string.Empty : message.Content ?? string.Empty
            };

            if (message != null && message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                json["tool_calls"] = new JsonArray(message.ToolCalls.Select(BuildOllamaToolCallNode).ToArray());
            }

            // Ollama 把图像放在消息的 images 数组（纯 base64，不带 data: 前缀）。
            if (message != null && message.ContentParts != null)
            {
                var images = message.ContentParts
                    .Where(p => p != null
                        && !string.IsNullOrWhiteSpace(p.FileContentBase64)
                        && (string.Equals(p.Type, "input_image", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(p.Type, "image_url", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(p.Type, "image", StringComparison.OrdinalIgnoreCase)))
                    .Select(p => (JsonNode)JsonValue.Create(p.FileContentBase64.Trim()))
                    .ToArray();
                if (images.Length > 0)
                {
                    json["images"] = new JsonArray(images);
                }
            }

            return json;
        }

        private static JsonObject BuildOllamaToolCallNode(ModelClientToolCall toolCall)
        {
            return new JsonObject
            {
                ["id"] = toolCall == null ? string.Empty : toolCall.Id ?? string.Empty,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = toolCall == null ? string.Empty : toolCall.Name ?? string.Empty,
                    ["arguments"] = BuildOllamaToolArgumentsNode(toolCall == null ? "{}" : toolCall.ArgumentsJson ?? "{}")
                }
            };
        }

        private static JsonNode BuildOllamaToolArgumentsNode(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return new JsonObject();
            }

            try
            {
                return JsonNode.Parse(argumentsJson);
            }
            catch
            {
                return argumentsJson.Trim();
            }
        }

        private static string GetOllamaFinishReason(JsonObject json)
        {
            return GetString(
                json == null
                    ? null
                    : json["done_reason"]
                        ?? json["finish_reason"]
                        ?? json["message"]?["done_reason"]);
        }

        private static string ExtractOllamaStreamContentDelta(JsonObject json)
        {
            return NormalizeTextNode(
                json == null
                    ? null
                    : json["message"]?["content"]
                        ?? json["content"]
                        ?? json["response"]);
        }

        private static string ExtractOllamaStreamReasoningDelta(JsonObject json)
        {
            return NormalizeTextNode(
                json == null
                    ? null
                    : json["message"]?["reasoning"]
                        ?? json["message"]?["thinking"]
                        ?? json["message"]?["think"]
                        ?? json["reasoning"]
                        ?? json["thinking"]
                        ?? json["think"]);
        }

        private static void ApplyOllamaStreamToolCalls(JsonObject json, List<StreamingToolCallState> toolCallStates)
        {
            var toolCalls = json == null
                ? null
                : json["message"]?["tool_calls"] as JsonArray
                    ?? json["tool_calls"] as JsonArray;

            if (toolCalls == null || toolCalls.Count == 0)
            {
                return;
            }

            for (var index = 0; index < toolCalls.Count; index++)
            {
                var toolCall = toolCalls[index] as JsonObject;
                if (toolCall == null)
                {
                    continue;
                }

                var state = GetOrCreateToolCallState(toolCallStates, index);

                var id = GetString(toolCall["id"]);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    state.Id = id;
                }

                var type = GetString(toolCall["type"]);
                if (!string.IsNullOrWhiteSpace(type))
                {
                    state.Type = type;
                }

                var functionName = GetString(toolCall["function"]?["name"] ?? toolCall["name"]);
                if (!string.IsNullOrWhiteSpace(functionName))
                {
                    state.FunctionName.Clear();
                    state.FunctionName.Append(functionName);
                }

                var argumentsNode = toolCall["function"]?["arguments"] ?? toolCall["arguments"];
                if (argumentsNode != null)
                {
                    state.Arguments.Clear();
                    state.Arguments.Append(NormalizeArgumentsJson(argumentsNode));
                }
            }
        }

        private static ModelClientCompletion ExtractOllamaCompletion(string jsonText)
        {
            var root = JsonNode.Parse(jsonText) as JsonObject ?? new JsonObject();
            var errorMessage = GetString(root["error"]);
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new InvalidOperationException("Ollama model request failed: " + errorMessage);
            }

            var message = root["message"] as JsonObject ?? new JsonObject();
            var completion = new ModelClientCompletion
            {
                FinishReason = GetString(root["done_reason"] ?? root["finish_reason"] ?? message["done_reason"]),
                Content = NormalizeTextNode(message["content"] ?? message["text"] ?? root["response"]),
                ReasoningContent = NormalizeTextNode(message["reasoning"] ?? message["thinking"] ?? message["think"] ?? root["reasoning"] ?? root["thinking"] ?? root["think"])
            };

            var toolCalls = message["tool_calls"] as JsonArray ?? root["tool_calls"] as JsonArray;
            if (toolCalls != null)
            {
                var index = 0;
                foreach (var node in toolCalls.OfType<JsonObject>())
                {
                    index++;
                    var name = GetString(node["function"]?["name"] ?? node["name"]);
                    var id = GetString(node["id"]);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        id = BuildSyntheticToolCallId(name, index);
                    }

                    completion.ToolCalls.Add(new ModelClientToolCall
                    {
                        Id = id,
                        Name = name,
                        ArgumentsJson = NormalizeArgumentsJson(node["function"]?["arguments"] ?? node["arguments"])
                    });
                }
            }

            return completion;
        }

        private static string ResolveOllamaEndpoint(AgentModelOptions modelOptions)
        {
            var endpoint = (modelOptions.Endpoint ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException("Ollama endpoint has not been configured.");
            }

            if (endpoint.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            return endpoint + "/api/chat";
        }
    }
}
