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
    /// <summary>Adds a single-series clustered column chart to an .xlsx worksheet by path.</summary>
    public sealed class ExcelFileAddChartTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_add_chart",
            DisplayName = "Add XLSX Column Chart",
            Description = "Adds a single-series clustered column chart to a worksheet of an .xlsx file by path. categories and values are parallel arrays.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"},\"categories\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"values\":{\"type\":\"array\",\"items\":{\"type\":\"number\"}},\"seriesName\":{\"type\":\"string\"},\"title\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"sheetName\",\"values\"],\"additionalProperties\":false}"
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
                var categories = ReadStringArray(args, "categories");
                var values = ReadDoubleArray(args, "values");
                var seriesName = ReadString(args, "seriesName", null);
                var title = ReadString(args, "title", null);

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName))
                {
                    return Task.FromResult(Fail(request, "filePath and sheetName are required.", "excel.add_chart_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.add_chart_not_found"));
                }

                if (values.Count == 0)
                {
                    return Task.FromResult(Fail(request, "values must be a non-empty array.", "excel.add_chart_invalid_values"));
                }

                int chartIndex;
                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    chartIndex = engine.AddChart(sheetName, categories, values, seriesName, title);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Added chart " + chartIndex + " to '" + sheetName + "'.",
                    JsonConvert.SerializeObject(new { filePath, sheetName, chartIndex })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.add_chart_failed"));
            }
        }
    }
}
