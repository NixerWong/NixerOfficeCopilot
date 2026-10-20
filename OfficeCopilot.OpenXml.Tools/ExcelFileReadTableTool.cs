using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.Excel;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>
    /// Reads a ListObject table (headers + data rows) from an .xlsx file by
    /// path. The table is addressed by name or 1-based sheet index.
    /// </summary>
    public sealed class ExcelFileReadTableTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_read_table",
            DisplayName = "Read XLSX Table",
            Description = "Reads a ListObject table (headers and data rows) from an .xlsx file by path. table is its name or 1-based sheet index.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"table\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"sheetName\",\"table\"],\"additionalProperties\":false}"
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
                var table = ReadString(args, "table", null);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(table))
                {
                    return Task.FromResult(Fail(request, "filePath, sheetName and table are required.", "excel.read_table_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.read_table_not_found"));
                }

                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, false))
                {
                    var data = engine.ReadTable(sheetName, table);
                    var rows = new List<IReadOnlyList<string>>(data.Rows);
                    return Task.FromResult(Success(
                        request,
                        string.Format("Read table '{0}' ({1} row(s) x {2} column(s)).", data.Name, rows.Count, data.Headers.Count),
                        JsonConvert.SerializeObject(new
                        {
                            filePath,
                            sheetName,
                            name = data.Name,
                            displayName = data.DisplayName,
                            reference = data.Reference,
                            headers = data.Headers,
                            rows
                        })));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.read_table_failed"));
            }
        }
    }
}
