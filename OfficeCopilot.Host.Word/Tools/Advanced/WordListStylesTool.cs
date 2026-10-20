using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    public sealed class WordListStylesTool : WordToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_list_styles",
            DisplayName = "List Styles",
            Description = "Lists the styles available in the active Word document.",
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
                var result = host.ListStyles();

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "No active Word document."
                            : result.ErrorMessage,
                        "word.list_styles_failed"));
                }

                var sb = new StringBuilder();
                sb.AppendLine("Styles: " + (result.Styles == null ? 0 : result.Styles.Count));
                if (result.Styles != null)
                {
                    var count = 0;
                    foreach (var style in result.Styles)
                    {
                        if (count >= 200)
                        {
                            sb.AppendLine("... (truncated)");
                            break;
                        }

                        sb.AppendLine("- " + style.NameLocal + (style.IsBuiltIn ? " [built-in]" : string.Empty));
                        count++;
                    }
                }

                return Task.FromResult(Success(
                    request,
                    sb.ToString(),
                    Newtonsoft.Json.JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.list_styles_failed"));
            }
        }
    }
}
