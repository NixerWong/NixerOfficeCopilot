using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.PowerPoint;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>
    /// Appends a new slide (optionally with title/body text) to a .pptx file by
    /// path and returns its 1-based index.
    /// </summary>
    public sealed class PowerPointFileAddSlideTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "ppt_file_add_slide",
            DisplayName = "Add PPTX Slide",
            Description = "Appends a new slide to a .pptx file by path, optionally with title and body text, and returns the new slide's 1-based index.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"title\":{\"type\":\"string\"},\"body\":{\"type\":\"string\"}},\"required\":[\"filePath\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                DocumentEngineBootstrapper.EnsureRegistered();
                var args = ParseArguments(request);
                var filePath = ReadString(args, "filePath", null);
                var title = ReadString(args, "title", null);
                var body = ReadString(args, "body", null);

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Task.FromResult(Fail(request, "filePath is required.", "ppt.add_slide_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "ppt.add_slide_not_found"));
                }

                int slideIndex;
                using (var engine = (PowerPointDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    slideIndex = engine.AddSlide(title, body);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Added slide " + slideIndex + " to " + filePath,
                    JsonConvert.SerializeObject(new { filePath, slideIndex })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.add_slide_failed"));
            }
        }
    }
}
