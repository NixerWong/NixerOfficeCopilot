using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.OpenXml.Word;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Adds, removes or finds a text watermark in a .docx file.</summary>
    public sealed class WordFileWatermarkTool : WordFileToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_file_watermark",
            DisplayName = "Watermark",
            Description = "Adds, removes or finds a classic text watermark in a .docx file (VML).",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"filePath\":{\"type\":\"string\",\"description\":\"Path to the .docx file.\"},\"action\":{\"type\":\"string\",\"description\":\"add | remove | find. Default add.\"},\"text\":{\"type\":\"string\",\"description\":\"Watermark text. Default DRAFT.\"},\"color\":{\"type\":\"string\",\"description\":\"Color (hex or name). Default silver.\"},\"font\":{\"type\":\"string\",\"description\":\"Font name. Default Calibri.\"},\"size\":{\"type\":\"string\",\"description\":\"Font size, e.g. 80pt.\"},\"rotation\":{\"type\":\"string\",\"description\":\"Rotation degrees. Default 315.\"},\"opacity\":{\"type\":\"string\",\"description\":\"Opacity. Default .5.\"}},\"required\":[\"filePath\"],\"additionalProperties\":false}"
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
                    return Task.FromResult(Fail(request, "filePath is required.", "word.file_watermark_invalid_arguments"));
                }

                var action = ReadString(args, "action", "add").ToLowerInvariant();

                if (action == "find")
                {
                    using (var engine = new WordDocumentEngine(filePath, false))
                    {
                        var found = engine.FindWatermark();
                        return Task.FromResult(Success(
                            request,
                            found == null ? "No watermark found." : "Watermark: " + found,
                            JsonConvert.SerializeObject(new { filePath, watermark = found })));
                    }
                }

                using (var engine = new WordDocumentEngine(filePath, true))
                {
                    if (action == "remove")
                    {
                        engine.RemoveWatermark();
                        engine.Save();
                        return Task.FromResult(Success(
                            request,
                            "Removed watermark from: " + filePath,
                            JsonConvert.SerializeObject(new { filePath, action = "remove" })));
                    }

                    var options = new WatermarkOptions
                    {
                        Text = ReadString(args, "text", "DRAFT"),
                        Color = ReadString(args, "color", "silver"),
                        Font = ReadString(args, "font", "Calibri"),
                        Size = ReadString(args, "size", "1pt"),
                        Rotation = ReadString(args, "rotation", "315"),
                        Opacity = ReadString(args, "opacity", ".5")
                    };
                    engine.AddWatermark(options);
                    engine.Save();
                    return Task.FromResult(Success(
                        request,
                        "Added watermark '" + options.Text + "' to: " + filePath,
                        JsonConvert.SerializeObject(new { filePath, action = "add", options.Text })));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.file_watermark_failed"));
            }
        }
    }
}
