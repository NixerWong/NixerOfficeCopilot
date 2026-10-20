using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace OfficeCopilot.Slides.Fonts
{
    public sealed class FontPolicyFamily
    {
        [JsonProperty("bundled")] public bool Bundled { get; set; }
        [JsonProperty("weights")] public IList<int> Weights { get; set; }
        [JsonProperty("italic")] public string Italic { get; set; }
        [JsonProperty("cjk")] public bool Cjk { get; set; }
        [JsonProperty("emojiOnly")] public bool EmojiOnly { get; set; }
        [JsonProperty("coverage")] public IList<UnicodeCoverageRange> Coverage { get; set; }
        [JsonProperty("emojiCoverage")] public IList<UnicodeCoverageRange> EmojiCoverage { get; set; }
    }

    public sealed class FontPolicy
    {
        private IDictionary<string, string> _canonicalFamilyNames;

        private static readonly string[] SystemFamilyNames =
        {
            "Segoe UI", "Microsoft YaHei", "SimSun", "SimHei", "Consolas", "Tahoma", "Verdana",
            "Georgia", "Arial", "Times New Roman", "Courier New", "Calibri", "Cambria"
        };

        private static readonly string[] GenericFamilyNames =
        {
            "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif",
            "ui-sans-serif", "ui-monospace", "ui-rounded", "math", "emoji", "fangsong",
            "-apple-system", "BlinkMacSystemFont", "inherit", "initial", "unset"
        };

        private static readonly HashSet<string> SystemFamilySet =
            new HashSet<string>(SystemFamilyNames, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> GenericFamilySet =
            new HashSet<string>(GenericFamilyNames, StringComparer.OrdinalIgnoreCase);
        private static readonly IList<string> GenericFamilyList = Array.AsReadOnly(GenericFamilyNames);

        [JsonProperty("families")]
        public IDictionary<string, FontPolicyFamily> Families { get; private set; }

        [JsonProperty("system")]
        public IList<string> System { get; private set; }

        [JsonProperty("generic")]
        public IList<string> Generic { get; private set; }

        private FontPolicy()
        {
            _canonicalFamilyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public static FontPolicy FromCatalog(FontCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var families = new SortedDictionary<string, FontPolicyFamily>(StringComparer.OrdinalIgnoreCase);
            var canonicalFamilyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ambiguousFamilyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = catalog.Families;
            var emojiEntry = entries.FirstOrDefault(entry => entry.EmojiOnly);
            var emojiCoverage = emojiEntry == null
                ? new List<UnicodeCoverageRange>()
                : ReadCoverage(emojiEntry);
            foreach (var entry in entries)
            {
                var policy = new FontPolicyFamily
                {
                    Bundled = true,
                    Weights = entry.Faces.Select(face => face.Weight).Distinct().OrderBy(weight => weight).ToList(),
                    Italic = ResolveItalicPolicy(entry),
                    Cjk = entry.Cjk,
                    EmojiOnly = entry.EmojiOnly,
                    Coverage = ReadCoverage(entry),
                    EmojiCoverage = entry.EmojiOnly ? new List<UnicodeCoverageRange>() : emojiCoverage
                };
                families[entry.Family] = policy;
                AddCanonicalFamilyName(canonicalFamilyNames, ambiguousFamilyNames, entry.Family, entry.Family);
                foreach (var alias in entry.CssAliases ?? new List<string>())
                {
                    if (!string.IsNullOrWhiteSpace(alias) && !families.ContainsKey(alias))
                    {
                        families[alias] = policy;
                    }

                    AddCanonicalFamilyName(canonicalFamilyNames, ambiguousFamilyNames, alias, entry.Family);
                }

                foreach (var face in entry.Faces)
                {
                    var path = Path.Combine(entry.Directory ?? string.Empty, face.File ?? string.Empty);
                    AddCanonicalFamilyName(
                        canonicalFamilyNames,
                        ambiguousFamilyNames,
                        TrueTypeInfo.ReadLegacyFamilyName(path),
                        entry.Family);
                    AddCanonicalFamilyName(
                        canonicalFamilyNames,
                        ambiguousFamilyNames,
                        TrueTypeInfo.ReadName(path, 16),
                        entry.Family);
                }
            }

            var systemFonts = SystemFontCatalog.Default;
            foreach (var entry in systemFonts.Families.Where(family => family.SupportsChinese))
            {
                if (families.ContainsKey(entry.Family)) continue;
                families[entry.Family] = new FontPolicyFamily
                {
                    Bundled = false,
                    Weights = new List<int>(),
                    Italic = "system",
                    Cjk = true,
                    EmojiOnly = false,
                    Coverage = entry.Coverage ?? new List<UnicodeCoverageRange>(),
                    EmojiCoverage = new List<UnicodeCoverageRange>()
                };
                if (!canonicalFamilyNames.ContainsKey(entry.Family))
                {
                    AddCanonicalFamilyName(canonicalFamilyNames, ambiguousFamilyNames, entry.Family, entry.Family);
                }
            }

            var systemNames = SystemFamilyNames
                .Concat(systemFonts.Families.Select(family => family.Family))
                .Where(name => !families.TryGetValue(name, out var known) || !known.Bundled)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new FontPolicy
            {
                Families = families,
                System = systemNames,
                Generic = GenericFamilyList,
                _canonicalFamilyNames = canonicalFamilyNames
            };
        }

        public static bool IsSystemFamily(string family)
        {
            return SystemFamilySet.Contains(family ?? string.Empty) || SystemFontCatalog.Default.Contains(family);
        }

        public static bool IsGenericFamily(string family)
        {
            return GenericFamilySet.Contains(family ?? string.Empty);
        }

        public bool AreEquivalentFamilyNames(string first, string second)
        {
            if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            string firstCanonical;
            string secondCanonical;
            return _canonicalFamilyNames.TryGetValue(first, out firstCanonical)
                && _canonicalFamilyNames.TryGetValue(second, out secondCanonical)
                && string.Equals(firstCanonical, secondCanonical, StringComparison.OrdinalIgnoreCase);
        }

        internal static string ResolveItalicPolicy(FontFamilyEntry family)
        {
            if (!string.IsNullOrWhiteSpace(family.Italic))
            {
                return family.Italic;
            }

            return family.Faces.Any(face =>
                string.Equals(face.Style, "italic", StringComparison.OrdinalIgnoreCase))
                ? "true"
                : "none";
        }

        private static void AddCanonicalFamilyName(
            IDictionary<string, string> canonicalFamilyNames,
            ISet<string> ambiguousFamilyNames,
            string name,
            string canonicalFamily)
        {
            if (string.IsNullOrWhiteSpace(name) || ambiguousFamilyNames.Contains(name))
            {
                return;
            }

            string existingCanonical;
            if (canonicalFamilyNames.TryGetValue(name, out existingCanonical))
            {
                if (!string.Equals(existingCanonical, canonicalFamily, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalFamilyNames.Remove(name);
                    ambiguousFamilyNames.Add(name);
                }

                return;
            }

            canonicalFamilyNames[name] = canonicalFamily;
        }

        private static IList<UnicodeCoverageRange> ReadCoverage(FontFamilyEntry family)
        {
            var ranges = new List<UnicodeCoverageRange>();
            foreach (var face in family.Faces)
            {
                var path = Path.Combine(family.Directory ?? string.Empty, face.File ?? string.Empty);
                ranges.AddRange(TrueTypeInfo.ReadUnicodeCoverage(path));
            }

            var merged = new List<UnicodeCoverageRange>();
            foreach (var range in ranges.OrderBy(item => item.Start).ThenBy(item => item.End))
            {
                if (merged.Count == 0 || (long)range.Start > (long)merged[merged.Count - 1].End + 1)
                {
                    merged.Add(new UnicodeCoverageRange { Start = range.Start, End = range.End });
                }
                else
                {
                    merged[merged.Count - 1].End = Math.Max(merged[merged.Count - 1].End, range.End);
                }
            }

            return merged;
        }
    }
}
