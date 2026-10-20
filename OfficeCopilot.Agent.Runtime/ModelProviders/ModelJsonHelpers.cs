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
    internal static class ModelJsonHelpers
    {
        internal static JsonObject BuildOpenAiCompatibleTool(ModelClientTool tool)
        {
            JsonNode parameters;
            try
            {
                parameters = string.IsNullOrWhiteSpace(tool == null ? null : tool.InputSchemaJson)
                    ? new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                    : JsonNode.Parse(tool.InputSchemaJson);
            }
            catch
            {
                parameters = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                };
            }

            return new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool == null ? string.Empty : tool.Name ?? string.Empty,
                    ["description"] = tool == null ? string.Empty : tool.Description ?? string.Empty,
                    ["parameters"] = parameters
                }
            };
        }

        internal static string ExtractTextContentFromParts(IList<ModelClientMessageContentPart> contentParts)
        {
            if (contentParts == null || contentParts.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var part in contentParts)
            {
                if (part == null)
                {
                    continue;
                }

                if (string.Equals(part.Type, "input_text", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(part.Type, "text", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(part.Text ?? string.Empty);
                }
            }

            return builder.ToString();
        }

        internal static JsonObject BuildToolCallNode(ModelClientToolCall toolCall)
        {
            return new JsonObject
            {
                ["id"] = toolCall == null ? string.Empty : toolCall.Id ?? string.Empty,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = toolCall == null ? string.Empty : toolCall.Name ?? string.Empty,
                    ["arguments"] = toolCall == null ? "{}" : toolCall.ArgumentsJson ?? "{}"
                }
            };
        }

        internal static string NormalizeStreamedArgumentsJson(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        }

        internal static int GetInt32(JsonNode node)
        {
            if (node == null)
            {
                return 0;
            }

            try
            {
                return node.GetValue<int>();
            }
            catch
            {
                int value;
                return int.TryParse(GetString(node), out value) ? value : 0;
            }
        }

        internal static bool GetBoolean(JsonNode node)
        {
            if (node == null)
            {
                return false;
            }

            try
            {
                return node.GetValue<bool>();
            }
            catch
            {
                bool value;
                return bool.TryParse(GetString(node), out value) && value;
            }
        }

        internal static string NormalizeTextNode(JsonNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            if (node is JsonValue nullValue
                && nullValue.TryGetValue<object>(out var rawValue)
                && rawValue == null)
            {
                return string.Empty;
            }

            if (node is JsonValue)
            {
                return GetString(node);
            }

            var array = node as JsonArray;
            if (array != null)
            {
                var builder = new StringBuilder();
                foreach (var item in array)
                {
                    var text = GetString(item?["text"]);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        builder.Append(text);
                    }
                }

                return builder.ToString();
            }

            return node.ToJsonString();
        }

        internal static string NormalizeArgumentsJson(JsonNode node)
        {
            if (node == null)
            {
                return "{}";
            }

            if (node is JsonValue)
            {
                var stringValue = GetString(node);
                return string.IsNullOrWhiteSpace(stringValue) ? "{}" : stringValue;
            }

            return node.ToJsonString();
        }

        internal static string GetString(JsonNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            if (node is JsonValue nullValue
                && nullValue.TryGetValue<object>(out var rawValue)
                && rawValue == null)
            {
                return string.Empty;
            }

            try
            {
                return node.GetValue<string>() ?? string.Empty;
            }
            catch
            {
                return node.ToJsonString();
            }
        }

        internal static string BuildSyntheticToolCallId(string toolName, int index)
        {
            var safeName = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName.Trim();
            return "ollama_" + safeName + "_" + index.ToString();
        }
    }
}
