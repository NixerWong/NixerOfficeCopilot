using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Slides.Assets
{
    public sealed class TemplateRewriteResult
    {
        public string Html { get; set; }
        public List<string> Families { get; } = new List<string>();
        public List<string> Rewrites { get; } = new List<string>();
        public List<string> Unresolved { get; } = new List<string>();
    }

    /// <summary>
    /// 模板私有化改写（蓝图 §3.3）：Google Fonts / Fontshare → 本地 fonts.css，Chart.js CDN → 本地 vendor。
    /// 路径相对模板目录（<c>templates/{slug}/</c>）。
    /// </summary>
    public static class TemplateHtmlRewriter
    {
        public const string FontsCssRelative = "../../fonts/fonts.css";
        public const string ChartJsRelative = "../../runtime/vendor/chart.umd.min.js";

        private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled;

        private static readonly Regex PreconnectLink = new Regex(
            @"[ \t]*<link\b[^>]*\brel\s*=\s*[""']?(?:preconnect|dns-prefetch)[""']?[^>]*>[ \t]*\r?\n?", Opts);

        private static readonly Regex FontStylesheetLink = new Regex(
            @"<link\b[^>]*\bhref\s*=\s*[""'](?<url>https?://(?:fonts\.googleapis\.com|api\.fontshare\.com|cdn\.fontshare\.com)/[^""']*)[""'][^>]*>", Opts);

        private static readonly Regex FontImport = new Regex(
            @"@import\s+(?:url\(\s*)?[""']?(?<url>https?://(?:fonts\.googleapis\.com|api\.fontshare\.com)/[^""')]*)[""']?\s*\)?\s*;?", Opts);

        private static readonly Regex ChartScript = new Regex(
            @"(<script\b[^>]*\bsrc\s*=\s*[""'])https?://cdn\.jsdelivr\.net/npm/chart\.js[^""']*([""'])", Opts);

        private static readonly Regex GoogleFamily = new Regex(@"[?&]family=(?<f>[^&:]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FontshareFamily = new Regex(@"[?&]f\[\]=(?<f>[^&@]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static TemplateRewriteResult Rewrite(string html)
        {
            var result = new TemplateRewriteResult();
            if (string.IsNullOrEmpty(html))
            {
                result.Html = html ?? string.Empty;
                return result;
            }

            var linked = false;
            var text = PreconnectLink.Replace(html, m =>
            {
                var tag = m.Value;
                if (tag.IndexOf("fonts.g", StringComparison.OrdinalIgnoreCase) >= 0
                    || tag.IndexOf("fontshare", StringComparison.OrdinalIgnoreCase) >= 0
                    || tag.IndexOf("jsdelivr", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Rewrites.Add("移除 preconnect");
                    return string.Empty;
                }

                return tag;
            });

            text = FontStylesheetLink.Replace(text, m =>
            {
                CollectFamilies(m.Groups["url"].Value, result.Families);
                result.Rewrites.Add("字体链接 → fonts.css：" + m.Groups["url"].Value);
                if (linked)
                {
                    return string.Empty;
                }

                linked = true;
                return "<link rel=\"stylesheet\" href=\"" + FontsCssRelative + "\">";
            });

            text = FontImport.Replace(text, m =>
            {
                CollectFamilies(m.Groups["url"].Value, result.Families);
                result.Rewrites.Add("@import → fonts.css：" + m.Groups["url"].Value);
                return "@import url('" + FontsCssRelative + "');";
            });

            text = ChartScript.Replace(text, m =>
            {
                result.Rewrites.Add("Chart.js CDN → vendor");
                return m.Groups[1].Value + ChartJsRelative + m.Groups[2].Value;
            });

            result.Html = text;
            result.Unresolved.AddRange(HtmlContractScanner.FindExternalReferences(text));
            return result;
        }

        public static void CollectFamilies(string url, List<string> target)
        {
            foreach (Match m in GoogleFamily.Matches(url ?? string.Empty))
            {
                AddFamily(target, m.Groups["f"].Value);
            }

            foreach (Match m in FontshareFamily.Matches(url ?? string.Empty))
            {
                AddFamily(target, m.Groups["f"].Value);
            }
        }

        private static void AddFamily(List<string> target, string raw)
        {
            var name = Uri.UnescapeDataString(raw.Replace('+', ' ')).Trim();
            if (name.Length > 0 && !target.Exists(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(name);
            }
        }
    }
}
