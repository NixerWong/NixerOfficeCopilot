using System;
using System.IO;

namespace OfficeCopilot.Slides.Fonts
{
    public static class FontInstallConstants
    {
        public const string FileNamePrefix = "NixerOfficeCopilot_";
        public const string LegacyFileNamePrefix = "OfficeCopilot-";

        public static string DefaultManifestPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NixerOfficeCopilot",
                    "fonts",
                    "installed.json");
            }
        }
    }
}
