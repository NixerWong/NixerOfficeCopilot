using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Slides.Catalog;
using OfficeCopilot.Slides.Deck;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    /// <summary>风格库工具的公共部分：目录来源可注入（测试用），默认进程共享目录。</summary>
    public abstract class PowerPointStyleToolBase : PowerPointToolBase
    {
        private readonly Func<SlideAssetCatalogs> _catalogs;

        protected PowerPointStyleToolBase(Func<SlideAssetCatalogs> catalogs)
        {
            _catalogs = catalogs ?? (() => SlideAssetCatalogs.Default);
        }

        protected SlideAssetCatalogs Catalogs
        {
            get { return _catalogs(); }
        }

        protected static string Join(System.Collections.Generic.IEnumerable<string> values)
        {
            return values == null ? string.Empty : string.Join(", ", values.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }

    public sealed class PowerPointStyleSearchTool : PowerPointStyleToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.StyleSearch,
            DisplayName = "搜索风格库",
            Description = "按关键词与筛选条件检索幻灯片风格库（HTML 模板 + 描述型预设），返回候选 slug、名称、气质、适用场景与字体可用性。"
                + "元数据为英文：query 请用英文关键词（如 \"finance formal light\"、\"tech launch bold\"）。"
                + "选型时通常给用户 3 个候选：1 个稳妥（certified）+ 1 个大胆 + 1 个灵活。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"query\":{\"type\":\"string\",\"description\":\"英文关键词，空格分隔；省略表示按筛选条件列出。\"},"
                + "\"kind\":{\"type\":\"string\",\"enum\":[\"template\",\"preset\"]},"
                + "\"scheme\":{\"type\":\"string\",\"enum\":[\"light\",\"dark\",\"mixed\"]},"
                + "\"formality\":{\"type\":\"string\",\"enum\":[\"low\",\"medium\",\"high\"]},"
                + "\"density\":{\"type\":\"string\",\"enum\":[\"low\",\"medium\",\"high\"]},"
                + "\"tier\":{\"type\":\"string\",\"enum\":[\"certified\",\"experimental\",\"user\"]},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":50,\"description\":\"默认 8。\"}"
                + "},"
                + "\"additionalProperties\":false"
                + "}"
        };

        public PowerPointStyleSearchTool() : this(null) { }

        public PowerPointStyleSearchTool(Func<SlideAssetCatalogs> catalogs) : base(catalogs) { }

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var args = ParseArguments(request);
                var query = new StyleSearchQuery
                {
                    Text = (string)args["query"],
                    Kind = (string)args["kind"],
                    Scheme = (string)args["scheme"],
                    Formality = (string)args["formality"],
                    Density = (string)args["density"],
                    Tier = (string)args["tier"],
                    MaxResults = (int?)args["maxResults"] ?? 8
                };

                var hits = Catalogs.Styles.Search(query);
                var systemFonts = Catalogs.SystemFonts;
                var chineseFonts = systemFonts.ChineseFamilies
                    .Concat(Catalogs.Fonts.Families
                        .Where(family => family.Cjk && !family.EmojiOnly)
                        .Select(family => family.Family))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var sb = new StringBuilder();
                sb.AppendLine("命中 " + hits.Count + " 个风格");
                sb.Append("本机字体分析：").AppendLine(systemFonts.Summary);
                sb.Append("检测到可用于中文的字体族：")
                    .AppendLine(chineseFonts.Count == 0 ? "未检测到；请使用随包中文字体 Noto Sans SC、Noto Serif SC、LXGW WenKai、Smiley Sans 或 ZCOOL XiaoWei。" : Join(chineseFonts));
                foreach (var warning in systemFonts.Warnings)
                {
                    sb.Append("本机字体分析提示：").AppendLine(warning);
                }

                foreach (var hit in hits)
                {
                    var e = hit.Entry;
                    sb.Append("- ").Append(e.Slug).Append(" 「").Append(e.Name).Append("」 ")
                        .Append(e.Kind).Append('/').Append(e.Tier)
                        .Append(" | ").Append(e.Scheme ?? "-").Append(" | 正式度 ").Append(e.Formality ?? "-")
                        .AppendLine();
                    if (!string.IsNullOrEmpty(e.Tagline)) sb.Append("  ").AppendLine(e.Tagline);
                    if (e.Mood.Count > 0 || e.Tone.Count > 0) sb.Append("  气质：").AppendLine(Join(e.Mood.Concat(e.Tone)));
                    if (!string.IsNullOrEmpty(e.BestFor)) sb.Append("  适用：").AppendLine(PowerPointHostAdapter.Truncate(e.BestFor, 240));
                    if (e.MissingFonts.Count > 0) sb.Append("  本地缺字体（将回退）：").AppendLine(Join(e.MissingFonts));
                }

                var json = ToJson(hits.Select(p => new
                {
                    p.Entry.Slug,
                    p.Entry.Name,
                    p.Entry.Kind,
                    p.Entry.Tier,
                    p.Entry.Scheme,
                    p.Entry.Formality,
                    p.Entry.Density,
                    p.Entry.Tagline,
                    p.Entry.Mood,
                    p.Entry.Tone,
                    p.Entry.MissingFonts,
                    SystemChineseFonts = chineseFonts,
                    SystemFontInventory = systemFonts.Summary,
                    Score = Math.Round(p.Score, 2)
                }));
                return Task.FromResult(Success(request, sb.ToString().TrimEnd(), json));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.style_search_failed"));
            }
        }
    }

    public sealed class PowerPointStyleGetTool : PowerPointStyleToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.StyleGet,
            DisplayName = "读取风格详情",
            Description = "按 slug 读取一个风格的详情：调色板、字体（含本地可用性）、版式样例清单、预览卡。"
                + "part=preview 只返回预览卡（生成候选封面用）；part=design 返回完整设计系统（用户选定后生成整套时用，较长）。"
                + "不要一次读取多个风格的 design。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"slug\":{\"type\":\"string\",\"description\":\"ppt_style_search 返回的 slug。\"},"
                + "\"part\":{\"type\":\"string\",\"enum\":[\"summary\",\"preview\",\"design\"],\"description\":\"默认 preview。\"}"
                + "},"
                + "\"required\":[\"slug\"],"
                + "\"additionalProperties\":false"
                + "}"
        };

        public PowerPointStyleGetTool() : this(null) { }

        public PowerPointStyleGetTool(Func<SlideAssetCatalogs> catalogs) : base(catalogs) { }

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var args = ParseArguments(request);
                var slug = ((string)args["slug"] ?? string.Empty).Trim();
                var part = ((string)args["part"] ?? "preview").Trim().ToLowerInvariant();
                if (slug.Length == 0)
                {
                    return Task.FromResult(Fail(request, "缺少 slug。", "ppt.style_invalid_argument"));
                }

                var detail = Catalogs.Styles.GetDetail(slug, part == "design");
                if (detail == null)
                {
                    return Task.FromResult(Fail(request, "风格库中没有 " + slug + "，请先用 ppt_style_search 检索。", "ppt.style_not_found"));
                }

                var e = detail.Entry;
                var sb = new StringBuilder();
                sb.Append("# ").Append(e.Name).Append(" (").Append(e.Slug).Append(") ").Append(e.Kind).Append('/').Append(e.Tier).AppendLine();
                if (!e.IsValid) sb.Append("⚠ 不可用：").AppendLine(e.InvalidReason);
                if (!string.IsNullOrEmpty(e.Tagline)) sb.AppendLine(e.Tagline);
                if (e.Palette.Count > 0) sb.Append("调色板：").AppendLine(string.Join("; ", e.Palette.Select(p => p.Key + " " + p.Value)));
                if (e.Fonts.Count > 0) sb.Append("字体：").AppendLine(Join(e.Fonts));
                if (e.MissingFonts.Count > 0) sb.Append("本地缺字体（将回退，建议替换为本地字体）：").AppendLine(Join(e.MissingFonts));
                if (e.Layouts.Count > 0) sb.Append("版式样例：").AppendLine(Join(e.Layouts));
                var reservedClasses = DeckAssembler.GetTemplateReservedClasses(e)
                    .Where(name => !string.Equals(name, "slide", StringComparison.Ordinal))
                    .ToList();
                if (e.Kind == StyleKinds.Template)
                {
                    sb.Append("模板结构类（仍作用于外层容器/.slide，自己的 HTML 不要使用这些类名）：")
                        .AppendLine(reservedClasses.Count == 0 ? "无" : Join(reservedClasses));
                    sb.AppendLine("模板示例页的内容样式（卡片、标签、标题等类）不会注入演示文稿：设计文档里的效果需要在自己的 CSS 中实现，类名用 cover-* / theme-* / sNN-* 前缀。");
                }

                if (part == "preview")
                {
                    AppendSection(sb, "预览卡", detail.PreviewMarkdown ?? detail.PresetMarkdown);
                }
                else if (part == "design")
                {
                    AppendSection(sb, "设计系统", detail.DesignMarkdown ?? detail.PresetMarkdown);
                    if (detail.DesignTruncated) sb.AppendLine("（设计文档过长，已截断。）");
                }

                var json = ToJson(new
                {
                    e.Slug,
                    e.Name,
                    e.Kind,
                    e.Tier,
                    e.IsValid,
                    e.InvalidReason,
                    e.Palette,
                    e.Fonts,
                    e.LocalFonts,
                    e.MissingFonts,
                    e.Layouts,
                    e.Thumbs,
                    detail.Typography,
                    TemplateReservedClasses = reservedClasses,
                    Origin = e.Origin
                });
                return Task.FromResult(Success(request, sb.ToString().TrimEnd(), json));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.style_get_failed"));
            }
        }

        private static void AppendSection(StringBuilder sb, string title, string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return;
            sb.AppendLine().Append("## ").AppendLine(title).AppendLine(markdown.Trim());
        }
    }
}
