using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Slides.Catalog;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Rendering;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    /// <summary>演示工作区工具的公共部分：工作区根目录与风格目录可注入（测试用）。</summary>
    public abstract class PowerPointDeckToolBase : PowerPointToolBase
    {
        private static readonly Lazy<DeckWorkspaceStore> DefaultStore =
            new Lazy<DeckWorkspaceStore>(() => new DeckWorkspaceStore(), LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly Func<DeckWorkspaceStore> _store;
        private readonly Func<SlideAssetCatalogs> _catalogs;

        protected PowerPointDeckToolBase(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs)
        {
            _store = store ?? (() => DefaultStore.Value);
            _catalogs = catalogs ?? (() => SlideAssetCatalogs.Default);
        }

        protected DeckWorkspaceStore Store
        {
            get { return _store(); }
        }

        protected SlideAssetCatalogs Catalogs
        {
            get { return _catalogs(); }
        }

        protected abstract string FailureCode { get; }

        public sealed override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                return await ExecuteCoreAsync(context, request, ParseArguments(request)).ConfigureAwait(false);
            }
            catch (DeckException ex)
            {
                return Fail(request, ex.Message, ex.Code);
            }
            catch (Exception ex)
            {
                return Fail(request, ex, FailureCode);
            }
        }

        protected abstract Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args);

        protected DeckWorkspace OpenWorkspace(JObject args)
        {
            var deckId = (string)args["deckId"];
            if (string.IsNullOrWhiteSpace(deckId))
            {
                throw new DeckException("ppt.deck_invalid_argument", "缺少 deckId（由 ppt_deck_create 返回）。");
            }

            return Store.Open(deckId);
        }

        protected StyleEntry ResolveStyle(string slug, bool required)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                if (required) throw new DeckException("ppt.deck_invalid_argument", "缺少 style。");
                return null;
            }

            StyleEntry entry;
            if (!Catalogs.Styles.TryGet(slug.Trim(), out entry))
            {
                throw new DeckException("ppt.style_not_found", "风格库中没有 " + slug + "，请先用 ppt_style_search 检索。");
            }

            if (!entry.IsValid)
            {
                throw new DeckException("ppt.style_not_found", "风格 " + slug + " 不可用：" + entry.InvalidReason);
            }

            return entry;
        }

        /// <summary>把工作区组装成 build/deck.html，返回渲染地址（不含 hash）。</summary>
        protected string BuildDeck(DeckWorkspace workspace)
        {
            return BuildDeck(workspace, false);
        }

        protected string BuildValidationDeck(DeckWorkspace workspace)
        {
            return BuildDeck(workspace, true);
        }

        private string BuildDeck(DeckWorkspace workspace, bool validation)
        {
            StyleEntry style = null;
            if (!string.IsNullOrWhiteSpace(workspace.Manifest.Style))
            {
                Catalogs.Styles.TryGet(workspace.Manifest.Style, out style);
            }

            var options = new DeckAssemblyOptions
            {
                DeckId = workspace.Id,
                Style = style,
                Slides = workspace.GetAllSlideHtml(),
                ThemeCss = workspace.ReadThemeCss(),
                Language = workspace.Manifest.Language,
                Title = workspace.Manifest.Title
            };
            var html = validation ? DeckAssembler.AssembleForValidation(options) : DeckAssembler.Assemble(options);
            var fileName = validation ? "validate.html" : "deck.html";
            workspace.WriteBuildFile(fileName, html);
            return DeckUrls.DeckBase(workspace.Id) + "build/" + fileName;
        }

        protected static IDeckRenderService GetRenderer(ToolExecutionContext context)
        {
            return context == null ? null : context.GetService<IDeckRenderService>();
        }

        protected static string DescribeSlides(DeckWorkspace workspace)
        {
            if (workspace.Slides.Count == 0) return "（暂无页面）";
            var sb = new StringBuilder();
            for (var i = 0; i < workspace.Slides.Count; i++)
            {
                var s = workspace.Slides[i];
                sb.Append(i + 1).Append(". ").Append(s.Id);
                if (!string.IsNullOrEmpty(s.Title)) sb.Append(" 「").Append(s.Title).Append('」');
                if (!string.IsNullOrEmpty(s.Layout)) sb.Append(" [").Append(s.Layout).Append(']');
                sb.Append(" #").AppendLine(s.Hash);
            }

            return sb.ToString().TrimEnd();
        }

        protected static object SlidesJson(DeckWorkspace workspace)
        {
            return workspace.Slides.Select((s, i) => new { Position = i + 1, s.Id, s.Title, s.Layout, s.Hash }).ToList();
        }

        protected static List<string> ReadStringArray(JObject args, string name)
        {
            var array = args[name] as JArray;
            if (array == null) return new List<string>();
            return array.Select(p => p.Type == JTokenType.String ? (string)p : p.ToString()).ToList();
        }

        protected static void AppendRemoved(StringBuilder sb, string label, IList<string> removed)
        {
            if (removed == null || removed.Count == 0) return;
            sb.Append(label).Append("已清除不允许的内容：").AppendLine(string.Join("；", removed.Distinct().Take(12)));
        }

        protected static string GetWorkspaceRevision(DeckWorkspace workspace)
        {
            var payload = JsonConvert.SerializeObject(workspace.Manifest)
                + "\0" + workspace.ReadThemeCss()
                + "\0" + string.Join("\0", workspace.GetAllSlideHtml());
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        protected static ToolDefinition Define(string name, string displayName, string description, bool readOnly, string schema, int timeoutSeconds = 90)
        {
            return new ToolDefinition
            {
                Name = name,
                DisplayName = displayName,
                Description = description,
                SourceKind = ToolSourceKind.BuiltIn,
                SourceName = "PowerPoint",
                IsReadOnly = readOnly,
                AffectsDocument = false,
                TimeoutSeconds = timeoutSeconds,
                InputSchemaJson = schema
            };
        }
    }

    public sealed class PowerPointDeckCreateTool : PowerPointDeckToolBase
    {
        private static readonly ToolDefinition _definition = Define(
            PowerPointToolNames.DeckCreate,
            "Create Presentation Draft",
            "Creates an HTML workspace for a new presentation without modifying the current presentation, and returns deckId. "
                + "Use a style slug selected with ppt_style_search / ppt_style_get; css is the shared theme CSS for the deck (optional, and can be changed later with ppt_deck_put_slides). "
                + "After the user selects a ppt_style_preview candidate, pass both the returned deckId and the fromPreview candidate name; the candidate cover is reused as slide 1, with its CSS locked in <style data-nx-cover> on slide 1 (not written to theme.css), and subsequent slides starting at slide 2. "
                + "When fromPreview is used, do not pass css; if style is supplied, it must match the candidate's style.",
            false,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"style\":{\"type\":\"string\",\"description\":\"Style slug; omit to reuse the workspace style or the default from settings.\"},"
                + "\"title\":{\"type\":\"string\",\"description\":\"Presentation title.\"},"
                + "\"density\":{\"type\":\"string\",\"enum\":[\"low\",\"medium\",\"high\"],\"description\":\"Information density; defaults to the value from settings.\"},"
                + "\"language\":{\"type\":\"string\",\"description\":\"BCP-47 language tag; defaults to zh-CN.\"},"
                + "\"css\":{\"type\":\"string\",\"description\":\"Shared theme CSS for the entire deck.\"},"
                + "\"deckId\":{\"type\":\"string\",\"description\":\"Reuse the workspace returned by ppt_style_preview; required with fromPreview.\"},"
                + "\"fromPreview\":{\"type\":\"string\",\"description\":\"Candidate name selected by the user in ppt_style_preview; reuses its sanitized cover and CSS.\"}"
                + "},"
                + "\"additionalProperties\":false"
                + "}");

        public PowerPointDeckCreateTool() : this(null, null) { }

        public PowerPointDeckCreateTool(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs) : base(store, catalogs) { }

        public override ToolDefinition Definition => _definition;

        protected override string FailureCode => "ppt.deck_create_failed";

        protected override Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args)
        {
            var options = context.GetService<PowerPointSlidesOptions>();
            var requestedStyle = (string)args["style"];
            var defaultStyle = options == null ? null : options.DefaultStyle;
            var title = (string)args["title"];
            var density = (string)args["density"];
            var language = (string)args["language"];
            var fromPreviewProperty = args.Property("fromPreview");
            var fromPreviewName = ((string)args["fromPreview"] ?? string.Empty).Trim().ToLowerInvariant();
            if (fromPreviewProperty != null && string.IsNullOrWhiteSpace(fromPreviewName))
            {
                throw new DeckException("ppt.deck_invalid_argument", "fromPreview 不能为空。");
            }

            DeckWorkspace workspace;
            StyleEntry style;
            bool reused;
            DeckPreviewSource previewSource = null;
            string coverSlideId = null;
            IList<string> removed = null;
            if (fromPreviewProperty != null)
            {
                if (string.IsNullOrWhiteSpace((string)args["deckId"]))
                {
                    throw new DeckException("ppt.deck_invalid_argument", "使用 fromPreview 时必须传入 ppt_style_preview 返回的 deckId。");
                }

                if (args["css"] != null)
                {
                    throw new DeckException("ppt.deck_invalid_argument", "fromPreview 已包含候选 CSS，不能同时传 css。");
                }

                workspace = OpenWorkspace(args);
                previewSource = workspace.ReadPreviewSource(fromPreviewName);
                if (!string.IsNullOrWhiteSpace(requestedStyle)
                    && !string.Equals(requestedStyle.Trim(), previewSource.Style, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DeckException("ppt.deck_invalid_argument", "style 与候选 " + fromPreviewName + " 的风格 " + previewSource.Style + " 冲突。");
                }

                style = ResolveStyle(previewSource.Style, true);
                workspace.Configure(style.Slug, density, language, title);
                // 封面 CSS 随第 1 页的 <style> 一起保存：之后 ppt_deck_put_slides 的 css 只替换 theme.css，不会冲掉封面。
                removed = workspace.WriteThemeCss(string.Empty, Catalogs.Fonts);
                var cover = workspace.PutSlides(new[]
                {
                    new DeckSlideInput { Html = EmbedCoverCss(previewSource.Html, previewSource.Css), Position = 1 }
                }, Catalogs.Fonts);
                coverSlideId = cover[0].Id;
                workspace.RecordPreviewOrigin(previewSource, coverSlideId);
                reused = true;
            }
            else if (!string.IsNullOrWhiteSpace((string)args["deckId"]))
            {
                workspace = OpenWorkspace(args);
                style = ResolveStyle(FirstNonBlank(requestedStyle, workspace.Manifest.Style, defaultStyle), true);
                workspace.Configure(style.Slug, density, language, title);
                reused = true;
            }
            else
            {
                style = ResolveStyle(FirstNonBlank(requestedStyle, defaultStyle), true);
                TryPurge(options == null ? 30 : options.WorkspaceRetentionDays);
                workspace = Store.Create(style.Slug, density ?? (options == null ? null : options.DefaultDensity), language, title);
                reused = false;
            }

            if (previewSource == null && args["css"] != null)
            {
                removed = workspace.WriteThemeCss((string)args["css"], Catalogs.Fonts);
            }

            var sb = new StringBuilder();
            sb.Append(reused ? "已复用工作区 " : "已新建工作区 ").AppendLine(workspace.Id);
            sb.Append("风格：").Append(style.Slug).Append(" 「").Append(style.Name).Append("」 ").Append(style.Kind)
                .Append(" | 密度 ").Append(workspace.Manifest.Density)
                .Append(" | 语言 ").AppendLine(workspace.Manifest.Language);
            if (style.MissingFonts.Count > 0) sb.Append("本地缺字体（将回退）：").AppendLine(string.Join(", ", style.MissingFonts));
            AppendRemoved(sb, "主题 CSS ", removed);
            if (previewSource != null)
            {
                sb.Append("已复用候选 ").Append(previewSource.Name).Append(" 的封面作为第 1 页（").Append(coverSlideId).Append("）；该页已整页锁定（HTML 与 <style data-nx-cover> 均不可覆盖，ppt_deck_put_slides 对它的覆盖会被忽略），theme.css 目前为空。").AppendLine();
                sb.AppendLine("下一步：先规划全稿类名，再从第 2 页开始用 ppt_deck_put_slides 写入（每页一个 <section class=\"slide\">，每次 ≤10 页）。ppt_deck_put_slides 的 css 只替换 theme.css，不会影响封面样式；不要把封面 CSS 复制进 theme.css。theme.css 与页内 <style> 都作用于整稿：封面专用类用 cover-*，跨页共享类用 theme-*，页面私有类用 sNN-*（如 s03-day）；nx-* 与 .slide 保留给运行时/结构。不要把未命名空间化的封面类复用到内容页；ppt_deck_validate 会报告冲突。");
            }
            else
            {
                sb.AppendLine("下一步：先规划全稿类名，再用 ppt_deck_put_slides 按页写入（每页一个 <section class=\"slide\">，每次 ≤10 页）。theme.css 会作用于整稿：封面专用类用 cover-*，跨页共享类用 theme-*，页面私有类用 sNN-*（如 s03-day）；nx-* 与 .slide 保留给运行时/结构。ppt_deck_validate 会报告类名冲突。");
            }

            return Task.FromResult(Success(request, sb.ToString().TrimEnd(), ToJson(new
            {
                DeckId = workspace.Id,
                Reused = reused,
                Style = style.Slug,
                FromPreview = previewSource == null ? null : previewSource.Name,
                PreviewHash = previewSource == null ? null : previewSource.Hash,
                CoverSlideId = coverSlideId,
                workspace.Manifest.Density,
                workspace.Manifest.Language,
                workspace.Manifest.Title,
                SlideCount = workspace.Slides.Count,
                WorkspaceRevision = GetWorkspaceRevision(workspace)
            })));
        }

        private static string FirstNonBlank(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string EmbedCoverCss(string html, string css)
        {
            if (string.IsNullOrWhiteSpace(css)) return html;
            return "<style data-nx-cover>" + css.Replace("</", "<\\/") + "</style>" + html;
        }

        private void TryPurge(int retentionDays)
        {
            try
            {
                Store.Purge(TimeSpan.FromDays(Math.Max(1, retentionDays)));
            }
            catch (Exception)
            {
                // 清理失败不影响新建。
            }
        }
    }

    public sealed class PowerPointDeckPutSlidesTool : PowerPointDeckToolBase
    {
        private const int MaxSlidesPerCall = 10;

        private static readonly ToolDefinition _definition = Define(
            PowerPointToolNames.DeckPutSlides,
            "Add or Update Slides",
            "Adds or replaces workspace slides (up to 10 per call). Each html value must be a <section class=\"slide\">...</section> designed at 1920x1080; "
                + "it may include <style>, but <script>, external links, and event attributes are prohibited and will be removed. "
                + "Omit id to add a slide (position is 1-based and defaults to append); provide an existing id to replace that entire slide. "
                + "css is optional; when supplied, it replaces theme.css entirely. A cover created from fromPreview is locked as a full slide: updates targeting it are ignored, and css does not affect its styles.",
            false,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"deckId\":{\"type\":\"string\"},"
                + "\"slides\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":10,\"items\":{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"id\":{\"type\":\"string\",\"description\":\"An existing slide ID (for example, s03) replaces that slide; omit to add a slide.\"},"
                + "\"position\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Insertion position for a new slide.\"},"
                + "\"html\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"html\"],"
                + "\"additionalProperties\":false"
                + "}},"
                + "\"css\":{\"type\":\"string\",\"description\":\"Replaces the entire theme.css; does not include slide 1 cover styles.\"}"
                + "},"
                + "\"required\":[\"deckId\",\"slides\"],"
                + "\"additionalProperties\":false"
                + "}",
            120);

        public PowerPointDeckPutSlidesTool() : this(null, null) { }

        public PowerPointDeckPutSlidesTool(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs) : base(store, catalogs) { }

        public override ToolDefinition Definition => _definition;

        protected override string FailureCode => "ppt.deck_put_failed";

        protected override Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args)
        {
            var workspace = OpenWorkspace(args);
            var items = args["slides"] as JArray;
            if (items == null || items.Count == 0)
            {
                throw new DeckException("ppt.deck_invalid_argument", "slides 为空。");
            }

            if (items.Count > MaxSlidesPerCall)
            {
                throw new DeckException("ppt.deck_invalid_argument", "每次最多写入 " + MaxSlidesPerCall + " 页，请分批。");
            }

            var inputs = items.OfType<JObject>().Select(p => new DeckSlideInput
            {
                Id = (string)p["id"],
                Html = (string)p["html"],
                Position = (int?)p["position"]
            }).ToList();

            if (inputs.Count != items.Count || inputs.Any(p => string.IsNullOrWhiteSpace(p.Html)))
            {
                throw new DeckException("ppt.deck_invalid_argument", "slides 每一项都必须是含 html 的对象。");
            }

            var results = workspace.PutSlides(inputs, Catalogs.Fonts);
            IList<string> cssRemoved = null;
            if (args["css"] != null)
            {
                cssRemoved = workspace.WriteThemeCss((string)args["css"], Catalogs.Fonts);
            }

            var sb = new StringBuilder();
            sb.Append("已写入 ").Append(results.Count).Append(" 页（共 ").Append(workspace.Slides.Count).AppendLine(" 页）：");
            foreach (var r in results)
            {
                sb.Append("- ").Append(r.Id).Append(r.Locked ? " 已锁定（未改动）" : r.Created ? " 新增" : " 覆盖").Append(" @").Append(r.Position);
                if (!string.IsNullOrEmpty(r.Title)) sb.Append(" 「").Append(r.Title).Append('」');
                sb.Append(" #").AppendLine(r.Hash);
                AppendRemoved(sb, "  ", r.Removed);
            }

            AppendRemoved(sb, "主题 CSS ", cssRemoved);

            return Task.FromResult(Success(request, sb.ToString().TrimEnd(), ToJson(new
            {
                DeckId = workspace.Id,
                Written = results.Select(r => new { r.Id, r.Position, r.Created, r.Locked, r.Hash, r.Title, r.Removed }),
                Slides = SlidesJson(workspace)
            })));
        }
    }

    public sealed class PowerPointDeckGetSlidesTool : PowerPointDeckToolBase
    {
        private const int MaxHtmlChars = 60000;

        private static readonly ToolDefinition _definition = Define(
            PowerPointToolNames.DeckGetSlides,
            "Read Draft Slides",
            "Reads the workspace slide list; when ids are supplied, also returns their HTML (read a slide before editing it). Set includeCss=true to include the theme CSS.",
            true,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"deckId\":{\"type\":\"string\"},"
                + "\"ids\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":10},"
                + "\"includeCss\":{\"type\":\"boolean\"}"
                + "},"
                + "\"required\":[\"deckId\"],"
                + "\"additionalProperties\":false"
                + "}");

        public PowerPointDeckGetSlidesTool() : this(null, null) { }

        public PowerPointDeckGetSlidesTool(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs) : base(store, catalogs) { }

        public override ToolDefinition Definition => _definition;

        protected override string FailureCode => "ppt.deck_get_failed";

        protected override Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args)
        {
            var workspace = OpenWorkspace(args);
            var ids = ReadStringArray(args, "ids").Select(p => (p ?? string.Empty).Trim().ToLowerInvariant()).Distinct().ToList();

            var sb = new StringBuilder();
            var m = workspace.Manifest;
            sb.Append("工作区 ").Append(workspace.Id).Append(" | 风格 ").Append(m.Style ?? "-")
                .Append(" | 密度 ").Append(m.Density).Append(" | ").Append(workspace.Slides.Count).AppendLine(" 页");
            if (!string.IsNullOrEmpty(m.Title)) sb.Append("标题：").AppendLine(m.Title);
            sb.AppendLine(DescribeSlides(workspace));

            var budget = MaxHtmlChars;
            var pages = new List<object>();
            foreach (var id in ids)
            {
                var html = workspace.GetSlideHtml(id);
                if (html.Length > budget)
                {
                    sb.AppendLine().Append("（其余页超出单次读取上限，未返回：").Append(id).AppendLine(" 及之后）");
                    break;
                }

                budget -= html.Length;
                sb.AppendLine().Append("## ").AppendLine(id).AppendLine(html);
                pages.Add(new { Id = id, Html = html });
            }

            string css = null;
            if ((bool?)args["includeCss"] == true)
            {
                css = workspace.ReadThemeCss();
                sb.AppendLine().AppendLine("## theme.css").AppendLine(string.IsNullOrEmpty(css) ? "（空）" : css);
            }

            return Task.FromResult(Success(request, sb.ToString().TrimEnd(), ToJson(new
            {
                DeckId = workspace.Id,
                m.Style,
                m.Density,
                m.Language,
                m.Title,
                Slides = SlidesJson(workspace),
                Pages = pages,
                ThemeCss = css
            })));
        }
    }

    public sealed class PowerPointDeckRemoveSlidesTool : PowerPointDeckToolBase
    {
        private static readonly ToolDefinition _definition = Define(
            PowerPointToolNames.DeckRemoveSlides,
            "Remove Draft Slides",
            "Removes the specified slides from the workspace by ID.",
            false,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"deckId\":{\"type\":\"string\"},"
                + "\"ids\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1}"
                + "},"
                + "\"required\":[\"deckId\",\"ids\"],"
                + "\"additionalProperties\":false"
                + "}");

        public PowerPointDeckRemoveSlidesTool() : this(null, null) { }

        public PowerPointDeckRemoveSlidesTool(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs) : base(store, catalogs) { }

        public override ToolDefinition Definition => _definition;

        protected override string FailureCode => "ppt.deck_remove_failed";

        protected override Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args)
        {
            var workspace = OpenWorkspace(args);
            var ids = ReadStringArray(args, "ids");
            workspace.RemoveSlides(ids);
            var text = "已删除 " + string.Join(", ", ids) + "，剩余 " + workspace.Slides.Count + " 页：\n" + DescribeSlides(workspace);
            return Task.FromResult(Success(request, text, ToJson(new { DeckId = workspace.Id, Slides = SlidesJson(workspace) })));
        }
    }

    public sealed class PowerPointDeckReorderTool : PowerPointDeckToolBase
    {
        private static readonly ToolDefinition _definition = Define(
            PowerPointToolNames.DeckReorder,
            "Reorder Draft Slides",
            "Reorders workspace slides in the specified order; order must list every slide ID exactly once.",
            false,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"deckId\":{\"type\":\"string\"},"
                + "\"order\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1}"
                + "},"
                + "\"required\":[\"deckId\",\"order\"],"
                + "\"additionalProperties\":false"
                + "}");

        public PowerPointDeckReorderTool() : this(null, null) { }

        public PowerPointDeckReorderTool(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs) : base(store, catalogs) { }

        public override ToolDefinition Definition => _definition;

        protected override string FailureCode => "ppt.deck_reorder_failed";

        protected override Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args)
        {
            var workspace = OpenWorkspace(args);
            workspace.Reorder(ReadStringArray(args, "order"));
            var text = "已重排：\n" + DescribeSlides(workspace);
            return Task.FromResult(Success(request, text, ToJson(new { DeckId = workspace.Id, Slides = SlidesJson(workspace) })));
        }
    }

    public sealed class PowerPointDeckPreviewTool : PowerPointDeckToolBase
    {
        private static readonly ToolDefinition _definition = Define(
            PowerPointToolNames.DeckPreview,
            "Preview Presentation Draft",
            "Assembles the workspace into a complete presentation and opens it in a separate window for the user to review (use the left/right arrow keys to navigate). slide is the 1-based starting slide. This is for user review only and does not return screenshots; call once after writing all slides, not once per slide.",
            true,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"deckId\":{\"type\":\"string\"},"
                + "\"slide\":{\"type\":\"integer\",\"minimum\":1}"
                + "},"
                + "\"required\":[\"deckId\"],"
                + "\"additionalProperties\":false"
                + "}");

        public PowerPointDeckPreviewTool() : this(null, null) { }

        public PowerPointDeckPreviewTool(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs) : base(store, catalogs) { }

        public override ToolDefinition Definition => _definition;

        protected override string FailureCode => "ppt.deck_preview_failed";

        protected override async Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args)
        {
            var workspace = OpenWorkspace(args);
            if (workspace.Slides.Count == 0)
            {
                throw new DeckException("ppt.deck_empty", "工作区还没有页面，请先用 ppt_deck_put_slides 写入。");
            }

            var renderer = GetRenderer(context);
            if (renderer == null)
            {
                return Fail(request, "当前环境没有预览窗口。", "ppt.deck_render_unavailable");
            }

            var slide = Math.Max(1, Math.Min((int?)args["slide"] ?? 1, workspace.Slides.Count));
            var url = BuildDeck(workspace) + "#" + slide;
            await renderer.ShowPreviewAsync(workspace.Id, workspace.Manifest.Title, url).ConfigureAwait(false);
            return Success(
                request,
                "已在预览窗口打开 " + workspace.Id + "（第 " + slide + " / " + workspace.Slides.Count + " 页）。",
                ToJson(new
                {
                    DeckId = workspace.Id,
                    Slide = slide,
                    SlideCount = workspace.Slides.Count,
                    workspace.Manifest.Title,
                    WorkspaceRevision = GetWorkspaceRevision(workspace)
                }));
        }
    }

    public sealed class PowerPointDeckValidateTool : PowerPointDeckToolBase
    {
        private const int TimeoutSeconds = 180;

        private static readonly ToolDefinition _definition = Define(
            PowerPointToolNames.DeckValidate,
            "Validate Presentation Draft",
            "Runs L1 DOM checks and L2 PDF pagination checks in local WebView2, covering global CSS class conflicts, overflow, text outside bounds/overlap/occlusion, font size, font fallback, placeholder text, broken images, and density. "
                + "Screenshots of slides with issues are saved in workspace previews/. Does not include the L3 read-back check after building in PowerPoint.",
            true,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{\"deckId\":{\"type\":\"string\"}},"
                + "\"required\":[\"deckId\"],"
                + "\"additionalProperties\":false"
                + "}",
            TimeoutSeconds);

        public PowerPointDeckValidateTool() : this(null, null) { }

        public PowerPointDeckValidateTool(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs) : base(store, catalogs) { }

        public override ToolDefinition Definition => _definition;

        protected override string FailureCode => "ppt.deck_validate_failed";

        protected override async Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args)
        {
            var workspace = OpenWorkspace(args);
            if (workspace.Slides.Count == 0)
            {
                throw new DeckException("ppt.deck_empty", "工作区还没有页面，请先用 ppt_deck_put_slides 写入。");
            }

            var renderer = GetRenderer(context);
            if (renderer == null)
            {
                return Fail(request, "当前环境没有 DOM 验证器。", "ppt.deck_render_unavailable");
            }

            var url = BuildDeck(workspace);
            var validationUrl = BuildValidationDeck(workspace);
            var report = await renderer.ValidateAsync(url, validationUrl, workspace.Manifest.Density, context.CancellationToken).ConfigureAwait(false);
            if (report.SlideCount != workspace.Slides.Count)
            {
                throw new InvalidOperationException("渲染器识别到 " + report.SlideCount + " 页，工作区记录为 " + workspace.Slides.Count + " 页。");
            }

            var screenshots = new List<ValidationImage>();
            foreach (var screenshot in report.Screenshots ?? new List<DeckValidationScreenshot>())
            {
                var name = "validation-s" + screenshot.Slide.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
                var path = workspace.PreviewPath(name, ".png");
                System.IO.File.WriteAllBytes(path, screenshot.Png);
                screenshots.Add(new ValidationImage
                {
                    Slide = screenshot.Slide,
                    Image = DeckUrls.PanelImage(workspace.Id, "previews/" + name + ".png")
                        + "?v=" + DeckWorkspace.ComputeHash(Convert.ToBase64String(screenshot.Png))
                });
            }

            var lockedCoverPage = 0;
            for (var i = 0; i < workspace.Slides.Count; i++)
            {
                if (workspace.IsLockedCover(workspace.Slides[i].Id)) lockedCoverPage = i + 1;
            }

            var allIssues = report.Issues ?? new List<DeckValidationIssue>();
            // layout.tight 只是"未达推荐边距"的提示，不可操作；锁定封面的问题不允许修改，均不计入。
            var actionable = allIssues
                .Where(i => !string.Equals(i.Code, "layout.tight", StringComparison.OrdinalIgnoreCase))
                .Where(i => lockedCoverPage == 0 || i.Slide != lockedCoverPage)
                .ToList();
            var coverIssues = lockedCoverPage == 0 ? 0 : allIssues.Count(i => i.Slide == lockedCoverPage);
            var errors = actionable.Count(i => string.Equals(i.Severity, "error", StringComparison.OrdinalIgnoreCase));
            var warnings = actionable.Count - errors;
            var sb = new StringBuilder();
            sb.Append("L1 检查 ").Append(report.SlideCount).Append(" 页：").Append(errors).Append(" 项错误，").Append(warnings).AppendLine(" 项警告。");
            sb.Append("字体回退检查抽样 ").Append(report.FontTargets == null ? 0 : report.FontTargets.Count).Append(" 个文本节点");
            if (report.FontTargetsTruncated) sb.Append("（已达抽样上限）");
            sb.AppendLine("。");
            if (report.PdfPaginationChecked)
            {
                sb.Append("L2 PDF 分页：").Append(report.PdfPageCount).Append(" 页（预期 ").Append(report.SlideCount).AppendLine(" 页）。");
            }
            if (coverIssues > 0)
            {
                sb.Append("第 ").Append(lockedCoverPage).Append(" 页是用户选定的锁定封面，已省略 ")
                    .Append(coverIssues).AppendLine(" 条参考问题，不要修改该页。");
            }

            var groups = actionable
                .GroupBy(i => (i.Severity ?? string.Empty) + "\n" + i.Code + "\n" + i.Message + "\n" + i.Selector)
                .OrderBy(g => string.Equals(g.First().Severity, "error", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(g => g.Min(i => i.Slide));
            foreach (var group in groups)
            {
                var issue = group.First();
                var slides = group.Select(i => i.Slide).Where(s => s > 0).Distinct().OrderBy(s => s).ToList();
                sb.Append("- ").Append(issue.Severity == "error" ? "ERROR" : "WARN")
                    .Append(" [").Append(issue.Code).Append("] ");
                if (slides.Count > 0) sb.Append("第 ").Append(FormatSlideRanges(slides)).Append(" 页：");
                else sb.Append("整份演示：");
                sb.Append(issue.Message);
                if (!string.IsNullOrWhiteSpace(issue.Selector)) sb.Append("（").Append(issue.Selector).Append("）");
                var texts = group.Select(i => i.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(3).ToList();
                if (texts.Count > 0) sb.Append(" 文本：").Append(string.Join(" / ", texts));
                sb.AppendLine();
            }

            if (screenshots.Count > 0)
            {
                sb.AppendLine("问题页截图：");
                foreach (var image in screenshots) sb.Append("- 第 ").Append(image.Slide).Append(" 页：").AppendLine(image.Image);
            }

            if (report.Truncated) sb.AppendLine("结果达到每页问题上限，可能还有未列出的问题。");
            sb.Append("检查范围为 L1 DOM + L2 PDF；PowerPoint 构建后的 L3 回读检查尚未运行。");

            var json = ToJson(new
            {
                DeckId = workspace.Id,
                report.SlideCount,
                workspace.Manifest.Title,
                WorkspaceRevision = GetWorkspaceRevision(workspace),
                Errors = errors,
                Warnings = warnings,
                report.Truncated,
                FontSamples = report.FontTargets == null ? 0 : report.FontTargets.Count,
                report.FontTargetsTruncated,
                Issues = actionable,
                SuppressedLockedCoverIssues = coverIssues,
                Screenshots = screenshots
            });
            return AttachValidationImages(Success(request, sb.ToString().TrimEnd(), json), report);
        }

        private static string FormatSlideRanges(IList<int> slides)
        {
            var parts = new List<string>();
            for (var i = 0; i < slides.Count; i++)
            {
                var start = slides[i];
                while (i + 1 < slides.Count && slides[i + 1] == slides[i] + 1) i++;
                parts.Add(start == slides[i] ? start.ToString() : start + "–" + slides[i]);
            }

            return string.Join("、", parts);
        }

        private const int MaxModelImages = 4;

        private static ToolExecutionResult AttachValidationImages(ToolExecutionResult result, DeckValidationCapture report)
        {
            foreach (var screenshot in (report.Screenshots ?? new List<DeckValidationScreenshot>()).Take(MaxModelImages))
            {
                AttachImage(result, screenshot.Png, "第 " + screenshot.Slide + " 页问题截图");
            }

            return result;
        }

        private sealed class ValidationImage
        {
            public int Slide { get; set; }

            public string Image { get; set; }
        }
    }

    public class PowerPointDeckBuildTool : PowerPointDeckToolBase, IToolExecutionPreflight
    {
        protected const int TimeoutSeconds = 180;

        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.DeckBuild,
            DisplayName = "Build Presentation from Workspace",
            Description = "Builds slides from the most recently previewed workspace in this session. mode: editable converts text, fills, and icons into native editable elements where possible;"
                + " fidelity uses native text boxes with decorative elements retained in a background image; image places one complete HTML screenshot on each slide, matching the preview regardless of font, icon, or shape differences, but the content is not editable."
                + " If mode is omitted, the default mode from settings is used."
                + " This tool does not run ppt_deck_validate or perform a PowerPoint read-back or visual verification after building; the user can request ppt_deck_validate separately."
                + " deckId may be omitted; when omitted, the latest workspace preview from this session is used automatically, and the confirmation dialog shows its title, ID, and slide count. Omission is recommended; do not guess."
                + " If deckId is provided, it must exactly match the complete ID returned by ppt_deck_create and the latest workspace preview in this session; otherwise the request is rejected. Never invent or abbreviate it or use a deck-... form."
                + " workspaceTitle and slideCount may also be omitted and filled automatically; if supplied, they must exactly match the latest preview."
                + " If the current presentation is blank, build directly in it by replacing the blank slide; target is not required."
                + " If the current presentation contains content, target is required, and runtime.ask_user_question must be used first to ask the user whether to create a new 16:9 presentation (target=new) or append to the end of the current 16:9 presentation (target=append; automatic backup)."
                + " replace=true inserts all workspace slides starting at fromIndex and deletes the original fromIndex-to-toIndex slides; the number of built slides must equal the number replaced or the request is rejected. A backup is created before modification. User confirmation is required.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = false,
            AffectsDocument = true,
            RequiresConfirmation = true,
            RiskLevel = "high",
            TimeoutSeconds = TimeoutSeconds,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"deckId\":{\"type\":\"string\",\"pattern\":\"^[0-9]{8}-[0-9]{6}-[a-z0-9]{6}$\",\"description\":\"Optional (defaults to the most recently previewed workspace in this session); if supplied, use the complete ID returned by ppt_deck_create exactly as-is. Do not construct one.\"},"
                + "\"workspaceTitle\":{\"type\":\"string\",\"description\":\"Optional (auto-filled); if supplied, it must come from the most recent preview result.\"},"
                + "\"slideCount\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Optional (auto-filled); if supplied, it must come from the most recent preview result.\"},"
                + "\"mode\":{\"type\":\"string\",\"enum\":[\"editable\",\"fidelity\",\"image\"],\"description\":\"editable=editable elements; fidelity=native text with a background image; image=one full-slide image (matches the preview but is not editable). Must match the user's selection in the build confirmation.\"},"
                + "\"target\":{\"type\":\"string\",\"enum\":[\"append\",\"new\"]},"
                + "\"replace\":{\"type\":\"boolean\"},"
                + "\"fromIndex\":{\"type\":\"integer\",\"minimum\":1},"
                + "\"toIndex\":{\"type\":\"integer\",\"minimum\":1}"
                + "},"
                + "\"additionalProperties\":false"
                + "}"
        };

        public PowerPointDeckBuildTool() : this(null, null) { }

        public PowerPointDeckBuildTool(Func<DeckWorkspaceStore> store, Func<SlideAssetCatalogs> catalogs) : base(store, catalogs) { }

        public override ToolDefinition Definition => _definition;

        protected override string FailureCode => "ppt.deck_build_failed";

        public ToolExecutionPreflightResult Preflight(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var workspace = OpenWorkspace(ParseArguments(request));
                if (workspace.Slides.Count == 0)
                {
                    return ToolExecutionPreflightResult.CreateFailure(
                        "ppt.deck_empty",
                        "工作区还没有页面，请先用 ppt_deck_put_slides 写入。");
                }

                return ToolExecutionPreflightResult.CreateSuccess(ToJson(new
                {
                    DeckId = workspace.Id,
                    workspace.Manifest.Title,
                    SlideCount = workspace.Slides.Count,
                    WorkspaceRevision = GetWorkspaceRevision(workspace)
                }));
            }
            catch (DeckException ex)
            {
                return ToolExecutionPreflightResult.CreateFailure(ex.Code, ex.Message);
            }
        }

        protected virtual PowerPointDeckBuildTarget ResolveTarget(JObject args)
        {
            if (args.Value<bool?>("replace") == true)
            {
                if (args["target"] != null)
                {
                    throw new DeckException("ppt.deck_invalid_argument", "replace=true 不能与 target 同时使用。");
                }

                return new PowerPointDeckBuildTarget
                {
                    Kind = PowerPointDeckBuildTarget.ReplaceKind,
                    ReplaceFrom = args.Value<int?>("fromIndex") ?? 0,
                    ReplaceTo = args.Value<int?>("toIndex") ?? 0
                };
            }

            if (args["fromIndex"] != null || args["toIndex"] != null)
            {
                throw new DeckException("ppt.deck_invalid_argument", "fromIndex / toIndex 仅能与 replace=true 一起使用。");
            }

            var kind = (string)args["target"];
            if (kind == null) return new PowerPointDeckBuildTarget { Kind = null };
            if (!string.Equals(kind, PowerPointDeckBuildTarget.AppendKind, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(kind, PowerPointDeckBuildTarget.NewKind, StringComparison.OrdinalIgnoreCase))
            {
                throw new DeckException("ppt.deck_invalid_argument", "target 必须是 append 或 new；替换请设置 replace=true 并提供 fromIndex / toIndex。");
            }

            return new PowerPointDeckBuildTarget { Kind = kind.ToLowerInvariant() };
        }

        protected override async Task<ToolExecutionResult> ExecuteCoreAsync(ToolExecutionContext context, ToolExecutionRequest request, JObject args)
        {
            var workspace = OpenWorkspace(args);
            if (workspace.Slides.Count == 0)
            {
                throw new DeckException("ppt.deck_empty", "工作区还没有页面，请先用 ppt_deck_put_slides 写入。");
            }

            var target = ResolveTarget(args);
            var presentation = GetHost(context).GetActivePresentationInfo();
            var hasPresentation = presentation != null && presentation.IsAvailable;
            if (!target.IsReplace && hasPresentation && presentation.IsBlank && !presentation.IsReadOnly)
            {
                target = new PowerPointDeckBuildTarget { Kind = PowerPointDeckBuildTarget.ReuseBlankKind };
            }
            else if (target.Kind == null)
            {
                if (hasPresentation)
                {
                    return Fail(request, "当前演示文稿「" + presentation.Name + "」已有 " + presentation.SlideCount + " 页内容，构建位置需要用户决定。"
                        + "请先用 runtime.ask_user_question 询问用户：生成一个全新的演示文稿（target=new），还是追加到当前文档末尾（target=append）；"
                        + "再按用户的选择带上 target 重新调用。", "ppt.deck_target_required");
                }

                target.Kind = PowerPointDeckBuildTarget.NewKind;
            }

            if (!target.IsNew && !target.IsReuseBlank)
            {
                if (!hasPresentation) return Fail(request, "当前没有打开的演示文稿。", "ppt.presentation_unavailable");
                if (presentation.IsReadOnly) return Fail(request, "当前演示文稿为只读状态，无法构建。", "ppt.presentation_read_only");
                if (Math.Abs(presentation.SlideWidth - 960f) > 2f || Math.Abs(presentation.SlideHeight - 540f) > 2f)
                {
                    return Fail(request, "当前页面尺寸不是 16:9（960×540 pt），本工具不会自动缩放或修改文稿尺寸；可改用 target=new。", "ppt.slide_size_unsupported");
                }

                if (target.IsReplace && (target.ReplaceFrom < 1 || target.ReplaceTo < target.ReplaceFrom || target.ReplaceTo > presentation.SlideCount))
                {
                    return Fail(request, "替换范围无效：第 " + target.ReplaceFrom + "–" + target.ReplaceTo + " 页（共 " + presentation.SlideCount + " 页）。", "ppt.deck_invalid_argument");
                }

                if (target.IsReplace)
                {
                    var replacementCount = target.ReplaceTo - target.ReplaceFrom + 1;
                    if (workspace.Slides.Count != replacementCount)
                    {
                        return Fail(request, "工作区包含 " + workspace.Slides.Count + " 页，但替换范围包含 " + replacementCount
                            + " 页。为避免重复生成，构建页数必须与替换页数一致；整套重建请提供覆盖整套页面的范围。", "ppt.deck_replace_count_mismatch");
                    }
                }
            }

            var service = context.GetService<IPowerPointDeckBuildService>();
            if (service == null) return Fail(request, "当前环境没有演示构建服务。", "ppt.deck_build_unavailable");
            var options = context.GetService<PowerPointSlidesOptions>();
            var mode = (string)args["mode"];
            if (string.IsNullOrWhiteSpace(mode)) mode = options == null || string.IsNullOrWhiteSpace(options.DefaultBuildMode) ? SlideBuildModes.Fidelity : options.DefaultBuildMode;
            var url = BuildDeck(workspace) + "?mode=capture";
            var result = await service.BuildAsync(workspace, url, mode, context.CancellationToken, target).ConfigureAwait(false);
            var slideRange = result.SlideIndexes.Count == 0
                ? "无新增页"
                : result.SlideIndexes.First() == result.SlideIndexes.Last()
                    ? "第 " + result.SlideIndexes[0] + " 页"
                    : "第 " + result.SlideIndexes.First() + "–" + result.SlideIndexes.Last() + " 页";
            var buildIssues = result.BuildIssues ?? new List<PptVerificationIssue>();
            var text = new StringBuilder();
            text.Append("已将工作区 ").Append(workspace.Id).Append(" 以 ").Append(mode).Append(" 模式")
                .Append(target.IsNew ? "构建为新演示文稿"
                    : target.IsReplace ? "替换原第 " + target.ReplaceFrom + "–" + target.ReplaceTo + " 页"
                    : target.IsReuseBlank ? "直接构建到当前空白演示文稿（已移除原有空白页）"
                    : "追加到当前演示文稿")
                .Append("：").Append(slideRange).Append("。")
                .Append(SlideBuildModes.IsImage(mode)
                    ? "整页截图已保存在工作区 build/capture/。"
                    : "采集截图与背景截图已保存在工作区 build/capture/。")
                .Append("本次构建未执行 PowerPoint 回读或视觉验证。");

            foreach (var issue in buildIssues)
            {
                text.AppendLine().Append("- [build_issue] 第 ").Append(issue.Slide).Append(" 页「").Append(issue.ShapeName).Append("」：").Append(issue.Message);
                if (!string.IsNullOrWhiteSpace(issue.Expected)) text.Append(" 预期 ").Append(issue.Expected).Append("。");
                if (!string.IsNullOrWhiteSpace(issue.Actual)) text.Append(" 实际 ").Append(issue.Actual).Append("。");
            }
            return Success(request, text.ToString(), ToJson(new
            {
                DeckId = workspace.Id,
                Mode = mode,
                Target = target.Kind,
                SlideIndexes = result.SlideIndexes,
                CaptureFiles = result.CaptureFiles,
                PostBuildValidationPerformed = false,
                BuildIssues = buildIssues
            }));
        }
    }

}
