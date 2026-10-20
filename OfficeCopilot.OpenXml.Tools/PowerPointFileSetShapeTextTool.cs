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
    /// Replaces the text of a shape on a slide of a .pptx file by path. The
    /// shape is addressed by its name or numeric shape id.
    /// </summary>
    public sealed class PowerPointFileSetShapeTextTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "ppt_file_set_shape_text",
            DisplayName = "Set PPTX Shape Text",
            Description = "Replaces the text of a shape on a slide (1-based slideIndex) of a .pptx file by path. shape is the shape name or numeric id.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"slideIndex\":{\"type\":\"integer\"},\"shape\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"slideIndex\",\"shape\",\"text\"],\"additionalProperties\":false}"
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
                var shape = ReadString(args, "shape", null);
                var text = ReadString(args, "text", null);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(shape))
                {
                    return Task.FromResult(Fail(request, "filePath and shape are required.", "ppt.set_shape_text_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "ppt.set_shape_text_not_found"));
                }

                bool found;
                using (var engine = (PowerPointDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    found = engine.SetShapeText(slideIndex, shape, text);
                    engine.Save();
                }

                if (!found)
                {
                    return Task.FromResult(Fail(request, "Shape not found: " + shape, "ppt.set_shape_text_not_found"));
                }

                return Task.FromResult(Success(
                    request,
                    "Set text on shape '" + shape + "' of slide " + slideIndex + ".",
                    JsonConvert.SerializeObject(new { filePath, slideIndex, shape })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.set_shape_text_failed"));
            }
        }
    }
}
