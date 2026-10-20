using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Slides.Html
{
    public sealed class SanitizedSlide
    {
        /// <summary>清洗后的单页 HTML：一个 &lt;section class="slide"&gt;。</summary>
        public string Html { get; set; }

        public string Title { get; set; }

        public string Layout { get; set; }

        /// <summary>被删除 / 改写的内容摘要（面向模型）。</summary>
        public IList<string> Removed { get; } = new List<string>();
    }

    /// <summary>
    /// 幻灯片片段清洗（蓝图 §5.9）：删除脚本、事件属性、内嵌框架与外链 / 越界路径，
    /// 并把片段规整为恰好一个 <c>section.slide</c>。
    /// </summary>
    public static class SlideHtmlSanitizer
    {
        private static readonly HashSet<string> ForbiddenElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "script", "noscript", "iframe", "frame", "frameset", "object", "embed", "applet", "base", "meta",
            "link", "form", "input", "select", "textarea", "portal", "fencedframe", "title", "set"
        };

        private static readonly HashSet<string> ForbiddenAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "srcdoc", "formaction", "action", "srcset", "ping", "http-equiv", "target", "contenteditable", "autofocus"
        };

        private static readonly HashSet<string> UrlAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src", "href", "xlink:href", "poster", "background", "data", "cite", "longdesc", "lowsrc", "dynsrc"
        };

        /// <summary>清洗一页。片段里出现多于一个顶层 <c>section.slide</c> 时抛出 <see cref="ArgumentException"/>。</summary>
        public static SanitizedSlide SanitizeSlide(string html, FontCatalog fonts = null)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                throw new ArgumentException("幻灯片 HTML 为空。");
            }

            var result = new SanitizedSlide();
            var doc = new HtmlParser().ParseDocument("<!DOCTYPE html><html><head></head><body>" + html + "</body></html>");
            var body = doc.Body;

            // head 中被解析器提升的样式放回 body，其余丢弃。
            foreach (var style in doc.Head.QuerySelectorAll("style").ToList())
            {
                body.Prepend(style);
            }

            foreach (var comment in body.Descendants<IComment>().ToList())
            {
                comment.Remove();
            }

            CleanTree(body, result.Removed, fonts);

            var slide = NormalizeSlide(doc, body, result.Removed);
            result.Title = ExtractTitle(slide);
            result.Layout = slide.GetAttribute("data-layout");
            result.Html = slide.OuterHtml;
            return result;
        }

        /// <summary>对任意 HTML 子树执行删除规则（不做单页规整）。</summary>
        public static void CleanTree(IElement root, IList<string> removed, FontCatalog fonts = null)
        {
            foreach (var element in root.QuerySelectorAll("*").ToList())
            {
                if (element.Parent == null)
                {
                    continue;
                }

                var name = element.LocalName;
                if (ForbiddenElements.Contains(name) || IsHrefAnimation(element))
                {
                    Note(removed, "<" + name + ">");
                    element.Remove();
                    continue;
                }

                if (HasExternalIconClass(element))
                {
                    throw new ArgumentException("不支持外部图标字体或库类名；请改用 <i class=\"nx-icon\" data-icon=\"leaf\"></i>。");
                }

                if (element.ClassList.Contains("nx-icon"))
                {
                    ExpandNxIcon(element, removed);
                }

                CleanAttributes(element, removed, fonts);

                if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
                {
                    element.TextContent = CssSanitizer.Sanitize(element.TextContent, removed, fonts);
                }
            }

            CleanAttributes(root, removed, fonts);
        }

        private static void ExpandNxIcon(IElement element, IList<string> removed)
        {
            if (!string.Equals(element.LocalName, "i", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("nx-icon 必须使用 <i class=\"nx-icon\" data-icon=\"...\">。");
            }

            var name = element.GetAttribute("data-icon");
            if (string.IsNullOrWhiteSpace(name))
            {
                name = element.GetAttribute("data-nx-icon");
            }

            string substitutedFrom;
            var iconName = LucideIconCatalog.Resolve(name, out substitutedFrom);
            var body = LucideIconCatalog.GetSvgBody(iconName);
            if (substitutedFrom != null)
            {
                Note(removed, "nx-icon 随包无“" + substitutedFrom + "”，已替换为 " + iconName + "（如不合适请换成随包图标名）");
            }
            var style = element.GetAttribute("style");
            var suffix = "display:inline-block;position:relative;width:1em;height:1em;line-height:1em;vertical-align:-0.125em;font-style:normal";
            element.SetAttribute("style", string.IsNullOrWhiteSpace(style)
                ? suffix
                : style.Trim().TrimEnd(';') + ";" + suffix);
            element.SetAttribute("data-nx-icon", iconName);
            element.RemoveAttribute("data-icon");
            element.InnerHtml = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" width=\"100%\" height=\"100%\" style=\"position:absolute;inset:0\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">"
                + body
                + "</svg>&#x2003;";
            Note(removed, "nx-icon → Lucide:" + iconName);
        }

        private static bool HasExternalIconClass(IElement element)
        {
            foreach (var className in element.ClassList)
            {
                if (className.StartsWith("fa-", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(className, "fas", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(className, "far", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(className, "fab", StringComparison.OrdinalIgnoreCase)
                    || className.StartsWith("lucide-", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CleanAttributes(IElement element, IList<string> removed, FontCatalog fonts)
        {
            foreach (var attr in element.Attributes.ToList())
            {
                var name = attr.Name;
                var drop = false;
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || ForbiddenAttributes.Contains(name))
                {
                    drop = true;
                }
                else if (UrlAttributes.Contains(name))
                {
                    var value = attr.Value ?? string.Empty;
                    var allowData = string.Equals(name, "src", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(element.LocalName, "image", StringComparison.OrdinalIgnoreCase);
                    drop = !(SafeUrl.IsWorkspaceRelative(value) || (allowData && SafeUrl.IsDataImage(value)));
                    if (drop)
                    {
                        Note(removed, name + "=" + SafeUrl.Clip(value));
                        RemoveAttribute(element, attr);
                        continue;
                    }
                }
                else if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
                {
                    var cleaned = CssSanitizer.Sanitize(attr.Value, removed, fonts);
                    if (element.ClassList.Contains("nx-chrome"))
                    {
                        cleaned = CssSanitizer.PinChromeDeclarations(cleaned, removed);
                    }

                    if (!string.Equals(cleaned, attr.Value, StringComparison.Ordinal))
                    {
                        element.SetAttribute("style", cleaned);
                    }
                }

                if (drop)
                {
                    Note(removed, name + "@" + element.LocalName);
                    RemoveAttribute(element, attr);
                }
            }
        }

        private static IElement NormalizeSlide(IDocument doc, IElement body, IList<string> removed)
        {
            var slides = body.Children
                .Where(e => IsSlideSection(e))
                .ToList();

            if (slides.Count > 1)
            {
                throw new ArgumentException("每个 html 只能包含一页（一个 <section class=\"slide\">），当前有 " + slides.Count + " 页；请拆成多个条目。");
            }

            IElement slide;
            if (slides.Count == 0)
            {
                var nested = body.QuerySelectorAll("section.slide").FirstOrDefault();
                if (nested != null && body.QuerySelectorAll("section.slide").Length == 1 && body.Children.Length == 1)
                {
                    // 模型整份复制了模板外壳：只取其中那一页。
                    slide = nested;
                    Note(removed, "外层包装");
                }
                else
                {
                    slide = doc.CreateElement("section");
                    slide.ClassList.Add("slide");
                    foreach (var child in body.ChildNodes.ToList())
                    {
                        slide.AppendChild(child);
                    }

                    body.AppendChild(slide);
                }
            }
            else
            {
                slide = slides[0];
                IElement anchor = null;
                foreach (var child in body.ChildNodes.ToList())
                {
                    if (child == slide)
                    {
                        continue;
                    }

                    var element = child as IElement;
                    if (element != null && string.Equals(element.LocalName, "style", StringComparison.OrdinalIgnoreCase))
                    {
                        if (anchor == null) slide.Prepend(element);
                        else anchor.After(element);
                        anchor = element;
                    }
                    else if (element != null || !string.IsNullOrWhiteSpace(child.TextContent))
                    {
                        slide.AppendChild(child);
                        Note(removed, "页外内容已移入页内");
                    }
                }
            }

            foreach (var cls in new[] { "active", "is-active", "visible", "current" })
            {
                slide.ClassList.Remove(cls);
            }

            slide.RemoveAttribute("data-deck-active");
            slide.RemoveAttribute("data-nx-hidden");
            return slide;
        }

        private static bool IsSlideSection(IElement element)
        {
            return string.Equals(element.LocalName, "section", StringComparison.OrdinalIgnoreCase)
                && element.ClassList.Contains("slide");
        }

        private static string ExtractTitle(IElement slide)
        {
            var heading = slide.QuerySelector("h1, h2, h3");
            var text = heading == null ? null : heading.TextContent;
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            text = string.Join(" ", text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= 60 ? text : text.Substring(0, 60);
        }

        private static bool IsHrefAnimation(IElement element)
        {
            if (!element.LocalName.StartsWith("animate", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var target = element.GetAttribute("attributeName") ?? string.Empty;
            return target.IndexOf("href", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void RemoveAttribute(IElement element, IAttr attr)
        {
            if (!string.IsNullOrEmpty(attr.NamespaceUri))
            {
                element.RemoveAttribute(attr.NamespaceUri, attr.LocalName);
            }
            else
            {
                element.RemoveAttribute(attr.Name);
            }
        }

        private static void Note(IList<string> removed, string item)
        {
            if (removed != null && removed.Count < 40 && !removed.Contains(item))
            {
                removed.Add(item);
            }
        }
    }
}
