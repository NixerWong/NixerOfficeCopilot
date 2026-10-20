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
    internal sealed class OpenAiCompatibleProviderAdapter : IModelProviderAdapter
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
                return await StreamOpenAiCompatibleAsync(modelOptions, messages, tools, streamingCallbacks, cancellationToken).ConfigureAwait(false);
            }

            var endpoint = ResolveOpenAiCompatibleEndpoint(modelOptions);
            var requestBody = BuildOpenAiCompatibleRequest(modelOptions, messages, tools);
            var requestJson = SerializeRequestJson(requestBody);

            var jsonText = await SendJsonAsync(
                "agent.turn",
                endpoint,
                requestJson,
                modelOptions,
                cancellationToken,
                "OpenAI-compatible model request failed.").ConfigureAwait(false);

            return ExtractOpenAiCompatibleCompletion(jsonText);
        }

        private async Task<ModelClientCompletion> StreamOpenAiCompatibleAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken)
        {
            var endpoint = ResolveOpenAiCompatibleEndpoint(modelOptions);
            var requestBody = BuildOpenAiCompatibleRequest(modelOptions, messages, tools);
            requestBody["stream"] = true;
            var requestJson = SerializeRequestJson(requestBody);

            s_transcriptLogger.WriteRequest(modelOptions, "agent.turn.stream", endpoint, "application/json", requestJson);

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            using (var requestTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
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
                            // Force-close the underlying stream so ReadLineAsync unblocks.
                            try { responseStream.Dispose(); } catch { }
                        }))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                var errorText = await reader.ReadToEndAsync().ConfigureAwait(false);
                                s_transcriptLogger.WriteResponse(modelOptions, "agent.turn.stream.error", endpoint, "text/plain", errorText);
                                throw CreateModelRequestException("OpenAI-compatible model request failed.", response.StatusCode, errorText);
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

                                if (string.IsNullOrWhiteSpace(line)
                                    || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                var data = line.Substring(5).Trim();
                                if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                                {
                                    break;
                                }

                                if (string.IsNullOrWhiteSpace(data))
                                {
                                    continue;
                                }

                                var json = JsonNode.Parse(data) as JsonObject ?? new JsonObject();
                                var finishReason = GetOpenAiFinishReason(json);
                                if (!string.IsNullOrWhiteSpace(finishReason))
                                {
                                    completion.FinishReason = finishReason;
                                }

                                await DispatchContentDeltaAsync(
                                    streamingCallbacks,
                                    contentBuilder,
                                    ExtractOpenAiContentDelta(json),
                                    cancellationToken).ConfigureAwait(false);

                                await DispatchReasoningDeltaAsync(
                                    streamingCallbacks,
                                    reasoningBuilder,
                                    ExtractOpenAiReasoningDelta(json),
                                    cancellationToken).ConfigureAwait(false);

                                ApplyOpenAiCompatibleToolCallDelta(json, toolCallStates);
                            }

                            s_transcriptLogger.WriteResponse(modelOptions, "agent.turn.stream", endpoint, "text/event-stream", responseRaw.ToString());

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
                    throw new TimeoutException("OpenAI-compatible model request failed. Request timed out.", ex);
                }
                catch (HttpRequestException ex)
                {
                    s_transcriptLogger.WriteException(modelOptions, "agent.turn.stream.error", endpoint, ex);
                    throw new InvalidOperationException("OpenAI-compatible model request failed.", ex);
                }
            }
        }

        private static JsonObject BuildOpenAiCompatibleRequest(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools)
        {
            var normalizedMessages = NormalizeMessagesForRequest(messages);
            var requestBody = new JsonObject
            {
                ["messages"] = new JsonArray(normalizedMessages.Select(BuildOpenAiCompatibleMessage).ToArray()),
                ["stream"] = false
            };

            if (!string.Equals(modelOptions.ProviderKind, "AzureOpenAI", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(modelOptions.ModelId))
            {
                requestBody["model"] = modelOptions.ModelId;
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
                requestBody["tool_choice"] = "auto";
            }

            ApplyAdditionalRequestParameters(
                requestBody,
                modelOptions == null ? null : modelOptions.AdditionalParameters,
                "messages",
                "model",
                "stream",
                "tools",
                "tool_choice",
                "temperature",
                "top_p",
                "max_tokens",
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
                "topP",
                "maxTokens");

            return requestBody;
        }

        private static JsonObject BuildOpenAiCompatibleMessage(ModelClientMessage message)
        {
            var json = new JsonObject
            {
                ["role"] = message == null ? string.Empty : message.Role ?? string.Empty
            };

            if (message != null && message.ContentParts != null && message.ContentParts.Count > 0)
            {
                json["content"] = BuildOpenAiCompatibleContentParts(message.ContentParts);
            }
            else if (!string.IsNullOrWhiteSpace(message == null ? null : message.Content))
            {
                json["content"] = message.Content;
            }
            else if (message != null
                && string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                && message.ToolCalls != null
                && message.ToolCalls.Count > 0)
            {
                json["content"] = null;
            }
            else
            {
                json["content"] = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(message == null ? null : message.Name))
            {
                json["name"] = message.Name;
            }

            if (!string.IsNullOrWhiteSpace(message == null ? null : message.ToolCallId))
            {
                json["tool_call_id"] = message.ToolCallId;
            }

            if (message != null && message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                json["tool_calls"] = new JsonArray(message.ToolCalls.Select(BuildToolCallNode).ToArray());
            }

            return json;
        }

        private static JsonArray BuildOpenAiCompatibleContentParts(IList<ModelClientMessageContentPart> contentParts)
        {
            var result = new JsonArray();
            if (contentParts == null || contentParts.Count == 0)
            {
                return result;
            }

            foreach (var part in contentParts)
            {
                if (part == null || string.IsNullOrWhiteSpace(part.Type))
                {
                    continue;
                }

                var type = part.Type.Trim();
                if (string.Equals(type, "input_text", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = part.Text ?? string.Empty
                    });
                    continue;
                }

                if (string.Equals(type, "input_image", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "image_url", StringComparison.OrdinalIgnoreCase))
                {
                    var url = part.Url ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(part.FileContentBase64))
                    {
                        var mediaType = string.IsNullOrWhiteSpace(part.MediaType) ? "application/octet-stream" : part.MediaType.Trim();
                        url = "data:" + mediaType + ";base64," + part.FileContentBase64.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        result.Add(new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = url
                            }
                        });
                    }

                    continue;
                }

                if (string.Equals(type, "input_file", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
                {
                    var fileObject = new JsonObject
                    {
                        ["type"] = "file"
                    };

                    if (!string.IsNullOrWhiteSpace(part.FileName))
                    {
                        fileObject["filename"] = part.FileName.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(part.FileContentBase64))
                    {
                        fileObject["file_data"] = part.FileContentBase64.Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(part.MediaType))
                    {
                        fileObject["media_type"] = part.MediaType.Trim();
                    }

                    result.Add(fileObject);
                }
            }

            return result;
        }

        private static ModelClientCompletion ExtractOpenAiCompatibleCompletion(string jsonText)
        {
            var root = JsonNode.Parse(jsonText) as JsonObject ?? new JsonObject();
            var choice = root["choices"]?[0] as JsonObject ?? new JsonObject();
            var message = choice["message"] as JsonObject ?? new JsonObject();

            var completion = new ModelClientCompletion
            {
                FinishReason = GetString(choice["finish_reason"]),
                Content = NormalizeTextNode(message["content"]),
                ReasoningContent = NormalizeTextNode(message["reasoning_content"] ?? message["reasoning"] ?? message["thinking"])
            };

            var toolCalls = message["tool_calls"] as JsonArray;
            if (toolCalls != null)
            {
                foreach (var node in toolCalls.OfType<JsonObject>())
                {
                    completion.ToolCalls.Add(new ModelClientToolCall
                    {
                        Id = GetString(node["id"]),
                        Name = GetString(node["function"]?["name"]),
                        ArgumentsJson = NormalizeArgumentsJson(node["function"]?["arguments"])
                    });
                }
            }

            return completion;
        }

        private static string GetOpenAiFinishReason(JsonObject json)
        {
            var choice = GetChoiceNode(json);
            return GetString(choice == null ? null : choice["finish_reason"]);
        }

        private static string ExtractOpenAiContentDelta(JsonObject json)
        {
            var choice = GetChoiceNode(json);
            return NormalizeTextNode(
                choice == null
                    ? null
                    : choice["delta"]?["content"]
                        ?? choice["message"]?["content"]);
        }

        private static string ExtractOpenAiReasoningDelta(JsonObject json)
        {
            var choice = GetChoiceNode(json);
            return NormalizeTextNode(
                choice == null
                    ? null
                    : choice["delta"]?["reasoning_content"]
                        ?? choice["delta"]?["reasoning"]
                        ?? choice["delta"]?["thinking"]
                        ?? choice["message"]?["reasoning_content"]
                        ?? choice["message"]?["reasoning"]
                        ?? choice["message"]?["thinking"]);
        }

        private static void ApplyOpenAiCompatibleToolCallDelta(JsonObject json, List<StreamingToolCallState> toolCallStates)
        {
            var choice = GetChoiceNode(json);
            var toolCalls = choice == null
                ? null
                : choice["delta"]?["tool_calls"] as JsonArray
                    ?? choice["message"]?["tool_calls"] as JsonArray;

            if (toolCalls == null || toolCalls.Count == 0)
            {
                return;
            }

            foreach (var item in toolCalls)
            {
                var toolCall = item as JsonObject;
                if (toolCall == null)
                {
                    continue;
                }

                var index = GetInt32(toolCall["index"]);
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

                var functionName = GetString(toolCall["function"]?["name"]);
                if (!string.IsNullOrWhiteSpace(functionName))
                {
                    state.FunctionName.Append(functionName);
                }

                AppendToolCallArgumentsFragment(state.Arguments, toolCall["function"]?["arguments"]);
            }
        }

        private static JsonObject GetChoiceNode(JsonObject json)
        {
            if (json == null)
            {
                return null;
            }

            var choices = json["choices"] as JsonArray;
            if (choices == null || choices.Count == 0)
            {
                return null;
            }

            return choices[0] as JsonObject;
        }

        private static string ResolveOpenAiCompatibleEndpoint(AgentModelOptions modelOptions)
        {
            var endpoint = (modelOptions.Endpoint ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException("Model endpoint has not been configured.");
            }

            if (string.Equals(modelOptions.ProviderKind, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
            {
                if (endpoint.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (endpoint.IndexOf("api-version=", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return endpoint;
                    }

                    return endpoint + (endpoint.Contains("?") ? "&" : "?") + "api-version=2024-02-01";
                }

                if (string.IsNullOrWhiteSpace(modelOptions.ModelId))
                {
                    throw new InvalidOperationException("Azure OpenAI requires a deployment/model id.");
                }

                return endpoint + "/openai/deployments/" + Uri.EscapeDataString(modelOptions.ModelId.Trim()) + "/chat/completions?api-version=2024-02-01";
            }

            if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint + "/chat/completions";
            }

            if (endpoint.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return endpoint;
            }

            return endpoint + "/chat/completions";
        }
    }
}
