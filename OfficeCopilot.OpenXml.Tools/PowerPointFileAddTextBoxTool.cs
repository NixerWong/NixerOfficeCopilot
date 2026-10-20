using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.PowerPoint;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Appends a text box to a slide of a .pptx file by path.</summary>
    public sealed class PowerPointFileAddTextBoxTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "ppt_file_add_textbox",
            DisplayName = "Add PPTX Text Box",
            Description = "Appends a text box with the given text to a slide (1-based slideIndex) of a .pptx file by path, returning the new shape id.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"slideIndex\":{\"type\":\"integer\"},\"text\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"slideIndex\",\"text\"],\"additionalProperties\":false}"
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
                var text = ReadString(args, "text", null);

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Task.FromResult(Fail(request, "filePath is required.", "ppt.add_textbox_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "ppt.add_textbox_not_found"));
                }

                uint shapeId;
                using (var engine = (PowerPointDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    shapeId = engine.AddTextBox(slideIndex, text);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Added text box with shape id " + shapeId + " to slide " + slideIndex + ".",
                    JsonConvert.SerializeObject(new { filePath, slideIndex, shapeId })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.add_textbox_failed"));
            }
        }
    }
}
