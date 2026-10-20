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
    internal sealed class AnthropicProviderAdapter : IModelProviderAdapter
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
                return await StreamAnthropicAsync(modelOptions, messages, tools, streamingCallbacks, cancellationToken).ConfigureAwait(false);
            }

            var endpoint = ResolveAnthropicEndpoint(modelOptions);
            var anthropicToolNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var requestBody = BuildAnthropicRequest(modelOptions, messages, tools, anthropicToolNames);
            var requestJson = SerializeRequestJson(requestBody);

            var jsonText = await SendJsonAsync(
                "agent.turn",
                endpoint,
                requestJson,
                modelOptions,
                cancellationToken,
                "Anthropic model request failed.").ConfigureAwait(false);

            return ExtractAnthropicCompletion(jsonText, anthropicToolNames);
        }

        private async Task<ModelClientCompletion> StreamAnthropicAsync(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            ModelClientStreamingCallbacks streamingCallbacks,
            CancellationToken cancellationToken)
        {
            var endpoint = ResolveAnthropicEndpoint(modelOptions);
            var anthropicToolNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var requestBody = BuildAnthropicRequest(modelOptions, messages, tools, anthropicToolNames);
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
                            try { responseStream.Dispose(); } catch { }
                        }))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                var errorText = await reader.ReadToEndAsync().ConfigureAwait(false);
                                s_transcriptLogger.WriteResponse(modelOptions, "agent.turn.stream.error", endpoint, "text/plain", errorText);
                                throw CreateModelRequestException("Anthropic model request failed.", response.StatusCode, errorText);
                            }

                            var completion = new ModelClientCompletion();
                            var contentBuilder = new StringBuilder();
                            var reasoningBuilder = new StringBuilder();
                            var responseRaw = new StringBuilder();
                            var toolCallStates = new List<AnthropicStreamingToolState>();
                            var currentEvent = string.Empty;

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

                                if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
                                {
                                    currentEvent = line.Substring(6).Trim();
                                    continue;
                                }

                                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                var data = line.Substring(5).Trim();
                                if (string.IsNullOrWhiteSpace(data))
                                {
                                    continue;
                                }

                                var json = JsonNode.Parse(data) as JsonObject ?? new JsonObject();
                                var eventType = GetString(json["type"]);

                                switch (eventType)
                                {
                                    case "message_start":
                                        // Message metadata - we can capture model info here if needed
                                        break;

                                    case "content_block_start":
                                        ApplyAnthropicContentBlockStart(json, toolCallStates);
                                        break;

                                    case "content_block_delta":
                                        await ApplyAnthropicContentBlockDelta(
                                            json,
                                            toolCallStates,
                                            streamingCallbacks,
                                            contentBuilder,
                                            reasoningBuilder,
                                            cancellationToken);
                                        break;

                                    case "content_block_stop":
                                        // Block completed
                                        break;

                                    case "message_delta":
                                        var stopReason = GetString(json["delta"]?["stop_reason"]);
                                        if (!string.IsNullOrWhiteSpace(stopReason))
                                        {
                                            completion.FinishReason = stopReason;
                                        }
                                        break;

                                    case "message_stop":
                                        // Message complete
                                        break;
                                }
                            }

                            s_transcriptLogger.WriteResponse(modelOptions, "agent.turn.stream", endpoint, "text/event-stream", responseRaw.ToString());

                            completion.Content = contentBuilder.ToString();
                            completion.ReasoningContent = reasoningBuilder.ToString();
                            completion.ToolCalls = BuildAnthropicStreamedToolCalls(toolCallStates, anthropicToolNames);

                            return completion;
                        }
                    }
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    s_transcriptLogger.WriteException(modelOptions, "agent.turn.stream.error", endpoint, ex);
                    throw new TimeoutException("Anthropic model request failed. Request timed out.", ex);
                }
                catch (HttpRequestException ex)
                {
                    s_transcriptLogger.WriteException(modelOptions, "agent.turn.stream.error", endpoint, ex);
                    throw new InvalidOperationException("Anthropic model request failed.", ex);
                }
            }
        }

        private static JsonObject BuildAnthropicRequest(
            AgentModelOptions modelOptions,
            IReadOnlyList<ModelClientMessage> messages,
            IReadOnlyList<ModelClientTool> tools,
            Dictionary<string, string> anthropicToolNames)
        {
            var normalizedResult = NormalizeMessagesForAnthropic(messages);
            var requestBody = new JsonObject
            {
                ["model"] = modelOptions.ModelId ?? string.Empty,
                ["max_tokens"] = 16384,
                ["messages"] = new JsonArray(normalizedResult.Messages.Select(BuildAnthropicMessage).ToArray()),
                ["stream"] = false
            };

            if (!string.IsNullOrWhiteSpace(normalizedResult.SystemPrompt))
            {
                requestBody["system"] = normalizedResult.SystemPrompt;
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

            double temperature;
            if (TryReadDoubleParameter(
                modelOptions == null ? null : modelOptions.AdditionalParameters,
                out temperature,
                AgentModelAdditionalParameterNames.Temperature,
                "temperature"))
            {
                requestBody["temperature"] = temperature;
            }

            // Thinking configuration: user can set "thinking" in AdditionalParameters to control it.
            // Accepted values: "enabled", "disabled", or a JSON object {"type":"enabled","budget_tokens":8000}.
            // If not set, auto-detect: native Anthropic → enabled, third-party (/anthropic) → disabled.
            if (!TryApplyUserThinkingConfig(requestBody, modelOptions))
            {
                var isNativeAnthropic = (modelOptions.Endpoint ?? string.Empty).IndexOf("anthropic.com", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isNativeAnthropic)
                {
                    var maxTokensValue = requestBody["max_tokens"] != null ? (int)requestBody["max_tokens"] : 16384;
                    var thinkingBudget = Math.Min(Math.Max(maxTokensValue / 2, 1024), 32768);
                    requestBody["thinking"] = new JsonObject
                    {
                        ["type"] = "enabled",
                        ["budget_tokens"] = thinkingBudget
                    };
                }
                else
                {
                    requestBody["thinking"] = new JsonObject { ["type"] = "disabled" };
                }
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

            if (tools != null && tools.Count > 0 && modelOptions.GetSupportsFunctionCalling())
            {
                requestBody["tools"] = new JsonArray(tools.Select(t => BuildAnthropicTool(t, anthropicToolNames)).ToArray());
            }

            ApplyAdditionalRequestParameters(
                requestBody,
                modelOptions == null ? null : modelOptions.AdditionalParameters,
                "messages",
                "model",
                "max_tokens",
                "system",
                "stream",
                "tools",
                "temperature",
                "top_p",
                AgentModelAdditionalParameterNames.SupportsFunctionCalling,
                AgentModelAdditionalParameterNames.RequestTimeoutSeconds,
                AgentModelAdditionalParameterNames.Temperature,
                AgentModelAdditionalParameterNames.TopP,
                AgentModelAdditionalParameterNames.MaxTokens,
                AgentModelAdditionalParameterNames.SupportsDeepThinking,
                AgentModelAdditionalParameterNames.ModelTranscriptEnabled,
                AgentModelAdditionalParameterNames.ModelTranscriptMode,
                AgentModelAdditionalParameterNames.ModelTranscriptFilePath,
                "supports_function_calling",
                "supportsFunctionCalling",
                "request_timeout_seconds",
                "requestTimeoutSeconds",
                "topP",
                "maxTokens",
                "supportsDeepThinking",
                "thinking");

            return requestBody;
        }

        private static JsonObject BuildAnthropicMessage(ModelClientMessage message)
        {
            var json = new JsonObject
            {
                ["role"] = message == null ? "user" : NormalizeAnthropicRole(message.Role)
            };

            // Build content array (Anthropic always uses content array)
            var contentArray = new JsonArray();

            // Handle tool results → tool_result content blocks
            if (message != null
                && string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(message.ToolCallId))
            {
                // ToolCallId may be comma-separated (merged from multiple tool results)
                var toolIds = message.ToolCallId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => id.Length > 0)
                    .ToList();

                if (toolIds.Count == 1)
                {
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = toolIds[0],
                        ["content"] = message.Content ?? string.Empty
                    });
                }
                else
                {
                    // Multiple tool results merged: each gets its own tool_result block
                    // Content is shared across all (model will extract relevant parts)
                    foreach (var toolId in toolIds)
                    {
                        contentArray.Add(new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = toolId,
                            ["content"] = message.Content ?? string.Empty
                        });
                    }
                }
            }
            // Handle assistant with tool_calls → tool_use content blocks
            else if (message != null
                && string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                && message.ToolCalls != null
                && message.ToolCalls.Count > 0)
            {
                // Add text content if present
                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = message.Content
                    });
                }

                // Add tool_use blocks
                foreach (var toolCall in message.ToolCalls)
                {
                    if (toolCall == null) continue;

                    JsonNode inputNode;
                    try
                    {
                        inputNode = string.IsNullOrWhiteSpace(toolCall.ArgumentsJson)
                            ? new JsonObject()
                            : JsonNode.Parse(toolCall.ArgumentsJson);
                    }
                    catch
                    {
                        inputNode = new JsonObject();
                    }

                    contentArray.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = toolCall.Id ?? string.Empty,
                        ["name"] = toolCall.Name ?? string.Empty,
                        ["input"] = inputNode
                    });
                }
            }
            // Handle user/assistant with content parts (images etc.)
            else if (message != null && message.ContentParts != null && message.ContentParts.Count > 0)
            {
                foreach (var part in message.ContentParts)
                {
                    if (part == null || string.IsNullOrWhiteSpace(part.Type)) continue;

                    var type = part.Type.Trim();
                    if (string.Equals(type, "input_text", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        contentArray.Add(new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = part.Text ?? string.Empty
                        });
                    }
                    else if (string.Equals(type, "input_image", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "image_url", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
                    {
                        var imageSource = new JsonObject();
                        if (!string.IsNullOrWhiteSpace(part.Url))
                        {
                            imageSource["type"] = "url";
                            imageSource["url"] = part.Url;
                        }
                        else if (!string.IsNullOrWhiteSpace(part.FileContentBase64))
                        {
                            imageSource["type"] = "base64";
                            var mediaType = string.IsNullOrWhiteSpace(part.MediaType) ? "image/png" : part.MediaType.Trim();
                            imageSource["media_type"] = mediaType;
                            imageSource["data"] = part.FileContentBase64.Trim();
                        }

                        if (imageSource.Count > 0)
                        {
                            contentArray.Add(new JsonObject
                            {
                                ["type"] = "image",
                                ["source"] = imageSource
                            });
                        }
                    }
                }
            }
            else
            {
                contentArray.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = message == null ? string.Empty : message.Content ?? string.Empty
                });
            }

            json["content"] = contentArray;
            return json;
        }

        private static JsonObject BuildAnthropicTool(ModelClientTool tool, Dictionary<string, string> anthropicToolNames)
        {
            JsonNode inputSchema;
            try
            {
                inputSchema = string.IsNullOrWhiteSpace(tool == null ? null : tool.InputSchemaJson)
                    ? new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                    : JsonNode.Parse(tool.InputSchemaJson);
            }
            catch
            {
                inputSchema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                };
            }

            var originalName = tool == null ? string.Empty : tool.Name ?? string.Empty;
            var sanitizedName = SanitizeAnthropicToolName(originalName);
            if (anthropicToolNames != null && !string.Equals(sanitizedName, originalName, StringComparison.Ordinal))
            {
                anthropicToolNames[sanitizedName] = originalName;
            }

            return new JsonObject
            {
                ["name"] = sanitizedName,
                ["description"] = tool == null ? string.Empty : tool.Description ?? string.Empty,
                ["input_schema"] = inputSchema
            };
        }

        private static ModelClientCompletion ExtractAnthropicCompletion(string jsonText, IReadOnlyDictionary<string, string> anthropicToolNames = null)
        {
            var root = JsonNode.Parse(jsonText) as JsonObject ?? new JsonObject();
            var errorMessage = GetString(root["error"]?["message"] ?? root["error"]);
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new InvalidOperationException("Anthropic model request failed: " + errorMessage);
            }

            var completion = new ModelClientCompletion
            {
                FinishReason = GetString(root["stop_reason"]),
                Content = string.Empty,
                ReasoningContent = string.Empty
            };

            var contentArray = root["content"] as JsonArray;
            if (contentArray != null)
            {
                var textBuilder = new StringBuilder();
                var reasoningBuilder = new StringBuilder();

                foreach (var block in contentArray.OfType<JsonObject>())
                {
                    var blockType = GetString(block["type"]);

                    if (string.Equals(blockType, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        textBuilder.Append(GetString(block["text"]));
                    }
                    else if (string.Equals(blockType, "thinking", StringComparison.OrdinalIgnoreCase))
                    {
                        reasoningBuilder.Append(GetString(block["thinking"]));
                    }
                    else if (string.Equals(blockType, "tool_use", StringComparison.OrdinalIgnoreCase))
                    {
                        var toolId = GetString(block["id"]);
                        var toolName = GetString(block["name"]);
                        // Reverse-map sanitized name back to original (e.g. "host_excel_find" → "host.excel_find")
                        if (anthropicToolNames != null && anthropicToolNames.TryGetValue(toolName, out var originalName))
                        {
                            toolName = originalName;
                        }
                        var toolInput = block["input"];
                        var argumentsJson = toolInput != null
                            ? (toolInput is JsonValue ? GetString(toolInput) : toolInput.ToJsonString())
                            : "{}";

                        completion.ToolCalls.Add(new ModelClientToolCall
                        {
                            Id = toolId,
                            Name = toolName,
                            ArgumentsJson = argumentsJson
                        });
                    }
                }

                completion.Content = textBuilder.ToString();
                completion.ReasoningContent = reasoningBuilder.ToString();
            }

            return completion;
        }

        private static string ResolveAnthropicEndpoint(AgentModelOptions modelOptions)
        {
            var endpoint = (modelOptions.Endpoint ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new InvalidOperationException("Anthropic endpoint has not been configured.");
            }

            if (endpoint.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            if (endpoint.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }

            return endpoint + "/v1/messages";
        }

        private sealed class AnthropicStreamingToolState
        {
            public AnthropicStreamingToolState(int index)
            {
                Index = index;
                Id = string.Empty;
                Name = string.Empty;
                InputJson = new StringBuilder();
            }

            public int Index { get; private set; }
            public string Id { get; set; }
            public string Name { get; set; }
            public StringBuilder InputJson { get; private set; }
        }

        private static void ApplyAnthropicContentBlockStart(JsonObject json, List<AnthropicStreamingToolState> toolCallStates)
        {
            var index = GetInt32(json["index"]);
            var contentBlock = json["content_block"] as JsonObject;
            if (contentBlock == null) return;

            var blockType = GetString(contentBlock["type"]);
            if (!string.Equals(blockType, "tool_use", StringComparison.OrdinalIgnoreCase)) return;

            var state = new AnthropicStreamingToolState(index)
            {
                Id = GetString(contentBlock["id"]),
                Name = GetString(contentBlock["name"])
            };
            toolCallStates.Add(state);
        }

        private static async Task ApplyAnthropicContentBlockDelta(
            JsonObject json,
            List<AnthropicStreamingToolState> toolCallStates,
            ModelClientStreamingCallbacks streamingCallbacks,
            StringBuilder contentBuilder,
            StringBuilder reasoningBuilder,
            CancellationToken cancellationToken)
        {
            var index = GetInt32(json["index"]);
            var delta = json["delta"] as JsonObject;
            if (delta == null) return;

            var deltaType = GetString(delta["type"]);

            if (string.Equals(deltaType, "text_delta", StringComparison.OrdinalIgnoreCase))
            {
                var text = GetString(delta["text"]);
                if (!string.IsNullOrEmpty(text))
                {
                    contentBuilder.Append(text);
                    if (streamingCallbacks != null && streamingCallbacks.OnContentDeltaAsync != null)
                    {
                        await streamingCallbacks.OnContentDeltaAsync(text, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else if (string.Equals(deltaType, "thinking_delta", StringComparison.OrdinalIgnoreCase))
            {
                var thinking = GetString(delta["thinking"]);
                if (!string.IsNullOrEmpty(thinking))
                {
                    reasoningBuilder.Append(thinking);
                    if (streamingCallbacks != null && streamingCallbacks.OnReasoningDeltaAsync != null)
                    {
                        await streamingCallbacks.OnReasoningDeltaAsync(thinking, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else if (string.Equals(deltaType, "input_json_delta", StringComparison.OrdinalIgnoreCase))
            {
                var partialJson = GetString(delta["partial_json"]);
                if (!string.IsNullOrEmpty(partialJson))
                {
                    var state = FindAnthropicToolState(toolCallStates, index);
                    if (state != null)
                    {
                        state.InputJson.Append(partialJson);
                    }
                }
            }
        }

        private static AnthropicStreamingToolState FindAnthropicToolState(List<AnthropicStreamingToolState> toolCallStates, int index)
        {
            foreach (var state in toolCallStates)
            {
                if (state.Index == index) return state;
            }
            return null;
        }

        private static IList<ModelClientToolCall> BuildAnthropicStreamedToolCalls(
            List<AnthropicStreamingToolState> toolCallStates,
            IReadOnlyDictionary<string, string> anthropicToolNames = null)
        {
            var result = new List<ModelClientToolCall>();
            if (toolCallStates == null || toolCallStates.Count == 0) return result;

            foreach (var state in toolCallStates.OrderBy(s => s.Index))
            {
                var toolName = state.Name ?? string.Empty;
                // Reverse-map sanitized name back to original
                if (anthropicToolNames != null && anthropicToolNames.TryGetValue(toolName, out var originalName))
                {
                    toolName = originalName;
                }
                var argsJson = state.InputJson.ToString();
                result.Add(new ModelClientToolCall
                {
                    Id = state.Id ?? string.Empty,
                    Name = toolName,
                    ArgumentsJson = string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson
                });
            }

            return result;
        }

        private sealed class AnthropicNormalizedMessages
        {
            public string SystemPrompt { get; set; }
            public IReadOnlyList<ModelClientMessage> Messages { get; set; }
        }

        private static AnthropicNormalizedMessages NormalizeMessagesForAnthropic(IReadOnlyList<ModelClientMessage> messages)
        {
            var sourceMessages = (messages ?? Array.Empty<ModelClientMessage>())
                .Where(m => m != null)
                .ToList();

            var systemSections = new List<string>();
            var nonSystemMessages = new List<ModelClientMessage>();

            foreach (var message in sourceMessages)
            {
                if (string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(message.Content))
                    {
                        systemSections.Add(message.Content.Trim());
                    }
                    continue;
                }

                nonSystemMessages.Add(message);
            }

            // Anthropic constraint: after an assistant message with tool_use blocks,
            // the NEXT user message must contain ALL corresponding tool_result blocks.
            // Our internal format has separate tool-role messages per result — consolidate them.
            var consolidated = new List<ModelClientMessage>();
            var pendingToolResults = new List<ModelClientMessage>();

            foreach (var message in nonSystemMessages)
            {
                var isToolResult = string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "function", StringComparison.OrdinalIgnoreCase);

                if (isToolResult)
                {
                    pendingToolResults.Add(message);
                    continue;
                }

                // Flush any pending tool results before the next non-tool message
                if (pendingToolResults.Count > 0)
                {
                    consolidated.Add(MergeToolResults(pendingToolResults));
                    pendingToolResults.Clear();
                }

                consolidated.Add(message);
            }

            // Flush remaining tool results at the end
            if (pendingToolResults.Count > 0)
            {
                consolidated.Add(MergeToolResults(pendingToolResults));
            }

            return new AnthropicNormalizedMessages
            {
                SystemPrompt = systemSections.Count > 0
                    ? string.Join(Environment.NewLine + Environment.NewLine, systemSections)
                    : null,
                Messages = consolidated
            };
        }

        /// <summary>
        /// Merges multiple consecutive tool-result messages into a single user message
        /// with multiple tool_result content blocks, as required by Anthropic's API.
        /// </summary>
        private static ModelClientMessage MergeToolResults(List<ModelClientMessage> toolMessages)
        {
            if (toolMessages == null || toolMessages.Count == 0)
            {
                return new ModelClientMessage { Role = "user", Content = string.Empty };
            }

            if (toolMessages.Count == 1)
            {
                // Single tool result — keep as-is (BuildAnthropicMessage handles the conversion)
                return toolMessages[0];
            }

            // Merge multiple tool results: combine ToolCallIds and content
            // BuildAnthropicMessage will emit one user message with multiple tool_result blocks
            var mergedToolCallIds = new List<string>();
            var mergedContent = new StringBuilder();

            foreach (var tm in toolMessages)
            {
                if (!string.IsNullOrWhiteSpace(tm.ToolCallId))
                {
                    mergedToolCallIds.Add(tm.ToolCallId);
                }

                if (!string.IsNullOrWhiteSpace(tm.Content))
                {
                    if (mergedContent.Length > 0) mergedContent.AppendLine();
                    mergedContent.Append(tm.Content);
                }
            }

            return new ModelClientMessage
            {
                Role = "tool",
                Content = mergedContent.ToString(),
                ToolCallId = string.Join(",", mergedToolCallIds)
            };
        }

        private static string NormalizeAnthropicRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role)) return "user";

            var normalized = role.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "user":
                case "assistant":
                    return normalized;
                case "tool":
                case "function":
                    return "user"; // Anthropic uses user role for tool results
                case "system":
                    return "user"; // System is handled separately
                default:
                    return "user";
            }
        }

        /// <summary>
        /// Sanitizes a tool name for Anthropic's Messages API.
        /// Anthropic requires tool names to match ^[a-zA-Z0-9_-]+$.
        /// We replace '.' (our namespace separator) with '_'.
        /// </summary>
        private static string SanitizeAnthropicToolName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unknown_tool";

            var sb = new StringBuilder(name.Length);
            foreach (var ch in name.Trim())
            {
                if (ch >= 'a' && ch <= 'z') sb.Append(ch);
                else if (ch >= 'A' && ch <= 'Z') sb.Append(ch);
                else if (ch >= '0' && ch <= '9') sb.Append(ch);
                else if (ch == '_' || ch == '-') sb.Append(ch);
                else if (ch == '.') sb.Append('_');  // namespace separator → underscore
                // Other invalid chars are simply dropped
            }

            var result = sb.ToString();
            return result.Length > 0 ? result : "unknown_tool";
        }
    }
}
