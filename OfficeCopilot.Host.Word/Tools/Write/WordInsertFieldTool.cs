using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Inserts a simple Word field at the current selection.</summary>
    public sealed class WordInsertFieldTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_insert_field",
            DisplayName = "Insert Field",
            Description = "Inserts a simple Word field (page, numpages, date, time, author, title, filename, ...) at the current selection.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"fieldType\":{\"type\":\"string\",\"description\":\"page | numpages | date | time | author | title | subject | filename | numwords | numchars | edittime | lastsavedby | revnum | template.\"}},\"required\":[\"fieldType\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);
                var fieldType = ReadString(args, "fieldType", null);

                if (string.IsNullOrWhiteSpace(fieldType))
                {
                    return Task.FromResult(Fail(request, "fieldType is required.", "word.insert_field_invalid_arguments"));
                }

                var result = host.InsertField(new WordInsertFieldRequest { FieldType = fieldType });
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to insert field."
                            : result.ErrorMessage,
                        "word.insert_field_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Field inserted: " + result.FieldName,
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.insert_field_failed"));
            }
        }
    }
}
