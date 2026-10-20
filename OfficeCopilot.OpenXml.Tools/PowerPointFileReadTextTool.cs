using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.PowerPoint;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>
    /// Reads text from a .pptx file by path. With a 1-based slideIndex reads that
    /// slide's text; without it, returns the text of every slide in the deck.
    /// </summary>
    public sealed class PowerPointFileReadTextTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "ppt_file_read_text",
            DisplayName = "Read PPTX Text",
            Description = "Reads text from a .pptx file by path. With slideIndex (1-based) reads that slide's text; otherwise returns every slide's text.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"slideIndex\":{\"type\":\"integer\"}},\"required\":[\"filePath\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                DocumentEngineBootstrapper.EnsureRegistered();
                var args = ParseArguments(request);
                var filePath = ReadString(args, "filePath", null);
                var slideIndex = ReadInt(args, "slideIndex", 0);

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Task.FromResult(Fail(request, "filePath is required.", "ppt.read_text_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "ppt.read_text_not_found"));
                }

                using (var engine = (PowerPointDocumentEngine)DocumentEngineRegistry.Open(filePath, false))
                {
                    if (slideIndex > 0)
                    {
                        var text = engine.ReadSlideText(slideIndex);
                        return Task.FromResult(Success(
                            request,
                            text,
                            JsonConvert.SerializeObject(new { filePath, slideIndex, text })));
                    }

                    var slides = engine.ListSlides();
                    var list = new List<object>();
                    foreach (var slide in slides)
                    {
                        list.Add(new { slide.Index, slide.Text });
                    }

                    return Task.FromResult(Success(
                        request,
                        "Read " + slides.Count + " slide(s).",
                        JsonConvert.SerializeObject(new { filePath, slideCount = slides.Count, slides = list })));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.read_text_failed"));
            }
        }
    }
}
