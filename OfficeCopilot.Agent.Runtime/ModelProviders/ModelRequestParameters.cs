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
    internal static class ModelRequestParameters
    {
        internal static void ApplyAdditionalRequestParameters(
            JsonObject requestBody,
            IDictionary<string, object> additionalParameters,
            params string[] excludedKeys)
        {
            if (requestBody == null || additionalParameters == null)
            {
                return;
            }

            foreach (var pair in additionalParameters)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || ShouldSkipAdditionalParameter(pair.Key, excludedKeys))
                {
                    continue;
                }

                requestBody[pair.Key] = pair.Value == null
                    ? null
                    : JsonSerializer.SerializeToNode(pair.Value);
            }
        }

        internal static bool TryReadDoubleParameter(
            IDictionary<string, object> values,
            out double result,
            params string[] keys)
        {
            object value;
            if (!TryReadParameterValue(values, keys, out value))
            {
                result = 0d;
                return false;
            }

            if (value is double)
            {
                result = (double)value;
                return true;
            }

            if (value is float || value is decimal || value is int || value is long || value is short || value is byte)
            {
                result = Convert.ToDouble(value);
                return true;
            }

            return double.TryParse(Convert.ToString(value), out result);
        }

        internal static bool TryReadIntParameter(
            IDictionary<string, object> values,
            out int result,
            params string[] keys)
        {
            object value;
            if (!TryReadParameterValue(values, keys, out value))
            {
                result = 0;
                return false;
            }

            if (value is int)
            {
                result = (int)value;
                return true;
            }

            if (value is long || value is short || value is byte)
            {
                result = Convert.ToInt32(value);
                return true;
            }

            return int.TryParse(Convert.ToString(value), out result);
        }

        internal static bool TryReadBooleanParameter(
            IDictionary<string, object> values,
            out bool result,
            params string[] keys)
        {
            object value;
            if (!TryReadParameterValue(values, keys, out value))
            {
                result = false;
                return false;
            }

            if (value is bool)
            {
                result = (bool)value;
                return true;
            }

            return bool.TryParse(Convert.ToString(value), out result);
        }

        internal static bool TryReadParameterValue(
            IDictionary<string, object> values,
            IEnumerable<string> keys,
            out object value)
        {
            value = null;
            if (values == null || keys == null)
            {
                return false;
            }

            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (values.TryGetValue(key, out value))
                {
                    return true;
                }
            }

            return false;
        }

        internal static IReadOnlyList<ModelClientMessage> NormalizeMessagesForRequest(IReadOnlyList<ModelClientMessage> messages)
        {
            var sourceMessages = (messages ?? Array.Empty<ModelClientMessage>())
                .Where(message => message != null)
                .ToList();

            if (sourceMessages.Count == 0)
            {
                return sourceMessages;
            }

            var systemSections = new List<string>();
            var normalizedMessages = new List<ModelClientMessage>();

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

                normalizedMessages.Add(message);
            }

            if (systemSections.Count == 0)
            {
                return sourceMessages;
            }

            normalizedMessages.Insert(0, new ModelClientMessage
            {
                Role = "system",
                Content = string.Join(Environment.NewLine + Environment.NewLine, systemSections)
            });

            return normalizedMessages;
        }

        // 本地能力标记，只供运行时判断，绝不能泄漏到供应商请求体。
        private static readonly string[] AlwaysExcludedParameterKeys =
        {
            AgentModelAdditionalParameterNames.SupportsImageInput,
            "supports_image_input",
            "supportsImageInput"
        };

        internal static bool ShouldSkipAdditionalParameter(string key, IEnumerable<string> excludedKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            foreach (var internalKey in AlwaysExcludedParameterKeys)
            {
                if (string.Equals(key, internalKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (excludedKeys == null)
            {
                return false;
            }

            foreach (var excludedKey in excludedKeys)
            {
                if (string.Equals(key, excludedKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the user's "thinking" setting from AdditionalParameters and applies it to the request body.
        /// Accepted values:
        ///   - "enabled"  → {"type":"enabled","budget_tokens":auto}
        ///   - "disabled" → {"type":"disabled"}
        ///   - JSON object → passed through as-is (e.g. {"type":"enabled","budget_tokens":8000})
        /// Returns true if the user provided an explicit thinking config; false to use auto-detection.
        /// </summary>
        internal static bool TryApplyUserThinkingConfig(JsonObject requestBody, AgentModelOptions modelOptions)
        {
            if (modelOptions?.AdditionalParameters == null) return false;

            object thinkingValue;
            if (!modelOptions.AdditionalParameters.TryGetValue("thinking", out thinkingValue) || thinkingValue == null)
                return false;

            if (thinkingValue is JsonObject thinkingObj)
            {
                // User provided a full JSON object — use as-is
                requestBody["thinking"] = thinkingObj;
                return true;
            }

            var thinkingStr = (thinkingValue as string) ?? thinkingValue.ToString();
            if (string.IsNullOrWhiteSpace(thinkingStr)) return false;

            if (string.Equals(thinkingStr, "enabled", StringComparison.OrdinalIgnoreCase))
            {
                var maxTokensValue = requestBody["max_tokens"] != null ? (int)requestBody["max_tokens"] : 16384;
                var thinkingBudget = Math.Min(Math.Max(maxTokensValue / 2, 1024), 32768);
                requestBody["thinking"] = new JsonObject
                {
                    ["type"] = "enabled",
                    ["budget_tokens"] = thinkingBudget
                };
                return true;
            }

            if (string.Equals(thinkingStr, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                requestBody["thinking"] = new JsonObject { ["type"] = "disabled" };
                return true;
            }

            // Unknown value — treat as JSON string and try to parse
            try
            {
                var parsed = JsonNode.Parse(thinkingStr);
                if (parsed is JsonObject parsedObj)
                {
                    requestBody["thinking"] = parsedObj;
                    return true;
                }
            }
            catch { }

            return false;
        }
    }
}
