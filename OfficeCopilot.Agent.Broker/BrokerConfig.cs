using System;
using System.IO;
using System.Text.Json;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Broker
{
    /// <summary>
    /// Phase 1.3c：从 <c>appsettings.json</c> 加载 broker 运行时配置（StorageMode / JsonlRootDirectory 等）。
    /// 解析失败或文件缺失时返回当前的硬编码默认值（StorageMode=Jsonl）。
    /// </summary>
    /// <remarks>
    /// 查找顺序：
    /// <list type="number">
    /// <item>命令行 <c>--config &lt;path&gt;</c>。</item>
    /// <item>broker exe 所在目录下的 <c>appsettings.json</c>。</item>
    /// <item><c>%APPDATA%/NixerOfficeCopilot/appsettings.json</c>（用户级覆盖）。</item>
    /// </list>
    /// 任何一步成功即停。无任何文件命中则返回默认配置。
    /// </remarks>
    internal static class BrokerConfig
    {
        private const string DefaultFileName = "appsettings.json";

        public static AgentRuntimeOptions LoadRuntimeOptions(string[] args)
        {
            var options = new AgentRuntimeOptions
            {
                // 与 1.3 默认保持一致：开启 JSONL 持久化。
                StorageMode = ConversationStorageMode.Jsonl
            };

            var configPath = ResolveConfigPath(args);
            if (!string.IsNullOrEmpty(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath, System.Text.Encoding.UTF8);
                    var parsed = JsonSerializer.Deserialize<BrokerConfigFile>(json, JsonOptions);
                    if (parsed != null && parsed.Storage != null)
                    {
                        ApplyStorageSection(parsed.Storage, options);
                    }

                    // Also merge Runtime section from appsettings.json
                    if (parsed != null && parsed.Runtime != null)
                    {
                        ApplyRuntimeSection(parsed.Runtime, options);
                    }
                }
                catch (Exception ex)
                {
                    RuntimeTrace.Write("[broker.config] failed to parse '" + configPath + "': " + ex.Message + " — falling back to defaults.");
                }
            }

            // 第二阶段：从 settings.json 的 Runtime 节点读取（UI 设置面板写入的）
            MergeFromSettingsJson(options);

            RuntimeTrace.Write($"[broker.config] Final: StorageMode={options.StorageMode}, JsonlRootDirectory='{options.JsonlRootDirectory}', MaxToolRoundCount={options.MaxToolRoundCount}");
            return options;
        }

        /// <summary>
        /// 从 %APPDATA%/NixerOfficeCopilot/settings.json 的 Runtime 节点合并参数。
        /// settings.json 中的值优先级高于 appsettings.json 默认值。
        /// </summary>
        private static void MergeFromSettingsJson(AgentRuntimeOptions options)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var settingsPath = Path.Combine(appData, "NixerOfficeCopilot", "settings.json");
                RuntimeTrace.Write($"[broker.config] Reading settings from: {settingsPath}");
                if (!File.Exists(settingsPath))
                {
                    RuntimeTrace.Write("[broker.config] settings.json not found, using defaults.");
                    return;
                }

                var json = File.ReadAllText(settingsPath, System.Text.Encoding.UTF8);
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    if (!doc.RootElement.TryGetProperty("Runtime", out var runtime))
                    {
                        RuntimeTrace.Write("[broker.config] No 'Runtime' section in settings.json.");
                        return;
                    }

                    if (runtime.TryGetProperty("MaxToolRoundCount", out var mt) && mt.TryGetInt32(out var mtv) && mtv > 0)
                    {
                        options.MaxToolRoundCount = mtv;
                        RuntimeTrace.Write($"[broker.config] MaxToolRoundCount from settings.json = {mtv}");
                    }
                    else
                    {
                        RuntimeTrace.Write($"[broker.config] MaxToolRoundCount NOT found or invalid in settings.json Runtime section.");
                    }
                    if (runtime.TryGetProperty("EnableSkillPlanning", out var esp) && esp.ValueKind == System.Text.Json.JsonValueKind.True || esp.ValueKind == System.Text.Json.JsonValueKind.False)
                        options.EnableSkillPlanning = esp.GetBoolean();
                    if (runtime.TryGetProperty("DefaultSkillName", out var dsn))
                        options.DefaultSkillName = dsn.GetString() ?? options.DefaultSkillName;
                    if (runtime.TryGetProperty("SkillPlannerMode", out var spm))
                        options.SkillPlannerMode = spm.GetString() ?? options.SkillPlannerMode;
                    if (runtime.TryGetProperty("StorageMode", out var sm) && sm.GetString() is string smStr)
                    {
                        if (Enum.TryParse<ConversationStorageMode>(smStr, ignoreCase: true, out var smv))
                            options.StorageMode = smv;
                    }
                    if (runtime.TryGetProperty("JsonlRootDirectory", out var jrd))
                        options.JsonlRootDirectory = jrd.GetString() ?? options.JsonlRootDirectory;
                    if (runtime.TryGetProperty("ContextWindowSize", out var cws) && cws.TryGetInt32(out var cwsv) && cwsv > 0)
                        options.ContextWindowSize = cwsv;
                    if (runtime.TryGetProperty("AutoCompactEnabled", out var ace) && (ace.ValueKind == System.Text.Json.JsonValueKind.True || ace.ValueKind == System.Text.Json.JsonValueKind.False))
                        options.AutoCompactEnabled = ace.GetBoolean();
                    if (runtime.TryGetProperty("MicroCompactEnabled", out var mce) && (mce.ValueKind == System.Text.Json.JsonValueKind.True || mce.ValueKind == System.Text.Json.JsonValueKind.False))
                        options.MicroCompactEnabled = mce.GetBoolean();
                    if (runtime.TryGetProperty("MicroCompactCharThreshold", out var mct) && mct.TryGetInt32(out var mctv) && mctv > 0)
                        options.MicroCompactCharThreshold = mctv;
                    if (runtime.TryGetProperty("TokenBudgetStopThreshold", out var tbs) && tbs.TryGetDouble(out var tbsv) && tbsv > 0)
                        options.TokenBudgetStopThreshold = tbsv;
                    if (runtime.TryGetProperty("TokenBudgetSmallDeltaTokenThreshold", out var tbd) && tbd.TryGetInt32(out var tbdv) && tbdv > 0)
                        options.TokenBudgetSmallDeltaTokenThreshold = tbdv;
                    if (runtime.TryGetProperty("TokenBudgetMaxConsecutiveSmallDeltas", out var tbm) && tbm.TryGetInt32(out var tbmv) && tbmv > 0)
                        options.TokenBudgetMaxConsecutiveSmallDeltas = tbmv;
                    if (runtime.TryGetProperty("MaxConsecutiveTurnFailures", out var mcf) && mcf.TryGetInt32(out var mcfv) && mcfv > 0)
                        options.MaxConsecutiveTurnFailures = mcfv;
                }
            }
            catch (Exception ex)
            {
                RuntimeTrace.Write("[broker.config] failed to merge settings.json Runtime: " + ex.Message);
            }
        }

        internal static void ApplyRuntimeSection(RuntimeSection runtime, AgentRuntimeOptions options)
        {
            if (runtime == null || options == null) return;

            if (runtime.MaxToolRoundCount > 0)
                options.MaxToolRoundCount = runtime.MaxToolRoundCount;
            if (runtime.EnableSkillPlanning.HasValue)
                options.EnableSkillPlanning = runtime.EnableSkillPlanning.Value;
            if (!string.IsNullOrWhiteSpace(runtime.DefaultSkillName))
                options.DefaultSkillName = runtime.DefaultSkillName;
            if (!string.IsNullOrWhiteSpace(runtime.SkillPlannerMode))
                options.SkillPlannerMode = runtime.SkillPlannerMode;
            if (runtime.ContextWindowSize > 0)
                options.ContextWindowSize = runtime.ContextWindowSize;
            if (runtime.AutoCompactEnabled.HasValue)
                options.AutoCompactEnabled = runtime.AutoCompactEnabled.Value;
            if (runtime.MicroCompactEnabled.HasValue)
                options.MicroCompactEnabled = runtime.MicroCompactEnabled.Value;
            if (runtime.MicroCompactCharThreshold > 0)
                options.MicroCompactCharThreshold = runtime.MicroCompactCharThreshold;
            if (runtime.TokenBudgetStopThreshold > 0)
                options.TokenBudgetStopThreshold = runtime.TokenBudgetStopThreshold;
            if (runtime.TokenBudgetSmallDeltaTokenThreshold > 0)
                options.TokenBudgetSmallDeltaTokenThreshold = runtime.TokenBudgetSmallDeltaTokenThreshold;
            if (runtime.TokenBudgetMaxConsecutiveSmallDeltas > 0)
                options.TokenBudgetMaxConsecutiveSmallDeltas = runtime.TokenBudgetMaxConsecutiveSmallDeltas;
            if (runtime.MaxConsecutiveTurnFailures > 0)
                options.MaxConsecutiveTurnFailures = runtime.MaxConsecutiveTurnFailures;
        }

        internal static string ResolveConfigPath(string[] args)
        {
            var explicitPath = GetCliArg(args, "--config");
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return File.Exists(explicitPath) ? explicitPath : null;
            }

            var nextToExe = Path.Combine(AppContext.BaseDirectory ?? string.Empty, DefaultFileName);
            if (File.Exists(nextToExe))
            {
                return nextToExe;
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                var userOverride = Path.Combine(appData, "NixerOfficeCopilot", DefaultFileName);
                if (File.Exists(userOverride))
                {
                    return userOverride;
                }
            }

            return null;
        }

        internal static void ApplyStorageSection(StorageSection storage, AgentRuntimeOptions options)
        {
            if (storage == null || options == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(storage.Mode))
            {
                ConversationStorageMode parsed;
                if (Enum.TryParse(storage.Mode.Trim(), ignoreCase: true, out parsed))
                {
                    options.StorageMode = parsed;
                }
            }

            if (!string.IsNullOrWhiteSpace(storage.JsonlRootDirectory))
            {
                options.JsonlRootDirectory = storage.JsonlRootDirectory.Trim();
            }
        }

        private static string GetCliArg(string[] args, string name)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1] ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        internal sealed class BrokerConfigFile
        {
            public StorageSection Storage { get; set; }

            public RuntimeSection Runtime { get; set; }
        }

        internal sealed class StorageSection
        {
            public string Mode { get; set; }

            public string JsonlRootDirectory { get; set; }
        }

        internal sealed class RuntimeSection
        {
            public int MaxToolRoundCount { get; set; }

            public bool? EnableSkillPlanning { get; set; }

            public string DefaultSkillName { get; set; }

            public string SkillPlannerMode { get; set; }

            public int ContextWindowSize { get; set; }

            public bool? AutoCompactEnabled { get; set; }

            public bool? MicroCompactEnabled { get; set; }

            public int MicroCompactCharThreshold { get; set; }

            public double TokenBudgetStopThreshold { get; set; }

            public int TokenBudgetSmallDeltaTokenThreshold { get; set; }

            public int TokenBudgetMaxConsecutiveSmallDeltas { get; set; }

            public int MaxConsecutiveTurnFailures { get; set; }
        }
    }
}
