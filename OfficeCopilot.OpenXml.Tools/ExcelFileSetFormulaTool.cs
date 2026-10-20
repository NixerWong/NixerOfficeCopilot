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
    /// Writes a formula into a cell of an .xlsx file by path. Rows/columns are
    /// 1-based; a leading '=' on the formula is optional.
    /// </summary>
    public sealed class ExcelFileSetFormulaTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_set_formula",
            DisplayName = "Set XLSX Cell Formula",
            Description = "Writes a formula into a cell of an .xlsx file by path. row/column are 1-based; a leading '=' is optional.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"row\":{\"type\":\"integer\"},\"column\":{\"type\":\"integer\"},\"formula\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"sheetName\",\"row\",\"column\",\"formula\"],\"additionalProperties\":false}"
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
                var row = ReadInt(args, "row", 1);
                var column = ReadInt(args, "column", 1);
                var formula = ReadString(args, "formula", null);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(formula))
                {
                    return Task.FromResult(Fail(request, "filePath, sheetName and formula are required.", "excel.set_formula_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.set_formula_not_found"));
                }

                string normalized;
                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    engine.SetCellFormula(sheetName, row, column, formula);
                    engine.Save();
                    normalized = engine.ReadCellFormula(sheetName, row, column);
                }

                return Task.FromResult(Success(
                    request,
                    "Set formula " + normalized + " at " + sheetName + " (row " + row + ", column " + column + ").",
                    JsonConvert.SerializeObject(new { filePath, sheetName, row, column, formula = normalized })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.set_formula_failed"));
            }
        }
    }
}
