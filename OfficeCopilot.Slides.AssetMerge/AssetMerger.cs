using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Catalog;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Slides.AssetMerge
{
    internal sealed class MergeReport
    {
        public int Templates;
        public int Presets;
        public int Layouts;
        public int FontFamilies;
        public int UnresolvedReferences;
    }

    /// <summary>按蓝图 §3 合并两份技能包素材到 SlideAssets。</summary>
    internal sealed class AssetMerger
    {
        // 首批 certified（蓝图 §3.6）。
        private static readonly string[] CertifiedSlugs =
        {
            "signal", "soft-editorial", "blue-professional", "monochrome", "neo-grid-bold", "sakura-chroma"
        };

        // 本地缺失、但可由随包字体覆盖字形的族（CSS 别名，不复制文件）。
        private static readonly Dictionary<string, string> FontAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Noto Sans JP", "NotoSansSC" },
                { "Smiley Sans Oblique", "SmileySans" }
            };

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly Regex H2 = new Regex(@"^## (.+?)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

        private readonly string _source;
        private readonly string _target;
        private readonly TextWriter _log;
        private readonly MergeReport _report = new MergeReport();

        public AssetMerger(string source, string target, TextWriter log)
        {
            _source = source;
            _target = target;
            _log = log ?? TextWriter.Null;
        }

        private string Beautiful { get { return Path.Combine(_source, "beautiful-html-templates"); } }
        private string Frontend { get { return Path.Combine(_source, "frontend-slides"); } }
        private string BoldPack { get { return Path.Combine(Frontend, "bold-template-pack"); } }

        public MergeReport Run()
        {
            ResetGeneratedFolders();

            var fonts = WriteFonts();
            var fontCatalog = new HashSet<string>(fonts.Select(p => FontFaceNaming.NormalizeKey(p.Family)));
            foreach (var alias in fonts.SelectMany(
                p => p.CssAliases == null ? Enumerable.Empty<string>() : p.CssAliases))
            {
                fontCatalog.Add(FontFaceNaming.NormalizeKey(alias));
            }

            var entries = new List<StyleEntry>();
            entries.AddRange(MergeTemplates(fontCatalog));
            entries.AddRange(MergePresets(fontCatalog));
            WriteCatalog(entries);

            CopyRuntime();
            CopyGuides();
            WriteNotices(fonts);
            return _report;
        }

        private void ResetGeneratedFolders()
        {
            foreach (var name in new[] { "templates", "presets", "catalog", "guides" })
            {
                var dir = Path.Combine(_target, name);
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }

            Directory.CreateDirectory(_target);
        }

        // ---------- 字体 ----------

        private IList<FontFamilyEntry> WriteFonts()
        {
            var fontsDir = SlideAssetPaths.FontsDir(_target);
            var previousFamilies = ReadFontMetadata(Path.Combine(fontsDir, "fonts.json"));
            var families = FontCatalog.ScanFolder(fontsDir);
            PreserveFontMetadata(families, previousFamilies);
            foreach (var alias in FontAliases)
            {
                var targetFamily = families.FirstOrDefault(p => string.Equals(p.Folder, alias.Value, StringComparison.OrdinalIgnoreCase));
                if (targetFamily == null) continue;
                AddAlias(targetFamily, alias.Key);
            }

            WriteText(Path.Combine(fontsDir, "fonts.css"), FontCatalog.BuildCss(families, f => f.Folder + "/"));
            WriteJson(Path.Combine(fontsDir, "fonts.json"), families);
            foreach (var f in families.Where(p => !p.Embeddable && !p.EmojiOnly))
            {
                _log.WriteLine("[字体] 警告：" + f.Family + " 存在不可嵌入或非 TrueType 轮廓的字面。");
            }

            _report.FontFamilies = families.Count;
            _log.WriteLine("[字体] " + families.Count + " 个族 → fonts.css / fonts.json");
            return families;
        }

        private static IList<FontFamilyEntry> ReadFontMetadata(string path)
        {
            if (!File.Exists(path))
            {
                return new List<FontFamilyEntry>();
            }

            return JsonConvert.DeserializeObject<List<FontFamilyEntry>>(File.ReadAllText(path, Encoding.UTF8))
                ?? new List<FontFamilyEntry>();
        }

        private static void PreserveFontMetadata(
            IList<FontFamilyEntry> families,
            IEnumerable<FontFamilyEntry> previousFamilies)
        {
            foreach (var family in families)
            {
                var previous = previousFamilies.FirstOrDefault(p =>
                    string.Equals(p.Folder, family.Folder, StringComparison.OrdinalIgnoreCase));
                if (previous == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(previous.Italic))
                {
                    family.Italic = previous.Italic;
                }

                family.EmojiOnly = previous.EmojiOnly;
                if (family.EmojiOnly)
                {
                    family.Embeddable = false;
                }

                foreach (var alias in previous.CssAliases ?? new List<string>())
                {
                    AddAlias(family, alias);
                }

                foreach (var face in family.Faces)
                {
                    var previousFace = (previous.Faces ?? new List<FontFaceEntry>()).FirstOrDefault(p =>
                        string.Equals(p.File, face.File, StringComparison.OrdinalIgnoreCase));
                    if (previousFace == null)
                    {
                        continue;
                    }

                    face.PptName = previousFace.PptName;
                    face.PptBold = previousFace.PptBold;
                    face.PptItalic = previousFace.PptItalic;
                    face.Verified = previousFace.Verified;
                }
            }
        }

        private static void AddAlias(FontFamilyEntry family, string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            if (family.CssAliases == null)
            {
                family.CssAliases = new List<string>();
            }

            if (!family.CssAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                family.CssAliases.Add(alias);
            }
        }

        // ---------- 模板 ----------

        private IEnumerable<StyleEntry> MergeTemplates(HashSet<string> localFonts)
        {
            var index = ReadIndex(Path.Combine(Beautiful, "index.json"));
            var boldIndex = ReadIndex(Path.Combine(BoldPack, "selection-index.json"));
            var result = new List<StyleEntry>();

            foreach (var dir in Directory.GetDirectories(Path.Combine(Beautiful, "templates")).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var slug = Path.GetFileName(dir);
                var outDir = Path.Combine(SlideAssetPaths.TemplatesDir(_target), slug);
                Directory.CreateDirectory(outDir);

                var entry = BuildTemplateEntry(slug, dir, index, boldIndex);

                // template.html：改写外链、收集字体族。
                var htmlPath = Path.Combine(dir, "template.html");
                var rewrite = TemplateHtmlRewriter.Rewrite(ReadText(htmlPath));
                WriteText(Path.Combine(outDir, "template.html"), rewrite.Html);
                foreach (var family in rewrite.Families)
                {
                    if (!entry.Fonts.Contains(family, StringComparer.OrdinalIgnoreCase)) entry.Fonts.Add(family);
                }

                entry.CdnFree = rewrite.Unresolved.Count == 0;
                foreach (var url in rewrite.Unresolved)
                {
                    _log.WriteLine("[外链] " + slug + "：未改写 " + url);
                }

                _report.UnresolvedReferences += rewrite.Unresolved.Count;

                // 模板自带素材（如 deck-stage.js）原样保留在同目录，保证相对引用不变。
                foreach (var file in Directory.GetFiles(dir))
                {
                    var name = Path.GetFileName(file);
                    if (name == "template.html" || name == "design.md") continue;
                    File.Copy(file, Path.Combine(outDir, name), true);
                }

                foreach (var sub in Directory.GetDirectories(dir))
                {
                    CopyDirectory(sub, Path.Combine(outDir, Path.GetFileName(sub)));
                }

                WriteText(Path.Combine(outDir, "design.md"), MergeDesign(slug, dir));

                var preview = Path.Combine(BoldPack, "templates", slug, "preview.md");
                if (File.Exists(preview))
                {
                    WriteText(Path.Combine(outDir, "preview.md"), ReadText(preview));
                }

                // 版式切分。
                var layouts = TemplateLayoutSplitter.Split(rewrite.Html);
                if (layouts.Count > 0)
                {
                    var layoutsDir = Path.Combine(outDir, "layouts");
                    Directory.CreateDirectory(layoutsDir);
                    foreach (var layout in layouts)
                    {
                        WriteText(Path.Combine(layoutsDir, layout.FileName), layout.Html);
                        entry.Layouts.Add(layout.FileName);
                    }
                }
                else
                {
                    _log.WriteLine("[版式] " + slug + "：未识别出幻灯片元素，跳过切分。");
                }

                _report.Layouts += layouts.Count;
                if (entry.SlideCount <= 0) entry.SlideCount = layouts.Count;

                // 效果图。
                var shots = Path.Combine(Beautiful, "screenshots");
                if (Directory.Exists(shots))
                {
                    foreach (var shot in Directory.GetFiles(shots, slug + "-*.png").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        var suffix = Path.GetFileNameWithoutExtension(shot).Substring(slug.Length + 1);
                        int n;
                        if (!int.TryParse(suffix, out n)) continue;
                        var thumbs = Path.Combine(outDir, "thumbs");
                        Directory.CreateDirectory(thumbs);
                        var name = n.ToString("D2") + ".png";
                        File.Copy(shot, Path.Combine(thumbs, name), true);
                        entry.Thumbs.Add(name);
                    }
                }

                ClassifyFonts(entry, localFonts);
                result.Add(entry);
                _report.Templates++;
                _log.WriteLine("[模板] " + slug + "：版式 " + layouts.Count + "，改写 " + rewrite.Rewrites.Count
                    + "，缺字体 " + entry.MissingFonts.Count + (entry.Tier == StyleTiers.Certified ? "（certified）" : string.Empty));
                if (entry.Tier == StyleTiers.Certified && entry.MissingFonts.Count > 0)
                {
                    _log.WriteLine("[字体] 警告：certified 模板 " + slug + " 缺少 " + string.Join("、", entry.MissingFonts));
                }
            }

            return result;
        }

        private StyleEntry BuildTemplateEntry(string slug, string dir, Dictionary<string, JObject> index, Dictionary<string, JObject> boldIndex)
        {
            JObject meta;
            index.TryGetValue(slug, out meta);
            var entry = StyleCatalog.FromUpstreamJson(meta, slug);

            // bold 索引补缺字段。
            JObject bold;
            if (boldIndex.TryGetValue(slug, out bold))
            {
                var extra = StyleCatalog.FromUpstreamJson(bold, slug);
                entry.Tagline = entry.Tagline ?? extra.Tagline;
                entry.BestFor = entry.BestFor ?? extra.BestFor;
                entry.AvoidFor = entry.AvoidFor ?? extra.AvoidFor;
                entry.Formality = entry.Formality ?? extra.Formality;
                entry.Density = entry.Density ?? extra.Density;
                entry.Scheme = entry.Scheme ?? extra.Scheme;
                entry.Mood = entry.Mood.Union(extra.Mood, StringComparer.OrdinalIgnoreCase).ToList();
                entry.Tone = entry.Tone.Union(extra.Tone, StringComparer.OrdinalIgnoreCase).ToList();
                entry.Occasion = entry.Occasion.Union(extra.Occasion, StringComparer.OrdinalIgnoreCase).ToList();
            }

            // template.json 提供调色板与字体。
            var templateJson = Path.Combine(dir, "template.json");
            if (File.Exists(templateJson))
            {
                var fromTemplate = StyleCatalog.FromUpstreamJson(JObject.Parse(ReadText(templateJson)), slug);
                entry.Palette = fromTemplate.Palette;
                entry.Fonts = fromTemplate.Fonts;
                entry.SafeArea = fromTemplate.SafeArea;
                entry.Name = entry.Name ?? fromTemplate.Name;
            }

            entry.Kind = StyleKinds.Template;
            entry.Source = bold != null ? "beautiful-html-templates+bold-template-pack" : "beautiful-html-templates";
            entry.Tier = CertifiedSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase) ? StyleTiers.Certified : StyleTiers.Experimental;
            return entry;
        }

        /// <summary>design.md 优先取 bold 版（固定舞台改写），补回 bold 版缺失的原版二级章节。</summary>
        private string MergeDesign(string slug, string dir)
        {
            var original = ReadText(Path.Combine(dir, "design.md")) ?? string.Empty;
            var boldPath = Path.Combine(BoldPack, "templates", slug, "design.md");
            if (!File.Exists(boldPath))
            {
                return original;
            }

            var bold = ReadText(boldPath);
            var boldHeadings = new HashSet<string>(H2.Matches(bold).Cast<Match>().Select(m => m.Groups[1].Value.Trim()), StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder(bold.TrimEnd());
            var matches = H2.Matches(original).Cast<Match>().ToList();
            for (var i = 0; i < matches.Count; i++)
            {
                var heading = matches[i].Groups[1].Value.Trim();
                if (boldHeadings.Contains(heading)) continue;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : original.Length;
                sb.Append("\n\n").Append(original.Substring(matches[i].Index, end - matches[i].Index).TrimEnd());
                _log.WriteLine("[设计] " + slug + "：补回原版章节「" + heading + "」");
            }

            return sb.Append('\n').ToString();
        }

        private static void ClassifyFonts(StyleEntry entry, HashSet<string> localFonts)
        {
            entry.LocalFonts = entry.Fonts.Where(f => localFonts.Contains(FontFaceNaming.NormalizeKey(f))).ToList();
            entry.MissingFonts = entry.Fonts.Where(f => !localFonts.Contains(FontFaceNaming.NormalizeKey(f))).ToList();
        }

        // ---------- 预设 ----------

        private IEnumerable<StyleEntry> MergePresets(HashSet<string> localFonts)
        {
            var path = Path.Combine(Frontend, "STYLE_PRESETS.md");
            var result = new List<StyleEntry>();
            if (!File.Exists(path))
            {
                return result;
            }

            var parsed = StylePresetParser.Parse(ReadText(path));
            foreach (var preset in parsed.Presets)
            {
                var dir = Path.Combine(SlideAssetPaths.PresetsDir(_target), preset.Entry.Slug);
                WriteText(Path.Combine(dir, "preset.md"), preset.Markdown);
                preset.Entry.Directory = null;
                ClassifyFonts(preset.Entry, localFonts);
                result.Add(preset.Entry);
                _report.Presets++;
            }

            if (!string.IsNullOrEmpty(parsed.SharedReference))
            {
                WriteText(Path.Combine(_target, "guides", "style-presets-reference.md"), parsed.SharedReference);
            }

            _log.WriteLine("[预设] " + _report.Presets + " 个");
            return result;
        }

        // ---------- 目录 / 运行时 / 指南 / 许可 ----------

        private void WriteCatalog(List<StyleEntry> entries)
        {
            WriteJson(Path.Combine(_target, SlideAssetPaths.CatalogFileRelative), new JObject
            {
                ["schemaVersion"] = 1,
                ["styles"] = JArray.FromObject(entries.OrderBy(p => p.Kind).ThenBy(p => p.Slug, StringComparer.Ordinal))
            });
        }

        private void CopyRuntime()
        {
            var runtime = Path.Combine(_target, "runtime");
            Directory.CreateDirectory(runtime);
            // nixer-stage.js 已由 deck-stage.js 改造并在仓库内维护，只在缺失时用上游原版占位。
            var stage = Path.Combine(runtime, "nixer-stage.js");
            if (!File.Exists(stage))
            {
                CopyIfExists(Path.Combine(Beautiful, "runtime", "deck-stage.js"), stage);
            }

            CopyIfExists(Path.Combine(Frontend, "viewport-base.css"), Path.Combine(runtime, "viewport-base.css"));
        }

        private void CopyGuides()
        {
            var guides = Path.Combine(_target, "guides");
            CopyGuide(Path.Combine(Frontend, "animation-patterns.md"), Path.Combine(guides, "animation-patterns.md"), "frontend-slides/animation-patterns.md");
            CopyGuide(Path.Combine(Frontend, "html-template.md"), Path.Combine(guides, "authoring-contract.md"), "frontend-slides/html-template.md");
            CopyGuide(Path.Combine(Beautiful, "AGENTS.md"), Path.Combine(guides, "adaptation-rules.md"), "beautiful-html-templates/AGENTS.md");
            CopyGuide(Path.Combine(BoldPack, "README.md"), Path.Combine(guides, "style-selection.md"), "frontend-slides/bold-template-pack/README.md");
            CopyGuide(Path.Combine(Frontend, "SKILL.md"), Path.Combine(guides, "design-principles.md"), "frontend-slides/SKILL.md");
        }

        private void CopyGuide(string source, string target, string origin)
        {
            if (!File.Exists(source)) return;
            WriteText(target, "<!-- 来源：" + origin + "（原文入库，P2 按《HTML 创作契约》改写）。 -->\n\n" + ReadText(source));
        }

        private void WriteNotices(IList<FontFamilyEntry> fonts)
        {
            var sb = new StringBuilder();
            sb.Append("# Third-Party Notices\n\n");
            sb.Append("本目录素材随 Nixer Office Copilot 分发，第三方组件及其许可如下。\n\n");
            sb.Append("## 模板与技能\n\n");
            sb.Append("- beautiful-html-templates — MIT，Copyright (c) 2026 Zara Zhang。见 `LICENSES/beautiful-html-templates.MIT.txt`。\n");
            sb.Append("- frontend-slides — MIT。见 `LICENSES/frontend-slides.MIT.txt`。\n\n");
            sb.Append("## 运行时\n\n");
            sb.Append("- Chart.js 4.x（`runtime/vendor/chart.umd.min.js`）— MIT。见 `LICENSES/chart.js.MIT.txt`。\n\n");
            sb.Append("## 图标\n\n");
            sb.Append("- Lucide icons — ISC；Feather-derived icons retain their MIT notice。许可原文：`icons/lucide/LICENSE.txt`。\n\n");
            sb.Append("## 字体\n\n");
            foreach (var f in fonts)
            {
                sb.Append("- ").Append(f.Family).Append(" — ").Append(f.License)
                    .Append("。许可原文：`fonts/").Append(f.Folder).Append("/OFL.txt`。\n");
            }

            WriteText(Path.Combine(_target, "THIRD_PARTY_NOTICES.md"), sb.ToString());
        }

        // ---------- IO ----------

        private static Dictionary<string, JObject> ReadIndex(string path)
        {
            var map = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return map;
            var root = JObject.Parse(ReadText(path));
            foreach (var t in (root["templates"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var slug = (string)t["slug"];
                if (!string.IsNullOrEmpty(slug)) map[slug] = t;
            }

            return map;
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        private static void CopyIfExists(string source, string target)
        {
            if (!File.Exists(source)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, true);
        }

        internal static string ReadText(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        internal static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, (text ?? string.Empty).Replace("\r\n", "\n"), Utf8);
        }

        private static void WriteJson(string path, object value)
        {
            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore };
            WriteText(path, JsonConvert.SerializeObject(value, settings) + "\n");
        }
    }
}
