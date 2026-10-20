using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Word;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Inserts a complex Word field (PAGE, DATE, MERGEFIELD, IF, ...) in a .docx file.</summary>
    public sealed class WordFileFieldTool : WordFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_file_field",
            DisplayName = "Insert Field",
            Description = "Inserts a complex Word field (PAGE, DATE, MERGEFIELD, REF, SEQ, IF, ...) into a .docx file.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\",\"description\":\"Path to the .docx file.\"},\"fieldType\":{\"type\":\"string\",\"description\":\"Field type: page, date, author, mergefield, seq, if, ...\"},\"name\":{\"type\":\"string\",\"description\":\"Required name for MERGEFIELD/REF/SEQ/STYLEREF/DOCPROPERTY.\"},\"format\":{\"type\":\"string\",\"description\":\"DATE-family format, e.g. yyyy-MM-dd.\"},\"instruction\":{\"type\":\"string\",\"description\":\"Raw instruction override.\"},\"text\":{\"type\":\"string\",\"description\":\"Explicit cached result text.\"},\"expression\":{\"type\":\"string\",\"description\":\"IF expression.\"},\"trueText\":{\"type\":\"string\",\"description\":\"IF true branch.\"},\"falseText\":{\"type\":\"string\",\"description\":\"IF false branch.\"},\"paragraphIndex\":{\"type\":\"integer\",\"description\":\"0-based paragraph to insert into. Omit to append a new paragraph.\"}},\"required\":[\"filePath\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var args = ParseArguments(request);
                var filePath = ReadString(args, "filePath", null);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Task.FromResult(Fail(request, "filePath is required.", "word.file_field_invalid_arguments"));
                }

                var options = new FieldOptions
                {
                    FieldType = ReadString(args, "fieldType", null),
                    Name = ReadString(args, "name", null),
                    Format = ReadString(args, "format", null),
                    Instruction = ReadString(args, "instruction", null),
                    Text = ReadString(args, "text", null),
                    Expression = ReadString(args, "expression", null),
                    TrueText = ReadString(args, "trueText", null),
                    FalseText = ReadString(args, "falseText", null)
                };

                using (var engine = new WordDocumentEngine(filePath, true))
                {
                    if (args["paragraphIndex"] != null)
                    {
                        engine.AddFieldToParagraph(ReadInt(args, "paragraphIndex", 0), options);
                    }
                    else
                    {
                        engine.AppendFieldParagraph(options);
                    }
                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Inserted field in: " + filePath,
                    JsonConvert.SerializeObject(new { filePath, fieldType = options.FieldType ?? options.Instruction })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.file_field_failed"));
            }
        }
    }
}
