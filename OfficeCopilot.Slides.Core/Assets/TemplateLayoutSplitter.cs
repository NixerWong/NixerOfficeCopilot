using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OfficeCopilot.Slides.Catalog;

namespace OfficeCopilot.Slides.Assets
{
    public sealed class TemplateLayout
    {
        public int Index { get; set; }
        public string Label { get; set; }
        public string FileName { get; set; }
        public string SlideHtml { get; set; }
        public string Html { get; set; }
    }

    /// <summary>
    /// 把整套模板 HTML 切成单页版式样例：每个文件只保留一张幻灯片，其余结构（head、样式、脚本）不变。
    /// 版式文件位于 <c>templates/{slug}/layouts/</c>，通过 <c>&lt;base href="../"&gt;</c> 保持相对路径可用。
    /// </summary>
    public static class TemplateLayoutSplitter
    {
        private static readonly string[] IgnoredClasses = { "slide", "active", "current", "visible", "light", "dark", "is-active" };
        private static readonly string[] ClassPrefixes = { "slide--", "slide-", "s-", "layout-", "layout--" };

        public static IList<TemplateLayout> Split(string html)
        {
            var result = new List<TemplateLayout>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return result;
            }

            var parser = new HtmlParser();
            var count = FindSlides(parser.ParseDocument(html)).Count;
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < count; i++)
            {
                var doc = parser.ParseDocument(html);
                var slides = FindSlides(doc);
                if (i >= slides.Count)
                {
                    break;
                }

                var keep = slides[i];
                var label = DeriveLabel(keep, i);
                for (var j = 0; j < slides.Count; j++)
                {
                    if (j != i)
                    {
                        slides[j].Remove();
                    }
                }

                if (!keep.ClassList.Contains("active"))
                {
                    keep.ClassList.Add("active");
                }

                var head = doc.Head;
                if (head != null)
                {
                    var baseElement = doc.CreateElement("base");
                    baseElement.SetAttribute("href", "../");
                    head.Prepend(baseElement);
                }

                var fileName = (i + 1).ToString("D2", CultureInfo.InvariantCulture) + "-" + label + ".html";
                if (!usedNames.Add(fileName))
                {
                    fileName = (i + 1).ToString("D2", CultureInfo.InvariantCulture) + "-" + label + "-" + (i + 1) + ".html";
                    usedNames.Add(fileName);
                }

                result.Add(new TemplateLayout
                {
                    Index = i + 1,
                    Label = label,
                    FileName = fileName,
                    SlideHtml = keep.OuterHtml,
                    Html = "<!DOCTYPE html>\n" + doc.DocumentElement.OuterHtml + "\n"
                });
            }

            return result;
        }

        /// <summary>幻灯片识别：class 含 slide 的最外层元素 → deck-stage 直接子元素 → 最外层 section。</summary>
        public static IList<IElement> FindSlides(IDocument doc)
        {
            var slides = doc.QuerySelectorAll(".slide")
                .Where(e => !HasAncestorWithClass(e, "slide"))
                .ToList();
            if (slides.Count > 0)
            {
                return slides;
            }

            slides = doc.QuerySelectorAll("deck-stage > section, deck-stage > div").ToList();
            if (slides.Count > 0)
            {
                return slides;
            }

            return doc.Body == null
                ? new List<IElement>()
                : doc.Body.QuerySelectorAll("section").Where(e => !HasAncestorTag(e, "SECTION")).ToList();
        }

        private static string DeriveLabel(IElement slide, int index)
        {
            var dataLabel = slide.GetAttribute("data-label");
            if (!string.IsNullOrWhiteSpace(dataLabel))
            {
                var slug = StyleCatalog.ToSlug(dataLabel);
                if (slug.Length > 0)
                {
                    return Clip(slug);
                }
            }

            foreach (var cls in slide.ClassList)
            {
                if (IgnoredClasses.Contains(cls, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = cls;
                foreach (var prefix in ClassPrefixes)
                {
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(prefix.Length);
                        break;
                    }
                }

                int ignored;
                if (name.Length > 1 && !int.TryParse(name, out ignored))
                {
                    var slug = StyleCatalog.ToSlug(name);
                    if (slug.Length > 0)
                    {
                        return Clip(slug);
                    }
                }
            }

            return "slide";
        }

        private static string Clip(string slug)
        {
            return slug.Length <= 32 ? slug : slug.Substring(0, 32).TrimEnd('-');
        }

        private static bool HasAncestorWithClass(IElement element, string cls)
        {
            for (var p = element.ParentElement; p != null; p = p.ParentElement)
            {
                if (p.ClassList.Contains(cls)) return true;
            }

            return false;
        }

        private static bool HasAncestorTag(IElement element, string tag)
        {
            for (var p = element.ParentElement; p != null; p = p.ParentElement)
            {
                if (string.Equals(p.TagName, tag, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
