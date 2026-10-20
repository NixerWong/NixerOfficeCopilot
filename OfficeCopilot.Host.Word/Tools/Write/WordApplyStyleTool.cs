using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Applies a style by name to the current selection.</summary>
    public sealed class WordApplyStyleTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_apply_style",
            DisplayName = "Apply Style",
            Description = "Applies a paragraph or character style to the current selection. Built-in ids (Title, Heading1..Heading9, Normal, ...) and Chinese names (标题 1, 正文) work in every Word language; custom styles use the name from word_list_styles. Select the target first with word_select_paragraph or word_select_document. To style many paragraphs, use word_format_paragraphs instead.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"styleName\":{\"type\":\"string\",\"description\":\"Style name or built-in style name.\"}},\"required\":[\"styleName\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);
                var styleName = ReadString(args, "styleName", null);

                if (string.IsNullOrWhiteSpace(styleName))
                {
                    return Task.FromResult(Fail(request, "styleName is required.", "word.apply_style_invalid_arguments"));
                }

                var result = host.ApplyStyle(new WordApplyStyleRequest { StyleName = styleName });
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to apply style."
                            : result.ErrorMessage,
                        "word.apply_style_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Applied style: " + result.AppliedStyle,
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.apply_style_failed"));
            }
        }
    }
}
