using OfficeCopilot.Diagnostics.Logging;
using System;

namespace OfficeCopilot.HostBridge.Server
{
    internal static class HostBridgeRuntimeLogger
    {
        public static void Initialize()
        {
            OfficeCopilotLog.InitializeDefault();
        }

        public static void Info(string source, string message)
        {
            OfficeCopilotLog.Info("HostBridge." + source, message);
        }

        public static void Warning(string source, string message)
        {
            OfficeCopilotLog.Warning("HostBridge." + source, message);
        }

        public static void Error(string source, string message)
        {
            OfficeCopilotLog.Error("HostBridge." + source, message);
        }

        public static void Error(string source, string message, Exception exception)
        {
            OfficeCopilotLog.Error("HostBridge." + source, message, exception);
        }
    }
}
