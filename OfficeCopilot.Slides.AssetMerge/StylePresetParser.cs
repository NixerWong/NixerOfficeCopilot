using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OfficeCopilot.Slides.Catalog;

namespace OfficeCopilot.Slides.AssetMerge
{
    internal sealed class ParsedPreset
    {
        public StyleEntry Entry { get; set; }
        public string Markdown { get; set; }
    }

    internal sealed class ParsedPresets
    {
        public List<ParsedPreset> Presets { get; } = new List<ParsedPreset>();

        /// <summary>预设之后的共享章节（字体搭配、禁用清单、CSS 坑）。</summary>
        public string SharedReference { get; set; }
    }

    /// <summary>
    /// 解析 frontend-slides/STYLE_PRESETS.md：<c>## 分组</c> 下的 <c>### N. 名称</c> 各成一张预设卡。
    /// </summary>
    internal static class StylePresetParser
    {
        private static readonly Regex PresetHeading = new Regex(@"^###\s+\d+\.\s+(.+?)\s*$", RegexOptions.Compiled);
        private static readonly Regex Backticked = new Regex("`([^`]+)`", RegexOptions.Compiled);

        public static ParsedPresets Parse(string markdown)
        {
            var result = new ParsedPresets();
            var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            string group = null;
            string name = null;
            var body = new List<string>();
            var shared = new StringBuilder();
            var inShared = false;

            Action flush = () =>
            {
                if (name != null)
                {
                    result.Presets.Add(Build(name, group, body));
                }

                name = null;
                body.Clear();
            };

            foreach (var line in lines)
            {
                if (inShared)
                {
                    shared.Append(line).Append('\n');
                    continue;
                }

                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    flush();
                    var title = line.Substring(3).Trim();
                    if (title.EndsWith("Themes", StringComparison.OrdinalIgnoreCase))
                    {
                        group = title;
                        continue;
                    }

                    if (result.Presets.Count > 0)
                    {
                        inShared = true;
                        shared.Append(line).Append('\n');
                    }

                    continue;
                }

                var m = PresetHeading.Match(line);
                if (m.Success)
                {
                    flush();
                    name = m.Groups[1].Value.Trim();
                    continue;
                }

                if (name != null)
                {
                    body.Add(line);
                }
            }

            flush();
            if (shared.Length > 0)
            {
                result.SharedReference = "# Style Presets — Shared Reference\n\n" + shared.ToString().TrimEnd() + "\n";
            }

            return result;
        }

        private static ParsedPreset Build(string name, string group, List<string> body)
        {
            var trimmed = body.ToList();
            while (trimmed.Count > 0 && (trimmed[trimmed.Count - 1].Trim().Length == 0 || trimmed[trimmed.Count - 1].Trim() == "---"))
            {
                trimmed.RemoveAt(trimmed.Count - 1);
            }

            var entry = new StyleEntry
            {
                Slug = StyleCatalog.ToSlug(name.Replace("&", "and")),
                Name = name,
                Kind = StyleKinds.Preset,
                Source = "frontend-slides",
                Tier = StyleTiers.Experimental,
                Scheme = SchemeFromGroup(group),
                Tagline = FieldValue(trimmed, "Layout"),
                CdnFree = true
            };

            var vibe = FieldValue(trimmed, "Vibe");
            if (!string.IsNullOrEmpty(vibe))
            {
                entry.Mood = vibe.Split(',').Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
            }

            entry.BestFor = vibe;
            entry.Fonts = ExtractTypography(trimmed);

            var md = new StringBuilder();
            md.Append("# ").Append(name).Append("\n\n");
            md.Append("<!-- 来源：frontend-slides/STYLE_PRESETS.md（").Append(group ?? "Presets").Append("）。无 HTML 模板，按描述现写封面。 -->\n\n");
            md.Append(string.Join("\n", trimmed).Trim()).Append('\n');
            return new ParsedPreset { Entry = entry, Markdown = md.ToString() };
        }

        private static string SchemeFromGroup(string group)
        {
            if (group == null) return null;
            if (group.StartsWith("Dark", StringComparison.OrdinalIgnoreCase)) return "dark";
            if (group.StartsWith("Light", StringComparison.OrdinalIgnoreCase)) return "light";
            return null;
        }

        private static string FieldValue(List<string> lines, string field)
        {
            var prefix = "**" + field + ":**";
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return t.Substring(prefix.Length).Trim();
                }
            }

            return null;
        }

        private static List<string> ExtractTypography(List<string> lines)
        {
            var fonts = new List<string>();
            var inTypography = false;
            foreach (var line in lines)
            {
                var t = line.Trim();
                if (t.StartsWith("**Typography", StringComparison.OrdinalIgnoreCase))
                {
                    inTypography = true;
                    // 单行写法：**Typography:** `A` + `B`
                    AddBackticked(t, fonts);
                    continue;
                }

                if (inTypography && t.StartsWith("**", StringComparison.Ordinal))
                {
                    break;
                }

                if (inTypography)
                {
                    AddBackticked(t, fonts);
                }
            }

            return fonts;
        }

        private static void AddBackticked(string line, List<string> fonts)
        {
            foreach (Match m in Backticked.Matches(line))
            {
                var name = m.Groups[1].Value.Trim();
                if (name.Length == 0 || name.Length > 40 || name.IndexOfAny(new[] { ':', ';', '{', '(' }) >= 0) continue;
                if (!fonts.Contains(name, StringComparer.OrdinalIgnoreCase)) fonts.Add(name);
            }
        }
    }
}
