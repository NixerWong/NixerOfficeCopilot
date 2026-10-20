using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Catalog;

namespace OfficeCopilot.Slides.Deck
{
    public sealed class DeckAssemblyOptions
    {
        public string DeckId { get; set; }

        /// <summary>风格；为 null 或描述型预设时使用通用舞台外壳。</summary>
        public StyleEntry Style { get; set; }

        /// <summary>按页序排列的已清洗页 HTML。</summary>
        public IList<string> Slides { get; set; } = new List<string>();

        public string ThemeCss { get; set; }
        public string Language { get; set; }
        public string Title { get; set; }
    }

    /// <summary>
    /// 组装 deck.html（蓝图 §5.1）：模板 head（资源地址改为 <see cref="DeckUrls"/> 绝对地址，去掉模板脚本）
    /// + 模板中承载幻灯片的外层结构（去掉导航、计数等外围元素）+ 工作区各页 + 统一运行时 nixer-stage.js。
    /// <c>&lt;base&gt;</c> 指向工作区根，页内相对路径（assets/...）据此解析。
    /// </summary>
    public static class DeckAssembler
    {
        public const int DesignWidth = 1920;
        public const int DesignHeight = 1080;

        private const string Marker = "<nx-slides-marker></nx-slides-marker>";
        private const string RuntimeCss = "aside.notes{display:none!important}"
            + ":where(.slide){--nx-safe-x:128px;--nx-safe-y:96px;--nx-action-x:64px;--nx-action-y:48px}"
            + ":where(.nx-bg){position:absolute;inset:0;z-index:0;pointer-events:none}"
            + ":where(.nx-safe){position:absolute;left:var(--nx-safe-x);right:var(--nx-safe-x);top:var(--nx-safe-y);bottom:var(--nx-safe-y);z-index:1;display:flex;flex-direction:column}"
            + ":where(.nx-chrome){position:absolute;left:var(--nx-action-x);right:var(--nx-action-x);top:var(--nx-action-y);bottom:var(--nx-action-y);z-index:2;pointer-events:none}"
            + ":where(.nx-bg,[data-nx-layer='bg'],[data-nx-layer='background']){--nx-layer:background}"
            + ":where(.nx-safe,.nx-content,[data-nx-layer='content']){--nx-layer:content}"
            + ":where(.nx-chrome,[data-nx-layer='chrome']){--nx-layer:chrome}";
        private const string ValidationPrintCss = "@page{size:1920px 1080px;margin:0}"
            + "html,body{margin:0!important;padding:0!important;width:1920px!important;height:auto!important;overflow:visible!important;background:none!important}"
            + "body{display:block!important}"
            + ".nx-validation-flow{display:block!important;position:static!important;inset:auto!important;width:1920px!important;"
            + "height:auto!important;min-height:0!important;overflow:visible!important;transform:none!important}"
            + "deck-stage,.deck-stage,.deck-viewport{display:block!important;position:static!important;inset:auto!important;"
            + "width:1920px!important;height:auto!important;min-height:0!important;overflow:visible!important;transform:none!important}"
            // flow-root：防止首个子元素的上边距折叠出页外，多打一张空白 PDF 页。
            // 固定高度并裁切：装饰元素出血不得撑出额外 PDF 页；超出舞台的文字由 text.outside 检查。
            + ".slide{display:flow-root!important;position:relative!important;inset:auto!important;width:1920px!important;"
            + "height:1080px!important;min-height:0!important;overflow:hidden!important;transform:none!important;"
            + "opacity:1!important;visibility:visible!important;break-after:page!important;page-break-after:always!important;break-inside:auto!important}"
            + ".slide:last-child{break-after:auto!important;page-break-after:auto!important}"
            + "body[data-only] .slide{display:none!important}body[data-only] .slide.nx-only{display:flow-root!important;break-after:auto!important;page-break-after:auto!important}"
            + "*,*::before,*::after{animation:none!important;transition:none!important;-webkit-print-color-adjust:exact!important;print-color-adjust:exact!important}";

        private static readonly Regex CssUrl = new Regex(@"url\(\s*(?<q>['""]?)(?<u>.*?)\k<q>\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex CssImportString = new Regex(@"@import\s+(?<q>['""])(?<u>.*?)\k<q>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PseudoElement = new Regex(@"::[\w-]+(\([^)]*\))?|:(before|after|first-line|first-letter)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DynamicPseudoClass = new Regex(@"(?<!:):(hover|focus-visible|focus-within|focus|active|visited|link|target)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Assemble(DeckAssemblyOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!DeckIds.IsValidDeckId(options.DeckId)) throw new ArgumentException("deckId 无效。", nameof(options));

            var slides = string.Join("\n", (options.Slides ?? new List<string>()).Where(s => !string.IsNullOrEmpty(s)));
            var style = options.Style;
            var templatePath = style != null && style.Kind == StyleKinds.Template && !string.IsNullOrEmpty(style.Directory)
                ? Path.Combine(style.Directory, "template.html")
                : null;

            string html = null;
            if (templatePath != null && File.Exists(templatePath))
            {
                html = AssembleFromTemplate(File.ReadAllText(templatePath, Encoding.UTF8), TemplateBaseUrl(style), options);
            }

            if (html == null)
            {
                html = AssembleShell(options);
            }

            return html.Replace(Marker, slides);
        }

        public static string AssembleForValidation(DeckAssemblyOptions options)
        {
            var parser = new HtmlParser();
            var doc = parser.ParseDocument(Assemble(options));
            foreach (var script in doc.QuerySelectorAll("script[src]").ToList())
            {
                if ((script.GetAttribute("src") ?? string.Empty).IndexOf("nixer-stage.js", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    script.Remove();
                }
            }

            foreach (var slide in doc.QuerySelectorAll(".slide"))
            {
                var ancestor = slide.ParentElement;
                while (ancestor != null && ancestor != doc.Body)
                {
                    ancestor.ClassList.Add("nx-validation-flow");
                    ancestor = ancestor.ParentElement;
                }
            }

            var style = doc.CreateElement("style");
            style.SetAttribute("id", "nx-validation-print");
            style.TextContent = ValidationPrintCss;
            doc.Head.AppendChild(style);
            return "<!DOCTYPE html>\n" + doc.DocumentElement.OuterHtml + "\n";
        }

        public static string TemplateBaseUrl(StyleEntry style)
        {
            var relative = "templates/" + style.Slug + "/";
            return string.Equals(style.Origin, "user", StringComparison.OrdinalIgnoreCase)
                ? DeckUrls.User(relative)
                : DeckUrls.Lib(relative);
        }

        private static string AssembleFromTemplate(string templateHtml, string assetBase, DeckAssemblyOptions options)
        {
            var doc = new HtmlParser().ParseDocument(templateHtml);
            var slides = TemplateLayoutSplitter.FindSlides(doc);
            if (slides.Count == 0 || doc.Head == null || doc.Body == null)
            {
                return null;
            }

            var baseUri = new Uri(assetBase);
            var head = doc.Head;

            foreach (var element in head.QuerySelectorAll("base, title").ToList())
            {
                element.Remove();
            }

            foreach (var script in doc.QuerySelectorAll("script").ToList())
            {
                var src = script.GetAttribute("src");
                var keep = script.ParentElement == head
                    && !string.IsNullOrWhiteSpace(src)
                    && src.IndexOf("deck-stage.js", StringComparison.OrdinalIgnoreCase) < 0;
                if (keep)
                {
                    script.SetAttribute("src", Absolutize(baseUri, src));
                }
                else
                {
                    script.Remove();
                }
            }

            PruneTemplate(doc, slides, null);

            RewriteUrls(head, baseUri);
            RewriteUrls(doc.Body, baseUri);

            var baseElement = doc.CreateElement("base");
            baseElement.SetAttribute("href", DeckUrls.DeckBase(options.DeckId));
            head.Prepend(baseElement);
            var title = doc.CreateElement("title");
            title.TextContent = string.IsNullOrWhiteSpace(options.Title) ? "Nixer Deck" : options.Title;
            baseElement.After(title);

            var fontsHref = DeckUrls.Lib("fonts/fonts.css");
            if (!head.QuerySelectorAll("link[href]").Any(l => string.Equals(l.GetAttribute("href"), fontsHref, StringComparison.OrdinalIgnoreCase)))
            {
                var link = doc.CreateElement("link");
                link.SetAttribute("rel", "stylesheet");
                link.SetAttribute("href", fontsHref);
                title.After(link);
            }

            AppendStyle(doc, head, "nx-runtime", RuntimeCss);
            AppendStyle(doc, head, "nx-theme", options.ThemeCss);

            if (doc.QuerySelector("[data-nx-chart]") != null
                && !doc.QuerySelectorAll("script[src]").Any(script => (script.GetAttribute("src") ?? string.Empty).IndexOf("chart.umd.min.js", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var chartRuntime = doc.CreateElement("script");
                chartRuntime.SetAttribute("src", DeckUrls.Lib("runtime/vendor/chart.umd.min.js"));
                doc.Body.AppendChild(chartRuntime);
            }

            var runtime = doc.CreateElement("script");
            runtime.SetAttribute("src", DeckUrls.Lib("runtime/nixer-stage.js"));
            doc.Body.AppendChild(runtime);

            if (!string.IsNullOrWhiteSpace(options.Language))
            {
                doc.DocumentElement.SetAttribute("lang", options.Language);
            }

            return "<!DOCTYPE html>\n" + doc.DocumentElement.OuterHtml + "\n";
        }

        /// <summary>
        /// 模板隔离后仍生效的类名（作用于外层容器或 .slide 本身）。模型自己的类不应与之重名。
        /// 非模板风格返回空列表。
        /// </summary>
        public static IList<string> GetTemplateReservedClasses(StyleEntry style)
        {
            var templatePath = style != null && style.Kind == StyleKinds.Template && !string.IsNullOrEmpty(style.Directory)
                ? Path.Combine(style.Directory, "template.html")
                : null;
            if (templatePath == null || !File.Exists(templatePath)) return new List<string>();

            var doc = new HtmlParser().ParseDocument(File.ReadAllText(templatePath, Encoding.UTF8));
            var slides = TemplateLayoutSplitter.FindSlides(doc);
            if (slides.Count == 0 || doc.Head == null || doc.Body == null) return new List<string>();

            var reserved = new SortedSet<string>(StringComparer.Ordinal);
            PruneTemplate(doc, slides, reserved);
            return reserved.ToList();
        }

        /// <summary>
        /// 用标记替换全部模板页，只保留 body 到幻灯片容器这一条路径，并隔离模板 CSS。
        /// </summary>
        private static void PruneTemplate(IDocument doc, IList<IElement> slides, ISet<string> reservedClasses)
        {
            var slideTag = slides[0].LocalName;
            var marker = doc.CreateElement("nx-slides-marker");
            slides[0].Before(marker);
            foreach (var slide in slides)
            {
                slide.Remove();
            }

            // 导航点、页码、提示等外围元素会出现在截图里。
            INode node = marker;
            while (node.Parent != null && node != doc.Body)
            {
                var parent = node.Parent as IElement;
                if (parent == null) break;
                foreach (var sibling in parent.Children.ToList())
                {
                    if (sibling != node && !IsStructural(sibling))
                    {
                        sibling.Remove();
                    }
                }

                node = parent;
            }

            IsolateTemplateStyles(doc, marker, slideTag, reservedClasses);
        }

        /// <summary>
        /// 在标记处放两个不带模板专属类的探针页（当前页 / 非当前页），模板 CSS 只保留能命中
        /// 外层结构或探针页本身的选择器；探针页没有子元素，所以 .pill、.slide h2 之类的内容规则都会被去掉。
        /// </summary>
        private static void IsolateTemplateStyles(IDocument doc, IElement marker, string slideTag, ISet<string> reservedClasses)
        {
            var activeProbe = doc.CreateElement(slideTag);
            activeProbe.ClassName = "slide active visible is-active current";
            activeProbe.SetAttribute("data-deck-active", string.Empty);
            var idleProbe = doc.CreateElement(slideTag);
            idleProbe.ClassName = "slide";
            marker.Before(activeProbe);
            marker.Before(idleProbe);
            try
            {
                Func<string, bool> keep = selector =>
                {
                    var probe = ProbeSelector(selector);
                    try
                    {
                        return doc.QuerySelector(probe) != null;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                };

                foreach (var style in doc.QuerySelectorAll("style").ToList())
                {
                    style.TextContent = TemplateCssIsolator.Filter(style.TextContent, keep, reservedClasses);
                }
            }
            finally
            {
                activeProbe.Remove();
                idleProbe.Remove();
            }
        }

        private static string ProbeSelector(string selector)
        {
            var probe = PseudoElement.Replace(selector, string.Empty);
            probe = DynamicPseudoClass.Replace(probe, string.Empty).Trim();
            if (probe.Length == 0) return "*";
            var last = probe[probe.Length - 1];
            return last == '>' || last == '+' || last == '~' ? probe + " *" : probe;
        }

        private static string AssembleShell(DeckAssemblyOptions options)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\n<html lang=\"").Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(options.Language) ? "zh-CN" : options.Language)).Append("\">\n<head>\n")
                .Append("<meta charset=\"utf-8\">\n")
                .Append("<base href=\"").Append(DeckUrls.DeckBase(options.DeckId)).Append("\">\n")
                .Append("<title>").Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(options.Title) ? "Nixer Deck" : options.Title)).Append("</title>\n")
                .Append("<link rel=\"stylesheet\" href=\"").Append(DeckUrls.Lib("fonts/fonts.css")).Append("\">\n")
                .Append("<link rel=\"stylesheet\" href=\"").Append(DeckUrls.Lib("runtime/viewport-base.css")).Append("\">\n")
                .Append("<style id=\"nx-runtime\">").Append(RuntimeCss).Append("</style>\n");
            if (!string.IsNullOrWhiteSpace(options.ThemeCss))
            {
                sb.Append("<style id=\"nx-theme\">\n").Append(EscapeStyleText(options.ThemeCss)).Append("\n</style>\n");
            }

            sb.Append("</head>\n<body>\n<div class=\"deck-viewport\"><div class=\"deck-stage\">\n")
                .Append(Marker)
                .Append("\n</div></div>\n");
            if (options.Slides != null && options.Slides.Any(html => html != null && html.IndexOf("data-nx-chart", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                sb.Append("<script src=\"").Append(DeckUrls.Lib("runtime/vendor/chart.umd.min.js")).Append("\"></script>\n");
            }
            sb.Append("<script src=\"").Append(DeckUrls.Lib("runtime/nixer-stage.js")).Append("\"></script>\n</body>\n</html>\n");
            return sb.ToString();
        }

        private static bool IsStructural(IElement element)
        {
            switch (element.LocalName.ToLowerInvariant())
            {
                case "style":
                case "template":
                    return true;
                case "svg":
                    // 共享的 <defs>/<symbol> 被页内 <use> 引用。
                    return element.QuerySelector("defs, symbol") != null;
                default:
                    return false;
            }
        }

        private static void RewriteUrls(IElement root, Uri baseUri)
        {
            var elements = new List<IElement> { root };
            elements.AddRange(root.QuerySelectorAll("*"));
            foreach (var element in elements)
            {
                foreach (var name in new[] { "href", "src", "poster" })
                {
                    var value = element.GetAttribute(name);
                    if (value != null && !(string.Equals(name, "href", StringComparison.Ordinal) && element.LocalName == "base"))
                    {
                        element.SetAttribute(name, Absolutize(baseUri, value));
                    }
                }

                var style = element.GetAttribute("style");
                if (!string.IsNullOrEmpty(style))
                {
                    element.SetAttribute("style", RewriteCss(style, baseUri));
                }

                if (element.LocalName == "style")
                {
                    element.TextContent = RewriteCss(element.TextContent, baseUri);
                }
            }
        }

        private static string RewriteCss(string css, Uri baseUri)
        {
            if (string.IsNullOrEmpty(css)) return css;
            var result = CssUrl.Replace(css, m => "url(" + m.Groups["q"].Value + Absolutize(baseUri, m.Groups["u"].Value.Trim()) + m.Groups["q"].Value + ")");
            return CssImportString.Replace(result, m => "@import " + m.Groups["q"].Value + Absolutize(baseUri, m.Groups["u"].Value.Trim()) + m.Groups["q"].Value);
        }

        private static string Absolutize(Uri baseUri, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var trimmed = value.Trim();
            if (trimmed[0] == '#' || trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            Uri absolute;
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out absolute) && !absolute.IsFile)
            {
                return value;
            }

            return Uri.TryCreate(baseUri, trimmed, out absolute) ? absolute.AbsoluteUri : value;
        }

        private static void AppendStyle(IDocument doc, IElement head, string id, string css)
        {
            if (string.IsNullOrWhiteSpace(css)) return;
            var style = doc.CreateElement("style");
            style.Id = id;
            style.TextContent = EscapeStyleText(css);
            head.AppendChild(style);
        }

        private static string EscapeStyleText(string css)
        {
            return (css ?? string.Empty).Replace("</", "<\\/");
        }
    }
}
