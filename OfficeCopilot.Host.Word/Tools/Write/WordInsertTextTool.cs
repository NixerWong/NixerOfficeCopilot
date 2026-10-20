using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    public sealed class WordInsertTextTool : WordToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_insert_text",
            DisplayName = "Insert Text",
            Description = "Inserts plain text at the cursor, replaces the current selection when replaceSelection is true, or (when insertAtEnd is true) appends at the end of the document. Returns the new cursor position. This is raw Word text: do NOT pass Markdown syntax such as **bold** or # headings - use word_apply_style / word_format_selection / word_set_paragraph_format for formatting instead.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"description\":\"The plain text to insert. Do not include Markdown markers.\"},\"replaceSelection\":{\"type\":\"boolean\",\"description\":\"If true, replace the current selection instead of inserting at the cursor. Default false.\"},\"insertAtEnd\":{\"type\":\"boolean\",\"description\":\"If true, move the cursor to the end of the document first. Default false.\"}},\"required\":[\"text\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var text = ReadText(request);
                if (string.IsNullOrEmpty(text))
                {
                    return Task.FromResult(Fail(request, "Text to insert cannot be empty.", "word.insert_text_empty"));
                }

                var replaceSelection = ReadReplaceSelection(request);
                var result = host.InsertText(new WordInsertTextRequest
                {
                    Text = text,
                    ReplaceSelection = replaceSelection
                });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to insert text."
                            : result.ErrorMessage,
                        "word.insert_text_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Inserted " + result.InsertedCharacters + " characters. Cursor at position " + result.EndPosition + ".",
                    Newtonsoft.Json.JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.insert_text_failed"));
            }
        }

        private static string ReadText(ToolExecutionRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ArgumentsJson))
                {
                    return string.Empty;
                }

                var json = JObject.Parse(request.ArgumentsJson);
                var token = json["text"];
                return token == null ? string.Empty : token.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ReadReplaceSelection(ToolExecutionRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ArgumentsJson))
                {
                    return false;
                }

                var json = JObject.Parse(request.ArgumentsJson);
                var token = json["replaceSelection"];
                if (token == null)
                {
                    return false;
                }

                bool value;
                return bool.TryParse(token.ToString(), out value) && value;
            }
            catch
            {
                return false;
            }
        }
    }
}
