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
            "新建演示工作区",
            "为一套新幻灯片建立 HTML 工作区（不改动当前演示文稿），返回 deckId。"
                + "style 用 ppt_style_search / ppt_style_get 选定的 slug；css 为整套共用的主题 CSS（可选，以后可在 ppt_deck_put_slides 里改）。"
                + "用户选定 ppt_style_preview 的候选后，必须同时传入返回的 deckId 和 fromPreview 候选名；该候选封面会作为第 1 页复用，候选 CSS 锁定在第 1 页的 <style data-nx-cover> 中（不写入 theme.css），后续从第 2 页开始写。"
                + "使用 fromPreview 时不得传 css，style 若传入必须与候选风格一致。",
            false,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"style\":{\"type\":\"string\",\"description\":\"风格 slug；省略时沿用复用工作区的风格或设置中的默认风格。\"},"
                + "\"title\":{\"type\":\"string\",\"description\":\"演示标题。\"},"
                + "\"density\":{\"type\":\"string\",\"enum\":[\"low\",\"medium\",\"high\"],\"description\":\"信息密度；省略时采用设置中的默认密度。\"},"
                + "\"language\":{\"type\":\"string\",\"description\":\"BCP-47 语言，默认 zh-CN。\"},"
                + "\"css\":{\"type\":\"string\",\"description\":\"整套共用的主题 CSS。\"},"
                + "\"deckId\":{\"type\":\"string\",\"description\":\"复用 ppt_style_preview 返回的工作区；fromPreview 必填。\"},"
                + "\"fromPreview\":{\"type\":\"string\",\"description\":\"用户选定的 ppt_style_preview 候选名；复用其已清洗封面和 CSS。\"}"
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
            "写入工作区页面",
            "向工作区新增或覆盖页面（每次 ≤10 页）。每页 html 是一个 <section class=\"slide\">…</section>，按 1920×1080 设计；"
                + "可含 <style>，不得含 <script>、外链或事件属性（会被清除）。"
                + "省略 id 表示新增（position 为插入位置，1 起，省略则追加）；给出已有 id 表示整页覆盖。"
                + "css 可选，给出时整体替换 theme.css。由 fromPreview 生成的封面页整页锁定：覆盖它的项会被忽略，封面样式也不受 css 影响。",
            false,
            "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"deckId\":{\"type\":\"string\"},"
                + "\"slides\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":10,\"items\":{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"id\":{\"type\":\"string\",\"description\":\"已有页 id（如 s03）表示覆盖；省略表示新增。\"},"
                + "\"position\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"新增页的插入位置。\"},"
                + "\"html\":{\"type\":\"string\"}"
                + "},"
                + "\"required\":[\"html\"],"
                + "\"additionalProperties\":false"
                + "}},"
                + "\"css\":{\"type\":\"string\",\"description\":\"替换整个 theme.css（不含第 1 页封面样式）。\"}"
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
            "读取工作区页面",
            "读取工作区的页清单；给出 ids 时同时返回这些页的 HTML（修改某页前先读回）。includeCss=true 时附带主题 CSS。",
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
            "删除工作区页面",
            "从工作区删除指定页（按 id）。",
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
            "调整工作区页序",
            "按给定顺序重排工作区页面；order 必须恰好列出全部页 id。",
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
            "预览工作区",
            "把工作区组装成完整演示并在独立窗口中打开，供用户翻页查看（← → 翻页）。slide 为起始页（1 起）。只给用户看，不返回截图；写完全部页面后调用一次即可，不要逐页调用。",
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
            "检查工作区页面",
            "在本地 WebView2 中执行 L1 DOM 检查和 L2 PDF 分页检查，覆盖全局 CSS 类名冲突、溢出、文字越界/重叠/遮挡、字号、字体回退、占位文字、坏图及密度；"
                + "问题页截图保存在工作区 previews/。不包含 PowerPoint 构建后的 L3 回读检查。",
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
            DisplayName = "构建工作区到演示文稿",
            Description = "把本会话最近预览的工作区构建为幻灯片。mode：editable=文字、色块、图标尽量转为可编辑的原生元素；"
                + "fidelity=文字为原生文本框，其余装饰留在背景图；image=每页只放一张 HTML 完整截图，与预览完全一致、不受字体/图标/形状差异影响，但内容不可编辑。"
                + "省略 mode 时使用设置中的默认模式。"
                + "构建不自动运行 ppt_deck_validate，也不执行构建后的 PowerPoint 回读或视觉验证；用户可单独要求调用 ppt_deck_validate。"
                + "deckId 可省略：省略时自动使用本会话最近一次预览的工作区，确认框会展示其标题、ID 和页数；推荐省略，不要猜测。"
                + "若提供 deckId，必须原样使用 ppt_deck_create 返回的完整 ID，且须与本会话最近预览的工作区一致，否则拒绝；严禁自行编造、缩写或使用 deck-... 形式。"
                + "workspaceTitle 与 slideCount 同样可省略（自动补全）；若提供必须照抄最近一次预览结果。"
                + "当前演示文稿为空白时一律直接在当前文稿中构建（替换空白页，无需 target）。"
                + "当前文稿已有内容时必须提供 target，且须先用 runtime.ask_user_question 询问用户："
                + "target=new 新建 16:9 演示文稿，target=append 追加到当前 16:9 文稿末尾（自动备份）。"
                + "replace=true 会把工作区全部页面插入 fromIndex，并删除原 fromIndex–toIndex 页；构建页数必须与被替换页数一致，否则拒绝，修改前自动备份。构建需要用户确认。",
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
                + "\"deckId\":{\"type\":\"string\",\"pattern\":\"^[0-9]{8}-[0-9]{6}-[a-z0-9]{6}$\",\"description\":\"可省略（默认本会话最近预览的工作区）；提供时必须原样使用 ppt_deck_create 返回的完整 ID，不要自行构造。\"},"
                + "\"workspaceTitle\":{\"type\":\"string\",\"description\":\"可省略（自动补全）；提供时必须来自最近一次预览结果。\"},"
                + "\"slideCount\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"可省略（自动补全）；提供时必须来自最近一次预览结果。\"},"
                + "\"mode\":{\"type\":\"string\",\"enum\":[\"editable\",\"fidelity\",\"image\"],\"description\":\"editable=可编辑；fidelity=原生文字+背景图；image=整页图片（与预览一致，不可编辑）。须按用户在构建确认中的选择传入。\"},"
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
