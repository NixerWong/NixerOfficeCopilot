using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Word;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Creates a multi-level numbering list definition in a .docx file.</summary>
    public sealed class WordFileNumberingTool : WordFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_file_numbering",
            DisplayName = "Create Numbering List",
            Description = "Creates a multi-level numbering list definition (abstractNum + num) in a .docx file and optionally applies it to a paragraph.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\",\"description\":\"Path to the .docx file.\"},\"format\":{\"type\":\"string\",\"description\":\"Number format: decimal, bullet, lowerRoman, upperRoman, lowerLetter, upperLetter... Default decimal.\"},\"type\":{\"type\":\"string\",\"description\":\"hybridMultilevel | multilevel | singleLevel.\"},\"name\":{\"type\":\"string\",\"description\":\"Optional list name.\"},\"start\":{\"type\":\"integer\",\"description\":\"Level-0 start value. Default 1.\"},\"applyToParagraph\":{\"type\":\"integer\",\"description\":\"Optional 0-based body paragraph index to apply the numbering to.\"}},\"required\":[\"filePath\"],\"additionalProperties\":false}"
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
                    return Task.FromResult(Fail(request, "filePath is required.", "word.file_numbering_invalid_arguments"));
                }

                var options = new NumberingListOptions
                {
                    Format = ReadString(args, "format", "decimal"),
                    Type = ReadString(args, "type", "hybridMultilevel"),
                    Name = ReadString(args, "name", null),
                    Start = args["start"] == null ? 1 : (int?)ReadInt(args, "start", 1)
                };

                int numId;
                using (var engine = new WordDocumentEngine(filePath, true))
                {
                    numId = engine.AddNumberingList(options);

                    if (args["applyToParagraph"] != null)
                    {
                        var paragraphIndex = ReadInt(args, "applyToParagraph", -1);
                        if (paragraphIndex >= 0)
                        {
                            engine.ApplyNumberingToBodyParagraph(paragraphIndex, numId);
                        }
                    }

                    engine.Save();
                }

                return Task.FromResult(Success(
                    request,
                    "Created numbering list with numId=" + numId + " in: " + filePath,
                    JsonConvert.SerializeObject(new { filePath, numId })));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.file_numbering_failed"));
            }
        }
    }
}
