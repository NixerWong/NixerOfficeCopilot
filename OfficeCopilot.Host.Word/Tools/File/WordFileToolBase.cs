using System;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Word;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>
    /// Base for file-level (Open XML SDK) Word tools. These operate on .docx
    /// files on disk via <see cref="WordDocumentEngine"/>, independent of the
    /// COM host except when defaulting a path to the active document.
    /// </summary>
    public abstract class WordFileToolBase : WordToolBase
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

        protected static WordDocumentEngine OpenEngine(string filePath, bool editable)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A file path is required.");
            }

            return new WordDocumentEngine(filePath, editable);
        }

        /// <summary>Returns the given path, or the active document's full path when omitted.</summary>
        protected static string ResolveFilePath(ToolExecutionContext context, string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                return filePath;
            }

            var host = GetHost(context);
            var info = host.GetActiveDocumentInfo();
            if (info == null || !info.IsAvailable || string.IsNullOrWhiteSpace(info.FullName))
            {
                throw new InvalidOperationException("No file path given and no active Word document to default to.");
            }

            return info.FullName;
        }
    }
}
