using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Validates a .docx/.xlsx/.pptx file against the Open XML schema.</summary>
    public sealed class OpenXmlFileValidateTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "openxml_file_validate",
            DisplayName = "Validate Document File",
            Description = "Validates an Office document file (.docx/.xlsx/.pptx) against the Open XML schema.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\",\"description\":\"Path to the .docx/.xlsx/.pptx file.\"}},\"required\":[\"filePath\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                DocumentEngineBootstrapper.EnsureRegistered();
                var args = ParseArguments(request);
                var filePath = ReadString(args, "filePath", null);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Task.FromResult(Fail(request, "filePath is required.", "openxml.validate_invalid_arguments"));
                }

                using (var engine = DocumentEngineRegistry.Open(filePath, false))
                {
                    var errors = engine.Validate();
                    var sb = new StringBuilder();
                    if (errors.Count == 0)
                    {
                        sb.AppendLine("Validation passed: no errors found.");
                    }
                    else
                    {
                        sb.AppendLine("Found " + errors.Count + " validation error(s):");
                        foreach (var error in errors)
                        {
                            sb.AppendLine("  " + error);
                        }
                    }

                    return Task.FromResult(Success(
                        request,
                        sb.ToString(),
                        JsonConvert.SerializeObject(new { filePath, errorCount = errors.Count })));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "openxml.validate_failed"));
            }
        }
    }
}
