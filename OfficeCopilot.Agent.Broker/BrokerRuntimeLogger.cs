using OfficeCopilot.Diagnostics.Logging;
using System;

namespace OfficeCopilot.Agent.Broker
{
    internal static class BrokerRuntimeLogger
    {
        public static void Initialize()
        {
            OfficeCopilotLog.InitializeDefault();
        }

        public static void Info(string source, string message)
        {
            OfficeCopilotLog.Info("Broker." + source, message);
        }

        public static void Warning(string source, string message)
        {
            OfficeCopilotLog.Warning("Broker." + source, message);
        }

        public static void Error(string source, string message)
        {
            OfficeCopilotLog.Error("Broker." + source, message);
        }

        public static void Error(string source, string message, Exception exception)
        {
            OfficeCopilotLog.Error("Broker." + source, message, exception);
        }
    }
}
