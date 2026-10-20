using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    public sealed class WordGetActiveDocumentTool : WordToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_get_active_document",
            DisplayName = "Get Active Document",
            Description = "Returns information about the active Word document: name, path, page count, paragraphs, read-only status.",
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
                var doc = host.GetActiveDocumentInfo();
                if (doc == null || !doc.IsAvailable)
                    return Task.FromResult(Fail(request, "No active Word document.", "word.no_active_document"));

                var sb = new StringBuilder();
                sb.AppendLine("Document: " + doc.Name);
                sb.AppendLine("Path: " + (string.IsNullOrWhiteSpace(doc.FullName) ? "(unsaved)" : doc.FullName));
                sb.AppendLine("Pages: " + doc.PageCount);
                sb.AppendLine("Paragraphs: " + doc.ParagraphCount);
                sb.Append("Read Only: " + (doc.IsReadOnly ? "Yes" : "No"));
                return Task.FromResult(Success(request, sb.ToString(), Newtonsoft.Json.JsonConvert.SerializeObject(doc)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.get_active_document_failed"));
            }
        }
    }
}
