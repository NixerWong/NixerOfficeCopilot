using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Slides.Deck
{
    /// <summary>
    /// 模板 CSS 隔离：模板示例页被替换后，只保留仍作用于外层结构（html/body/幻灯片容器/.slide 本身）的规则、
    /// 变量与字体/动画等 at-rule；作用于模板示例内容的规则（如 .pill、.kicker、.slide-1 h2）一律去掉，
    /// 避免它们命中模型自己写的同名类。
    /// </summary>
    public static class TemplateCssIsolator
    {
        private static readonly HashSet<string> KeptAtRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "font-face", "keyframes", "-webkit-keyframes", "page", "property", "counter-style",
            "font-feature-values", "font-palette-values"
        };

        private static readonly HashSet<string> NestedAtRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "media", "supports", "layer", "container", "document", "-moz-document"
        };

        private static readonly Regex ClassName = new Regex(@"\.(-?[_a-zA-Z][\w-]*)", RegexOptions.Compiled);

        /// <summary>
        /// 过滤 CSS：<paramref name="keepSelector"/> 判定单个选择器是否保留；保留选择器中的类名写入 <paramref name="keptClasses"/>。
        /// </summary>
        public static string Filter(string css, Func<string, bool> keepSelector, ISet<string> keptClasses)
        {
            if (string.IsNullOrWhiteSpace(css)) return css ?? string.Empty;
            if (keepSelector == null) throw new ArgumentNullException(nameof(keepSelector));
            var text = StripComments(css);
            var sb = new StringBuilder();
            FilterBlock(text, 0, text.Length, keepSelector, keptClasses, sb);
            return sb.ToString();
        }

        private static void FilterBlock(string css, int start, int end, Func<string, bool> keepSelector, ISet<string> keptClasses, StringBuilder sb)
        {
            var i = start;
            while (i < end)
            {
                var j = i;
                while (j < end && css[j] != '{' && css[j] != ';' && css[j] != '}')
                {
                    j = css[j] == '"' || css[j] == '\'' ? SkipString(css, j, end) : j + 1;
                }

                if (j >= end) break;
                var prelude = css.Substring(i, j - i).Trim();
                if (css[j] == ';')
                {
                    if (prelude.StartsWith("@", StringComparison.Ordinal)) sb.Append(prelude).Append(";\n");
                    i = j + 1;
                    continue;
                }

                if (css[j] == '}')
                {
                    i = j + 1;
                    continue;
                }

                var close = FindClose(css, j, end);
                if (prelude.StartsWith("@", StringComparison.Ordinal))
                {
                    var name = AtRuleName(prelude);
                    if (NestedAtRules.Contains(name))
                    {
                        var inner = new StringBuilder();
                        FilterBlock(css, j + 1, close, keepSelector, keptClasses, inner);
                        if (inner.Length > 0) sb.Append(prelude).Append("{\n").Append(inner).Append("}\n");
                    }
                    else if (KeptAtRules.Contains(name))
                    {
                        sb.Append(prelude).Append('{').Append(css, j + 1, close - j - 1).Append("}\n");
                    }
                }
                else
                {
                    var kept = SplitSelectors(prelude).Where(selector => selector.Length > 0 && keepSelector(selector)).ToList();
                    if (kept.Count > 0)
                    {
                        if (keptClasses != null)
                        {
                            foreach (var selector in kept)
                            {
                                foreach (Match match in ClassName.Matches(selector)) keptClasses.Add(match.Groups[1].Value);
                            }
                        }

                        sb.Append(string.Join(", ", kept)).Append('{').Append(css, j + 1, close - j - 1).Append("}\n");
                    }
                }

                i = Math.Min(end, close + 1);
            }
        }

        private static string AtRuleName(string prelude)
        {
            var length = 1;
            while (length < prelude.Length && (char.IsLetterOrDigit(prelude[length]) || prelude[length] == '-')) length++;
            return prelude.Substring(1, length - 1);
        }

        private static int FindClose(string css, int open, int end)
        {
            var depth = 0;
            var i = open;
            while (i < end)
            {
                var ch = css[i];
                if (ch == '"' || ch == '\'')
                {
                    i = SkipString(css, i, end);
                    continue;
                }

                if (ch == '{') depth++;
                else if (ch == '}' && --depth == 0) return i;
                i++;
            }

            return end;
        }

        private static int SkipString(string css, int start, int end)
        {
            var quote = css[start];
            var i = start + 1;
            while (i < end)
            {
                if (css[i] == '\\') i += 2;
                else if (css[i] == quote) return i + 1;
                else i++;
            }

            return end;
        }

        private static IList<string> SplitSelectors(string prelude)
        {
            var result = new List<string>();
            var depth = 0;
            var last = 0;
            for (var i = 0; i < prelude.Length; i++)
            {
                var ch = prelude[i];
                if (ch == '"' || ch == '\'')
                {
                    i = SkipString(prelude, i, prelude.Length) - 1;
                }
                else if (ch == '(' || ch == '[') depth++;
                else if (ch == ')' || ch == ']') depth--;
                else if (ch == ',' && depth == 0)
                {
                    result.Add(prelude.Substring(last, i - last).Trim());
                    last = i + 1;
                }
            }

            result.Add(prelude.Substring(last).Trim());
            return result;
        }

        private static string StripComments(string css)
        {
            var sb = new StringBuilder(css.Length);
            var i = 0;
            while (i < css.Length)
            {
                if (css[i] == '"' || css[i] == '\'')
                {
                    var next = SkipString(css, i, css.Length);
                    sb.Append(css, i, next - i);
                    i = next;
                }
                else if (css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*')
                {
                    var close = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? css.Length : close + 2;
                }
                else
                {
                    sb.Append(css[i]);
                    i++;
                }
            }

            return sb.ToString();
        }
    }
}
