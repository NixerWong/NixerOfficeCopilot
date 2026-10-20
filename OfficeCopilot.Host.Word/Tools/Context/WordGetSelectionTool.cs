using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    public sealed class WordGetSelectionTool : WordToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_get_selection",
            DisplayName = "Get Selection",
            Description = "Returns the currently selected text, its position range, and page number.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
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
                var sel = host.GetSelectionInfo();
                if (sel == null)
                    return Task.FromResult(Fail(request, "No selection in active document.", "word.no_selection"));

                var sb = new StringBuilder();
                sb.AppendLine("Page: " + sel.PageNumber);
                sb.AppendLine("Range: [" + sel.StartPosition + " - " + sel.EndPosition + "]");
                var preview = string.IsNullOrEmpty(sel.Text) ? "(empty)" :
                    sel.Text.Length > 500 ? sel.Text.Substring(0, 500) + "..." : sel.Text;
                sb.Append("Text: " + preview);
                return Task.FromResult(Success(request, sb.ToString(), Newtonsoft.Json.JsonConvert.SerializeObject(sel)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.get_selection_failed"));
            }
        }
    }
}
