using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Inserts a table (optionally filled from a 2D data array).</summary>
    public sealed class WordInsertTableTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_insert_table",
            DisplayName = "Insert Table",
            Description = "Inserts a table and fills it from a 2D data array in a single call. rows/columns are derived from data when omitted. The cursor is moved to just after the new table. Never nests tables: if the cursor is already inside a table, the table is appended at the end of the document instead.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"rows\":{\"type\":\"integer\",\"description\":\"Row count. Derived from data if omitted.\"},\"columns\":{\"type\":\"integer\",\"description\":\"Column count. Derived from data if omitted.\"},\"data\":{\"type\":\"array\",\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"description\":\"Optional 2D cell values. Pass this to fill the whole table in one call; rows/columns will match the data.\"},\"insertAtEnd\":{\"type\":\"boolean\",\"description\":\"Insert at end of document instead of selection. Set true when appending a table. Default false.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);

                var data = new List<IList<string>>();
                var dataToken = args["data"] as JArray;
                if (dataToken != null)
                {
                    foreach (var rowToken in dataToken)
                    {
                        var rowArray = rowToken as JArray;
                        var row = new List<string>();
                        if (rowArray != null)
                        {
                            foreach (var cell in rowArray)
                            {
                                row.Add(cell == null ? string.Empty : cell.ToString());
                            }
                        }
                        data.Add(row);
                    }
                }

                var req = new WordInsertTableRequest
                {
                    Rows = ReadInt(args, "rows", 1),
                    Columns = ReadInt(args, "columns", 1),
                    Data = data,
                    InsertAtEnd = ReadBool(args, "insertAtEnd", false)
                };

                var result = host.InsertTable(req);
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to insert table."
                            : result.ErrorMessage,
                        "word.insert_table_failed"));
                }

                var message = "Table inserted (" + result.Rows + "x" + result.Columns + "). Total tables: " + result.TableCount
                    + (result.EscapedNestedTable
                        ? " Note: the cursor was inside a table, so the table was appended at the end of the document."
                        : string.Empty);

                return Task.FromResult(Success(
                    request,
                    message,
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.insert_table_failed"));
            }
        }
    }
}
