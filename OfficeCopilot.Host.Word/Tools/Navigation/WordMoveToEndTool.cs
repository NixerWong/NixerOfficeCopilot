using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Moves the cursor (selection) to the end of the document.</summary>
    public sealed class WordMoveToEndTool : WordToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_move_to_end",
            DisplayName = "Move to End of Document",
            Description = "Moves the cursor (selection) to the very end of the document. Use this before inserting text or a table when you want to append content, and after inserting a table when you want to continue writing below it. Returns the new cursor position.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            // 仅移动光标/选区，不修改文档内容，因此视为只读（不需确认、不需备份）。
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var result = host.MoveToEnd();
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to move to end of document."
                            : result.ErrorMessage,
                        "word.move_to_end_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Cursor moved to end of document (position " + result.StartPosition + ", page " + result.PageNumber + ").",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.move_to_end_failed"));
            }
        }
    }
}
