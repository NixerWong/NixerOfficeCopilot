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
    /// Writes a rectangular grid of string values into an .xlsx file by path.
    /// Rows and columns are 1-based; <c>values</c> is a row-major JSON array.
    /// </summary>
    public sealed class ExcelFileWriteRangeTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_write_range",
            DisplayName = "Write XLSX Range",
            Description = "Writes a rectangular grid of string values into a worksheet of an .xlsx file by path. startRow/startColumn are 1-based; values is a row-major JSON array of arrays.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"startRow\":{\"type\":\"integer\"},\"startColumn\":{\"type\":\"integer\"},\"values\":{\"type\":\"array\",\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}},\"required\":[\"filePath\",\"sheetName\",\"startRow\",\"startColumn\",\"values\"],\"additionalProperties\":false}"
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
                var values = ReadStringArrayArray(args, "values");

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName))
                {
                    return Task.FromResult(Fail(request, "filePath and sheetName are required.", "excel.write_range_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.write_range_not_found"));
                }

                if (values.Count == 0)
                {
                    return Task.FromResult(Fail(request, "values must be a non-empty row-major array.", "excel.write_range_invalid_values"));
                }

                int rowsWritten = values.Count;
                int columnsWritten = 0;
                foreach (var row in values)
                {
                    if (row.Count > columnsWritten)
                        columnsWritten = row.Count;
                }

                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    engine.WriteRange(sheetName, startRow, startColumn, values);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    string.Format("Wrote {0}x{1} range to '{2}' at ({3},{4}).", rowsWritten, columnsWritten, sheetName, startRow, startColumn),
                    JsonConvert.SerializeObject(new
                    {
                        filePath,
                        sheetName,
                        startRow,
                        startColumn,
                        rows = rowsWritten,
                        columns = columnsWritten
                    })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.write_range_failed"));
            }
        }
    }
}
