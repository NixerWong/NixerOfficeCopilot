using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.PowerPoint;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Inserts a picture from a local file into a slide of a .pptx file by path.</summary>
    public sealed class PowerPointFileAddPictureTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "ppt_file_add_picture",
            DisplayName = "Add PPTX Picture",
            Description = "Inserts a picture from a local file into a slide (1-based slideIndex) of a .pptx file by path, returning the new shape id. Supports png/jpg/gif/bmp/tif/emf/wmf.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"slideIndex\":{\"type\":\"integer\"},\"imagePath\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"slideIndex\",\"imagePath\"],\"additionalProperties\":false}"
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
                var imagePath = ReadString(args, "imagePath", null);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(imagePath))
                {
                    return Task.FromResult(Fail(request, "filePath and imagePath are required.", "ppt.add_picture_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "ppt.add_picture_not_found"));
                }

                uint shapeId;
                using (var engine = (PowerPointDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    shapeId = engine.AddPicture(slideIndex, imagePath);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Added picture with shape id " + shapeId + " to slide " + slideIndex + ".",
                    JsonConvert.SerializeObject(new { filePath, slideIndex, shapeId })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.add_picture_failed"));
            }
        }
    }
}
