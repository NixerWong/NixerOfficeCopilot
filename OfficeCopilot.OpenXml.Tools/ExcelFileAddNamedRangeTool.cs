using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.Excel;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Adds a workbook-scoped defined name for a range in an .xlsx file by path.</summary>
    public sealed class ExcelFileAddNamedRangeTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_add_named_range",
            DisplayName = "Add XLSX Named Range",
            Description = "Adds a workbook-scoped defined name pointing at a range (e.g. A1:B10) on a worksheet of an .xlsx file by path.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"range\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"name\",\"sheetName\",\"range\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                DocumentEngineBootstrapper.EnsureRegistered();
                var args = ParseArguments(request);
                var filePath = ReadString(args, "filePath", null);
                var name = ReadString(args, "name", null);
                var sheetName = ReadString(args, "sheetName", null);
                var range = ReadString(args, "range", null);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(range))
                {
                    return Task.FromResult(Fail(request, "filePath, name, sheetName and range are required.", "excel.add_named_range_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.add_named_range_not_found"));
                }

                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    engine.AddNamedRange(name, sheetName, range);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    string.Format("Added defined name '{0}' -> '{1}'!{2}.", name, sheetName, range.ToUpperInvariant()),
                    JsonConvert.SerializeObject(new { filePath, name, sheetName, range = range.ToUpperInvariant() })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.add_named_range_failed"));
            }
        }
    }
}
