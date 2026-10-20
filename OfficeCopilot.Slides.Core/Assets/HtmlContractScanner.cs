using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Slides.Assets
{
    /// <summary>
    /// HTML 模板的静态契约检查：外链、幻灯片标记。合并工具与用户模板加载共用。
    /// </summary>
    public static class HtmlContractScanner
    {
        // src / href / url(...) / @import 中的 http(s) 或协议相对地址。
        private static readonly Regex ExternalRef = new Regex(
            @"(?:\b(?:src|href|poster|data)\s*=\s*[""']?\s*|url\(\s*[""']?\s*|@import\s+[""']\s*)((?:https?:)?//[^\s""'()<>]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SlideMarker = new Regex(
            @"<(?:section|div)\b[^>]*\bclass\s*=\s*[""'](?:[^""']*\s)?slide(?:\s[^""']*)?[""']|<deck-stage\b|<section\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>返回全部外部引用（去重，保持出现顺序）。</summary>
        public static IList<string> FindExternalReferences(string html)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(html))
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in ExternalRef.Matches(html))
            {
                var url = m.Groups[1].Value;
                if (IsNamespaceUri(url))
                {
                    continue;
                }

                if (seen.Add(url))
                {
                    result.Add(url);
                }
            }

            return result;
        }

        public static bool HasSlideMarkup(string html)
        {
            return !string.IsNullOrEmpty(html) && SlideMarker.IsMatch(html);
        }

        private static bool IsNamespaceUri(string url)
        {
            // SVG / XLink 命名空间只是标识符，不会发起请求。
            return url.IndexOf("//www.w3.org/", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
