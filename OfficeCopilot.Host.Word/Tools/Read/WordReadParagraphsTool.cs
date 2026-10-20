using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Reads the document as an indexed paragraph list.</summary>
    public sealed class WordReadParagraphsTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_read_paragraphs",
            DisplayName = "Read Paragraphs",
            Description = "Returns the document paragraphs as an indexed (1-based) list. Use this before a formatting pass to map heading/body positions to paragraph indices, then style them with word_select_paragraph + word_apply_style. Empty paragraphs are included so indices stay accurate.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"maxCount\":{\"type\":\"integer\",\"description\":\"Maximum number of paragraphs to return. Default 200. Set 0 to return all.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var result = host.ReadParagraphs(new WordReadParagraphsRequest
                {
                    MaxCount = ReadInt(args, "maxCount", 200)
                });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "No active Word document."
                            : result.ErrorMessage,
                        "word.read_paragraphs_failed"));
                }

                var sb = new StringBuilder();
                sb.AppendLine("Total paragraphs: " + result.TotalParagraphs);
                if (result.Paragraphs != null)
                {
                    foreach (var p in result.Paragraphs)
                    {
                        sb.AppendLine(p.Index + "\t" + p.Text);
                    }
                }

                return Task.FromResult(Success(request, sb.ToString(), JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.read_paragraphs_failed"));
            }
        }
    }
}
