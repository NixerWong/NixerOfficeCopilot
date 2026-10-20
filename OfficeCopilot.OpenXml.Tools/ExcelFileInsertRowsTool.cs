using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.Excel;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>
    /// Inserts empty rows into an .xlsx worksheet by path, shifting existing
    /// rows (and their cell references) down.
    /// </summary>
    public sealed class ExcelFileInsertRowsTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_insert_rows",
            DisplayName = "Insert XLSX Rows",
            Description = "Inserts count empty rows starting at the 1-based startRow of a worksheet in an .xlsx file by path, shifting existing rows down.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"startRow\":{\"type\":\"integer\"},\"count\":{\"type\":\"integer\"}},\"required\":[\"filePath\",\"sheetName\",\"startRow\",\"count\"],\"additionalProperties\":false}"
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
                var startRow = ReadInt(args, "startRow", 1);
                var count = ReadInt(args, "count", 1);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName))
                {
                    return Task.FromResult(Fail(request, "filePath and sheetName are required.", "excel.insert_rows_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.insert_rows_not_found"));
                }

                int shifted;
                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    shifted = engine.InsertRows(sheetName, startRow, count);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    string.Format("Inserted {0} row(s) at row {1} of '{2}' ({3} row(s) shifted).", count, startRow, sheetName, shifted),
                    JsonConvert.SerializeObject(new { filePath, sheetName, startRow, count, shiftedRows = shifted })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.insert_rows_failed"));
            }
        }
    }
}
