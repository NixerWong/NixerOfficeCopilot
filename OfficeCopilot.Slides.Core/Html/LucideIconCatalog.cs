using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OfficeCopilot.Slides.Assets;

namespace OfficeCopilot.Slides.Html
{
    internal static class LucideIconCatalog
    {
        private static readonly Regex ValidName = new Regex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
        private static readonly Regex SvgBody = new Regex(
            @"<svg\b[^>]*>(?<body>.*?)</svg\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<string, string> Cache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Lucide 改名前的旧名（模型常用）→ 现名。
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["home"] = "house",
            ["bar-chart"] = "chart-column",
            ["bar-chart-2"] = "chart-column",
            ["bar-chart-3"] = "chart-column",
            ["line-chart"] = "chart-line",
            ["pie-chart"] = "chart-pie",
            ["alert-triangle"] = "triangle-alert",
            ["alert-circle"] = "circle-alert",
            ["check-circle"] = "circle-check",
            ["check-circle-2"] = "circle-check",
            ["help-circle"] = "circle-help",
            ["tree"] = "tree-deciduous",
            ["globe-2"] = "earth",
            ["train"] = "train-front"
        };

        /// <summary>
        /// 归一化图标名：去掉模型常加的 <c>lucide-</c>/<c>lucide:</c> 前缀并映射旧名。
        /// </summary>
        public static string NormalizeName(string name)
        {
            var key = (name ?? string.Empty).Trim().ToLowerInvariant();
            if (key.StartsWith("lucide-", StringComparison.Ordinal) || key.StartsWith("lucide:", StringComparison.Ordinal))
            {
                key = key.Substring("lucide-".Length);
            }

            string alias;
            return Aliases.TryGetValue(key, out alias) ? alias : key;
        }

        /// <summary>
        /// 把模型写的图标名解析为随包图标；不存在时替换为最相近的图标（无相近项则用 <see cref="FallbackIcon"/>），
        /// 不再让单个未知图标拒绝整页/整批写入。
        /// </summary>
        public static string Resolve(string name, out string substitutedFrom)
        {
            substitutedFrom = null;
            var key = NormalizeName(name);
            var directory = IconDirectory();
            if (ValidName.IsMatch(key) && File.Exists(Path.Combine(directory, key + ".svg")))
            {
                return key;
            }

            substitutedFrom = string.IsNullOrEmpty(key) ? "(空)" : key;
            var suggestions = ValidName.IsMatch(key) ? Suggest(key, AvailableIcons(directory)) : new List<string>();
            return suggestions.Count > 0 ? suggestions[0] : FallbackIcon;
        }

        public const string FallbackIcon = "sparkle";

        public static string GetSvgBody(string name)
        {
            var key = NormalizeName(name);
            if (!ValidName.IsMatch(key))
            {
                throw new ArgumentException("nx-icon 的 data-icon 必须是小写 Lucide 图标名，例如 leaf 或 arrow-right。", nameof(name));
            }

            return Cache.GetOrAdd(key, ReadSvgBody);
        }

        private static string ReadSvgBody(string name)
        {
            var iconDirectory = IconDirectory();
            var path = Path.Combine(iconDirectory, name + ".svg");
            if (!File.Exists(path))
            {
                var available = AvailableIcons(iconDirectory);
                var suggestions = Suggest(name, available);
                throw new ArgumentException(
                    "不支持 Lucide 图标“" + name + "”"
                    + (suggestions.Count > 0 ? "，可改用：" + string.Join(", ", suggestions) : string.Empty)
                    + "。data-icon 只写图标名（不加 lucide- 前缀）。当前随包图标：" + string.Join(", ", available) + "。",
                    nameof(name));
            }

            var match = SvgBody.Match(File.ReadAllText(path));
            if (!match.Success)
            {
                throw new InvalidDataException("随包 Lucide 图标 SVG 格式无效：" + name);
            }

            return match.Groups["body"].Value;
        }

        private static string IconDirectory()
        {
            return Path.Combine(SlideAssetPaths.GetDefaultBuiltInRoot(), "icons", "lucide");
        }

        private static IList<string> Suggest(string name, IList<string> available)
        {
            var parts = name.Split('-');
            return available
                .Select(candidate => new
                {
                    Name = candidate,
                    Distance = Distance(name, candidate),
                    Shared = parts.Count(part => part.Length > 2 && candidate.Split('-').Contains(part))
                })
                .Where(x => x.Distance <= 2
                    || x.Shared > 0
                    || (name.Length >= 3 && x.Name.Contains(name))
                    || (x.Name.Length >= 3 && name.Contains(x.Name)))
                .OrderByDescending(x => x.Shared)
                .ThenBy(x => x.Distance)
                .Take(3)
                .Select(x => x.Name)
                .ToList();
        }

        private static int Distance(string a, string b)
        {
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) previous[j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }

                var swap = previous;
                previous = current;
                current = swap;
            }

            return previous[b.Length];
        }

        private static string[] AvailableIcons(string directory)
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("Lucide 图标素材目录不存在：" + directory);
            }

            var names = Directory.GetFiles(directory, "*.svg");
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = Path.GetFileNameWithoutExtension(names[i]);
            }

            return names;
        }
    }
}
