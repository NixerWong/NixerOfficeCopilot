using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using OfficeCopilot.Slides.Assets;

namespace OfficeCopilot.Slides.Fonts
{
    public sealed class FontFaceEntry
    {
        [JsonProperty("file")] public string File { get; set; }
        [JsonProperty("weight")] public int Weight { get; set; }
        [JsonProperty("style")] public string Style { get; set; }
        [JsonProperty("embedding")] public string Embedding { get; set; }
        [JsonProperty("trueTypeOutline")] public bool TrueTypeOutline { get; set; }
        [JsonProperty("pptName", NullValueHandling = NullValueHandling.Ignore)] public string PptName { get; set; }
        [JsonProperty("pptFarEastName", NullValueHandling = NullValueHandling.Ignore)] public string PptFarEastName { get; set; }
        [JsonProperty("pptBold", NullValueHandling = NullValueHandling.Ignore)] public bool? PptBold { get; set; }
        [JsonProperty("pptItalic", NullValueHandling = NullValueHandling.Ignore)] public bool? PptItalic { get; set; }
        [JsonProperty("verified", NullValueHandling = NullValueHandling.Ignore)] public FontFaceVerification Verified { get; set; }
    }

    public sealed class FontFaceVerification
    {
        [JsonProperty("date")] public string Date { get; set; }
        [JsonProperty("officeVersion")] public string OfficeVersion { get; set; }
    }

    public sealed class FontFamilyEntry
    {
        [JsonProperty("family")] public string Family { get; set; }
        [JsonProperty("folder")] public string Folder { get; set; }
        [JsonProperty("license")] public string License { get; set; }
        [JsonProperty("cjk")] public bool Cjk { get; set; }
        [JsonProperty("embeddable")] public bool Embeddable { get; set; }
        [JsonProperty("emojiOnly")] public bool EmojiOnly { get; set; }
        [JsonProperty("italic", NullValueHandling = NullValueHandling.Ignore)] public string Italic { get; set; }
        [JsonProperty("cssAliases", NullValueHandling = NullValueHandling.Ignore)] public List<string> CssAliases { get; set; }
        [JsonProperty("faces")] public List<FontFaceEntry> Faces { get; set; } = new List<FontFaceEntry>();

        /// <summary>字体目录的绝对路径（加载时填充，不序列化）。</summary>
        [JsonIgnore] public string Directory { get; set; }

        /// <summary>"builtin" / "user"。</summary>
        [JsonIgnore] public string Origin { get; set; }
    }

    /// <summary>一次字体选择的结果：PowerPoint 应使用的字体名，以及需要额外设置的粗体/斜体标记。</summary>
    public sealed class PowerPointFace
    {
        public string Name { get; set; }

        public string FarEastName { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }
    }

    /// <summary>
    /// 本地字体目录：内置 <c>fonts/fonts.json</c> + 用户 <c>fonts/{Folder}/*.ttf</c>。同族名用户优先。
    /// </summary>
    public sealed class FontCatalog
    {
        private static readonly string[] CjkFolders = { "NotoSansSC", "NotoSerifSC", "LXGWWenKai", "SmileySans", "ZCOOLXiaoWei", "ZCOOLKuaiLe", "NotoSansJP" };

        private readonly Dictionary<string, FontFamilyEntry> _byKey =
            new Dictionary<string, FontFamilyEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, FontFamilyEntry> _aliases =
            new Dictionary<string, FontFamilyEntry>(StringComparer.Ordinal);

        public IReadOnlyList<FontFamilyEntry> Families
        {
            get { return _byKey.Values.OrderBy(p => p.Family, StringComparer.OrdinalIgnoreCase).ToList(); }
        }

        public static FontCatalog Load(SlideAssetPaths paths)
        {
            var catalog = new FontCatalog();
            if (paths == null)
            {
                return catalog;
            }

            var builtInJson = Path.Combine(paths.BuiltInRoot, SlideAssetPaths.FontsJsonRelative);
            if (System.IO.File.Exists(builtInJson))
            {
                var list = JsonConvert.DeserializeObject<List<FontFamilyEntry>>(
                    System.IO.File.ReadAllText(builtInJson, Encoding.UTF8)) ?? new List<FontFamilyEntry>();
                foreach (var entry in list)
                {
                    entry.Directory = Path.Combine(SlideAssetPaths.FontsDir(paths.BuiltInRoot), entry.Folder ?? string.Empty);
                    entry.Origin = "builtin";
                    catalog.Add(entry);
                }
            }

            foreach (var entry in ScanFolder(SlideAssetPaths.FontsDir(paths.UserRoot)))
            {
                entry.Origin = "user";
                catalog.Add(entry);
            }

            return catalog;
        }

        /// <summary>扫描 <c>fonts/{Folder}/*.ttf</c>，生成族条目。合并工具与用户目录共用。</summary>
        public static IList<FontFamilyEntry> ScanFolder(string fontsRoot)
        {
            var result = new List<FontFamilyEntry>();
            if (string.IsNullOrEmpty(fontsRoot) || !System.IO.Directory.Exists(fontsRoot))
            {
                return result;
            }

            foreach (var dir in System.IO.Directory.GetDirectories(fontsRoot).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var folder = Path.GetFileName(dir);
                var files = System.IO.Directory.GetFiles(dir, "*.ttf").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
                if (files.Length == 0)
                {
                    continue;
                }

                var entry = new FontFamilyEntry
                {
                    Family = FontFaceNaming.FolderToFamily(folder),
                    Folder = folder,
                    Directory = dir,
                    License = DetectLicense(dir),
                    Cjk = CjkFolders.Contains(folder, StringComparer.OrdinalIgnoreCase),
                    Italic = string.Equals(folder, "SmileySans", StringComparison.OrdinalIgnoreCase) ? "builtin" : null
                };

                foreach (var file in files)
                {
                    int weight;
                    string style;
                    FontFaceNaming.ParseFace(file, out weight, out style);
                    if (string.Equals(entry.Italic, "builtin", StringComparison.OrdinalIgnoreCase))
                    {
                        style = "normal";
                    }

                    bool tt;
                    var embedding = TrueTypeInfo.ReadEmbedding(file, out tt);
                    entry.Faces.Add(new FontFaceEntry
                    {
                        File = Path.GetFileName(file),
                        Weight = weight,
                        Style = style,
                        Embedding = embedding,
                        TrueTypeOutline = tt
                    });
                }

                entry.Faces = entry.Faces.OrderBy(p => p.Style == "italic" ? 1 : 0).ThenBy(p => p.Weight).ToList();
                entry.Embeddable = entry.Faces.All(p => TrueTypeInfo.IsEmbeddable(p.Embedding) && p.TrueTypeOutline);
                result.Add(entry);
            }

            return result;
        }

        public bool TryResolve(string family, out FontFamilyEntry entry)
        {
            if (_byKey.TryGetValue(FontFaceNaming.NormalizeKey(family), out entry))
            {
                return true;
            }

            return _aliases.TryGetValue(FontFaceNaming.NormalizeKey(family), out entry);
        }

        public bool Contains(string family)
        {
            FontFamilyEntry ignored;
            return TryResolve(family, out ignored);
        }

        public bool IsCjk(string family)
        {
            FontFamilyEntry entry;
            return (TryResolve(family, out entry) && entry.Cjk)
                || SystemFontCatalog.Default.IsChineseFamily(family);
        }

        public bool IsEmojiOnly(string family)
        {
            FontFamilyEntry entry;
            return TryResolve(family, out entry) && entry.EmojiOnly;
        }

        /// <summary>
        /// 按 CSS 字体匹配规则（先字重、后斜体）在族内选面，返回 PowerPoint 应使用的字体名与粗斜体标记。
        /// 族不在字体库中时返回 null。
        /// </summary>
        public PowerPointFace SelectFace(string family, int weight, bool italic)
        {
            FontFamilyEntry entry;
            if (!TryResolve(family, out entry) || entry.Faces.Count == 0)
            {
                return null;
            }

            var face = MatchFace(entry.Faces, weight, italic);
            var legacyName = entry.Directory == null
                ? null
                : TrueTypeInfo.ReadLegacyFamilyName(Path.Combine(entry.Directory, face.File));
            if (string.IsNullOrWhiteSpace(legacyName))
            {
                legacyName = entry.Family;
            }
            if (!string.IsNullOrWhiteSpace(face.PptName))
            {
                legacyName = face.PptName;
            }

            // 族名本身（RIBBI 面）由 PowerPoint 的粗体/斜体开关选择；其余字重和斜体变体用各自的旧式族名。
            var ribbi = string.Equals(legacyName, entry.Family, StringComparison.OrdinalIgnoreCase);
            var faceItalic = string.Equals(face.Style, "italic", StringComparison.OrdinalIgnoreCase);
            var italicPolicy = FontPolicy.ResolveItalicPolicy(entry);
            var inferredItalic = (ribbi && faceItalic)
                || (italic
                    && !faceItalic
                    && string.Equals(italicPolicy, "true", StringComparison.OrdinalIgnoreCase));
            if (string.Equals(italicPolicy, "builtin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(italicPolicy, "none", StringComparison.OrdinalIgnoreCase))
            {
                inferredItalic = false;
            }

            return new PowerPointFace
            {
                Name = legacyName,
                FarEastName = string.IsNullOrWhiteSpace(face.PptFarEastName) ? legacyName : face.PptFarEastName,
                Bold = face.PptBold ?? ((ribbi && face.Weight >= 600) || (weight >= 600 && face.Weight < 600)),
                Italic = face.PptItalic ?? inferredItalic
            };
        }

        internal int? SelectAvailableWeight(string family, int weight, bool italic)
        {
            FontFamilyEntry entry;
            if (!TryResolve(family, out entry) || entry.Faces.Count == 0)
            {
                return null;
            }

            return MatchFace(entry.Faces, weight, italic).Weight;
        }

        /// <summary>CSS 字重匹配（CSS Fonts 4 §5.2）：返回越小越优先。</summary>
        internal static int WeightRank(int available, int desired)
        {
            if (desired >= 400 && desired <= 500)
            {
                if (available >= desired && available <= 500) return available - desired;
                if (available < desired) return 1000 + (desired - available);
                return 2000 + (available - 500);
            }

            if (desired < 400)
            {
                return available <= desired ? desired - available : 1000 + (available - desired);
            }

            return available >= desired ? available - desired : 1000 + (desired - available);
        }

        private static FontFaceEntry MatchFace(IList<FontFaceEntry> faces, int weight, bool italic)
        {
            var styles = italic ? new[] { "italic", "normal" } : new[] { "normal", "italic" };
            foreach (var style in styles)
            {
                var pool = faces.Where(face => string.Equals(face.Style, style, StringComparison.OrdinalIgnoreCase)).ToList();
                if (pool.Count > 0)
                {
                    return pool.OrderBy(face => WeightRank(face.Weight, weight)).First();
                }
            }

            return faces[0];
        }

        /// <summary>
        /// 生成 @font-face 样式表。<paramref name="urlPrefixResolver"/> 把族条目映射为 URL 前缀（以 / 结尾）。
        /// </summary>
        public static string BuildCss(IEnumerable<FontFamilyEntry> families, Func<FontFamilyEntry, string> urlPrefixResolver)
        {
            var sb = new StringBuilder();
            var familyList = (families ?? Enumerable.Empty<FontFamilyEntry>()).ToList();
            var emojiFamily = familyList.FirstOrDefault(family => family.EmojiOnly && family.Faces.Count > 0);
            sb.Append("/* 由 OfficeCopilot 生成：本地字体，禁止改为 CDN。 */\n");
            foreach (var family in familyList)
            {
                AppendCssFaces(sb, family, family.Family, urlPrefixResolver);
                if (emojiFamily != null && !family.EmojiOnly)
                {
                    AppendEmojiCssFace(sb, family.Family, emojiFamily, urlPrefixResolver);
                }
            }

            foreach (var family in familyList)
            {
                foreach (var alias in family.CssAliases ?? new List<string>())
                {
                    sb.Append("/* 别名：").Append(alias).Append(" → ").Append(family.Family).Append(" */\n");
                    AppendCssFaces(sb, family, alias, urlPrefixResolver);
                    if (emojiFamily != null && !family.EmojiOnly)
                    {
                        AppendEmojiCssFace(sb, alias, emojiFamily, urlPrefixResolver);
                    }
                }
            }

            return sb.ToString();
        }

        private static void AppendCssFaces(
            StringBuilder sb,
            FontFamilyEntry family,
            string cssFamily,
            Func<FontFamilyEntry, string> urlPrefixResolver)
        {
            var prefix = urlPrefixResolver == null ? family.Folder + "/" : urlPrefixResolver(family);
            foreach (var face in family.Faces)
            {
                sb.Append("@font-face {\n")
                    .Append("  font-family: '").Append(cssFamily.Replace("'", string.Empty)).Append("';\n")
                    .Append("  src: url('").Append(prefix).Append(Uri.EscapeDataString(face.File)).Append("') format('truetype');\n")
                    .Append("  font-weight: ").Append(face.Weight).Append(";\n")
                    .Append("  font-style: ").Append(face.Style).Append(";\n")
                    .Append("  font-display: block;\n")
                    .Append("}\n");
            }
        }

        private static void AppendEmojiCssFace(
            StringBuilder sb,
            string cssFamily,
            FontFamilyEntry emojiFamily,
            Func<FontFamilyEntry, string> urlPrefixResolver)
        {
            var face = emojiFamily.Faces[0];
            var prefix = urlPrefixResolver == null ? emojiFamily.Folder + "/" : urlPrefixResolver(emojiFamily);
            sb.Append("@font-face {\n")
                .Append("  font-family: '").Append(cssFamily.Replace("'", string.Empty)).Append("';\n")
                .Append("  src: url('").Append(prefix).Append(Uri.EscapeDataString(face.File)).Append("') format('truetype');\n")
                .Append("  font-weight: 100 900;\n")
                .Append("  font-style: normal;\n")
                .Append("  font-display: block;\n")
                .Append("  unicode-range: U+00A9,U+00AE,U+203C,U+2049,U+2122,U+2139,U+2194-21AA,U+231A-23FA,U+24C2,U+25AA-27BF,U+2934-2935,U+2B00-2BFF,U+3030,U+303D,U+3297,U+3299,U+1F000-1FAFF,U+200D,U+20E3,U+FE0F;\n")
                .Append("}\n");
        }

        private void Add(FontFamilyEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Family))
            {
                return;
            }

            _byKey[FontFaceNaming.NormalizeKey(entry.Family)] = entry;
            foreach (var alias in entry.CssAliases ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    _aliases[FontFaceNaming.NormalizeKey(alias)] = entry;
                }
            }
        }

        private static string DetectLicense(string dir)
        {
            if (System.IO.File.Exists(Path.Combine(dir, "OFL.txt"))) return "OFL-1.1";
            if (System.IO.File.Exists(Path.Combine(dir, "LICENSE.txt")) || System.IO.File.Exists(Path.Combine(dir, "LICENSE"))) return "see LICENSE";
            return "unknown";
        }
    }
}
