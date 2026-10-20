using System.Collections.Generic;

namespace OfficeCopilot.Agent.Runtime
{
    internal sealed class ModelClientMessage
    {
        public ModelClientMessage()
        {
            Role = string.Empty;
            Content = string.Empty;
            ContentParts = new List<ModelClientMessageContentPart>();
            Name = string.Empty;
            ToolCallId = string.Empty;
            ToolCalls = new List<ModelClientToolCall>();
        }

        public string Role { get; set; }

        public string Content { get; set; }

        public IList<ModelClientMessageContentPart> ContentParts { get; set; }

        public string Name { get; set; }

        public string ToolCallId { get; set; }

        public IList<ModelClientToolCall> ToolCalls { get; set; }
    }

    internal sealed class ModelClientMessageContentPart
    {
        public ModelClientMessageContentPart()
        {
            Type = string.Empty;
            Text = string.Empty;
            MediaType = string.Empty;
            Url = string.Empty;
            FileName = string.Empty;
            FileContentBase64 = string.Empty;
        }

        public string Type { get; set; }

        public string Text { get; set; }

        public string MediaType { get; set; }

        public string Url { get; set; }

        public string FileName { get; set; }

        public string FileContentBase64 { get; set; }
    }

    internal sealed class ModelClientTool
    {
        public ModelClientTool()
        {
            Name = string.Empty;
            Description = string.Empty;
            InputSchemaJson = "{}";
        }

        public string Name { get; set; }

        public string Description { get; set; }

        public string InputSchemaJson { get; set; }
    }

    internal sealed class ModelClientToolCall
    {
        public ModelClientToolCall()
        {
            Id = string.Empty;
            Name = string.Empty;
            ArgumentsJson = "{}";
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public string ArgumentsJson { get; set; }
    }

    internal sealed class ModelClientCompletion
    {
        public ModelClientCompletion()
        {
            Content = string.Empty;
            ReasoningContent = string.Empty;
            FinishReason = string.Empty;
            ToolCalls = new List<ModelClientToolCall>();
        }

        public string Content { get; set; }

        public string ReasoningContent { get; set; }

        public string FinishReason { get; set; }

        public IList<ModelClientToolCall> ToolCalls { get; set; }

        public bool HasToolCalls
        {
            get { return ToolCalls != null && ToolCalls.Count > 0; }
        }
    }

    internal sealed class ModelClientStreamingCallbacks
    {
        public ModelClientStreamingCallbacks()
        {
            OnContentDeltaAsync = null;
            OnReasoningDeltaAsync = null;
        }

        public System.Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task> OnContentDeltaAsync { get; set; }

        public System.Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task> OnReasoningDeltaAsync { get; set; }
    }
}
