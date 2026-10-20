using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Slides.Html
{
    /// <summary>
    /// 模型写入的 CSS（style 属性、&lt;style&gt;、主题 CSS）清洗：去掉 @import、外链 / 越界 url()、
    /// 脚本型表达式；<c>position:fixed</c> 改为 absolute（创作契约禁止）。
    /// </summary>
    public static class CssSanitizer
    {
        private static readonly Regex ImportRule = new Regex(@"@import\b[^;]*;?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UrlFunction = new Regex(@"url\(\s*(?<q>['""]?)(?<u>.*?)\k<q>\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex Dangerous = new Regex(@"expression\s*\(|javascript\s*:|vbscript\s*:|-moz-binding|behavior\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PositionFixed = new Regex(@"position\s*:\s*fixed", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RuleBlock = new Regex(@"(?<selector>[^{}]+)\{(?<declarations>[^{}]*)\}", RegexOptions.Compiled);
        private static readonly Regex CssComment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
        // 选择器最后一个复合选择器命中 .nx-chrome（.nx-chrome .pg 这类后代规则不算）。
        private static readonly Regex ChromeSelector = new Regex(@"\.nx-chrome(?![\w-])[^\s>+~]*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Sanitize(string css, IList<string> removed, FontCatalog fonts = null)
        {
            if (string.IsNullOrEmpty(css))
            {
                return css ?? string.Empty;
            }

            var result = ImportRule.Replace(css, m =>
            {
                Note(removed, "@import");
                return string.Empty;
            });

            result = UrlFunction.Replace(result, m =>
            {
                var url = m.Groups["u"].Value.Trim();
                if (SafeUrl.IsWorkspaceRelative(url) || SafeUrl.IsDataImage(url))
                {
                    return m.Value;
                }

                Note(removed, "url(" + SafeUrl.Clip(url) + ")");
                return "none";
            });

            result = Dangerous.Replace(result, m =>
            {
                Note(removed, m.Value.Trim());
                return "/*x*/";
            });

            result = PositionFixed.Replace(result, m =>
            {
                Note(removed, "position:fixed→absolute");
                return "position:absolute";
            });

            result = PinChromeEdges(result, removed);

            if (fonts != null)
            {
                result = RuleBlock.Replace(result, match =>
                {
                    var selector = match.Groups["selector"].Value.Trim();
                    if (selector.StartsWith("@", StringComparison.Ordinal))
                    {
                        return match.Value;
                    }

                    var declarations = match.Groups["declarations"].Value;
                    var family = FindDeclaration(declarations, "font-family");
                    var weight = FindDeclaration(declarations, "font-weight");
                    if (family == null || weight == null)
                    {
                        return match.Value;
                    }

                    var bundledFamily = ParseFamilyStack(family.Value)
                        .FirstOrDefault(name => fonts.Contains(name) && !fonts.IsEmojiOnly(name));
                    int requestedWeight;
                    if (bundledFamily == null || !TryParseWeight(weight.Value, out requestedWeight))
                    {
                        return match.Value;
                    }

                    var style = FindDeclaration(declarations, "font-style");
                    var italic = style != null
                        && (string.Equals(style.Value.Trim(), "italic", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(style.Value.Trim(), "oblique", StringComparison.OrdinalIgnoreCase));
                    var availableWeight = fonts.SelectAvailableWeight(bundledFamily, requestedWeight, italic);
                    if (!availableWeight.HasValue || availableWeight.Value == requestedWeight)
                    {
                        return match.Value;
                    }

                    var rewritten = ReplaceDeclarationValue(
                        declarations,
                        "font-weight",
                        weight,
                        availableWeight.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Note(removed, "font-weight " + requestedWeight + " → " + availableWeight.Value
                        + " (" + bundledFamily + " 无 " + requestedWeight + ")");
                    return match.Groups["selector"].Value + "{" + rewritten + "}";
                });
            }

            return result;
        }

        private static CssDeclaration FindDeclaration(string declarations, string property)
        {
            CssDeclaration found = null;
            var parts = declarations.Split(';');
            var offset = 0;
            foreach (var part in parts)
            {
                var colon = part.IndexOf(':');
                if (colon > 0)
                {
                    var name = CssComment.Replace(part.Substring(0, colon), string.Empty).Trim();
                    if (string.Equals(name, property, StringComparison.OrdinalIgnoreCase))
                    {
                        var value = part.Substring(colon + 1).Trim();
                        var important = Regex.Match(value, @"\s*!important\s*$", RegexOptions.IgnoreCase);
                        if (important.Success)
                        {
                            value = value.Substring(0, important.Index).TrimEnd();
                        }

                        found = new CssDeclaration
                        {
                            Value = value,
                            Start = offset + colon + 1,
                            Length = part.Length - colon - 1
                        };
                    }
                }

                offset += part.Length + 1;
            }

            return found;
        }

        private static string ReplaceDeclarationValue(
            string declarations,
            string property,
            CssDeclaration declaration,
            string value)
        {
            var rawValue = declarations.Substring(declaration.Start, declaration.Length);
            var leadingWhitespace = rawValue.Length - rawValue.TrimStart().Length;
            var trimmed = rawValue.Trim();
            var important = Regex.Match(trimmed, @"\s*!important\s*$", RegexOptions.IgnoreCase);
            var suffix = important.Success ? trimmed.Substring(important.Index) : string.Empty;
            var replacement = rawValue.Substring(0, leadingWhitespace) + value + suffix;
            return declarations.Substring(0, declaration.Start)
                + replacement
                + declarations.Substring(declaration.Start + declaration.Length);
        }

        private static IList<string> ParseFamilyStack(string value)
        {
            var families = new List<string>();
            var current = new StringBuilder();
            char quote = '\0';
            var escaped = false;
            foreach (var ch in value)
            {
                if (escaped)
                {
                    current.Append(ch);
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (quote != '\0')
                {
                    if (ch == quote)
                    {
                        quote = '\0';
                    }
                    else
                    {
                        current.Append(ch);
                    }
                }
                else if (ch == '\'' || ch == '"')
                {
                    quote = ch;
                }
                else if (ch == ',')
                {
                    AddFamily(families, current);
                }
                else
                {
                    current.Append(ch);
                }
            }

            AddFamily(families, current);
            return families;
        }

        private static void AddFamily(ICollection<string> families, StringBuilder value)
        {
            var family = value.ToString().Trim();
            if (family.Length > 0)
            {
                families.Add(family);
            }

            value.Clear();
        }

        private static bool TryParseWeight(string value, out int weight)
        {
            var normalized = value.Trim();
            if (string.Equals(normalized, "normal", StringComparison.OrdinalIgnoreCase))
            {
                weight = 400;
                return true;
            }

            if (string.Equals(normalized, "bold", StringComparison.OrdinalIgnoreCase))
            {
                weight = 700;
                return true;
            }

            return int.TryParse(normalized, out weight) && weight >= 1 && weight <= 1000;
        }

        private sealed class CssDeclaration
        {
            public string Value { get; set; }
            public int Start { get; set; }
            public int Length { get; set; }
        }

        private static void Note(IList<string> removed, string item)
        {
            if (removed != null && removed.Count < 40 && !removed.Contains(item))
            {
                removed.Add(item);
            }
        }

        /// <summary>
        /// 运行时把 <c>.nx-chrome</c> 定义为全页覆盖层（top/bottom 均为 action 边距）。作者只写 bottom 想做页脚条时，
        /// 残留的 top 会把盒子撑满整页，flex 居中后页脚跑到页面中部；只写 top 的页眉同理。这里补齐另一侧为 auto。
        /// </summary>
        public static string PinChromeEdges(string css, IList<string> removed)
        {
            if (string.IsNullOrEmpty(css) || css.IndexOf("nx-chrome", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return css ?? string.Empty;
            }

            return RuleBlock.Replace(css, match =>
            {
                var selector = CssComment.Replace(match.Groups["selector"].Value, string.Empty).Trim();
                if (selector.StartsWith("@", StringComparison.Ordinal)
                    || !selector.Split(',').Any(item => ChromeSelector.IsMatch(item.Trim())))
                {
                    return match.Value;
                }

                var declarations = match.Groups["declarations"].Value;
                var pinned = PinChromeDeclarations(declarations, removed);
                return ReferenceEquals(pinned, declarations)
                    ? match.Value
                    : match.Groups["selector"].Value + "{" + pinned + "}";
            });
        }

        /// <summary>对 .nx-chrome 元素的一组声明（规则体或 style 属性）补齐缺失的 top/bottom。</summary>
        public static string PinChromeDeclarations(string declarations, IList<string> removed)
        {
            if (declarations == null || FindDeclaration(declarations, "inset") != null)
            {
                return declarations;
            }

            var hasTop = FindDeclaration(declarations, "top") != null;
            var hasBottom = FindDeclaration(declarations, "bottom") != null;
            if (hasTop == hasBottom)
            {
                return declarations;
            }

            var addition = hasBottom ? "top:auto" : "bottom:auto";
            Note(removed, ".nx-chrome +" + addition);
            var trimmed = declarations.TrimEnd();
            return trimmed + (trimmed.Length == 0 || trimmed.EndsWith(";", StringComparison.Ordinal) ? string.Empty : ";") + addition;
        }
    }

    /// <summary>工作区内 URL 判定：只允许不带协议、不越级的相对路径，防止外链与路径穿越。</summary>
    public static class SafeUrl
    {
        private static readonly Regex DataImage = new Regex(@"^data:image/(png|jpe?g|gif|webp|svg\+xml)[;,]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsWorkspaceRelative(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var value = url.Trim();
            if (value[0] == '#')
            {
                return true;
            }

            string decoded;
            try
            {
                decoded = Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (decoded.IndexOf('\\') >= 0 || decoded.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            var pathEnd = decoded.IndexOfAny(new[] { '?', '#' });
            var path = pathEnd >= 0 ? decoded.Substring(0, pathEnd) : decoded;
            if (path.IndexOf(':') >= 0)
            {
                return false;
            }

            foreach (var segment in path.Split('/'))
            {
                if (segment == "..")
                {
                    return false;
                }
            }

            foreach (var ch in decoded)
            {
                if (char.IsControl(ch))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsDataImage(string url)
        {
            return !string.IsNullOrEmpty(url) && DataImage.IsMatch(url.Trim());
        }

        public static string Clip(string url)
        {
            if (url == null) return string.Empty;
            return url.Length <= 80 ? url : url.Substring(0, 80) + "…";
        }
    }
}
