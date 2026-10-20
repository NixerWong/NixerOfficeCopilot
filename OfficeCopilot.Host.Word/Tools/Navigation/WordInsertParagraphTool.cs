using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Inserts one or more paragraph breaks at the current cursor position.</summary>
    public sealed class WordInsertParagraphTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_insert_paragraph",
            DisplayName = "Insert Paragraph Break",
            Description = "Inserts one or more paragraph breaks at the current cursor position. Use this to start a new paragraph or leave an empty line between sections. Note: when the cursor is inside a table cell this starts a new paragraph within that cell; use word_move_to_end first to escape a table.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\",\"description\":\"Number of paragraph breaks to insert. Default 1. Capped at 100.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var result = host.InsertParagraph(new WordInsertParagraphRequest
                {
                    Count = ReadInt(args, "count", 1)
                });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to insert paragraph break."
                            : result.ErrorMessage,
                        "word.insert_paragraph_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Inserted " + result.InsertedParagraphs + " paragraph break(s). Cursor at position " + result.StartPosition + ".",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.insert_paragraph_failed"));
            }
        }
    }
}
