using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Sets page margins, size and orientation of the active document.</summary>
    public sealed class WordSetPageSetupTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_set_page_setup",
            DisplayName = "Set Page Setup",
            Description = "Sets page margins (points), page size (points) and orientation (portrait | landscape) of the active document.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"topMargin\":{\"type\":\"number\",\"description\":\"Points.\"},\"bottomMargin\":{\"type\":\"number\"},\"leftMargin\":{\"type\":\"number\"},\"rightMargin\":{\"type\":\"number\"},\"pageWidth\":{\"type\":\"number\",\"description\":\"Points.\"},\"pageHeight\":{\"type\":\"number\",\"description\":\"Points.\"},\"orientation\":{\"type\":\"string\",\"description\":\"portrait | landscape.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var req = new WordPageSetupRequest
                {
                    TopMargin = ReadNullableDouble(args, "topMargin"),
                    BottomMargin = ReadNullableDouble(args, "bottomMargin"),
                    LeftMargin = ReadNullableDouble(args, "leftMargin"),
                    RightMargin = ReadNullableDouble(args, "rightMargin"),
                    PageWidth = ReadNullableDouble(args, "pageWidth"),
                    PageHeight = ReadNullableDouble(args, "pageHeight"),
                    Orientation = ReadString(args, "orientation", null)
                };

                var result = host.SetPageSetup(req);
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to set page setup."
                            : result.ErrorMessage,
                        "word.page_setup_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Page setup applied.",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.page_setup_failed"));
            }
        }
    }
}
