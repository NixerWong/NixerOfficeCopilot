using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    public sealed class WordFindReplaceTool : WordToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_find_replace",
            DisplayName = "Find And Replace",
            Description = "Finds and replaces text in the active Word document. Replaces all occurrences by default.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"findText\":{\"type\":\"string\",\"description\":\"Text to find.\"},\"replaceWith\":{\"type\":\"string\",\"description\":\"Replacement text.\"},\"matchCase\":{\"type\":\"boolean\",\"description\":\"Match case. Default false.\"},\"replaceAll\":{\"type\":\"boolean\",\"description\":\"Replace all occurrences. Default true.\"}},\"required\":[\"findText\",\"replaceWith\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);

                string findText;
                string replaceWith;
                bool matchCase;
                bool replaceAll;
                if (!TryReadArguments(request, out findText, out replaceWith, out matchCase, out replaceAll))
                {
                    return Task.FromResult(Fail(
                        request,
                        "findText and replaceWith are required.",
                        "word.find_replace_invalid_arguments"));
                }

                var result = host.FindReplace(new WordFindReplaceRequest
                {
                    FindText = findText,
                    ReplaceWith = replaceWith,
                    MatchCase = matchCase,
                    ReplaceAll = replaceAll
                });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Find and replace failed."
                            : result.ErrorMessage,
                        "word.find_replace_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Replaced " + result.ReplacementCount + " occurrence(s).",
                    Newtonsoft.Json.JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.find_replace_failed"));
            }
        }

        private static bool TryReadArguments(
            ToolExecutionRequest request,
            out string findText,
            out string replaceWith,
            out bool matchCase,
            out bool replaceAll)
        {
            findText = string.Empty;
            replaceWith = string.Empty;
            matchCase = false;
            replaceAll = true;

            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ArgumentsJson))
                {
                    return false;
                }

                var json = JObject.Parse(request.ArgumentsJson);

                var findToken = json["findText"];
                var replaceToken = json["replaceWith"];
                if (findToken == null || replaceToken == null)
                {
                    return false;
                }

                findText = findToken.ToString() ?? string.Empty;
                replaceWith = replaceToken.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(findText))
                {
                    return false;
                }

                var matchCaseToken = json["matchCase"];
                if (matchCaseToken != null)
                {
                    bool parsed;
                    if (bool.TryParse(matchCaseToken.ToString(), out parsed))
                    {
                        matchCase = parsed;
                    }
                }

                var replaceAllToken = json["replaceAll"];
                if (replaceAllToken != null)
                {
                    bool parsed;
                    if (bool.TryParse(replaceAllToken.ToString(), out parsed))
                    {
                        replaceAll = parsed;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
