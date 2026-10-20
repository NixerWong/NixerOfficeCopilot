using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Selects a single paragraph by its 1-based index.</summary>
    public sealed class WordSelectParagraphTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_select_paragraph",
            DisplayName = "Select Paragraph",
            Description = "Selects a single paragraph by its 1-based index in the document. Use this to position the cursor at a specific paragraph, or to apply a style or formatting to one specific paragraph (with word_apply_style, word_set_paragraph_format or word_format_selection). Returns the selected paragraph text.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            // 仅改变选区，不修改文档内容，因此视为只读（不需确认、不需备份）。
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"paragraphIndex\":{\"type\":\"integer\",\"description\":\"1-based paragraph index. Default 1.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var result = host.SelectParagraph(new WordSelectRequest
                {
                    ParagraphIndex = ReadInt(args, "paragraphIndex", 1)
                });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to select paragraph."
                            : result.ErrorMessage,
                        "word.select_paragraph_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Selected paragraph " + result.ParagraphIndex + " (range [" + result.StartPosition + " - " + result.EndPosition + "]).",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.select_paragraph_failed"));
            }
        }
    }
}
