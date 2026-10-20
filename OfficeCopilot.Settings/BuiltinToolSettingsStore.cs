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
        /// "Excel" → "builtin_tools.excel.json", "Word" → "builtin_tools.word.json"
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
        /// Load shared + app merged settings. If new files don't exist, migrates from main settings.json.
        /// </summary>
        public BuiltinToolSettings LoadMerged(string appName, OfficeCopilotSettings legacySettings = null)
        {
            var shared = LoadShared();
            var app = LoadApp(appName);

            if (app == null && legacySettings != null)
            {
                // Migration: extract app-specific tools from legacy settings.json
                app = MigrateFromLegacy(appName, legacySettings);
                if (app != null)
                {
                    SaveApp(appName, app);
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
                result.ToolGroupRegistrations.AddRange(shared.ToolGroupRegistrations);
                result.ToolRegistrations.AddRange(shared.ToolRegistrations);
                result.ToolConfirmationPolicies.AddRange(shared.ToolConfirmationPolicies);
                result.ToolDefinitionOverrides.AddRange(shared.ToolDefinitionOverrides);
            }

            if (app != null)
            {
                result.ToolGroupRegistrations.AddRange(app.ToolGroupRegistrations);
                result.ToolRegistrations.AddRange(app.ToolRegistrations);
                result.ToolConfirmationPolicies.AddRange(app.ToolConfirmationPolicies);
                result.ToolDefinitionOverrides.AddRange(app.ToolDefinitionOverrides);
            }

            return result;
        }

        // ======== Migration ========

        private static BuiltinToolSettings MigrateFromLegacy(string appName, OfficeCopilotSettings legacy)
        {
            if (legacy == null) return null;

            var appKey = (appName ?? string.Empty).ToLowerInvariant();
            var hasAppTools = legacy.ToolRegistrations?.Any(r =>
                r?.ToolName?.StartsWith(appKey + "_", StringComparison.OrdinalIgnoreCase) == true ||
                r?.ToolName?.StartsWith(appKey + ".", StringComparison.OrdinalIgnoreCase) == true) == true;

            var hasAppGroups = legacy.ToolGroupRegistrations?.Any(g =>
                g?.GroupName?.StartsWith(appKey + ".", StringComparison.OrdinalIgnoreCase) == true) == true;

            if (!hasAppTools && !hasAppGroups) return null;

            var result = CreateDefault();

            // Copy app-specific tool registrations
            if (legacy.ToolRegistrations != null)
            {
                foreach (var r in legacy.ToolRegistrations)
                {
                    if (r == null) continue;
                    var name = r.ToolName ?? string.Empty;
                    if (name.StartsWith(appKey + "_", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(appKey + ".", StringComparison.OrdinalIgnoreCase))
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
                    if (gn.StartsWith(appKey + ".", StringComparison.OrdinalIgnoreCase))
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
                    if (name.StartsWith(appKey + "_", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(appKey + ".", StringComparison.OrdinalIgnoreCase))
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
                    if (name.StartsWith(appKey + "_", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(appKey + ".", StringComparison.OrdinalIgnoreCase))
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
