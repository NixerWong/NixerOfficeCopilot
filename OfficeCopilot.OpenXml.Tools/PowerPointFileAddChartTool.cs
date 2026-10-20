using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.PowerPoint;

namespace OfficeCopilot.OpenXml.Tools
{
    /// <summary>Adds a single-series clustered column chart to a .pptx slide by path.</summary>
    public sealed class PowerPointFileAddChartTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "ppt_file_add_chart",
            DisplayName = "Add PPTX Column Chart",
            Description = "Adds a single-series clustered column chart to a slide (1-based slideIndex) of a .pptx file by path. categories and values are parallel arrays.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"slideIndex\":{\"type\":\"integer\"},\"categories\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"values\":{\"type\":\"array\",\"items\":{\"type\":\"number\"}},\"seriesName\":{\"type\":\"string\"},\"title\":{\"type\":\"string\"}},\"required\":[\"filePath\",\"slideIndex\",\"values\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                DocumentEngineBootstrapper.EnsureRegistered();
                var args = ParseArguments(request);
                var filePath = ReadString(args, "filePath", null);
                var slideIndex = ReadInt(args, "slideIndex", 1);
                var categories = ReadStringArray(args, "categories");
                var values = ReadDoubleArray(args, "values");
                var seriesName = ReadString(args, "seriesName", null);
                var title = ReadString(args, "title", null);

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Task.FromResult(Fail(request, "filePath is required.", "ppt.add_chart_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "ppt.add_chart_not_found"));
                }

                if (values.Count == 0)
                {
                    return Task.FromResult(Fail(request, "values must be a non-empty array.", "ppt.add_chart_invalid_values"));
                }

                int chartIndex;
                using (var engine = (PowerPointDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    chartIndex = engine.AddChart(slideIndex, categories, values, seriesName, title);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Added chart " + chartIndex + " to slide " + slideIndex + ".",
                    JsonConvert.SerializeObject(new { filePath, slideIndex, chartIndex })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.add_chart_failed"));
            }
        }
    }
}
