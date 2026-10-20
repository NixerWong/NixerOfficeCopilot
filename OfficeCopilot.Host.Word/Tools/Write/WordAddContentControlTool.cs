using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Adds a content control at the current selection.</summary>
    public sealed class WordAddContentControlTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_add_content_control",
            DisplayName = "Add Content Control",
            Description = "Adds a content control (text, richText, checkbox, dropdown, combo, date) at the current selection, with optional title and tag.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\",\"description\":\"text | richText | checkbox | dropdown | combo | date. Default text.\"},\"title\":{\"type\":\"string\"},\"tag\":{\"type\":\"string\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var result = host.AddContentControl(new WordAddContentControlRequest
                {
                    Type = ReadString(args, "type", "text"),
                    Title = ReadString(args, "title", null),
                    Tag = ReadString(args, "tag", null)
                });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to add content control."
                            : result.ErrorMessage,
                        "word.add_content_control_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Content control added.",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.add_content_control_failed"));
            }
        }
    }
}
