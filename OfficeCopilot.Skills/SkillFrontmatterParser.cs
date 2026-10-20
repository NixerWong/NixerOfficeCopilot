using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using YamlDotNet.Serialization;

namespace OfficeCopilot.Skills
{
    /// <summary>
    /// SKILL.md frontmatter 的统一解析器（文件型与内置 Skill 共用）。
    /// <list type="bullet">
    /// <item>键名大小写、连字符、下划线不敏感：<c>allowed-tools</c>、<c>allowedTools</c>、<c>allowed_tools</c> 等价。</item>
    /// <item>列表字段既接受 YAML 序列，也接受单个标量（编辑器对单项写成标量）。</item>
    /// <item>未知键记录告警，不再静默丢弃。</item>
    /// </list>
    /// </summary>
    public static class SkillFrontmatterParser
    {
        private static readonly HashSet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "name", "displayname", "description", "category", "model", "enabled",
            "usewhen", "avoidwhen", "allowedtools", "preferredtools", "verificationtools",
            "companionskills", "fallbackskills", "requireverification",
            "requiresconfirmation", "requiresbackup", "maxtoolroundcount",
            "allowhighrisktools", "allowcodeexecution", "allowvbafallback",
            "whentouse", "userinvocable", "disablemodelinvocation", "version",
            "argumenthint", "arguments", "effort", "context", "agent", "paths", "metadata"
        };

        /// <summary>
        /// 解析 YAML frontmatter 文本。失败时抛出异常，由调用方决定记录与跳过。
        /// </summary>
        public static SkillFrontmatter Parse(string yaml, string sourceDescription)
        {
            var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
            var raw = deserializer.Deserialize<Dictionary<object, object>>(yaml ?? string.Empty);
            var fm = new SkillFrontmatter();
            if (raw == null)
            {
                return fm;
            }

            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kvp in raw)
            {
                var key = Normalize(Convert.ToString(kvp.Key, CultureInfo.InvariantCulture));
                if (!KnownKeys.Contains(key))
                {
                    SkillLog.Write($"Unknown frontmatter key '{kvp.Key}' in {sourceDescription}; ignored.");
                    continue;
                }

                values[key] = kvp.Value;
            }

            fm.Name = ToText(Get(values, "name"));
            fm.DisplayName = NullIfEmpty(ToText(Get(values, "displayname")));
            fm.Description = ToText(Get(values, "description"));
            fm.Category = ToText(Get(values, "category"));
            fm.Model = NullIfEmpty(ToText(Get(values, "model")));
            fm.Enabled = ToBool(Get(values, "enabled"), true, "enabled", sourceDescription);
            fm.UseWhen = ToList(Get(values, "usewhen"));
            fm.AvoidWhen = ToList(Get(values, "avoidwhen"));
            fm.AllowedTools = ToList(Get(values, "allowedtools"));
            fm.PreferredTools = ToList(Get(values, "preferredtools"));
            fm.VerificationTools = ToList(Get(values, "verificationtools"));
            fm.CompanionSkills = ToList(Get(values, "companionskills"));
            fm.FallbackSkills = ToList(Get(values, "fallbackskills"));
            fm.RequireVerification = ToBool(Get(values, "requireverification"), false, "require-verification", sourceDescription);
            fm.RequiresConfirmation = ToBool(Get(values, "requiresconfirmation"), false, "requires-confirmation", sourceDescription);
            fm.RequiresBackup = ToBool(Get(values, "requiresbackup"), false, "requires-backup", sourceDescription);
            fm.MaxToolRoundCount = ToInt(Get(values, "maxtoolroundcount"), 5, "max-tool-round-count", sourceDescription);
            fm.AllowHighRiskTools = ToBool(Get(values, "allowhighrisktools"), false, "allow-high-risk-tools", sourceDescription);

            var codeExec = Get(values, "allowcodeexecution") ?? Get(values, "allowvbafallback");
            fm.AllowCodeExecution = ToBool(codeExec, false, "allow-code-execution", sourceDescription);

            fm.WhenToUse = NullIfEmpty(ToText(Get(values, "whentouse")));
            fm.UserInvocable = ToBool(Get(values, "userinvocable"), true, "user-invocable", sourceDescription);
            fm.DisableModelInvocation = ToBool(Get(values, "disablemodelinvocation"), false, "disable-model-invocation", sourceDescription);
            fm.Version = NullIfEmpty(ToText(Get(values, "version")));
            fm.ArgumentHint = NullIfEmpty(ToText(Get(values, "argumenthint")));
            fm.Arguments = ToList(Get(values, "arguments"));
            fm.Effort = NullIfEmpty(ToText(Get(values, "effort")));
            fm.Context = NullIfEmpty(ToText(Get(values, "context")));
            fm.Agent = NullIfEmpty(ToText(Get(values, "agent")));
            fm.Paths = ToList(Get(values, "paths"));
            fm.Metadata = ToStringMap(Get(values, "metadata"));

            return fm;
        }

        private static object Get(Dictionary<string, object> values, string key)
        {
            return values.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>键归一化：去掉连字符与下划线并转小写。</summary>
        public static string Normalize(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            return key.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        }

        private static string ToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>列表字段：接受序列或单个标量。</summary>
        private static List<string> ToList(object value)
        {
            var result = new List<string>();
            if (value == null)
            {
                return result;
            }

            if (value is string single)
            {
                if (!string.IsNullOrWhiteSpace(single))
                {
                    result.Add(single.Trim());
                }

                return result;
            }

            if (value is IEnumerable items)
            {
                foreach (var item in items)
                {
                    var text = ToText(item);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Add(text);
                    }
                }
            }

            return result;
        }

        private static Dictionary<string, string> ToStringMap(object value)
        {
            if (!(value is IDictionary map))
            {
                return null;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in map)
            {
                var key = ToText(entry.Key);
                if (!string.IsNullOrEmpty(key))
                {
                    result[key] = ToText(entry.Value);
                }
            }

            return result.Count == 0 ? null : result;
        }

        private static bool ToBool(object value, bool fallback, string keyForLog, string sourceDescription)
        {
            if (value == null)
            {
                return fallback;
            }

            var text = ToText(value);
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }

            SkillLog.Write($"Invalid boolean '{text}' for '{keyForLog}' in {sourceDescription}; using {fallback}.");
            return fallback;
        }

        private static int ToInt(object value, int fallback, string keyForLog, string sourceDescription)
        {
            if (value == null)
            {
                return fallback;
            }

            var text = ToText(value);
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                return parsed;
            }

            SkillLog.Write($"Invalid integer '{text}' for '{keyForLog}' in {sourceDescription}; using {fallback}.");
            return fallback;
        }
    }
}
