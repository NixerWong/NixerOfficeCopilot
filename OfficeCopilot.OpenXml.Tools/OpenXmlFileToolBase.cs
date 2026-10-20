using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>
    /// Base for format-agnostic file tools. Operates purely on file paths via
    /// the Open XML engines — no Office host dependency.
    /// </summary>
    public abstract class OpenXmlFileToolBase : IAgentTool
    {
        public abstract ToolDefinition Definition { get; }
        public abstract Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request);

        protected static JObject ParseArguments(ToolExecutionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ArgumentsJson))
            {
                return new JObject();
            }

            try
            {
                return JObject.Parse(request.ArgumentsJson) ?? new JObject();
            }
            catch
            {
                return new JObject();
            }
        }

        protected static string ReadString(JObject args, string key, string fallback)
        {
            if (args == null)
            {
                return fallback;
            }

            var token = args[key];
            return token == null ? fallback : token.ToString();
        }

        protected static int ReadInt(JObject args, string key, int fallback)
        {
            var raw = ReadString(args, key, null);
            if (raw == null)
            {
                return fallback;
            }

            int value;
            return int.TryParse(raw, out value) ? value : fallback;
        }

        protected static bool ReadBool(JObject args, string key, bool fallback)
        {
            var raw = ReadString(args, key, null);
            if (raw == null)
            {
                return fallback;
            }

            bool value;
            return bool.TryParse(raw, out value) ? value : fallback;
        }

        protected static List<string> ReadStringArray(JObject args, string key)
        {
            var result = new List<string>();
            var token = args == null ? null : args[key] as JArray;
            if (token == null)
            {
                return result;
            }

            foreach (var item in token)
            {
                result.Add(item == null ? string.Empty : item.ToString());
            }
            return result;
        }

        protected static List<IReadOnlyList<string>> ReadStringArrayArray(JObject args, string key)
        {
            var result = new List<IReadOnlyList<string>>();
            var token = args == null ? null : args[key] as JArray;
            if (token == null)
            {
                return result;
            }

            foreach (var rowToken in token)
            {
                var row = new List<string>();
                var rowArray = rowToken as JArray;
                if (rowArray != null)
                {
                    foreach (var cellToken in rowArray)
                    {
                        row.Add(cellToken == null ? string.Empty : cellToken.ToString());
                    }
                }
                result.Add(row);
            }
            return result;
        }

        protected static List<double> ReadDoubleArray(JObject args, string key)
        {
            var result = new List<double>();
            var token = args == null ? null : args[key] as JArray;
            if (token == null)
            {
                return result;
            }

            foreach (var item in token)
            {
                double value;
                if (double.TryParse(item == null ? string.Empty : item.ToString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    result.Add(value);
                }
            }
            return result;
        }

        protected ToolExecutionResult Success(ToolExecutionRequest req, string content, string json = null)
            => ToolExecutionResult.CreateSuccess(Definition.Name, req == null ? string.Empty : req.ToolCallId, content, json);

        protected ToolExecutionResult Fail(ToolExecutionRequest req, string msg, string code = null)
            => ToolExecutionResult.CreateFailure(Definition.Name, req == null ? string.Empty : req.ToolCallId, msg, code);
    }
}
