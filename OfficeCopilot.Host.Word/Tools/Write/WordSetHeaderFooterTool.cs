using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Sets header and/or footer text for a section.</summary>
    public sealed class WordSetHeaderFooterTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_set_header_footer",
            DisplayName = "Set Header Footer",
            Description = "Sets header and/or footer text for a section (primary, first page or even pages) of the active document.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"headerText\":{\"type\":\"string\"},\"footerText\":{\"type\":\"string\"},\"target\":{\"type\":\"string\",\"description\":\"primary | first | even. Default primary.\"},\"sectionIndex\":{\"type\":\"integer\",\"description\":\"1-based section index. Default 1.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var req = new WordSetHeaderFooterRequest
                {
                    HeaderText = ReadString(args, "headerText", null),
                    FooterText = ReadString(args, "footerText", null),
                    Target = ReadString(args, "target", "primary"),
                    SectionIndex = ReadInt(args, "sectionIndex", 1)
                };

                if (string.IsNullOrWhiteSpace(req.HeaderText) && string.IsNullOrWhiteSpace(req.FooterText))
                {
                    return Task.FromResult(Fail(request, "headerText or footerText is required.", "word.header_footer_invalid_arguments"));
                }

                var result = host.SetHeaderFooter(req);
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to set header/footer."
                            : result.ErrorMessage,
                        "word.header_footer_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Header/footer updated.",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.header_footer_failed"));
            }
        }
    }
}
