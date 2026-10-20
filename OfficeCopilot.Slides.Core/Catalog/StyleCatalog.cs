using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Slides.Catalog
{
    /// <summary>
    /// 风格目录：内置 <c>catalog/styles.json</c> + 用户目录（templates / presets）。同 slug 用户优先；
    /// 用户模板加载时跑契约检查，不合格标记 invalid 并给出原因。
    /// </summary>
    public sealed class StyleCatalog
    {
        public const int DefaultDesignLimit = 24000;

        private static readonly Regex TokenSplitter = new Regex(@"[\s,;/|·、，；]+", RegexOptions.Compiled);
        private static readonly Regex SlugInvalidChars = new Regex(@"[^a-z0-9\-]+", RegexOptions.Compiled);

        private readonly Dictionary<string, StyleEntry> _entries =
            new Dictionary<string, StyleEntry>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<StyleEntry> Entries
        {
            get { return _entries.Values.OrderBy(TierRank).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
        }

        public static StyleCatalog Load(SlideAssetPaths paths, FontCatalog fonts)
        {
            var catalog = new StyleCatalog();
            if (paths == null)
            {
                return catalog;
            }

            foreach (var entry in ReadCatalogFile(Path.Combine(paths.BuiltInRoot, SlideAssetPaths.CatalogFileRelative)))
            {
                entry.Origin = "builtin";
                entry.Directory = ResolveEntryDirectory(paths.BuiltInRoot, entry);
                catalog._entries[entry.Slug] = entry;
            }

            foreach (var entry in LoadUserEntries(paths.UserRoot, fonts))
            {
                catalog._entries[entry.Slug] = entry;
            }

            return catalog;
        }

        public bool TryGet(string slug, out StyleEntry entry)
        {
            entry = null;
            return !string.IsNullOrWhiteSpace(slug) && _entries.TryGetValue(slug.Trim(), out entry);
        }

        public IList<StyleSearchHit> Search(StyleSearchQuery query)
        {
            query = query ?? new StyleSearchQuery();
            var tokens = Tokenize(query.Text);
            var max = query.MaxResults <= 0 ? 8 : Math.Min(query.MaxResults, 50);

            var hits = new List<StyleSearchHit>();
            foreach (var entry in _entries.Values)
            {
                if (!query.IncludeInvalid && !entry.IsValid) continue;
                if (!MatchesFilter(entry.Kind, query.Kind)) continue;
                if (!MatchesFilter(entry.Scheme, query.Scheme)) continue;
                if (!MatchesFilter(entry.Formality, query.Formality)) continue;
                if (!MatchesFilter(entry.Density, query.Density)) continue;
                if (!MatchesFilter(entry.Tier, query.Tier)) continue;

                var score = Score(entry, tokens, query.Text);
                if (tokens.Count > 0 && score <= 0) continue;

                hits.Add(new StyleSearchHit { Entry = entry, Score = score });
            }

            return hits
                .OrderByDescending(p => p.Score)
                .ThenBy(p => TierRank(p.Entry))
                .ThenBy(p => p.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .ToList();
        }

        public StyleDetail GetDetail(string slug, bool includeDesign, int designLimit = DefaultDesignLimit)
        {
            StyleEntry entry;
            if (!TryGet(slug, out entry))
            {
                return null;
            }

            var detail = new StyleDetail { Entry = entry };
            var dir = entry.Directory ?? string.Empty;
            if (entry.Kind == StyleKinds.Preset)
            {
                detail.PresetMarkdown = ReadText(Path.Combine(dir, "preset.md"));
                return detail;
            }

            detail.PreviewMarkdown = ReadText(Path.Combine(dir, "preview.md"));
            var templateJson = ReadText(Path.Combine(dir, "template.json"));
            if (!string.IsNullOrEmpty(templateJson))
            {
                try
                {
                    var typography = JObject.Parse(templateJson)["typography"];
                    detail.Typography = typography == null ? null : typography.ToString(Formatting.None);
                }
                catch (JsonException)
                {
                }
            }

            if (includeDesign)
            {
                var design = ReadText(Path.Combine(dir, "design.md"));
                if (design != null && designLimit > 0 && design.Length > designLimit)
                {
                    design = design.Substring(0, designLimit);
                    detail.DesignTruncated = true;
                }

                detail.DesignMarkdown = design;
            }

            return detail;
        }

        /// <summary>把上游 template.json / index.json 条目（snake_case）读成目录条目。</summary>
        public static StyleEntry FromUpstreamJson(JObject json, string fallbackSlug)
        {
            json = json ?? new JObject();
            var entry = new StyleEntry
            {
                Slug = Str(json, "slug") ?? fallbackSlug,
                Name = Str(json, "name") ?? fallbackSlug,
                Tagline = Str(json, "tagline"),
                Mood = List(json, "mood"),
                Occasion = List(json, "occasion"),
                Tone = List(json, "tone"),
                Formality = Str(json, "formality"),
                Density = Str(json, "density"),
                Scheme = Str(json, "scheme"),
                BestFor = Str(json, "best_for") ?? Str(json, "bestFor"),
                AvoidFor = Str(json, "avoid_for") ?? Str(json, "avoidFor"),
                SlideCount = (int?)json["slide_count"] ?? (int?)json["slideCount"] ?? 0,
                SafeArea = SafeAreaFromJson(json["safeArea"] as JObject)
            };

            var palette = json["palette"] as JObject;
            if (palette != null)
            {
                foreach (var p in palette.Properties())
                {
                    if (p.Value.Type == JTokenType.String && !string.Equals(p.Name, "description", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Palette[p.Name] = (string)p.Value;
                    }
                }
            }

            var typography = json["typography"] as JObject;
            if (typography != null)
            {
                foreach (var p in typography.Properties())
                {
                    if (p.Value.Type == JTokenType.String && !string.Equals(p.Name, "style", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(p.Name, "description", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFontNames(entry.Fonts, (string)p.Value);
                    }
                }
            }

            return entry;
        }

        private static StyleSafeArea SafeAreaFromJson(JObject json)
        {
            if (json == null)
            {
                return null;
            }

            return new StyleSafeArea
            {
                Content = List(json, "content"),
                Background = List(json, "background"),
                Chrome = List(json, "chrome"),
                SafeX = (int?)json["safeX"] ?? 0,
                SafeY = (int?)json["safeY"] ?? 0
            };
        }

        public static string ToSlug(string text)
        {
            var s = (text ?? string.Empty).Trim().ToLowerInvariant().Replace('&', '-').Replace(' ', '-').Replace('_', '-');
            s = SlugInvalidChars.Replace(s, "-");
            while (s.Contains("--")) s = s.Replace("--", "-");
            return s.Trim('-');
        }

        private static void AddFontNames(List<string> target, string value)
        {
            // "Bebas Neue" / "Inter, sans-serif" / "Noto Sans SC (CJK)"
            foreach (var part in value.Split(','))
            {
                var name = part.Trim().Trim('\'', '"');
                var paren = name.IndexOf('(');
                if (paren > 0) name = name.Substring(0, paren).Trim();
                if (name.Length == 0 || name.Length > 40) continue;
                if (IsGenericFamily(name)) continue;
                if (!target.Contains(name, StringComparer.OrdinalIgnoreCase)) target.Add(name);
            }
        }

        private static bool IsGenericFamily(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "serif":
                case "sans-serif":
                case "monospace":
                case "cursive":
                case "fantasy":
                case "system-ui":
                    return true;
                default:
                    return false;
            }
        }

        private static IEnumerable<StyleEntry> ReadCatalogFile(string path)
        {
            if (!File.Exists(path))
            {
                return Enumerable.Empty<StyleEntry>();
            }

            var token = JToken.Parse(File.ReadAllText(path, Encoding.UTF8));
            var array = token as JArray ?? token["styles"] as JArray ?? new JArray();
            return array.ToObject<List<StyleEntry>>().Where(p => p != null && !string.IsNullOrWhiteSpace(p.Slug));
        }

        private static string ResolveEntryDirectory(string root, StyleEntry entry)
        {
            return entry.Kind == StyleKinds.Preset
                ? Path.Combine(SlideAssetPaths.PresetsDir(root), entry.Slug)
                : Path.Combine(SlideAssetPaths.TemplatesDir(root), entry.Slug);
        }

        private static IEnumerable<StyleEntry> LoadUserEntries(string userRoot, FontCatalog fonts)
        {
            var result = new List<StyleEntry>();
            if (string.IsNullOrEmpty(userRoot) || !System.IO.Directory.Exists(userRoot))
            {
                return result;
            }

            var declared = ReadCatalogFile(Path.Combine(userRoot, SlideAssetPaths.CatalogFileRelative))
                .ToDictionary(p => p.Slug, StringComparer.OrdinalIgnoreCase);

            var templatesDir = SlideAssetPaths.TemplatesDir(userRoot);
            if (System.IO.Directory.Exists(templatesDir))
            {
                foreach (var dir in System.IO.Directory.GetDirectories(templatesDir))
                {
                    var slug = Path.GetFileName(dir);
                    StyleEntry entry;
                    if (!declared.TryGetValue(slug, out entry))
                    {
                        var json = ReadText(Path.Combine(dir, "template.json"));
                        JObject obj = null;
                        try { obj = string.IsNullOrEmpty(json) ? null : JObject.Parse(json); }
                        catch (JsonException) { }
                        entry = FromUpstreamJson(obj, slug);
                    }

                    entry.Slug = slug;
                    entry.Kind = StyleKinds.Template;
                    entry.Source = "user";
                    entry.Tier = StyleTiers.User;
                    entry.Origin = "user";
                    entry.Directory = dir;
                    ValidateUserTemplate(entry, dir, fonts);
                    result.Add(entry);
                }
            }

            var presetsDir = SlideAssetPaths.PresetsDir(userRoot);
            if (System.IO.Directory.Exists(presetsDir))
            {
                foreach (var dir in System.IO.Directory.GetDirectories(presetsDir))
                {
                    var slug = Path.GetFileName(dir);
                    StyleEntry entry;
                    if (!declared.TryGetValue(slug, out entry))
                    {
                        entry = new StyleEntry { Slug = slug, Name = ReadFirstHeading(Path.Combine(dir, "preset.md")) ?? slug };
                    }

                    entry.Slug = slug;
                    entry.Kind = StyleKinds.Preset;
                    entry.Source = "user";
                    entry.Tier = StyleTiers.User;
                    entry.Origin = "user";
                    entry.Directory = dir;
                    if (!File.Exists(Path.Combine(dir, "preset.md")))
                    {
                        entry.IsValid = false;
                        entry.InvalidReason = "缺少 preset.md";
                    }

                    result.Add(entry);
                }
            }

            return result;
        }

        private static void ValidateUserTemplate(StyleEntry entry, string dir, FontCatalog fonts)
        {
            var html = ReadText(Path.Combine(dir, "template.html"));
            var reasons = new List<string>();
            if (html == null)
            {
                reasons.Add("缺少 template.html");
            }
            else
            {
                var external = HtmlContractScanner.FindExternalReferences(html);
                if (external.Count > 0)
                {
                    reasons.Add("存在外部引用：" + string.Join("，", external.Take(3)) + (external.Count > 3 ? " 等 " + external.Count + " 处" : string.Empty));
                }

                if (!HtmlContractScanner.HasSlideMarkup(html))
                {
                    reasons.Add("没有幻灯片标记（section / .slide）");
                }
            }

            entry.CdnFree = html != null && HtmlContractScanner.FindExternalReferences(html).Count == 0;
            entry.LocalFonts = new List<string>();
            entry.MissingFonts = new List<string>();
            foreach (var family in entry.Fonts ?? new List<string>())
            {
                if (fonts != null && fonts.Contains(family)) entry.LocalFonts.Add(family);
                else entry.MissingFonts.Add(family);
            }

            if (reasons.Count > 0)
            {
                entry.IsValid = false;
                entry.InvalidReason = string.Join("；", reasons);
            }
        }

        private static double Score(StyleEntry entry, IList<string> tokens, string rawText)
        {
            var score = 0d;
            foreach (var token in tokens)
            {
                if (Contains(entry.Slug, token) || Contains(entry.Name, token)) score += 3;
                if (AnyContains(entry.Mood, token) || AnyContains(entry.Tone, token)) score += 2;
                if (AnyContains(entry.Occasion, token)) score += 2;
                if (Contains(entry.Tagline, token)) score += 1;
                if (Contains(entry.BestFor, token)) score += 1;
                if (Contains(entry.Scheme, token) || Contains(entry.Formality, token)) score += 0.5;
                if (Contains(entry.AvoidFor, token)) score -= 2;
            }

            // 整句命中（中文查询常无空格）。
            if (tokens.Count > 1 && !string.IsNullOrWhiteSpace(rawText) && Contains(entry.BestFor, rawText.Trim()))
            {
                score += 2;
            }

            if (entry.Tier == StyleTiers.Certified) score += 0.25;
            return score;
        }

        private static IList<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return TokenSplitter.Split(text.Trim().ToLowerInvariant())
                .Where(p => p.Length > 1 || (p.Length == 1 && p[0] > 0x2E80))
                .Distinct()
                .ToList();
        }

        private static bool MatchesFilter(string value, string filter)
        {
            return string.IsNullOrWhiteSpace(filter) || string.Equals(value, filter.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool AnyContains(IEnumerable<string> values, string needle)
        {
            return values != null && values.Any(p => Contains(p, needle));
        }

        private static int TierRank(StyleEntry entry)
        {
            switch (entry.Tier)
            {
                case StyleTiers.User: return 0;
                case StyleTiers.Certified: return 1;
                default: return 2;
            }
        }

        private static string Str(JObject json, string name)
        {
            var token = json[name];
            return token == null || token.Type != JTokenType.String ? null : (string)token;
        }

        private static List<string> List(JObject json, string name)
        {
            var array = json[name] as JArray;
            return array == null
                ? new List<string>()
                : array.Where(p => p.Type == JTokenType.String).Select(p => (string)p).ToList();
        }

        private static string ReadText(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        private static string ReadFirstHeading(string path)
        {
            var text = ReadText(path);
            if (text == null) return null;
            foreach (var line in text.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("#")) return t.TrimStart('#').Trim();
            }

            return null;
        }
    }
}
