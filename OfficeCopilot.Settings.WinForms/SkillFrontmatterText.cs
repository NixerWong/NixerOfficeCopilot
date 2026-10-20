using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Settings.WinForms
{
    /// <summary>
    /// 对 SKILL.md frontmatter 做行级读写（net472 不能引用 YamlDotNet）。
    /// 只改动目标字段，其余行和正文保持原样，BOM 也保留。
    /// </summary>
    internal static class SkillFrontmatterText
    {
        // 组 1：可选 BOM + 开始分隔线；组 2：frontmatter 内容；组 3：结束分隔线
        private static readonly Regex FrontmatterRegex = new Regex(
            @"\A(\uFEFF?---[ \t]*\r?\n)(.*?)(\r?\n---[ \t]*(?:\r?\n|\z))",
            RegexOptions.Singleline);

        private static readonly Regex TopLevelKeyRegex = new Regex(
            @"^([A-Za-z0-9_-]+)[ \t]*:",
            RegexOptions.CultureInvariant);

        /// <summary>读取 enabled 字段；缺省视为启用。</summary>
        public static bool ReadEnabled(string skillMdPath)
        {
            var match = FrontmatterRegex.Match(ReadRaw(skillMdPath));
            if (!match.Success) return true;

            var lines = SplitLines(match.Groups[2].Value);
            var index = FindTopLevelKey(lines, "enabled");
            if (index < 0) return true;

            var line = lines[index];
            return ToEnabled(line.Substring(line.IndexOf(':') + 1).Trim().Trim('"', '\''));
        }

        /// <summary>读取顶层标量字段（name、description 等），去掉 YAML 引号；缺失返回 null。</summary>
        public static string ReadScalar(string skillMdPath, string key)
        {
            var match = FrontmatterRegex.Match(ReadRaw(skillMdPath));
            if (!match.Success) return null;

            var lines = SplitLines(match.Groups[2].Value);
            var index = FindTopLevelKey(lines, key);
            if (index < 0) return null;

            var line = lines[index];
            return UnquoteScalar(line.Substring(line.IndexOf(':') + 1).Trim());
        }

        /// <summary>
        /// 去掉 YAML 标量的引号。双引号按转义规则还原（\" 与 \\），单引号中 '' 还原为 '。
        /// 无引号的值原样返回。
        /// </summary>
        public static string UnquoteScalar(string raw)
        {
            if (raw == null) return null;
            if (raw.Length < 2) return raw;

            var first = raw[0];
            var last = raw[raw.Length - 1];
            if (first == '"' && last == '"')
            {
                var sb = new StringBuilder();
                for (var i = 1; i < raw.Length - 1; i++)
                {
                    if (raw[i] == '\\' && i + 1 < raw.Length - 1)
                    {
                        i++;
                    }
                    sb.Append(raw[i]);
                }
                return sb.ToString();
            }
            if (first == '\'' && last == '\'')
            {
                return raw.Substring(1, raw.Length - 2).Replace("''", "'");
            }
            return raw;
        }

        /// <summary>将 enabled 写入 frontmatter，替换已有的值或在 name 之后插入。</summary>
        public static void SetEnabled(string skillMdPath, bool enabled)
        {
            var text = ReadRaw(skillMdPath);
            var match = FrontmatterRegex.Match(text);
            if (!match.Success)
            {
                throw new InvalidOperationException("SKILL.md 缺少 frontmatter，无法保存启用状态。");
            }

            var yaml = match.Groups[2].Value;
            var newline = yaml.Contains("\r\n") ? "\r\n" : "\n";
            var lines = SplitLines(yaml);
            SetTopLevelScalar(lines, "enabled", enabled ? "true" : "false");

            var joined = string.Join(newline, lines);
            var updated = FrontmatterRegex.Replace(text,
                m => m.Groups[1].Value + joined + m.Groups[3].Value, 1);
            File.WriteAllText(skillMdPath, updated, new UTF8Encoding(false));
        }

        /// <summary>
        /// 返回 frontmatter 中不在 managedKeys 内的顶层字段块（含续行），保存时用于保留未知字段。
        /// </summary>
        public static List<string> ExtractUnmanagedBlocks(string yaml, ISet<string> managedKeys)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(yaml)) return result;

            var keep = false;
            foreach (var line in SplitLines(yaml))
            {
                var key = TopLevelKeyOf(line);
                if (key != null) keep = !managedKeys.Contains(key);
                if (keep) result.Add(line);
            }

            while (result.Count > 0 && string.IsNullOrWhiteSpace(result[result.Count - 1]))
            {
                result.RemoveAt(result.Count - 1);
            }
            return result;
        }

        /// <summary>enabled 的原始值转为布尔；缺省或无法识别的值视为启用，仅 false/no/off/0 视为禁用。</summary>
        public static bool ToEnabled(string raw)
        {
            if (raw == null) return true;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "false":
                case "no":
                case "off":
                case "0":
                    return false;
                default:
                    return true;
            }
        }

        private static string ReadRaw(string path)
        {
            // 不用 ReadAllText：它会吞掉 BOM，写回时就丢了
            return Encoding.UTF8.GetString(File.ReadAllBytes(path));
        }

        private static List<string> SplitLines(string text)
        {
            return new List<string>(Regex.Split(text, "\r?\n"));
        }

        private static string TopLevelKeyOf(string line)
        {
            if (string.IsNullOrEmpty(line) || IsContinuation(line)) return null;
            var match = TopLevelKeyRegex.Match(line);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static bool IsContinuation(string line)
        {
            return line.Length > 0 && (char.IsWhiteSpace(line[0]) || line[0] == '-' || line[0] == '#');
        }

        private static int FindTopLevelKey(List<string> lines, string key)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (string.Equals(TopLevelKeyOf(lines[i]), key, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private static void SetTopLevelScalar(List<string> lines, string key, string value)
        {
            var index = FindTopLevelKey(lines, key);
            if (index >= 0)
            {
                var end = index + 1;
                while (end < lines.Count && IsContinuation(lines[end])) end++;
                lines.RemoveRange(index + 1, end - index - 1);
                lines[index] = key + ": " + value;
                return;
            }

            var insertAt = 0;
            var nameIndex = FindTopLevelKey(lines, "name");
            if (nameIndex >= 0)
            {
                insertAt = nameIndex + 1;
                while (insertAt < lines.Count && IsContinuation(lines[insertAt])) insertAt++;
            }
            lines.Insert(insertAt, key + ": " + value);
        }
    }
}
