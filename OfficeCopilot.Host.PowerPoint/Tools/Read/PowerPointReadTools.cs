using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    public sealed class PowerPointListSlidesTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.ListSlides,
            DisplayName = "列出幻灯片",
            Description = "列出当前演示文稿的全部幻灯片：页码、SlideID、版式名、标题、形状数、是否隐藏、是否有备注；"
                + "由演示工作区构建的页还带 deckSlideId（如 s03）。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var slides = GetHost(context).ListSlides();
                var sb = new StringBuilder();
                sb.AppendLine("共 " + slides.Count + " 页");
                foreach (var s in slides)
                {
                    sb.Append('#').Append(s.Index)
                        .Append(" [id=").Append(s.SlideId).Append("] ")
                        .Append(string.IsNullOrEmpty(s.Title) ? "(无标题)" : s.Title)
                        .Append(" | 版式：").Append(s.LayoutName)
                        .Append(" | 形状 ").Append(s.ShapeCount);
                    if (s.Hidden) sb.Append(" | 隐藏");
                    if (s.HasNotes) sb.Append(" | 有备注");
                    if (!string.IsNullOrEmpty(s.DeckSlideId)) sb.Append(" | 工作区页 ").Append(s.DeckSlideId);
                    sb.AppendLine();
                }

                return Task.FromResult(Success(request, sb.ToString().TrimEnd(), ToJson(slides)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.list_slides_failed"));
            }
        }
    }

    public sealed class PowerPointReadSlideTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.ReadSlide,
            DisplayName = "读取幻灯片",
            Description = "读取单页的形状树：每个形状的 id、名称、类型、占位符类型、位置尺寸（磅）、旋转、层级、文本、表格行列。"
                + "slideIndex 为 1 起的页码，省略表示当前页。detail=summary 只返回形状名、类型与文本前 200 字。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"slideIndex\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"1 起的页码；省略表示当前页。\"},"
                + "\"detail\":{\"type\":\"string\",\"enum\":[\"summary\",\"full\"],\"description\":\"默认 full。\"},"
                + "\"includeNotes\":{\"type\":\"boolean\",\"description\":\"是否返回演讲者备注，默认 true。\"}"
                + "},"
                + "\"additionalProperties\":false"
                + "}"
        };

        private const int SummaryTextLimit = 200;

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var args = ParseArguments(request);
                var slideIndex = args.Value<int?>("slideIndex") ?? 0;
                var summary = string.Equals(args.Value<string>("detail"), "summary", StringComparison.OrdinalIgnoreCase);
                var includeNotes = args.Value<bool?>("includeNotes") ?? true;

                var slide = GetHost(context).ReadSlide(slideIndex, includeNotes);

                var sb = new StringBuilder();
                sb.AppendLine("第 " + slide.Index + " 页 [id=" + slide.SlideId + "] 版式：" + slide.LayoutName
                    + "，尺寸 " + slide.SlideWidth + " × " + slide.SlideHeight + " pt，形状 " + slide.Shapes.Count + " 个");
                foreach (var shape in slide.Shapes)
                {
                    sb.Append("- [").Append(shape.Id).Append("] ").Append(shape.Name)
                        .Append(" (").Append(shape.Kind);
                    if (!string.IsNullOrEmpty(shape.PlaceholderType)) sb.Append(':').Append(shape.PlaceholderType);
                    sb.Append(')');
                    if (!summary)
                    {
                        sb.Append(" @").Append(F(shape.Left)).Append(',').Append(F(shape.Top))
                            .Append(' ').Append(F(shape.Width)).Append('×').Append(F(shape.Height));
                        if (Math.Abs(shape.Rotation) > 0.01f) sb.Append(" rot=").Append(F(shape.Rotation));
                        if (shape.HasTable) sb.Append(" 表格 ").Append(shape.TableRows).Append('×').Append(shape.TableColumns);
                        if (shape.HasChart) sb.Append(" 图表");
                    }

                    if (!string.IsNullOrEmpty(shape.Text))
                    {
                        var text = summary ? PowerPointHostAdapter.Truncate(shape.Text, SummaryTextLimit) : shape.Text;
                        sb.Append(" 文本：").Append(text.Replace("\r", "\\n").Replace("\v", "\\n"));
                    }

                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(slide.Notes))
                {
                    sb.AppendLine("备注：" + slide.Notes);
                }

                if (summary)
                {
                    foreach (var shape in slide.Shapes)
                    {
                        shape.Text = PowerPointHostAdapter.Truncate(shape.Text, SummaryTextLimit);
                    }
                }

                return Task.FromResult(Success(request, sb.ToString().TrimEnd(), ToJson(slide)));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Task.FromResult(Fail(request, ex.Message, "ppt.slide_index_out_of_range"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.read_slide_failed"));
            }
        }

        private static string F(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
