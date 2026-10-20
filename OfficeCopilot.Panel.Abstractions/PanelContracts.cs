namespace OfficeCopilot.Panel.Abstractions
{
    public static class PanelMessageTypes
    {
        public const string Ready = "ready";
        public const string WelcomeMessage = "welcomeMessage";
        public const string RequestHostContext = "requestHostContext";
        public const string HostContext = "hostContext";
        public const string UserPrompt = "userPrompt";
        public const string AssistantMessage = "assistantMessage";
        public const string InsertTextToHost = "insertTextToHost";
        public const string Status = "status";
        public const string Error = "error";
    }

    public sealed class PanelEnvelope
    {
        public string Type { get; set; }

        public string Text { get; set; }

        public HostContextDto Context { get; set; }
    }

    public sealed class HostContextDto
    {
        public string HostApplicationName { get; set; }

        public string DocumentName { get; set; }

        public string WorksheetName { get; set; }

        public string SelectionAddress { get; set; }

        public string SelectionPreview { get; set; }

        public string Summary { get; set; }
    }
}
