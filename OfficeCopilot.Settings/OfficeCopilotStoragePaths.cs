using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Settings
{
    public static class OfficeCopilotStoragePaths
    {
        public static string GetRootFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NixerOfficeCopilot");
        }

        public static string GetSettingsFilePath()
        {
            return Path.Combine(GetRootFolder(), "settings.json");
        }

        public static string GetConversationsFolder()
        {
            return Path.Combine(GetRootFolder(), "conversations");
        }

        public static string GetConversationIndexFilePath()
        {
            return Path.Combine(GetConversationsFolder(), "index.json");
        }

        public static string GetConversationFilePath(string sessionId)
        {
            return Path.Combine(GetConversationsFolder(), sessionId + ".json");
        }

        public static string GetLogsFolder()
        {
            return Path.Combine(GetRootFolder(), "logs");
        }

        public static string GetApplicationLogFilePath()
        {
            return Path.Combine(GetLogsFolder(), "application.log");
        }
    }
}
