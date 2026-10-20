using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.PowerPoint;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Sets a solid color background on a slide of a .pptx file by path.</summary>
    public sealed class PowerPointFileSetBackgroundTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "ppt_file_set_background",
            DisplayName = "Set PPTX Slide Background",
            Description = "Sets a solid color background (RRGGBB hex) on a slide (1-based slideIndex) of a .pptx file by path.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"slideIndex\":{\"type\":\"integer\"},\"color\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"slideIndex\",\"color\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                DocumentEngineBootstrapper.EnsureRegistered();
                var args = ParseArguments(request);
                var filePath = ReadString(args, "filePath", null);
                var slideIndex = ReadInt(args, "slideIndex", 1);
                var color = ReadString(args, "color", null);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(color))
                {
                    return Task.FromResult(Fail(request, "filePath and color are required.", "ppt.set_background_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "ppt.set_background_not_found"));
                }

                using (var engine = (PowerPointDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    engine.SetBackground(slideIndex, color);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Set background " + color + " on slide " + slideIndex + ".",
                    JsonConvert.SerializeObject(new { filePath, slideIndex, color })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.set_background_failed"));
            }
        }
    }
}
