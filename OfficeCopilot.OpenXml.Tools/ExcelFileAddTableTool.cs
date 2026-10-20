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
    /// Creates a ListObject table (header row + data rows) in an .xlsx file by
    /// path. startCell is the top-left cell; headers set the column count/names.
    /// </summary>
    public sealed class ExcelFileAddTableTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_add_table",
            DisplayName = "Add XLSX Table",
            Description = "Creates a ListObject table (header row + data rows) in an .xlsx file by path. startCell is the top-left cell; headers is an array of column names; rows is a row-major JSON array.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"startCell\":{\"type\":\"string\"},\"headers\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"rows\":{\"type\":\"array\",\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"name\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"sheetName\",\"startCell\",\"headers\"],\"additionalProperties\":false}"
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
                var startCell = ReadString(args, "startCell", null);
                var headers = ReadStringArray(args, "headers");
                var rows = ReadStringArrayArray(args, "rows");
                var name = ReadString(args, "name", null);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(startCell) || headers.Count == 0)
                {
                    return Task.FromResult(Fail(request, "filePath, sheetName, startCell and a non-empty headers array are required.", "excel.add_table_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.add_table_not_found"));
                }

                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    var result = engine.AddTable(sheetName, startCell, headers, rows, name);
                    engine.Save();
                    return Task.FromResult(Success(
                        request,
                        string.Format("Added table '{0}' ({1} row(s) x {2} column(s)).", result.Name, result.RowCount, result.ColumnCount),
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
                return Task.FromResult(Fail(request, ex.Message, "excel.add_table_failed"));
            }
        }
    }
}
