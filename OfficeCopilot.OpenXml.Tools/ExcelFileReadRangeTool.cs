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
    /// Reads a rectangular range of cell values from an .xlsx file by path.
    /// Rows and columns are 1-based and inclusive.
    /// </summary>
    public sealed class ExcelFileReadRangeTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_read_range",
            DisplayName = "Read XLSX Range",
            Description = "Reads a rectangular range of cell values from a worksheet in an .xlsx file by path. Rows and columns are 1-based and inclusive.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"startRow\":{\"type\":\"integer\"},\"startColumn\":{\"type\":\"integer\"},\"endRow\":{\"type\":\"integer\"},\"endColumn\":{\"type\":\"integer\"}},\"required\":[\"filePath\",\"sheetName\",\"startRow\",\"startColumn\",\"endRow\",\"endColumn\"],\"additionalProperties\":false}"
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
                var startColumn = ReadInt(args, "startColumn", 1);
                var endRow = ReadInt(args, "endRow", 1);
                var endColumn = ReadInt(args, "endColumn", 1);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName))
                {
                    return Task.FromResult(Fail(request, "filePath and sheetName are required.", "excel.read_range_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.read_range_not_found"));
                }

                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, false))
                {
                    var result = engine.ReadRange(sheetName, startRow, startColumn, endRow, endColumn);
                    var rows = new List<IReadOnlyList<string>>();
                    foreach (var row in result.Cells)
                        rows.Add(new List<string>(row));

                    return Task.FromResult(Success(
                        request,
                        string.Format("Read {0}x{1} range from '{2}'.", result.RowCount, result.ColumnCount, sheetName),
                        JsonConvert.SerializeObject(new
                        {
                            filePath,
                            sheetName,
                            rowCount = result.RowCount,
                            columnCount = result.ColumnCount,
                            rows
                        })));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.read_range_failed"));
            }
        }
    }
}
