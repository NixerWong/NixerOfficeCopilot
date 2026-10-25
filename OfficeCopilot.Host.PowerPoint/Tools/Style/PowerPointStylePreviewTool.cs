using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Slides.Catalog;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Html;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    /// <summary>
    /// 候选风格封面截图：每个候选一页 HTML → previews/{name}.html → 隐藏 WebView2 截 1280×720 PNG。
    /// 返回 previewImageId，由面板解析为本地候选截图地址，避免模型转抄完整 URL。
    /// </summary>
    public sealed class PowerPointStylePreviewTool : PowerPointDeckToolBase
    {
        private const int MaxCandidates = 4;
        private const double CaptureScale = 2.0 / 3.0;

        private static readonly ToolDefinition _definition = Define(
            PowerPointToolNames.StylePreview,
            "Preview Presentation Style",
            "Renders the cover slide for each of 3–4 distinct style candidates as an image without modifying the current presentation. "
                + "Before calling this tool, call ppt_style_get (part=preview) for each candidate style and follow its preview card and template-structure classes. Content classes from template example slides (such as cards and tags) are not injected; define all cover styles in that candidate's css using the cover-* prefix. "
                + "For each candidate, provide name (a-z0-9- starting with a lowercase letter), style (slug), html (one <section class=\"slide\"> designed at 1920x1080), and optional css. "
                + "Use px for font sizes; do not use vw, vh, or clamp. "
                + "Font rules: if a text block contains any Chinese, use a Chinese font for the entire block. Do not split Chinese and English text, and do not include English fonts in that block's font-family stack. Use the locally installed Chinese fonts returned by ppt_style_search; if the list is empty, use the bundled Chinese font. Noto Color Emoji is only for automatic emoji fallback. "
                + "Set font-weight to a value supported by the selected font family. "
                + "Do not use text symbols such as ✿, ✦, or ★ for decoration. Use <i class=\"nx-icon\" data-icon=\"leaf\"></i> for icons (use only lowercase Lucide icon names without the lucide- prefix). Use elements with backgrounds or borders for color blocks, pills, and lines, and add data-nx-shape=\"rect|rounded-rect|line\" so they become editable shapes in PowerPoint. "
                + "Returns each candidate's previewImageId and font/layout validation issues (issues do not prevent candidates from being shown). Then call runtime.ask_user_question and copy each previewImageId unchanged into the matching option's previewImageId field so the user can see and choose an image; do not copy or rewrite image URLs. "
                + "After the user selects a candidate, pass its returned deckId to ppt_deck_create to reuse the workspace.",
            true,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"deckId\":{\"type\":\"string\",\"description\":\"Reuse an existing workspace; omit to create a new one.\"},"
                + "\"candidates\":{\"type\":\"array\",\"minItems\":3,\"maxItems\":4,\"items\":{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"name\":{\"type\":\"string\"},"
                + "\"style\":{\"type\":\"string\"},"
                + "\"html\":{\"type\":\"string\"},"
                + "\"css\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"name\",\"style\",\"html\"],"
                + "\"additionalProperties\":false"
                + "}}"
                + "},"
                + "\"required\":[\"candidates\"],"
                + "\"additionalProperties\":false"
                + "}",
            180);

        public PowerPointStylePreviewTool() : this(null, null) { }

        public PowerPointStylePreviewTool(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs) : base(store, catalogs) { }

        public override ToolDefinition Definition => _definition;

        protected override string FailureCode => "ppt.style_preview_failed";

        protected override async Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args)
        {
            var items = args["candidates"] as JArray;
            if (items == null || items.Count < 3 || items.Count > MaxCandidates)
            {
                throw new DeckException("ppt.deck_invalid_argument", "candidates 需要 3–" + MaxCandidates + " 个不同风格候选。");
            }

            var renderer = GetRenderer(context);
            if (renderer == null)
            {
                return Fail(request, "当前环境没有渲染器，无法生成预览图。", "ppt.deck_render_unavailable");
            }

            // 先全部校验，再建工作区。
            var prepared = new List<Candidate>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i] as JObject;
                if (item == null) throw new DeckException("ppt.deck_invalid_argument", "第 " + (i + 1) + " 个候选不是对象。");
                var name = ((string)item["name"] ?? string.Empty).Trim().ToLowerInvariant();
                if (!DeckIds.IsValidPreviewName(name))
                {
                    throw new DeckException("ppt.deck_invalid_argument", "第 " + (i + 1) + " 个候选名不合法（小写字母开头，a-z0-9-，≤24）：" + name);
                }

                if (prepared.Any(p => p.Name == name))
                {
                    throw new DeckException("ppt.deck_invalid_argument", "候选名重复：" + name);
                }

                var style = ResolveStyle((string)item["style"], true);
                if (prepared.Any(p => string.Equals(p.Style.Slug, style.Slug, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new DeckException("ppt.deck_invalid_argument", "候选必须使用不同风格，重复风格：" + style.Slug);
                }

                SanitizedSlide slide;
                try
                {
                    slide = SlideHtmlSanitizer.SanitizeSlide((string)item["html"], Catalogs.Fonts);
                }
                catch (ArgumentException ex)
                {
                    throw new DeckException("ppt.deck_invalid_html", "候选 " + name + "：" + ex.Message);
                }

                var removed = new List<string>(slide.Removed);
                var css = CssSanitizer.Sanitize((string)item["css"] ?? string.Empty, removed, Catalogs.Fonts);
                prepared.Add(new Candidate { Name = name, Style = style, Html = slide.Html, Css = css, Removed = removed });
            }

            DeckWorkspace workspace;
            if (!string.IsNullOrWhiteSpace((string)args["deckId"]))
            {
                workspace = OpenWorkspace(args);
            }
            else
            {
                workspace = Store.Create(null, null, null, null);
            }

            var results = new List<object>();
            var images = new List<KeyValuePair<string, byte[]>>();
            var sb = new StringBuilder();
            sb.Append("工作区 ").Append(workspace.Id).Append("，已渲染 ").Append(prepared.Count).AppendLine(" 个候选：");
            var systemFonts = Catalogs.SystemFonts;
            var chineseFonts = systemFonts.ChineseFamilies
                .Concat(Catalogs.Fonts.Families
                    .Where(family => family.Cjk && !family.EmojiOnly)
                    .Select(family => family.Family))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            sb.Append("本机字体分析：").AppendLine(systemFonts.Summary);
            sb.Append("含中文的文本块只能使用中文字体（不拆分中英混排）；当前可用：")
                .AppendLine(chineseFonts.Count == 0
                    ? "随包中文字体 Noto Sans SC、Noto Serif SC、LXGW WenKai、Smiley Sans、ZCOOL XiaoWei"
                    : string.Join(", ", chineseFonts));
            foreach (var warning in systemFonts.Warnings)
            {
                sb.Append("本机字体分析提示：").AppendLine(warning);
            }

            foreach (var c in prepared)
            {
                var source = workspace.WritePreviewSource(c.Name, c.Style.Slug, c.Html, c.Css);
                var assemblyOptions = new DeckAssemblyOptions
                {
                    DeckId = workspace.Id,
                    Style = c.Style,
                    Slides = new List<string> { c.Html },
                    ThemeCss = c.Css,
                    Language = workspace.Manifest.Language,
                    Title = c.Style.Name
                };
                var html = DeckAssembler.Assemble(assemblyOptions);
                File.WriteAllText(workspace.PreviewPath(c.Name, ".html"), html, new UTF8Encoding(false));
                var validationHtml = DeckAssembler.AssembleForValidation(assemblyOptions);
                File.WriteAllText(workspace.PreviewPath(c.Name, ".validate.html"), validationHtml, new UTF8Encoding(false));

                var url = DeckUrls.DeckBase(workspace.Id) + "previews/" + c.Name + ".html?mode=capture#1";
                var capture = await renderer.CaptureAsync(url, CaptureScale, context == null ? default(System.Threading.CancellationToken) : context.CancellationToken).ConfigureAwait(false);
                var pngPath = workspace.PreviewPath(c.Name, ".png");
                File.WriteAllBytes(pngPath, capture.Png);
                images.Add(new KeyValuePair<string, byte[]>("候选 " + c.Name + "（" + c.Style.Slug + "）封面", capture.Png));

                var validationUrl = DeckUrls.DeckBase(workspace.Id) + "previews/" + c.Name + ".validate.html?mode=validation#1";
                var validation = await renderer.ValidateAsync(
                    url,
                    validationUrl,
                    workspace.Manifest.Density,
                    context == null ? default(System.Threading.CancellationToken) : context.CancellationToken).ConfigureAwait(false);
                if (validation == null || validation.Issues == null)
                {
                    throw new InvalidOperationException("候选 " + c.Name + " 的字体/布局校验没有返回有效结果。");
                }

                var validationIssues = validation.Issues
                    .Where(IsPreviewIssue)
                    .Select(issue => new PreviewIssue
                    {
                        Code = issue.Code,
                        Severity = issue.Severity,
                        Slide = issue.Slide,
                        Message = issue.Message,
                        Selector = issue.Selector,
                        Text = issue.Text
                    })
                    .ToList();
                var imageVersion = DeckWorkspace.ComputeHash(Convert.ToBase64String(capture.Png));
                var previewImageId = workspace.Id + "/" + c.Name + "/" + imageVersion;
                sb.Append("- ").Append(c.Name).Append("（").Append(c.Style.Slug).Append("）previewImageId: ").AppendLine(previewImageId);
                if (validationIssues.Count == 0)
                {
                    sb.AppendLine("  字体/布局校验：没有需要展示的问题。");
                }
                else
                {
                    sb.AppendLine("  字体/布局校验问题：");
                    foreach (var issue in validationIssues)
                    {
                        sb.Append("  - `").Append(issue.Code).Append("` (").Append(issue.Severity).Append(")：")
                            .Append(issue.Message);
                        if (!string.IsNullOrWhiteSpace(issue.Text))
                        {
                            sb.Append(" 文本：").Append(issue.Text);
                        }
                        sb.AppendLine();
                    }
                }

                AppendRemoved(sb, "  ", c.Removed);
                var reservedClasses = DeckAssembler.GetTemplateReservedClasses(c.Style)
                    .Where(name => !string.Equals(name, "slide", StringComparison.Ordinal))
                    .ToList();
                var classConflicts = UsedClasses(c.Html, c.Css)
                    .Where(name => reservedClasses.Contains(name, StringComparer.Ordinal))
                    .ToList();
                if (classConflicts.Count > 0)
                {
                    sb.Append("  类名与模板结构类重名（会叠加模板样式，请改成 cover-* 后重新预览）：")
                        .AppendLine(string.Join(", ", classConflicts));
                }
                if (capture.BlockedRequests.Count > 0)
                {
                    sb.Append("  已拦截外部请求：").AppendLine(string.Join(", ", capture.BlockedRequests.Take(5)));
                }

                results.Add(new
                {
                    c.Name,
                    Style = c.Style.Slug,
                    SourceHash = source.Hash,
                    PreviewImageId = previewImageId,
                    capture.Width,
                    capture.Height,
                    Removed = c.Removed,
                    ValidationIssues = validationIssues,
                    TemplateReservedClasses = reservedClasses,
                    TemplateClassConflicts = classConflicts,
                    capture.BlockedRequests
                });
            }

            sb.AppendLine("必须调用 runtime.ask_user_question，让用户从以上候选中选择；每个候选一个选项，把 previewImageId 原样填入选项的 previewImageId 字段，不要自行选择或复制图片 URL。用户选定后，该候选封面会原样锁定为第 1 页，不要修改；以上校验问题仅供参考，内容页按相同字体规则编写即可。");
            var result = Success(request, sb.ToString().TrimEnd(), ToJson(new { DeckId = workspace.Id, Candidates = results }));
            foreach (var image in images)
            {
                AttachImage(result, image.Value, image.Key);
            }

            return result;
        }

        private static readonly Regex ClassAttribute = new Regex("\\bclass\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CssClass = new Regex(@"\.(-?[_a-zA-Z][\w-]*)", RegexOptions.Compiled);

        private static IList<string> UsedClasses(string html, string css)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match match in ClassAttribute.Matches(html ?? string.Empty))
            {
                foreach (var name in match.Groups[1].Value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    names.Add(name);
                }
            }

            foreach (Match match in CssClass.Matches(css ?? string.Empty))
            {
                names.Add(match.Groups[1].Value);
            }

            return names.ToList();
        }

        private static bool IsPreviewIssue(OfficeCopilot.Slides.Rendering.DeckValidationIssue issue)
        {
            if (issue == null || string.IsNullOrWhiteSpace(issue.Code)) return false;
            if (issue.Code.StartsWith("font.", StringComparison.OrdinalIgnoreCase)) return true;
            return issue.Code.StartsWith("layout.", StringComparison.OrdinalIgnoreCase)
                && string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class PreviewIssue
        {
            public string Code { get; set; }
            public string Severity { get; set; }
            public int Slide { get; set; }
            public string Message { get; set; }
            public string Selector { get; set; }
            public string Text { get; set; }
        }

        private sealed class Candidate
        {
            public string Name { get; set; }
            public StyleEntry Style { get; set; }
            public string Html { get; set; }
            public string Css { get; set; }
            public List<string> Removed { get; set; }
        }
    }
}
