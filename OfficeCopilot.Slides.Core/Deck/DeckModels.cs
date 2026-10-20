using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace OfficeCopilot.Slides.Deck
{
    public sealed class DeckException : Exception
    {
        public DeckException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        /// <summary>面向模型的错误码（ppt.deck_*）。</summary>
        public string Code { get; }
    }

    public static class DeckIds
    {
        private static readonly Regex DeckIdPattern = new Regex(@"^\d{8}-\d{6}-[a-z0-9]{6}$", RegexOptions.Compiled);
        private static readonly Regex SlideIdPattern = new Regex(@"^s\d{2}$", RegexOptions.Compiled);
        private static readonly Regex PreviewNamePattern = new Regex(@"^[a-z][a-z0-9-]{0,23}$", RegexOptions.Compiled);

        public const int MaxSlides = 99;

        public static bool IsValidDeckId(string value)
        {
            return !string.IsNullOrEmpty(value) && DeckIdPattern.IsMatch(value);
        }

        public static bool IsValidSlideId(string value)
        {
            return !string.IsNullOrEmpty(value) && SlideIdPattern.IsMatch(value) && value != "s00";
        }

        public static bool IsValidPreviewName(string value)
        {
            return !string.IsNullOrEmpty(value) && PreviewNamePattern.IsMatch(value);
        }

        public static string FormatSlideId(int number)
        {
            return "s" + number.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static int ParseSlideNumber(string slideId)
        {
            return IsValidSlideId(slideId) ? int.Parse(slideId.Substring(1), System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
    }

    public static class DeckDensities
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";

        public static string Normalize(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case Low: return Low;
                case High: return High;
                default: return Medium;
            }
        }
    }

    public sealed class DeckManifest
    {
        [JsonProperty("version")] public int Version { get; set; } = 1;
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("style")] public string Style { get; set; }
        [JsonProperty("density")] public string Density { get; set; } = DeckDensities.Medium;
        [JsonProperty("language")] public string Language { get; set; } = "zh-CN";
        [JsonProperty("createdUtc")] public DateTime CreatedUtc { get; set; }
        [JsonProperty("updatedUtc")] public DateTime UpdatedUtc { get; set; }
        [JsonProperty("slides")] public List<DeckSlideInfo> Slides { get; set; } = new List<DeckSlideInfo>();
        [JsonProperty("origin", NullValueHandling = NullValueHandling.Ignore)] public DeckManifestOrigin Origin { get; set; }
    }

    public sealed class DeckManifestOrigin
    {
        [JsonProperty("preview")] public string Preview { get; set; }
        [JsonProperty("hash")] public string Hash { get; set; }

        /// <summary>由候选生成的封面页 id；该页被锁定，ppt_deck_put_slides 不会覆盖。</summary>
        [JsonProperty("slide", NullValueHandling = NullValueHandling.Ignore)] public string Slide { get; set; }
    }

    public sealed class DeckPreviewSourceInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("style")] public string Style { get; set; }
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("generatedUtc")] public DateTime GeneratedUtc { get; set; }
    }

    public sealed class DeckPreviewSource
    {
        public string Name { get; set; }
        public string Style { get; set; }
        public string Hash { get; set; }
        public DateTime GeneratedUtc { get; set; }
        public string Html { get; set; }
        public string Css { get; set; }
    }

    public sealed class DeckSlideInfo
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("hash")] public string Hash { get; set; }
        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)] public string Title { get; set; }
        [JsonProperty("layout", NullValueHandling = NullValueHandling.Ignore)] public string Layout { get; set; }
    }

    public sealed class DeckSlideInput
    {
        /// <summary>已有页 id 表示覆盖；为空表示新增。</summary>
        public string Id { get; set; }

        public string Html { get; set; }

        /// <summary>新增页插入位置（1 起）；为空追加到末尾。覆盖时忽略。</summary>
        public int? Position { get; set; }
    }

    public sealed class DeckSlidePutResult
    {
        public string Id { get; set; }
        public int Position { get; set; }
        public bool Created { get; set; }
        public string Hash { get; set; }
        public string Title { get; set; }
        public bool Locked { get; set; }
        public IList<string> Removed { get; set; } = new List<string>();
    }
}
