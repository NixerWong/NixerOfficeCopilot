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
            DisplayName = "List Slides",
            Description = "Lists all slides in the current presentation, including slide number, SlideID, layout name, title, shape count, hidden status, and whether notes are present;"
                + " slides built from a deck workspace also include deckSlideId (for example, s03).",
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
            DisplayName = "Read Slide",
            Description = "Reads the shape tree for one slide, including each shape's ID, name, type, placeholder type, position and size in points, rotation, hierarchy, text, and table dimensions."
                + " slideIndex is a 1-based slide number; omit it to use the current slide. detail=summary returns only shape names, types, and the first 200 characters of text.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"slideIndex\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"1-based slide number; omit to use the current slide.\"},"
                + "\"detail\":{\"type\":\"string\",\"enum\":[\"summary\",\"full\"],\"description\":\"Default: full.\"},"
                + "\"includeNotes\":{\"type\":\"boolean\",\"description\":\"Whether to return speaker notes (default: true).\"}"
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
