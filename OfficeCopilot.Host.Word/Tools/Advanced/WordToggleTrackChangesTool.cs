using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>Toggles track changes (revisions) on the active document.</summary>
    public sealed class WordToggleTrackChangesTool : WordComToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "word_toggle_track_changes",
            DisplayName = "Toggle Track Changes",
            Description = "Enables or disables track changes (revisions) on the active Word document.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Word",
            IsReadOnly = false,
            RequiresConfirmation = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"enable\":{\"type\":\"boolean\",\"description\":\"True to enable track changes, false to disable.\"}},\"required\":[\"enable\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var host = GetHost(context);
                var args = ParseArguments(request);
                var enable = ReadBool(args, "enable", false);

                var result = host.SetTrackChanges(new WordTrackChangesRequest { Enable = enable });
                if (result == null || !result.Success)
                {
                    return Task.FromResult(Fail(
                        request,
                        result == null || string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Failed to toggle track changes."
                            : result.ErrorMessage,
                        "word.track_changes_failed"));
                }

                return Task.FromResult(Success(
                    request,
                    "Track changes " + (result.Enabled ? "enabled" : "disabled") + ".",
                    JsonConvert.SerializeObject(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "word.track_changes_failed"));
            }
        }
    }
}
