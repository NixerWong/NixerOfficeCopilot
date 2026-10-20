using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Reads the document outline (heading paragraphs and their levels).</summary>
    public sealed class WordReadOutlineTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_read_outline",
            DisplayName = "Read Outline",
            Description = "Reads the active document outline: heading paragraphs and their levels (1-9).",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"maxLevel\":{\"type\":\"integer\",\"description\":\"Maximum heading level to include. Default 9.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);
                var result = host.GetOutline(new WordOutlineRequest { MaxLevel = ReadInt(args, "maxLevel", 9) });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "No active Word document."
                            : result.ErrorMessage,
                        "word.read_outline_failed"));
                }

                var sb = new StringBuilder();
                sb.AppendLine("Outline entries: " + (result.Items == null ? 0 : result.Items.Count));
                if (result.Items != null)
                {
                    foreach (var item in result.Items)
                    {
                        sb.AppendLine(new string('#', item.Level) + " " + item.Text);
                    }
                }

                return Task.FromResult(Success(request, sb.ToString(), JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.read_outline_failed"));
            }
        }
    }
}
