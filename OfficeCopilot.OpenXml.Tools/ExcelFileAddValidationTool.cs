using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.Excel;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Adds a data validation rule to a range in an .xlsx file by path.</summary>
    public sealed class ExcelFileAddValidationTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_add_validation",
            DisplayName = "Add XLSX Data Validation",
            Description = "Adds a data validation rule to a range of an .xlsx file by path. type: list, whole, decimal, textLength, date, time, custom.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"range\":{\"type\":\"string\"},\"type\":{\"type\":\"string\"},\"formula1\":{\"type\":\"string\"},\"formula2\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"sheetName\",\"range\",\"type\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                DocumentEngineBootstrapper.EnsureRegistered();
                var args = ParseArguments(request);
                var filePath = ReadString(args, "filePath", null);
                var sheetName = ReadString(args, "sheetName", null);
                var range = ReadString(args, "range", null);
                var type = ReadString(args, "type", null);
                var formula1 = ReadString(args, "formula1", null);
                var formula2 = ReadString(args, "formula2", null);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(range) || string.IsNullOrWhiteSpace(type))
                {
                    return Task.FromResult(Fail(request, "filePath, sheetName, range and type are required.", "excel.add_validation_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.add_validation_not_found"));
                }

                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    engine.AddDataValidation(sheetName, range, type, formula1, formula2);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    string.Format("Added {0} validation to '{1}'!{2}.", type, sheetName, range.ToUpperInvariant()),
                    JsonConvert.SerializeObject(new { filePath, sheetName, range = range.ToUpperInvariant(), type })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.add_validation_failed"));
            }
        }
    }
}
