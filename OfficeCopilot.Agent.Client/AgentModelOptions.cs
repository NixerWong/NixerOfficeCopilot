using System;
using System.Collections.Generic;
using System.Globalization;

namespace OfficeCopilot.Agent.Client
{
    public sealed class AgentModelOptions
    {
        public AgentModelOptions()
        {
            ProviderKind = string.Empty;
            Endpoint = string.Empty;
            ApiKey = string.Empty;
            ModelId = string.Empty;
            SystemPrompt = string.Empty;
            AdditionalParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            HttpHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ProviderKind { get; set; }

        public string Endpoint { get; set; }

        public string ApiKey { get; set; }

        public string ModelId { get; set; }

        public int MaxContextTokens { get; set; }

        public Dictionary<string, string> HttpHeaders { get; set; }

        public Dictionary<string, object> AdditionalParameters { get; set; }

        public string SystemPrompt { get; set; }
    }

    public static class AgentModelAdditionalParameterNames
    {
        public const string SupportsFunctionCalling = "SupportsFunctionCalling";
        public const string SupportsImageInput = "SupportsImageInput";
        public const string SupportsDeepThinking = "SupportsDeepThinking";
        public const string RequestTimeoutSeconds = "RequestTimeoutSeconds";
        public const string Temperature = "Temperature";
        public const string TopP = "TopP";
        public const string MaxTokens = "MaxTokens";
        public const string ModelTranscriptEnabled = "ModelTranscriptEnabled";
        public const string ModelTranscriptMode = "ModelTranscriptMode";
        public const string ModelTranscriptFilePath = "ModelTranscriptFilePath";
    }

    public static class AgentModelOptionsExtensions
    {
        private static readonly string[] SupportsFunctionCallingKeys =
        {
            AgentModelAdditionalParameterNames.SupportsFunctionCalling,
            "supports_function_calling",
            "supportsFunctionCalling"
        };

        private static readonly string[] SupportsImageInputKeys =
        {
            AgentModelAdditionalParameterNames.SupportsImageInput,
            "supports_image_input",
            "supportsImageInput"
        };

        private static readonly string[] SupportsDeepThinkingKeys =
        {
            AgentModelAdditionalParameterNames.SupportsDeepThinking,
            "supports_deep_thinking",
            "supportsDeepThinking",
            "enable_thinking",
            "EnableThinking",
            "think"
        };

        private static readonly string[] RequestTimeoutSecondsKeys =
        {
            AgentModelAdditionalParameterNames.RequestTimeoutSeconds,
            "request_timeout_seconds",
            "requestTimeoutSeconds"
        };

        private static readonly string[] TemperatureKeys =
        {
            AgentModelAdditionalParameterNames.Temperature,
            "temperature"
        };

        private static readonly string[] TopPKeys =
        {
            AgentModelAdditionalParameterNames.TopP,
            "top_p",
            "topP"
        };

        private static readonly string[] MaxTokensKeys =
        {
            AgentModelAdditionalParameterNames.MaxTokens,
            "max_tokens",
            "maxTokens"
        };

        public static IDictionary<string, object> EnsureAdditionalParameters(this AgentModelOptions modelOptions)
        {
            if (modelOptions == null)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            if (modelOptions.AdditionalParameters == null)
            {
                modelOptions.AdditionalParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            return modelOptions.AdditionalParameters;
        }

        public static bool GetSupportsFunctionCalling(this AgentModelOptions modelOptions)
        {
            bool result;
            return TryReadBoolean(modelOptions == null ? null : modelOptions.AdditionalParameters, SupportsFunctionCallingKeys, out result)
                ? result
                : true;
        }

        /// <summary>模型是否接受图像输入；未声明时视为不支持，工具图像只以文字说明给出。</summary>
        public static bool GetSupportsImageInput(this AgentModelOptions modelOptions)
        {
            bool result;
            return TryReadBoolean(modelOptions == null ? null : modelOptions.AdditionalParameters, SupportsImageInputKeys, out result)
                && result;
        }

        public static bool GetSupportsDeepThinking(this AgentModelOptions modelOptions)
        {
            bool result;
            return TryReadBoolean(modelOptions == null ? null : modelOptions.AdditionalParameters, SupportsDeepThinkingKeys, out result)
                ? result
                : false;
        }

        public static int GetRequestTimeoutSeconds(this AgentModelOptions modelOptions)
        {
            int result;
            return TryReadInt(modelOptions == null ? null : modelOptions.AdditionalParameters, RequestTimeoutSecondsKeys, out result) && result > 0
                ? result
                : 60;
        }

        public static double GetTemperature(this AgentModelOptions modelOptions)
        {
            double result;
            return TryReadDouble(modelOptions == null ? null : modelOptions.AdditionalParameters, TemperatureKeys, out result)
                ? result
                : 0.7d;
        }

        public static double GetTopP(this AgentModelOptions modelOptions)
        {
            double result;
            return TryReadDouble(modelOptions == null ? null : modelOptions.AdditionalParameters, TopPKeys, out result)
                ? result
                : 1.0d;
        }

        public static int GetMaxTokens(this AgentModelOptions modelOptions)
        {
            int result;
            return TryReadInt(modelOptions == null ? null : modelOptions.AdditionalParameters, MaxTokensKeys, out result) && result > 0
                ? result
                : 8192;
        }

        private static bool TryReadDouble(IDictionary<string, object> values, IEnumerable<string> keys, out double result)
        {
            object value;
            if (!TryReadValue(values, keys, out value))
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
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }

            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryReadInt(IDictionary<string, object> values, IEnumerable<string> keys, out int result)
        {
            object value;
            if (!TryReadValue(values, keys, out value))
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
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }

            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryReadBoolean(IDictionary<string, object> values, IEnumerable<string> keys, out bool result)
        {
            object value;
            if (!TryReadValue(values, keys, out value))
            {
                result = false;
                return false;
            }

            if (value is bool)
            {
                result = (bool)value;
                return true;
            }

            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result);
        }

        private static bool TryReadValue(IDictionary<string, object> values, IEnumerable<string> keys, out object value)
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
    }
}
