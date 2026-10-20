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
    public sealed class JsonSettingsStore : ISettingsStore
    {
        private readonly string _filePath;
        private readonly OfficeCopilotSettingsValidator _validator;

        public JsonSettingsStore()
            : this(OfficeCopilotStoragePaths.GetSettingsFilePath(), new OfficeCopilotSettingsValidator())
        {
        }

        public JsonSettingsStore(string filePath, OfficeCopilotSettingsValidator validator)
        {
            _filePath = filePath;
            _validator = validator ?? new OfficeCopilotSettingsValidator();
        }

        public OfficeCopilotSettings Load()
        {
            if (!File.Exists(_filePath))
            {
                return OfficeCopilotSettingsFactory.CreateDefault();
            }

            var json = File.ReadAllText(_filePath, System.Text.Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return OfficeCopilotSettingsFactory.CreateDefault();
            }

            var settings = DeserializeSettingsWithMigration(json);
            NormalizeSettings(settings);
            return settings;
        }

        public void Save(OfficeCopilotSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            NormalizeSettings(settings);

            var validationResult = _validator.Validate(settings);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException("设置校验失败：" + Environment.NewLine + string.Join(Environment.NewLine, validationResult.Errors));
            }

            settings.UpdatedAtUtc = DateTime.UtcNow;
            if (settings.CreatedAtUtc == default(DateTime))
            {
                settings.CreatedAtUtc = settings.UpdatedAtUtc;
            }

            EnsureDirectory(Path.GetDirectoryName(_filePath));

            var json = OfficeCopilotJson.Serialize(settings);
            File.WriteAllText(_filePath, json, System.Text.Encoding.UTF8);
        }

        private static OfficeCopilotSettings DeserializeSettingsWithMigration(string json)
        {
            JObject root;
            try
            {
                root = JToken.Parse(json) as JObject;
            }
            catch
            {
                return OfficeCopilotSettingsFactory.CreateDefault();
            }

            if (root == null)
            {
                return OfficeCopilotSettingsFactory.CreateDefault();
            }

            var legacyIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            MigrateModelProviderIds(root, legacyIdMap);
            MigrateDefaultModelProviderId(root, legacyIdMap);

            var serializer = OfficeCopilotJson.CreateSerializer();
            return root.ToObject<OfficeCopilotSettings>(serializer) ?? OfficeCopilotSettingsFactory.CreateDefault();
        }

        private static void MigrateModelProviderIds(JObject root, IDictionary<string, int> legacyIdMap)
        {
            var providers = root["ModelProviders"] as JArray;
            if (providers == null || providers.Count == 0)
            {
                return;
            }

            var maxId = 0;
            foreach (var item in providers.OfType<JObject>())
            {
                var currentId = TryReadPositiveInt(item["Id"]);
                if (currentId > maxId)
                {
                    maxId = currentId;
                }
            }

            var nextId = maxId <= 0 ? 1 : maxId + 1;
            var usedIds = new HashSet<int>();

            foreach (var item in providers.OfType<JObject>())
            {
                var originalToken = item["Id"];
                var originalKey = GetTokenKey(originalToken);

                var currentId = TryReadPositiveInt(originalToken);
                if (currentId <= 0 || usedIds.Contains(currentId))
                {
                    currentId = nextId++;
                }

                item["Id"] = currentId;
                usedIds.Add(currentId);

                if (!string.IsNullOrWhiteSpace(originalKey) && !legacyIdMap.ContainsKey(originalKey))
                {
                    legacyIdMap.Add(originalKey, currentId);
                }

                var currentKey = currentId.ToString();
                if (!legacyIdMap.ContainsKey(currentKey))
                {
                    legacyIdMap.Add(currentKey, currentId);
                }
            }
        }

        private static void MigrateDefaultModelProviderId(JObject root, IDictionary<string, int> legacyIdMap)
        {
            var token = root["DefaultModelProviderId"];
            if (token == null)
            {
                return;
            }

            var currentId = TryReadPositiveInt(token);
            if (currentId > 0)
            {
                root["DefaultModelProviderId"] = currentId;
                return;
            }

            var originalKey = GetTokenKey(token);
            int mappedId;
            if (!string.IsNullOrWhiteSpace(originalKey) && legacyIdMap.TryGetValue(originalKey, out mappedId))
            {
                root["DefaultModelProviderId"] = mappedId;
            }
            else
            {
                root["DefaultModelProviderId"] = JValue.CreateNull();
            }
        }

        private static int TryReadPositiveInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0;
            }

            if (token.Type == JTokenType.Integer)
            {
                var intValue = token.Value<int>();
                return intValue > 0 ? intValue : 0;
            }

            int parsed;
            if (int.TryParse(token.ToString(), out parsed) && parsed > 0)
            {
                return parsed;
            }

            return 0;
        }

        private static string GetTokenKey(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            return token.ToString();
        }

        private static void NormalizeSettings(OfficeCopilotSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (settings.ConfigurationVersion <= 0)
            {
                settings.ConfigurationVersion = 2;
            }

            if (settings.ChatDefaults == null)
            {
                settings.ChatDefaults = new ChatDefaultsSettings();
            }

            if (settings.ModelProviders == null)
            {
                settings.ModelProviders = new List<ModelProviderSettings>();
            }

            if (settings.McpServers == null)
            {
                settings.McpServers = new List<McpServerSettings>();
            }

            if (settings.Logging == null)
            {
                settings.Logging = new LoggingSettings();
            }

            NormalizeLoggingSettings(settings.Logging);

            var maxModelId = settings.ModelProviders
                .Where(p => p != null && p.Id > 0)
                .Select(p => p.Id)
                .DefaultIfEmpty(0)
                .Max();

            var nextModelId = maxModelId <= 0 ? 1 : maxModelId + 1;
            var usedModelIds = new HashSet<int>();

            foreach (var provider in settings.ModelProviders)
            {
                if (provider == null)
                {
                    continue;
                }

                if (provider.Id <= 0 || usedModelIds.Contains(provider.Id))
                {
                    provider.Id = nextModelId++;
                }

                usedModelIds.Add(provider.Id);

                if (provider.SystemPrompt == null)
                {
                    provider.SystemPrompt = string.Empty;
                }
            }

            if (settings.DefaultModelProviderId.HasValue &&
                !settings.ModelProviders.Any(m => m != null && m.Id == settings.DefaultModelProviderId.Value))
            {
                settings.DefaultModelProviderId = null;
            }

            foreach (var server in settings.McpServers)
            {
                if (server == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(server.Id))
                {
                    server.Id = Guid.NewGuid().ToString("N");
                }

                if (server.Stdio != null)
                {
                    if (server.Stdio.Arguments == null)
                    {
                        server.Stdio.Arguments = new List<string>();
                    }

                    if (server.Stdio.EnvironmentVariables == null)
                    {
                        server.Stdio.EnvironmentVariables = new List<NameValueItem>();
                    }
                }

                if (server.Http != null)
                {
                    if (server.Http.Headers == null)
                    {
                        server.Http.Headers = new List<NameValueItem>();
                    }
                }
            }
        }

        private static void NormalizeLoggingSettings(LoggingSettings logging)
        {
            if (logging == null) { return; }
            if (logging.Runtime == null)
            {
                logging.Runtime = new RuntimeLoggingSettings();
            }

            if (logging.ModelTranscripts == null)
            {
                logging.ModelTranscripts = new ModelTranscriptLoggingSettings();
            }

            if (string.IsNullOrWhiteSpace(logging.Runtime.ApplicationLogFileName))
            {
                logging.Runtime.ApplicationLogFileName = "application.log";
            }
            else
            {
                logging.Runtime.ApplicationLogFileName = Path.GetFileName(logging.Runtime.ApplicationLogFileName.Trim());
            }

            if (logging.Runtime.MaxFileSizeMb <= 0)
            {
                logging.Runtime.MaxFileSizeMb = 5;
            }

            if (logging.Runtime.RetainedFileCountLimit <= 0)
            {
                logging.Runtime.RetainedFileCountLimit = 10;
            }

            if (string.IsNullOrWhiteSpace(logging.ModelTranscripts.FileName))
            {
                logging.ModelTranscripts.FileName = "model-transcript.log";
            }
            else
            {
                logging.ModelTranscripts.FileName = Path.GetFileName(logging.ModelTranscripts.FileName.Trim());
            }

            if (logging.ModelTranscripts.MaxContentLength < 0)
            {
                logging.ModelTranscripts.MaxContentLength = 0;
            }
        }


        private static void EnsureDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
    }
}
