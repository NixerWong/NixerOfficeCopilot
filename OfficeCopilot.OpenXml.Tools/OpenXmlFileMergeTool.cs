using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Merges an Office template (.docx/.xlsx/.pptx) with JSON data, replacing {{key}} placeholders.</summary>
    public sealed class OpenXmlFileMergeTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "openxml_file_merge",
            DisplayName = "Merge Template",
            Description = "Merges an Office template (.docx/.xlsx/.pptx) with JSON data, replacing {{key}} placeholders and writing the result to outputPath.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"templatePath\":{\"type\":\"string\",\"description\":\"Template path with {{key}} placeholders.\"},\"outputPath\":{\"type\":\"string\",\"description\":\"Output path.\"},\"dataJson\":{\"type\":\"string\",\"description\":\"JSON object with the merge values.\"}},\"required\":[\"templatePath\",\"outputPath\",\"dataJson\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                DocumentEngineBootstrapper.EnsureRegistered();
                var args = ParseArguments(request);
                var templatePath = ReadString(args, "templatePath", null);
                var outputPath = ReadString(args, "outputPath", null);
                var dataJson = ReadString(args, "dataJson", null);
                if (string.IsNullOrWhiteSpace(templatePath) || string.IsNullOrWhiteSpace(outputPath) || string.IsNullOrWhiteSpace(dataJson))
                {
                    return Task.FromResult(Fail(request, "templatePath, outputPath and dataJson are required.", "openxml.merge_invalid_arguments"));
                }

                var data = MergeData.FromJson(dataJson);
                var result = DocumentEngineRegistry.Merge(templatePath, outputPath, data);

                var message = "Merged " + result.ReplacedCount + " replacement(s).";
                if (result.UnresolvedPlaceholders.Count > 0)
                {
                    message += " Unresolved placeholders: " + string.Join(", ", result.UnresolvedPlaceholders) + ".";
                }

                return Task.FromResult(Success(
                    request,
                    message,
                    JsonConvert.SerializeObject(new
                    {
                        outputPath,
                        result.ReplacedCount,
                        result.UnresolvedPlaceholders,
                        result.UsedKeys
                    })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "openxml.merge_failed"));
            }
        }
    }
}
