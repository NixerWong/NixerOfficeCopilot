using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace OfficeCopilot.Diagnostics.Logging
{
    public sealed class OfficeCopilotLogOptions
    {
        public OfficeCopilotLogOptions()
        {
            IsEnabled = true;
            MinimumLevel = LogLevel.Warning;
            EnableDebugTrace = false;
            EnableFile = true;
            IncludeScopes = true;
            RootFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "logs");
            ApplicationLogFileName = "application.log";
            MaxFileSizeBytes = 5 * 1024 * 1024;
            RetainedFileCountLimit = 10;
        }

        public bool IsEnabled { get; set; }

        public LogLevel MinimumLevel { get; set; }

        public bool EnableDebugTrace { get; set; }

        public bool EnableFile { get; set; }

        public bool IncludeScopes { get; set; }

        public string RootFolder { get; set; }

        public string ApplicationLogFileName { get; set; }

        public long MaxFileSizeBytes { get; set; }

        public int RetainedFileCountLimit { get; set; }
    }
}