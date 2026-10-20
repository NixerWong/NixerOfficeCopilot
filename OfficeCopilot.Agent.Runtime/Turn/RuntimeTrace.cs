using OfficeCopilot.Diagnostics.Logging;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    internal static class RuntimeTrace
    {
        public static void Write(string message)
        {
            OfficeCopilotLog.Write("Runtime", message);
        }
    }
}
