using System;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Base for COM (active-document) tools with shared argument parsing.</summary>
    public abstract class WordComToolBase : WordToolBase
    {
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

        protected static bool? ReadNullableBool(JObject args, string key)
        {
            if (args == null || args[key] == null)
            {
                return null;
            }

            bool value;
            if (bool.TryParse(args[key].ToString(), out value))
            {
                return value;
            }

            return null;
        }

        protected static double? ReadNullableDouble(JObject args, string key)
        {
            if (args == null || args[key] == null)
            {
                return null;
            }

            double value;
            if (double.TryParse(args[key].ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return null;
        }
    }
}
