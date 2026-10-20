using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Selects the entire document content.</summary>
    public sealed class WordSelectDocumentTool : WordToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_select_document",
            DisplayName = "Select Whole Document",
            Description = "Selects the entire document content. Use this before applying character or paragraph formatting to the whole document (for example with word_format_selection, word_set_paragraph_format or word_apply_style).",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            // 仅改变选区，不修改文档内容，因此视为只读（不需确认、不需备份）。
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var result = host.SelectDocument();
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to select document."
                            : result.ErrorMessage,
                        "word.select_document_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Selected the whole document (range [" + result.StartPosition + " - " + result.EndPosition + "]).",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.select_document_failed"));
            }
        }
    }
}
