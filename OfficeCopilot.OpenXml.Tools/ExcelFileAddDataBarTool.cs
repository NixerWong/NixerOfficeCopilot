using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.Excel;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Adds a data-bar conditional formatting rule to a range in an .xlsx file by path.</summary>
    public sealed class ExcelFileAddDataBarTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_add_data_bar",
            DisplayName = "Add XLSX Data Bar",
            Description = "Adds a data-bar conditional formatting rule to a range of an .xlsx file by path. color is a 6-digit RRGGBB hex.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"range\":{\"type\":\"string\"},\"color\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"sheetName\",\"range\"],\"additionalProperties\":false}"
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
                var color = ReadString(args, "color", "638EC6");

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(range))
                {
                    return Task.FromResult(Fail(request, "filePath, sheetName and range are required.", "excel.add_data_bar_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.add_data_bar_not_found"));
                }

                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    engine.AddDataBarConditionalFormatting(sheetName, range, color);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    string.Format("Added data bar to '{0}'!{1}.", sheetName, range.ToUpperInvariant()),
                    JsonConvert.SerializeObject(new { filePath, sheetName, range = range.ToUpperInvariant(), color })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.add_data_bar_failed"));
            }
        }
    }
}
