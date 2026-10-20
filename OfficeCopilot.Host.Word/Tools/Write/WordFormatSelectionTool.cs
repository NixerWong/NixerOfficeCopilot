using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Applies character formatting to the selection, or to find matches.</summary>
    public sealed class WordFormatSelectionTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_format_selection",
            DisplayName = "Format Selection",
            Description = "Applies character formatting (bold, italic, underline, color, size, font, highlight, subscript, superscript) to the current selection, or to find matches when findText is given. Select text first with word_select_paragraph / word_select_document to format a specific range. Do not set size on the whole document (word_select_document): direct size overrides the Title/Heading style sizes and flattens the heading hierarchy; set size per paragraph instead.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"bold\":{\"type\":\"boolean\"},\"italic\":{\"type\":\"boolean\"},\"underline\":{\"type\":\"boolean\"},\"color\":{\"type\":\"string\",\"description\":\"Hex color, e.g. FF0000.\"},\"size\":{\"type\":\"number\",\"description\":\"Font size in points.\"},\"fontName\":{\"type\":\"string\"},\"highlightColor\":{\"type\":\"string\",\"description\":\"Hex highlight color.\"},\"subscript\":{\"type\":\"boolean\"},\"superscript\":{\"type\":\"boolean\"},\"findText\":{\"type\":\"string\",\"description\":\"Optional text to find; formatting applies to matches instead of the selection.\"},\"replaceAll\":{\"type\":\"boolean\",\"description\":\"Format all matches. Default true.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var req = new WordFormatSelectionRequest
                {
                    Bold = ReadNullableBool(args, "bold"),
                    Italic = ReadNullableBool(args, "italic"),
                    Underline = ReadNullableBool(args, "underline"),
                    Color = ReadString(args, "color", null),
                    Size = ReadNullableDouble(args, "size"),
                    FontName = ReadString(args, "fontName", null),
                    HighlightColor = ReadString(args, "highlightColor", null),
                    Subscript = ReadNullableBool(args, "subscript"),
                    Superscript = ReadNullableBool(args, "superscript"),
                    FindText = ReadString(args, "findText", null),
                    ReplaceAll = ReadBool(args, "replaceAll", true)
                };

                var result = host.FormatSelection(req);
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to format selection."
                            : result.ErrorMessage,
                        "word.format_selection_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Formatted " + result.AppliedCount + " location(s).",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.format_selection_failed"));
            }
        }
    }
}
