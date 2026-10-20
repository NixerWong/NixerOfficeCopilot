using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OfficeCopilot.Slides.Fonts
{
    /// <summary>
    /// 字体文件命名约定：<c>fonts/{Folder}/{Folder}-{Style}.ttf</c>，
    /// 例如 <c>SourceSerif4-SemiBold.ttf</c>、<c>Lora-Italic.ttf</c>、<c>Inter-BoldItalic.ttf</c>。
    /// </summary>
    public static class FontFaceNaming
    {
        private static readonly KeyValuePair<string, int>[] Weights =
        {
            // 长名在前，避免 "Bold" 先于 "SemiBold" 命中。
            new KeyValuePair<string, int>("ExtraLight", 200),
            new KeyValuePair<string, int>("UltraLight", 200),
            new KeyValuePair<string, int>("SemiBold", 600),
            new KeyValuePair<string, int>("DemiBold", 600),
            new KeyValuePair<string, int>("ExtraBold", 800),
            new KeyValuePair<string, int>("UltraBold", 800),
            new KeyValuePair<string, int>("Regular", 400),
            new KeyValuePair<string, int>("Medium", 500),
            new KeyValuePair<string, int>("Normal", 400),
            new KeyValuePair<string, int>("Light", 300),
            new KeyValuePair<string, int>("Black", 900),
            new KeyValuePair<string, int>("Heavy", 900),
            new KeyValuePair<string, int>("Thin", 100),
            new KeyValuePair<string, int>("Bold", 700),
            new KeyValuePair<string, int>("Book", 400)
        };

        // 驼峰拆分无法正确处理的族名。
        private static readonly Dictionary<string, string> KnownFamilies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "DMSans", "DM Sans" },
                { "DMMono", "DM Mono" },
                { "DMSerifDisplay", "DM Serif Display" },
                { "IBMPlexMono", "IBM Plex Mono" },
                { "IBMPlexSans", "IBM Plex Sans" },
                { "LXGWWenKai", "LXGW WenKai" },
                { "ZCOOLXiaoWei", "ZCOOL XiaoWei" },
                { "ZCOOLKuaiLe", "ZCOOL KuaiLe" },
                { "NotoSansSC", "Noto Sans SC" },
                { "NotoSerifSC", "Noto Serif SC" },
                { "NotoSansJP", "Noto Sans JP" },
                { "SourceSerif4", "Source Serif 4" },
                { "VT323", "VT323" },
                { "PressStart2P", "Press Start 2P" }
            };

        /// <summary>目录名 → CSS 族名。</summary>
        public static string FolderToFamily(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return string.Empty;
            }

            string known;
            if (KnownFamilies.TryGetValue(folder, out known))
            {
                return known;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < folder.Length; i++)
            {
                var c = folder[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(folder[i - 1]))
                {
                    sb.Append(' ');
                }
                else if (i > 0 && char.IsDigit(c) && !char.IsDigit(folder[i - 1]))
                {
                    sb.Append(' ');
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>CSS 族名 → 目录名（去掉空格）。</summary>
        public static string FamilyToFolder(string family)
        {
            return (family ?? string.Empty).Replace(" ", string.Empty).Trim();
        }

        /// <summary>比较用键：小写、去空格与连字符。</summary>
        public static string NormalizeKey(string family)
        {
            return (family ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty)
                .Replace("_", string.Empty).Trim().ToLowerInvariant();
        }

        /// <summary>从文件名解析字重与样式。无法识别时按 400 / normal。</summary>
        public static void ParseFace(string fileName, out int weight, out string style)
        {
            var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            var dash = name.LastIndexOf('-');
            var suffix = dash >= 0 ? name.Substring(dash + 1) : name;

            style = "normal";
            if (suffix.EndsWith("Italic", StringComparison.OrdinalIgnoreCase)
                || suffix.EndsWith("Oblique", StringComparison.OrdinalIgnoreCase))
            {
                style = "italic";
                suffix = suffix.EndsWith("Italic", StringComparison.OrdinalIgnoreCase)
                    ? suffix.Substring(0, suffix.Length - "Italic".Length)
                    : suffix.Substring(0, suffix.Length - "Oblique".Length);
            }

            weight = 400;
            foreach (var pair in Weights)
            {
                if (suffix.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    weight = pair.Value;
                    return;
                }
            }
        }
    }
}
