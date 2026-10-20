using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Word;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Regenerates every TOC field in a .docx file from its headings.</summary>
    public sealed class WordFileTocTool : WordFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_file_toc",
            DisplayName = "Rebuild Table Of Contents",
            Description = "Regenerates every TOC field in a .docx file from its headings, with hyperlinks and PAGEREF fields.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\",\"description\":\"Path to the .docx file.\"}},\"required\":[\"filePath\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var args = ParseArguments(request);
                var filePath = ReadString(args, "filePath", null);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Task.FromResult(Fail(request, "filePath is required.", "word.file_toc_invalid_arguments"));
                }

                using (var engine = new WordDocumentEngine(filePath, true))
                {
                    engine.RegenerateAllTocs();
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Regenerated TOC fields in: " + filePath,
                    JsonConvert.SerializeObject(new { filePath })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.file_toc_failed"));
            }
        }
    }
}
