using OfficeCopilot.Diagnostics.Logging;

namespace OfficeCopilot.Panel.WebView
{
    internal static class PanelTrace
    {
        public static void Write(string message)
        {
            OfficeCopilotLog.Write("Panel", message);
        }
    }
}
