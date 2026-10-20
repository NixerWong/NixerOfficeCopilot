using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Adds a bookmark to the current selection.</summary>
    public sealed class WordAddBookmarkTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_add_bookmark",
            DisplayName = "Add Bookmark",
            Description = "Adds a bookmark (by name) around the current selection in the active Word document.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"Bookmark name.\"}},\"required\":[\"name\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);
                var name = ReadString(args, "name", null);

                if (string.IsNullOrWhiteSpace(name))
                {
                    return Task.FromResult(Fail(request, "name is required.", "word.add_bookmark_invalid_arguments"));
                }

                var result = host.AddBookmark(new WordAddBookmarkRequest { Name = name });
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to add bookmark."
                            : result.ErrorMessage,
                        "word.add_bookmark_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Bookmark added: " + result.BookmarkName,
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.add_bookmark_failed"));
            }
        }
    }
}
