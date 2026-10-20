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
    internal static class OfficeCopilotJson
    {
        private static readonly JsonSerializerSettings SerializerSettings = CreateSettings();

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.Indented, SerializerSettings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, SerializerSettings);
        }

        public static JsonSerializer CreateSerializer()
        {
            return JsonSerializer.Create(CreateSettings());
        }

        private static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings();
            settings.NullValueHandling = NullValueHandling.Ignore;
            settings.Formatting = Formatting.Indented;
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }
    }
}
