using System.Collections.Generic;
using Newtonsoft.Json;

namespace OfficeCopilot.Slides.Catalog
{
    public static class StyleKinds
    {
        public const string Template = "template";
        public const string Preset = "preset";
    }

    public static class StyleTiers
    {
        public const string Certified = "certified";
        public const string Experimental = "experimental";
        public const string User = "user";
    }

    public sealed class StyleSafeArea
    {
        [JsonProperty("content")] public List<string> Content { get; set; } = new List<string>();
        [JsonProperty("background")] public List<string> Background { get; set; } = new List<string>();
        [JsonProperty("chrome")] public List<string> Chrome { get; set; } = new List<string>();
        [JsonProperty("safeX")] public int SafeX { get; set; }
        [JsonProperty("safeY")] public int SafeY { get; set; }
    }

    /// <summary>
    /// 风格目录条目。<c>catalog/styles.json</c> 由合并工具生成，字段为两份上游索引的并集。
    /// </summary>
    public sealed class StyleEntry
    {
        [JsonProperty("slug")] public string Slug { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; } = StyleKinds.Template;
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("tier")] public string Tier { get; set; } = StyleTiers.Experimental;
        [JsonProperty("tagline")] public string Tagline { get; set; }
        [JsonProperty("mood")] public List<string> Mood { get; set; } = new List<string>();
        [JsonProperty("occasion")] public List<string> Occasion { get; set; } = new List<string>();
        [JsonProperty("tone")] public List<string> Tone { get; set; } = new List<string>();
        [JsonProperty("formality")] public string Formality { get; set; }
        [JsonProperty("density")] public string Density { get; set; }
        [JsonProperty("scheme")] public string Scheme { get; set; }
        [JsonProperty("bestFor")] public string BestFor { get; set; }
        [JsonProperty("avoidFor")] public string AvoidFor { get; set; }
        [JsonProperty("slideCount")] public int SlideCount { get; set; }
        [JsonProperty("safeArea", NullValueHandling = NullValueHandling.Ignore)]
        public StyleSafeArea SafeArea { get; set; }

        /// <summary>模板声明使用的字体族。</summary>
        [JsonProperty("fonts")] public List<string> Fonts { get; set; } = new List<string>();

        /// <summary>其中本地可用（随包或用户目录）的字体族。</summary>
        [JsonProperty("localFonts")] public List<string> LocalFonts { get; set; } = new List<string>();

        /// <summary>本地缺失、渲染时会回退的字体族。</summary>
        [JsonProperty("missingFonts")] public List<string> MissingFonts { get; set; } = new List<string>();

        [JsonProperty("palette")] public Dictionary<string, string> Palette { get; set; } = new Dictionary<string, string>();
        [JsonProperty("layouts")] public List<string> Layouts { get; set; } = new List<string>();
        [JsonProperty("thumbs")] public List<string> Thumbs { get; set; } = new List<string>();

        /// <summary>模板已改写为零外链。</summary>
        [JsonProperty("cdnFree")] public bool CdnFree { get; set; }

        [JsonProperty("isValid")] public bool IsValid { get; set; } = true;
        [JsonProperty("invalidReason", NullValueHandling = NullValueHandling.Ignore)] public string InvalidReason { get; set; }

        /// <summary>"builtin" / "user"（加载时填充）。</summary>
        [JsonIgnore] public string Origin { get; set; }

        /// <summary>条目目录的绝对路径（加载时填充）。</summary>
        [JsonIgnore] public string Directory { get; set; }
    }

    public sealed class StyleSearchQuery
    {
        public string Text { get; set; }
        public string Kind { get; set; }
        public string Scheme { get; set; }
        public string Formality { get; set; }
        public string Density { get; set; }
        public string Tier { get; set; }
        public bool IncludeInvalid { get; set; }
        public int MaxResults { get; set; } = 8;
    }

    public sealed class StyleSearchHit
    {
        public StyleEntry Entry { get; set; }
        public double Score { get; set; }
    }

    public sealed class StyleDetail
    {
        public StyleEntry Entry { get; set; }
        public string DesignMarkdown { get; set; }
        public string PreviewMarkdown { get; set; }
        public string PresetMarkdown { get; set; }
        public string Typography { get; set; }
        public bool DesignTruncated { get; set; }
    }
}
