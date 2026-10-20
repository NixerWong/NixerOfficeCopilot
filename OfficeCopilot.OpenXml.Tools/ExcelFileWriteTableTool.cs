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
    /// Writes data rows into an existing ListObject table of an .xlsx file by
    /// path, expanding the table downward when more rows are supplied than fit.
    /// </summary>
    public sealed class ExcelFileWriteTableTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_write_table",
            DisplayName = "Write XLSX Table",
            Description = "Writes data rows into an existing ListObject table of an .xlsx file by path. table is its name or 1-based sheet index; rows is a row-major JSON array.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"table\":{\"type\":\"string\"},\"rows\":{\"type\":\"array\",\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}},\"required\":[\"filePath\",\"sheetName\",\"table\",\"rows\"],\"additionalProperties\":false}"
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
                var rows = ReadStringArrayArray(args, "rows");

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(table))
                {
                    return Task.FromResult(Fail(request, "filePath, sheetName and table are required.", "excel.write_table_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.write_table_not_found"));
                }

                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    var result = engine.WriteTable(sheetName, table, rows);
                    engine.Save();
                    return Task.FromResult(Success(
                        request,
                        string.Format("Wrote {0} row(s) to table '{1}'.", rows.Count, result.Name),
                        JsonConvert.SerializeObject(new
                        {
                            filePath,
                            sheetName,
                            name = result.Name,
                            reference = result.Reference,
                            rowCount = result.RowCount,
                            columnCount = result.ColumnCount
                        })));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.write_table_failed"));
            }
        }
    }
}
