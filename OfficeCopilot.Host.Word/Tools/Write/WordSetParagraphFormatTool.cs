using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Applies paragraph formatting to the current selection.</summary>
    public sealed class WordSetParagraphFormatTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_set_paragraph_format",
            DisplayName = "Set Paragraph Format",
            Description = "Applies paragraph formatting (alignment, line spacing, spacing before/after, indentation) to the paragraphs in the current selection. Select the target paragraphs first with word_select_paragraph / word_select_document.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"alignment\":{\"type\":\"string\",\"description\":\"left | center | right | justify.\"},\"lineSpacingRule\":{\"type\":\"string\",\"description\":\"single | 1.5 | double | atLeast | exactly | multiple.\"},\"lineSpacing\":{\"type\":\"number\"},\"spaceBefore\":{\"type\":\"number\",\"description\":\"Points.\"},\"spaceAfter\":{\"type\":\"number\",\"description\":\"Points.\"},\"firstLineIndent\":{\"type\":\"number\",\"description\":\"Points.\"},\"leftIndent\":{\"type\":\"number\",\"description\":\"Points.\"},\"rightIndent\":{\"type\":\"number\",\"description\":\"Points.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var req = new WordParagraphFormatRequest
                {
                    Alignment = ReadString(args, "alignment", null),
                    LineSpacingRule = ReadString(args, "lineSpacingRule", null),
                    LineSpacing = ReadNullableDouble(args, "lineSpacing"),
                    SpaceBefore = ReadNullableDouble(args, "spaceBefore"),
                    SpaceAfter = ReadNullableDouble(args, "spaceAfter"),
                    FirstLineIndent = ReadNullableDouble(args, "firstLineIndent"),
                    LeftIndent = ReadNullableDouble(args, "leftIndent"),
                    RightIndent = ReadNullableDouble(args, "rightIndent")
                };

                var result = host.SetParagraphFormat(req);
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to set paragraph format."
                            : result.ErrorMessage,
                        "word.paragraph_format_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Paragraph format applied.",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.paragraph_format_failed"));
            }
        }
    }
}
