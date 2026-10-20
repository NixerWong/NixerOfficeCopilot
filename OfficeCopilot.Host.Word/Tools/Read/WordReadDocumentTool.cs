using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    public sealed class WordReadDocumentTool : WordToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_read_document",
            DisplayName = "Read Document",
            Description = "Returns the full text of the active Word document, optionally truncated to a maximum character count.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"maxCharacters\":{\"type\":\"integer\",\"description\":\"Maximum characters to return. Omit or set 0 to return everything.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var result = host.GetDocumentText(new WordDocumentTextRequest
                {
                    MaxCharacters = ReadMaxCharacters(request)
                });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "No active Word document."
                            : result.ErrorMessage,
                        "word.read_document_failed"));
                }

                var sb = new StringBuilder();
                sb.AppendLine("Document: " + result.DocumentName);
                sb.AppendLine("Total Characters: " + result.TotalCharacters);
                if (result.Truncated)
                {
                    sb.AppendLine("(content truncated)");
                }

                sb.AppendLine();
                sb.Append(result.Text ?? string.Empty);

                return Task.FromResult(Success(
                    request,
                    sb.ToString(),
                    Newtonsoft.Json.JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.read_document_failed"));
            }
        }

        private static int ReadMaxCharacters(ToolExecutionRequest request)
        {
            const int defaultMax = 20000;
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ArgumentsJson))
                {
                    return defaultMax;
                }

                var json = JObject.Parse(request.ArgumentsJson);
                var token = json["maxCharacters"];
                if (token == null)
                {
                    return defaultMax;
                }

                int value;
                return int.TryParse(token.ToString(), out value) ? value : defaultMax;
            }
            catch
            {
                return defaultMax;
            }
        }
    }
}
