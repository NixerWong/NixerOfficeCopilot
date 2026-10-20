using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Adds a comment to the current selection.</summary>
    public sealed class WordAddCommentTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_add_comment",
            DisplayName = "Add Comment",
            Description = "Adds a comment anchored to the current selection in the active Word document.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"description\":\"Comment text.\"}},\"required\":[\"text\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);
                var text = ReadString(args, "text", null);

                if (string.IsNullOrWhiteSpace(text))
                {
                    return Task.FromResult(Fail(request, "text is required.", "word.add_comment_invalid_arguments"));
                }

                var result = host.AddComment(new WordAddCommentRequest { Text = text });
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to add comment."
                            : result.ErrorMessage,
                        "word.add_comment_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Comment added. Total comments: " + result.CommentCount,
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.add_comment_failed"));
            }
        }
    }
}
