using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OfficeCopilot.Settings.Abstractions;

namespace OfficeCopilot.Settings
{
    /// <summary>
    /// Loads and saves built-in tool configuration from:
    ///   builtin_tools.shared.json   — shared tools (runtime.*)
    ///   builtin_tools.{app}.json    — app-specific tools (excel.*, word.*, ppt.*)
    /// </summary>
    public sealed class BuiltinToolSettingsStore
    {
        private const string SharedFileName = "builtin_tools.shared.json";
        private const string AppFileNamePattern = "builtin_tools.{0}.json";

        private readonly string _rootFolder;

        public BuiltinToolSettingsStore()
            : this(OfficeCopilotStoragePaths.GetRootFolder())
        {
        }

        public BuiltinToolSettingsStore(string rootFolder)
        {
            _rootFolder = rootFolder ?? OfficeCopilotStoragePaths.GetRootFolder();
        }

        /// <summary>
        /// "Excel" → "builtin_tools.excel.json", "Word" → "builtin_tools.word.json",
        /// "PowerPoint" → "builtin_tools.powerpoint.json".
        /// </summary>
        private string GetAppFilePath(string appName)
        {
            var appKey = (appName ?? string.Empty).ToLowerInvariant();
            return Path.Combine(_rootFolder, string.Format(AppFileNamePattern, appKey));
        }

        private string GetSharedFilePath()
        {
            return Path.Combine(_rootFolder, SharedFileName);
        }

        // ======== Load ========

        /// <summary>
        /// Load shared + app settings and migrate missing app entries from the legacy settings.json.
        /// </summary>
        public BuiltinToolSettings LoadMerged(string appName, OfficeCopilotSettings legacySettings = null)
        {
            var shared = LoadShared();
            var app = LoadApp(appName);

            if (legacySettings != null)
            {
                var legacyApp = MigrateFromLegacy(appName, legacySettings);
                if (legacyApp != null)
                {
                    bool changed;
                    app = MergeMissingLegacySettings(legacyApp, app, out changed);
                    if (changed)
                    {
                        SaveApp(appName, app);
                    }
                }
            }

            return Merge(shared, app);
        }

        public BuiltinToolSettings LoadShared()
        {
            return LoadFile(GetSharedFilePath()) ?? CreateDefault();
        }

        public BuiltinToolSettings LoadApp(string appName)
        {
            return LoadFile(GetAppFilePath(appName));
        }

        // ======== Save ========

        public void SaveShared(BuiltinToolSettings settings)
        {
            SaveFile(GetSharedFilePath(), settings);
        }

        public void SaveApp(string appName, BuiltinToolSettings settings)
        {
            SaveFile(GetAppFilePath(appName), settings);
        }

        // ======== Merge ========

        private static BuiltinToolSettings Merge(BuiltinToolSettings shared, BuiltinToolSettings app)
        {
            var result = CreateDefault();

            if (shared != null)
            {
                AddRangeIfNotNull(result.ToolGroupRegistrations, shared.ToolGroupRegistrations);
                AddRangeIfNotNull(result.ToolRegistrations, shared.ToolRegistrations);
                AddRangeIfNotNull(result.ToolConfirmationPolicies, shared.ToolConfirmationPolicies);
                AddRangeIfNotNull(result.ToolDefinitionOverrides, shared.ToolDefinitionOverrides);
            }

            if (app != null)
            {
                AddRangeIfNotNull(result.ToolGroupRegistrations, app.ToolGroupRegistrations);
                AddRangeIfNotNull(result.ToolRegistrations, app.ToolRegistrations);
                AddRangeIfNotNull(result.ToolConfirmationPolicies, app.ToolConfirmationPolicies);
                AddRangeIfNotNull(result.ToolDefinitionOverrides, app.ToolDefinitionOverrides);
            }

            return result;
        }

        private static void AddRangeIfNotNull<T>(ICollection<T> target, IEnumerable<T> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var item in source)
            {
                target.Add(item);
            }
        }

        // ======== Migration ========

        private static BuiltinToolSettings MigrateFromLegacy(string appName, OfficeCopilotSettings legacy)
        {
            if (legacy == null) return null;

            var appKey = (appName ?? string.Empty).ToLowerInvariant();
            var hasAppTools = legacy.ToolRegistrations?.Any(r =>
                r != null && IsAppToolName(appKey, r.ToolName)) == true;

            var hasAppGroups = legacy.ToolGroupRegistrations?.Any(g =>
                g != null && IsAppGroupName(appKey, g.GroupName)) == true;

            if (!hasAppTools && !hasAppGroups) return null;

            var result = CreateDefault();

            // Copy app-specific tool registrations
            if (legacy.ToolRegistrations != null)
            {
                foreach (var r in legacy.ToolRegistrations)
                {
                    if (r == null) continue;
                    var name = r.ToolName ?? string.Empty;
                    if (IsAppToolName(appKey, name))
                    {
                        result.ToolRegistrations.Add(new ToolRegistrationSettings
                        {
                            GroupName = r.GroupName ?? name,
                            ToolName = name,
                            IsRegistered = r.IsRegistered,
                            UpdatedAtUtc = r.UpdatedAtUtc
                        });
                    }
                }
            }

            // Copy app-specific group registrations
            if (legacy.ToolGroupRegistrations != null)
            {
                foreach (var g in legacy.ToolGroupRegistrations)
                {
                    if (g == null) continue;
                    var gn = g.GroupName ?? string.Empty;
                    if (IsAppGroupName(appKey, gn))
                    {
                        result.ToolGroupRegistrations.Add(new ToolGroupRegistrationSettings
                        {
                            GroupName = gn,
                            IsRegistered = g.IsRegistered,
                            UpdatedAtUtc = g.UpdatedAtUtc
                        });
                    }
                }
            }

            // Copy confirmation policies and overrides for app tools
            if (legacy.ToolConfirmationPolicies != null)
            {
                foreach (var p in legacy.ToolConfirmationPolicies)
                {
                    if (p == null) continue;
                    var name = p.ToolName ?? string.Empty;
                    if (IsAppToolName(appKey, name))
                    {
                        result.ToolConfirmationPolicies.Add(new ToolConfirmationPolicySettings
                        {
                            ToolName = name,
                            Mode = p.Mode,
                            UpdatedAtUtc = p.UpdatedAtUtc
                        });
                    }
                }
            }

            if (legacy.ToolDefinitionOverrides != null)
            {
                foreach (var o in legacy.ToolDefinitionOverrides)
                {
                    if (o == null) continue;
                    var name = o.ToolName ?? string.Empty;
                    if (IsAppToolName(appKey, name))
                    {
                        result.ToolDefinitionOverrides.Add(new ToolDefinitionOverrideSettings
                        {
                            ToolName = name,
                            DisplayName = o.DisplayName,
                            Description = o.Description,
                            RequiresConfirmation = o.RequiresConfirmation,
                            RequiresBackup = o.RequiresBackup,
                            InputSchemaJson = o.InputSchemaJson,
                            OutputSchemaJson = o.OutputSchemaJson,
                            UpdatedAtUtc = o.UpdatedAtUtc
                        });
                    }
                }
            }

            // Only return if we actually found something
            if (result.ToolRegistrations.Count == 0 &&
                result.ToolGroupRegistrations.Count == 0 &&
                result.ToolConfirmationPolicies.Count == 0 &&
                result.ToolDefinitionOverrides.Count == 0)
            {
                return null;
            }

            return result;
        }

        private static BuiltinToolSettings MergeMissingLegacySettings(
            BuiltinToolSettings legacy,
            BuiltinToolSettings app,
            out bool changed)
        {
            changed = app == null;
            if (app == null)
            {
                return legacy;
            }

            var result = new BuiltinToolSettings
            {
                ConfigurationVersion = app.ConfigurationVersion,
                CreatedAtUtc = app.CreatedAtUtc,
                UpdatedAtUtc = app.UpdatedAtUtc
            };

            CopyMissing(
                legacy.ToolGroupRegistrations,
                app.ToolGroupRegistrations,
                result.ToolGroupRegistrations,
                item => item.GroupName,
                ref changed);
            AddRangeIfNotNull(result.ToolGroupRegistrations, app.ToolGroupRegistrations);

            CopyMissing(
                legacy.ToolRegistrations,
                app.ToolRegistrations,
                result.ToolRegistrations,
                item => item.ToolName,
                ref changed);
            AddRangeIfNotNull(result.ToolRegistrations, app.ToolRegistrations);

            CopyMissing(
                legacy.ToolConfirmationPolicies,
                app.ToolConfirmationPolicies,
                result.ToolConfirmationPolicies,
                item => item.ToolName,
                ref changed);
            AddRangeIfNotNull(result.ToolConfirmationPolicies, app.ToolConfirmationPolicies);

            CopyMissing(
                legacy.ToolDefinitionOverrides,
                app.ToolDefinitionOverrides,
                result.ToolDefinitionOverrides,
                item => item.ToolName,
                ref changed);
            AddRangeIfNotNull(result.ToolDefinitionOverrides, app.ToolDefinitionOverrides);
            return result;
        }

        private static void CopyMissing<T>(
            IEnumerable<T> legacyItems,
            IEnumerable<T> appItems,
            ICollection<T> target,
            Func<T, string> getKey,
            ref bool changed)
            where T : class
        {
            if (legacyItems == null)
            {
                return;
            }

            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (appItems != null)
            {
                foreach (var item in appItems)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var key = getKey(item);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        existingKeys.Add(key);
                    }
                }
            }

            foreach (var item in legacyItems)
            {
                if (item == null)
                {
                    continue;
                }

                var key = getKey(item);
                if (string.IsNullOrWhiteSpace(key)
                    || existingKeys.Contains(key))
                {
                    continue;
                }

                target.Add(item);
                existingKeys.Add(key);
                changed = true;
            }
        }

        private static bool IsAppToolName(string appKey, string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            return GetAppKeyPrefixes(appKey).Any(prefix =>
                toolName.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)
                || toolName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsAppGroupName(string appKey, string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return false;
            }

            return GetAppKeyPrefixes(appKey).Any(prefix =>
                groupName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
        }

        private static string[] GetAppKeyPrefixes(string appKey)
        {
            return string.Equals(appKey, "powerpoint", StringComparison.OrdinalIgnoreCase)
                ? new[] { "powerpoint", "ppt" }
                : new[] { appKey };
        }

        // ======== Helpers ========

        private static BuiltinToolSettings LoadFile(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                var json = File.ReadAllText(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<BuiltinToolSettings>(json);
                return settings;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveFile(string filePath, BuiltinToolSettings settings)
        {
            if (settings == null) return;

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            settings.UpdatedAtUtc = DateTime.UtcNow;
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        private static BuiltinToolSettings CreateDefault()
        {
            return new BuiltinToolSettings();
        }
    }
}
