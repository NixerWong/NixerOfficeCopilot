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
    /// <summary>
    /// Appends a table (rows x columns) to a slide of a .pptx file by path,
    /// optionally seeded from row-major data. Returns the 1-based table index.
    /// </summary>
    public sealed class PowerPointFileAddTableTool : OpenXmlFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "ppt_file_add_table",
            DisplayName = "Add PPTX Table",
            Description = "Appends a table (rows x columns) to a slide (1-based slideIndex) of a .pptx file by path, optionally seeded from row-major data. Returns the 1-based table index.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "OpenXml",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\"},\"slideIndex\":{\"type\":\"integer\"},\"rows\":{\"type\":\"integer\"},\"columns\":{\"type\":\"integer\"},\"data\":{\"type\":\"array\",\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}},\"required\":[\"filePath\",\"slideIndex\",\"rows\",\"columns\"],\"additionalProperties\":false}"
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
                var rows = ReadInt(args, "rows", 1);
                var columns = ReadInt(args, "columns", 1);
                var data = ReadStringArrayArray(args, "data");

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Task.FromResult(Fail(request, "filePath is required.", "ppt.add_table_invalid_arguments"));
                }

                if (!File.Exists(filePath))
                {
                    return Task.FromResult(Fail(request, "File not found: " + filePath, "ppt.add_table_not_found"));
                }

                int tableIndex;
                using (var engine = (PowerPointDocumentEngine)DocumentEngineRegistry.Open(filePath, true))
                {
                    tableIndex = engine.AddTable(slideIndex, rows, columns, data);
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Added table " + tableIndex + " (" + rows + "x" + columns + ") to slide " + slideIndex + ".",
                    JsonConvert.SerializeObject(new { filePath, slideIndex, tableIndex, rows, columns })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.add_table_failed"));
            }
        }
    }
}
