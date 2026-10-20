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
    public static class DocumentKeyBuilder
    {
        public static string Build(string hostApplication, string documentPath, string documentName)
        {
            var host = string.IsNullOrWhiteSpace(hostApplication) ? "UnknownHost" : hostApplication.Trim();

            if (!string.IsNullOrWhiteSpace(documentPath))
            {
                var normalizedPath = Path.GetFullPath(documentPath).Trim().Replace('/', '\\').ToLowerInvariant();
                return host + ":" + normalizedPath;
            }

            var fallbackName = string.IsNullOrWhiteSpace(documentName) ? "UnsavedDocument" : documentName.Trim();
            return host + ":unsaved:" + fallbackName.ToLowerInvariant();
        }
    }
}
