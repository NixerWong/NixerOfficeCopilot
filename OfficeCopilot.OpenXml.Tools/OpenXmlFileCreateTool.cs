using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Creates a new blank Office document (.docx/.xlsx/.pptx).</summary>
    public sealed class OpenXmlFileCreateTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "openxml_file_create",
            DisplayName = "Create Blank Document",
            Description = "Creates a new blank Office document (.docx/.xlsx/.pptx) at the given path.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\",\"description\":\"Output path (.docx/.xlsx/.pptx).\"}},\"required\":[\"filePath\"],\"additionalProperties\":false}"
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
                    return Task.FromResult(Fail(request, "filePath is required.", "openxml.create_invalid_arguments"));
                }

                if (File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File already exists: " + filePath, "openxml.create_exists"));
                }

                DocumentEngineRegistry.Create(filePath);
                return Task.FromResult(Success(
                    request,
                    "Created: " + filePath,
                    JsonConvert.SerializeObject(new { filePath })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "openxml.create_failed"));
            }
        }
    }
}
