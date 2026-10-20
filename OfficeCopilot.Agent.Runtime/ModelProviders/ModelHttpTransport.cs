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
    internal static class ModelHttpTransport
    {
        internal static readonly HttpClient s_httpClient = CreateHttpClient();
        internal static readonly AgentRuntimeTranscriptLogger s_transcriptLogger = new AgentRuntimeTranscriptLogger();
        internal static readonly JsonSerializerOptions s_requestJsonSerializerOptions = CreateRequestJsonSerializerOptions();

        internal static async Task<string> SendJsonAsync(
            string transcriptCategory,
            string endpoint,
            string requestJson,
            AgentModelOptions modelOptions,
            CancellationToken cancellationToken,
            string errorMessage)
        {
            s_transcriptLogger.WriteRequest(modelOptions, transcriptCategory, endpoint, "application/json", requestJson);

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                ApplyProviderHeaders(request, modelOptions);

                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(15, modelOptions.GetRequestTimeoutSeconds())));

                try
                {
                    using (var response = await s_httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeoutCts.Token).ConfigureAwait(false))
                    {
                        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        s_transcriptLogger.WriteResponse(
                            modelOptions,
                            response.IsSuccessStatusCode ? transcriptCategory : transcriptCategory + ".error",
                            endpoint,
                            "application/json",
                            responseText);

                        if (!response.IsSuccessStatusCode)
                        {
                            throw CreateModelRequestException(errorMessage, response.StatusCode, responseText);
                        }

                        return responseText;
                    }
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    s_transcriptLogger.WriteException(modelOptions, transcriptCategory + ".error", endpoint, ex);
                    throw new TimeoutException(errorMessage + " Request timed out.", ex);
                }
                catch (HttpRequestException ex)
                {
                    s_transcriptLogger.WriteException(modelOptions, transcriptCategory + ".error", endpoint, ex);
                    throw new InvalidOperationException(errorMessage, ex);
                }
            }
        }

        internal static void ApplyProviderHeaders(HttpRequestMessage request, AgentModelOptions modelOptions)
        {
            if (request == null || modelOptions == null)
            {
                return;
            }

            if (string.Equals(modelOptions.ProviderKind, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(modelOptions.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("api-key", modelOptions.ApiKey.Trim());
                }

                ApplyCustomHeaders(request, modelOptions.HttpHeaders);
                return;
            }

            if (string.Equals(modelOptions.ProviderKind, "Anthropic", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(modelOptions.ApiKey))
                {
                    request.Headers.TryAddWithoutValidation("x-api-key", modelOptions.ApiKey.Trim());
                }

                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

                ApplyCustomHeaders(request, modelOptions.HttpHeaders);
                return;
            }

            if (!string.IsNullOrWhiteSpace(modelOptions.ApiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + modelOptions.ApiKey.Trim());
            }

            ApplyCustomHeaders(request, modelOptions.HttpHeaders);
        }

        internal static void ApplyCustomHeaders(HttpRequestMessage request, IDictionary<string, string> headers)
        {
            if (request == null || headers == null || headers.Count == 0)
            {
                return;
            }

            foreach (var pair in headers)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                var headerName = pair.Key.Trim();
                var headerValue = pair.Value.Trim();

                if (string.Equals(headerName, "Accept", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(headerName, "Content-Type", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(headerName, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(headerName, "Connection", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(headerName, "Expect", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                request.Headers.Remove(headerName);
                if (request.Content != null)
                {
                    request.Content.Headers.Remove(headerName);
                }

                if (!request.Headers.TryAddWithoutValidation(headerName, headerValue)
                    && request.Content != null)
                {
                    request.Content.Headers.TryAddWithoutValidation(headerName, headerValue);
                }
            }
        }

        internal static Exception CreateModelRequestException(string message, HttpStatusCode statusCode, string errorBody)
        {
            if (string.IsNullOrWhiteSpace(errorBody))
            {
                return new InvalidOperationException(message + " HTTP " + ((int)statusCode).ToString() + " (" + statusCode.ToString() + ").");
            }

            return new InvalidOperationException(
                message + " HTTP " + ((int)statusCode).ToString() + " (" + statusCode.ToString() + "). " + errorBody);
        }

        internal static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = Timeout.InfiniteTimeSpan;
            return client;
        }

        internal static string SerializeRequestJson(JsonNode node)
        {
            return node == null
                ? "{}"
                : node.ToJsonString(s_requestJsonSerializerOptions);
        }

        internal static JsonSerializerOptions CreateRequestJsonSerializerOptions()
        {
            return new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            };
        }

        // ── Anthropic streaming helpers ──
    }
}
