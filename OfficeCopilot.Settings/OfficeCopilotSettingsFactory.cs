using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Settings.Abstractions;
using OfficeCopilot.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Settings
{
    public static class OfficeCopilotSettingsFactory
    {
        public static OfficeCopilotSettings CreateDefault()
        {
            return Normalize(new OfficeCopilotSettings());
        }

        public static OfficeCopilotSettings Normalize(OfficeCopilotSettings settings)
        {
            settings = settings ?? new OfficeCopilotSettings();
            var applyAttachmentDefaults = settings.ConfigurationVersion < 3;

            if (settings.ChatDefaults == null)
            {
                settings.ChatDefaults = new ChatDefaultsSettings();
            }

            settings.ChatDefaults = NormalizeChatDefaults(settings.ChatDefaults, applyAttachmentDefaults);

            if (settings.ModelProviders == null)
            {
                settings.ModelProviders = new List<ModelProviderSettings>();
            }
            else
            {
                foreach (var modelProvider in settings.ModelProviders.Where(p => p != null))
                {
                    NormalizeModelProvider(modelProvider);
                }
            }

            if (settings.McpServers == null)
            {
                settings.McpServers = new List<McpServerSettings>();
            }
            else
            {
                settings.McpServers = NormalizeMcpServers(settings.McpServers);
            }

            if (settings.ToolConfirmationPolicies == null)
            {
                settings.ToolConfirmationPolicies = new List<ToolConfirmationPolicySettings>();
            }

            if (settings.ToolGroupRegistrations == null)
            {
                settings.ToolGroupRegistrations = new List<ToolGroupRegistrationSettings>();
            }
            else
            {
                settings.ToolGroupRegistrations = NormalizeToolGroupRegistrations(settings.ToolGroupRegistrations);
            }

            if (settings.ToolRegistrations == null)
            {
                settings.ToolRegistrations = new List<ToolRegistrationSettings>();
            }
            else
            {
                settings.ToolRegistrations = NormalizeToolRegistrations(settings.ToolRegistrations);
            }

            if (settings.ToolDefinitionOverrides == null)
            {
                settings.ToolDefinitionOverrides = new List<ToolDefinitionOverrideSettings>();
            }
            else
            {
                settings.ToolDefinitionOverrides = NormalizeToolDefinitionOverrides(settings.ToolDefinitionOverrides);
            }

            settings.ExcelToolTrustMode = NormalizeToolTrustMode(settings.ExcelToolTrustMode);
            settings.WordToolTrustMode = NormalizeToolTrustMode(settings.WordToolTrustMode);
            settings.PowerPointToolTrustMode = NormalizeToolTrustMode(settings.PowerPointToolTrustMode);

            if (settings.Logging == null)
            {
                settings.Logging = new LoggingSettings();
            }
            else
            {
                if (settings.Logging.Runtime == null)
                {
                    settings.Logging.Runtime = new RuntimeLoggingSettings();
                }

                if (settings.Logging.ModelTranscripts == null)
                {
                    settings.Logging.ModelTranscripts = new ModelTranscriptLoggingSettings();
                }

                if (settings.Logging.ToolCalls == null)
                {
                    settings.Logging.ToolCalls = new ToolCallLoggingSettings();
                }
            }

            if (settings.Runtime == null)
            {
                settings.Runtime = new RuntimeSettings();
            }
            else
            {
                settings.Runtime = NormalizeRuntime(settings.Runtime);
            }

            settings.PowerPointSlides = NormalizePowerPointSlides(settings.PowerPointSlides);
            settings.UiLanguage = SupportedLanguages.NormalizePreference(settings.UiLanguage);

            settings.ConfigurationVersion = 8;
            return settings;
        }

        /// <summary>
        /// 读取指定宿主的工具执行权限模式；未设置或非法时返回提示确认。
        /// </summary>
        public static string GetToolTrustMode(OfficeCopilotSettings settings, string hostApplicationName)
        {
            return NormalizeToolTrustMode(ReadStoredToolTrustMode(settings, HostApplicationNames.Normalize(hostApplicationName)));
        }

        /// <summary>
        /// 设置指定宿主的工具执行权限模式；非法值按提示确认保存。
        /// </summary>
        public static void SetToolTrustMode(OfficeCopilotSettings settings, string hostApplicationName, string mode)
        {
            if (settings == null)
            {
                return;
            }

            WriteStoredToolTrustMode(settings, HostApplicationNames.Normalize(hostApplicationName), NormalizeToolTrustMode(mode));
        }

        /// <summary>
        /// 写回前用最新存储的值刷新 <paramref name="target"/> 的 TrustMode，
        /// 避免其他进程（其他宿主）或其他窗口的修改被旧缓存覆盖。
        /// <paramref name="keepHostApplicationName"/> 指定的宿主保留 target 当前值，其余宿主取 latest 的值；为空则全部取 latest。
        /// </summary>
        public static void RefreshToolTrustModes(OfficeCopilotSettings target, OfficeCopilotSettings latest, string keepHostApplicationName = null)
        {
            if (target == null || latest == null)
            {
                return;
            }

            var keepKey = string.IsNullOrWhiteSpace(keepHostApplicationName)
                ? null
                : HostApplicationNames.Normalize(keepHostApplicationName);
            foreach (var hostKey in KnownHostKeys)
            {
                var source = string.Equals(hostKey, keepKey, StringComparison.OrdinalIgnoreCase) ? target : latest;
                WriteStoredToolTrustMode(target, hostKey, GetToolTrustMode(source, hostKey));
            }
        }

        private static string NormalizeToolTrustMode(string mode)
        {
            return ToolTrustModeValues.IsValid(mode) ? mode : ToolTrustModeValues.NeedConfirm;
        }

        private static string ReadStoredToolTrustMode(OfficeCopilotSettings settings, string hostKey)
        {
            if (settings == null)
            {
                return null;
            }

            switch (hostKey)
            {
                case HostApplicationNames.Excel: return settings.ExcelToolTrustMode;
                case HostApplicationNames.Word: return settings.WordToolTrustMode;
                case HostApplicationNames.PowerPoint: return settings.PowerPointToolTrustMode;
                default: return null;
            }
        }

        private static void WriteStoredToolTrustMode(OfficeCopilotSettings settings, string hostKey, string mode)
        {
            switch (hostKey)
            {
                case HostApplicationNames.Excel: settings.ExcelToolTrustMode = mode; break;
                case HostApplicationNames.Word: settings.WordToolTrustMode = mode; break;
                case HostApplicationNames.PowerPoint: settings.PowerPointToolTrustMode = mode; break;
            }
        }

        private static readonly string[] KnownHostKeys =
        {
            HostApplicationNames.Excel,
            HostApplicationNames.Word,
            HostApplicationNames.PowerPoint
        };

        private static PowerPointSlidesSettings NormalizePowerPointSlides(PowerPointSlidesSettings settings)
        {
            settings = settings ?? new PowerPointSlidesSettings();
            settings.DefaultStyle = (settings.DefaultStyle ?? string.Empty).Trim();
            var density = (settings.DefaultDensity ?? string.Empty).Trim().ToLowerInvariant();
            settings.DefaultDensity = density == "low" || density == "high" ? density : "medium";
            var mode = (settings.DefaultBuildMode ?? string.Empty).Trim().ToLowerInvariant();
            settings.DefaultBuildMode = mode == "editable" || mode == "image" ? mode : "fidelity";
            settings.RepairIterationCap = Math.Max(0, Math.Min(10, settings.RepairIterationCap));
            settings.WorkspaceRetentionDays = Math.Max(1, Math.Min(3650, settings.WorkspaceRetentionDays));
            return settings;
        }

        public static ModelProviderSettings NormalizeModelProvider(ModelProviderSettings model)
        {
            model = model ?? new ModelProviderSettings();

            if (string.IsNullOrWhiteSpace(model.SupportedInputFileTypes))
            {
                model.SupportedInputFileTypes = ModelProviderSettings.DefaultSupportedInputFileTypes;
            }
            else
            {
                model.SupportedInputFileTypes = model.SupportedInputFileTypes.Trim();
            }

            if (model.SystemPrompt == null)
            {
                model.SystemPrompt = string.Empty;
            }

            if (model.MaxContextTokens <= 0)
            {
                model.MaxContextTokens = ModelProviderSettings.DefaultMaxContextTokens;
            }

            model.AdditionalParameters = NormalizeAdditionalParameters(model);
            model.HttpHeaders = NormalizeHttpHeaders(model.HttpHeaders);
            model.SetStream(model.GetStream());
            model.SetTemperature(model.GetTemperature());
            model.SetTopP(model.GetTopP());
            model.SetMaxTokens(model.GetMaxTokens());

            if (!model.AdditionalParameters.ContainsKey("stream"))
            {
                model.AdditionalParameters["stream"] = false;
            }

            return model;
        }

        private static ChatDefaultsSettings NormalizeChatDefaults(ChatDefaultsSettings chatDefaults, bool applyAttachmentDefaults)
        {
            chatDefaults = chatDefaults ?? new ChatDefaultsSettings();

            if (chatDefaults.RequestTimeoutSeconds <= 0)
            {
                chatDefaults.RequestTimeoutSeconds = 60;
            }

            if (chatDefaults.AttachmentFileCountThreshold <= 0)
            {
                chatDefaults.AttachmentFileCountThreshold = 5;
            }

            chatDefaults.MaxVisualPages = Math.Max(0, Math.Min(50, chatDefaults.MaxVisualPages));
            chatDefaults.AttachmentInlineCharBudget = Math.Max(1000, Math.Min(200000, chatDefaults.AttachmentInlineCharBudget));

            if (chatDefaults.NativeThresholdKb <= 0)
            {
                chatDefaults.NativeThresholdKb = 1024;
            }

            if (chatDefaults.InlineTextThresholdMb <= 0)
            {
                chatDefaults.InlineTextThresholdMb = 10;
            }

            if (chatDefaults.ToolFetchFileThresholdMb <= 0)
            {
                chatDefaults.ToolFetchFileThresholdMb = 10;
            }

            if (applyAttachmentDefaults)
            {
                chatDefaults.AutoResizeBigImage = true;
            }

            return chatDefaults;
        }

        private static Dictionary<string, object> NormalizeAdditionalParameters(ModelProviderSettings model)
        {
            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (model != null && model.AdditionalParameters != null)
            {
                foreach (var pair in model.AdditionalParameters)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                    {
                        continue;
                    }

                    parameters[pair.Key] = pair.Value;
                }
            }

            return parameters;
        }

        private static Dictionary<string, string> NormalizeHttpHeaders(Dictionary<string, string> headers)
        {
            var normalizedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers == null)
            {
                return normalizedHeaders;
            }

            foreach (var pair in headers)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                normalizedHeaders[pair.Key] = pair.Value ?? string.Empty;
            }

            return normalizedHeaders;
        }

        private static List<McpServerSettings> NormalizeMcpServers(IEnumerable<McpServerSettings> items)
        {
            var normalized = new List<McpServerSettings>();
            if (items == null)
            {
                return normalized;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                normalized.Add(NormalizeMcpServer(item));
            }

            return normalized
                .OrderBy(p => p.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static McpServerSettings NormalizeMcpServer(McpServerSettings server)
        {
            server = server ?? new McpServerSettings();

            if (string.IsNullOrWhiteSpace(server.Id))
            {
                server.Id = Guid.NewGuid().ToString("N");
            }

            server.Name = server.Name ?? string.Empty;
            server.Description = server.Description ?? string.Empty;

            if (server.Stdio == null && server.Http == null)
            {
                server.Stdio = new StdioMcpServerSettings();
            }

            if (server.Stdio != null)
            {
                server.Stdio.Command = server.Stdio.Command ?? string.Empty;
                server.Stdio.WorkingDirectory = server.Stdio.WorkingDirectory ?? string.Empty;
                server.Stdio.Arguments = server.Stdio.Arguments == null
                    ? new List<string>()
                    : server.Stdio.Arguments.Where(p => p != null).ToList();
                server.Stdio.EnvironmentVariables = NormalizeNameValueItems(server.Stdio.EnvironmentVariables);
            }

            if (server.Http != null)
            {
                server.Http.Endpoint = server.Http.Endpoint ?? string.Empty;
                server.Http.Headers = NormalizeNameValueItems(server.Http.Headers);
            }

            server.Tools = NormalizeMcpToolSettings(server.Tools, server.Name);
            return server;
        }

        private static List<McpToolSettings> NormalizeMcpToolSettings(IEnumerable<McpToolSettings> items, string sourceName)
        {
            var normalized = new Dictionary<string, McpToolSettings>(StringComparer.OrdinalIgnoreCase);
            if (items == null)
            {
                return new List<McpToolSettings>();
            }

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                var toolName = item.Name.Trim();
                normalized[toolName] = new McpToolSettings
                {
                    Name = toolName,
                    DisplayName = item.DisplayName ?? string.Empty,
                    Description = item.Description ?? string.Empty,
                    SourceName = string.IsNullOrWhiteSpace(item.SourceName) ? (sourceName ?? string.Empty) : item.SourceName,
                    InputSchemaJson = string.IsNullOrWhiteSpace(item.InputSchemaJson) ? "{}" : item.InputSchemaJson,
                    OutputSchemaJson = item.OutputSchemaJson ?? string.Empty,
                    IsReadOnly = item.IsReadOnly,
                    IsEnabled = item.IsEnabled,
                    Metadata = NormalizeNameValueItems(item.Metadata)
                };
            }

            return normalized.Values
                .OrderBy(p => p.DisplayName ?? p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static List<NameValueItem> NormalizeNameValueItems(IEnumerable<NameValueItem> items)
        {
            var normalized = new List<NameValueItem>();
            if (items == null)
            {
                return normalized;
            }

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                normalized.Add(new NameValueItem
                {
                    Name = item.Name.Trim(),
                    Value = item.Value ?? string.Empty
                });
            }

            return normalized;
        }

        private static List<ToolGroupRegistrationSettings> NormalizeToolGroupRegistrations(IEnumerable<ToolGroupRegistrationSettings> items)
        {
            var normalized = new Dictionary<string, ToolGroupRegistrationSettings>(StringComparer.OrdinalIgnoreCase);
            if (items == null)
            {
                return new List<ToolGroupRegistrationSettings>();
            }

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.GroupName))
                {
                    continue;
                }

                var groupName = item.GroupName.Trim();
                normalized[groupName] = new ToolGroupRegistrationSettings
                {
                    GroupName = groupName,
                    IsRegistered = item.IsRegistered,
                    UpdatedAtUtc = item.UpdatedAtUtc == default(DateTime) ? DateTime.UtcNow : item.UpdatedAtUtc
                };
            }

            return normalized.Values
                .OrderBy(p => p.GroupName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<ToolRegistrationSettings> NormalizeToolRegistrations(IEnumerable<ToolRegistrationSettings> items)
        {
            var normalized = new Dictionary<string, ToolRegistrationSettings>(StringComparer.OrdinalIgnoreCase);
            if (items == null)
            {
                return new List<ToolRegistrationSettings>();
            }

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ToolName))
                {
                    continue;
                }

                var toolName = item.ToolName.Trim();
                normalized[toolName] = new ToolRegistrationSettings
                {
                    GroupName = string.IsNullOrWhiteSpace(item.GroupName) ? string.Empty : item.GroupName.Trim(),
                    ToolName = toolName,
                    IsRegistered = item.IsRegistered,
                    UpdatedAtUtc = item.UpdatedAtUtc == default(DateTime) ? DateTime.UtcNow : item.UpdatedAtUtc
                };
            }

            return normalized.Values
                .OrderBy(p => p.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.ToolName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<ToolDefinitionOverrideSettings> NormalizeToolDefinitionOverrides(
            IEnumerable<ToolDefinitionOverrideSettings> items)
        {
            var normalized = new Dictionary<string, ToolDefinitionOverrideSettings>(StringComparer.OrdinalIgnoreCase);
            if (items == null)
            {
                return new List<ToolDefinitionOverrideSettings>();
            }

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ToolName))
                {
                    continue;
                }

                var toolName = item.ToolName.Trim();
                normalized[toolName] = new ToolDefinitionOverrideSettings
                {
                    ToolName = toolName,
                    DisplayName = item.DisplayName,
                    Description = item.Description,
                    RequiresBackup = item.RequiresBackup,
                    InputSchemaJson = item.InputSchemaJson,
                    OutputSchemaJson = item.OutputSchemaJson,
                    UpdatedAtUtc = item.UpdatedAtUtc == default(DateTime) ? DateTime.UtcNow : item.UpdatedAtUtc
                };
            }

            return normalized.Values
                .OrderBy(p => p.ToolName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Normalize Runtime settings with safe defaults.
        /// </summary>
        private static RuntimeSettings NormalizeRuntime(RuntimeSettings runtime)
        {
            runtime = runtime ?? new RuntimeSettings();

            if (runtime.MaxToolRoundCount <= 0)
            {
                runtime.MaxToolRoundCount = 300;
            }

            if (string.IsNullOrWhiteSpace(runtime.DefaultSkillName))
            {
                runtime.DefaultSkillName = "read-analysis";
            }

            if (string.IsNullOrWhiteSpace(runtime.SkillPlannerMode))
            {
                runtime.SkillPlannerMode = "keyword";
            }

            if (runtime.ContextWindowSize <= 0)
            {
                runtime.ContextWindowSize = 200_000;
            }

            if (runtime.MicroCompactCharThreshold <= 0)
            {
                runtime.MicroCompactCharThreshold = 50_000;
            }

            if (runtime.TokenBudgetStopThreshold < 0.5 || runtime.TokenBudgetStopThreshold > 0.99)
            {
                runtime.TokenBudgetStopThreshold = 0.85;
            }

            if (runtime.TokenBudgetSmallDeltaTokenThreshold <= 0)
            {
                runtime.TokenBudgetSmallDeltaTokenThreshold = 500;
            }

            if (runtime.TokenBudgetMaxConsecutiveSmallDeltas <= 0)
            {
                runtime.TokenBudgetMaxConsecutiveSmallDeltas = 100;
            }

            if (runtime.MaxConsecutiveTurnFailures <= 0)
            {
                runtime.MaxConsecutiveTurnFailures = 3;
            }

            if (runtime.BrokerReadyTimeoutSeconds <= 0)
            {
                runtime.BrokerReadyTimeoutSeconds = 5;
            }

            // 强制使用 JSONL 持久化 —— 会话历史不能丢
            if (runtime.StorageMode != ConversationStorageMode.Jsonl)
            {
                runtime.StorageMode = ConversationStorageMode.Jsonl;
            }

            return runtime;
        }
    }
}
