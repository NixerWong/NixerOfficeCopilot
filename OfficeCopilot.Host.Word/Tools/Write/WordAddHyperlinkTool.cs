using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Inserts a hyperlink at the current selection.</summary>
    public sealed class WordAddHyperlinkTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_add_hyperlink",
            DisplayName = "Add Hyperlink",
            Description = "Inserts a hyperlink at the current selection (text becomes the link; selection text is used when text is omitted).",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"address\":{\"type\":\"string\",\"description\":\"URL or file path.\"},\"text\":{\"type\":\"string\",\"description\":\"Optional link text. Omit to use the selection text.\"},\"subAddress\":{\"type\":\"string\",\"description\":\"Optional bookmark within the document.\"}},\"required\":[\"address\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);
                var address = ReadString(args, "address", null);

                if (string.IsNullOrWhiteSpace(address))
                {
                    return Task.FromResult(Fail(request, "address is required.", "word.add_hyperlink_invalid_arguments"));
                }

                var result = host.AddHyperlink(new WordAddHyperlinkRequest
                {
                    Address = address,
                    Text = ReadString(args, "text", null),
                    SubAddress = ReadString(args, "subAddress", null)
                });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to add hyperlink."
                            : result.ErrorMessage,
                        "word.add_hyperlink_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Hyperlink added: " + address,
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.add_hyperlink_failed"));
            }
        }
    }
}
