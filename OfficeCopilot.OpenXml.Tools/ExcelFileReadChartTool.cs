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
    /// <summary>Reads chart metadata (type, title, series count) from an .xlsx worksheet by path.</summary>
    public sealed class ExcelFileReadChartTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_file_read_chart",
            DisplayName = "Read XLSX Charts",
            Description = "Reads chart metadata (type, title, series count) from a worksheet of an .xlsx file by path.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"sheetName\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"sheetName\"],\"additionalProperties\":false}"
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

                if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(sheetName))
                {
                    return Task.FromResult(Fail(request, "filePath and sheetName are required.", "excel.read_chart_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "excel.read_chart_not_found"));
                }

                using (var engine = (ExcelDocumentEngine)DocumentEngineRegistry.Open(filePath, false))
                {
                    var charts = engine.ReadCharts(sheetName);
                    var list = new List<object>();
                    foreach (var chart in charts)
                    {
                        list.Add(new { chart.Index, chart.Type, chart.Title, chart.SeriesCount });
                    }

                    return Task.FromResult(Success(
                        request,
                        "Read " + charts.Count + " chart(s) from '" + sheetName + "'.",
                        JsonConvert.SerializeObject(new { filePath, sheetName, chartCount = charts.Count, charts = list })));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "excel.read_chart_failed"));
            }
        }
    }
}
