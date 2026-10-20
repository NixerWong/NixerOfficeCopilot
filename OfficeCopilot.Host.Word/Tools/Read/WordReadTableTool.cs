using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Reads a table from the active document as rows of cells.</summary>
    public sealed class WordReadTableTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_read_table",
            DisplayName = "Read Table",
            Description = "Reads a table from the active document as rows of cells.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"tableIndex\":{\"type\":\"integer\",\"description\":\"1-based table index. Default 1.\"},\"maxRows\":{\"type\":\"integer\",\"description\":\"Maximum rows to return. Default 200.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var result = host.ReadTable(new WordReadTableRequest
                {
                    TableIndex = ReadInt(args, "tableIndex", 1),
                    MaxRows = ReadInt(args, "maxRows", 200)
                });

                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "No active Word document."
                            : result.ErrorMessage,
                        "word.read_table_failed"));
                }

                var sb = new StringBuilder();
                sb.AppendLine("Tables: " + result.TableCount);
                if (result.Rows != null)
                {
                    foreach (var row in result.Rows)
                    {
                        sb.AppendLine(string.Join("\t", row.Cells ?? new System.Collections.Generic.List<string>()));
                    }
                }

                return Task.FromResult(Success(request, sb.ToString(), JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.read_table_failed"));
            }
        }
    }
}
