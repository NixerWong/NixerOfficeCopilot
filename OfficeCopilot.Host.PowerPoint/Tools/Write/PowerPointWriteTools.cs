using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    /// <summary>原子写入工具（蓝图 P4）：每次调用一个撤销单元，修改前由运行时自动备份。</summary>
    public sealed class PowerPointEditTool : PowerPointToolBase
    {
        private readonly ToolDefinition _definition;
        private readonly Func<IPowerPointEditHost, JObject, PowerPointEditResult> _action;
        private readonly string _failureCode;

        private PowerPointEditTool(ToolDefinition definition, string failureCode, Func<IPowerPointEditHost, JObject, PowerPointEditResult> action)
        {
            _definition = definition;
            _failureCode = failureCode;
            _action = action;
        }

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var result = _action(GetEditHost(context), ParseArguments(request));
                return Task.FromResult(Success(request, result.Message, ToJson(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, _failureCode));
            }
        }

        internal static IPowerPointEditHost GetEditHost(ToolExecutionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.GetService<IPowerPointEditHost>()
                ?? context.GetService<IPowerPointToolHost>() as IPowerPointEditHost
                ?? throw new InvalidOperationException("IPowerPointEditHost not registered.");
        }

        private const string SlideIndexSchema = "\"slideIndex\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"1-based slide number; omit to use the current slide.\"}";
        private const string ShapeSchema = "\"shape\":{\"type\":\"string\",\"description\":\"Shape name or numeric ID (see ppt_read_slide).\"}";
        private const string ShapesSchema = "\"shapes\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"description\":\"List of shape names or numeric IDs (see ppt_read_slide).\"}";
        private const string FormatProperties =
            "\"fillColor\":{\"type\":\"string\",\"description\":\"#RRGGBB\"},"
            + "\"noFill\":{\"type\":\"boolean\"},"
            + "\"lineColor\":{\"type\":\"string\"},"
            + "\"lineWidth\":{\"type\":\"number\",\"description\":\"Points.\"},"
            + "\"noLine\":{\"type\":\"boolean\"},"
            + "\"fontName\":{\"type\":\"string\"},"
            + "\"fontSize\":{\"type\":\"number\"},"
            + "\"bold\":{\"type\":\"boolean\"},"
            + "\"italic\":{\"type\":\"boolean\"},"
            + "\"fontColor\":{\"type\":\"string\"},"
            + "\"align\":{\"type\":\"string\",\"enum\":[\"left\",\"center\",\"right\",\"justify\"]},"
            + "\"left\":{\"type\":\"number\"},\"top\":{\"type\":\"number\"},\"width\":{\"type\":\"number\"},\"height\":{\"type\":\"number\"},"
            + "\"rotation\":{\"type\":\"number\"}";

        private static ToolDefinition Define(string name, string displayName, string description, string properties, string required, string riskLevel = "medium", bool confirm = false)
        {
            return new ToolDefinition
            {
                Name = name,
                DisplayName = displayName,
                Description = description,
                SourceKind = ToolSourceKind.BuiltIn,
                SourceName = "PowerPoint",
                IsReadOnly = false,
                AffectsDocument = true,
                RequiresConfirmation = confirm,
                RiskLevel = riskLevel,
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" + properties + "}"
                    + (string.IsNullOrEmpty(required) ? string.Empty : ",\"required\":[" + required + "]")
                    + ",\"additionalProperties\":false}"
            };
        }

        private static int Slide(JObject args) => args.Value<int?>("slideIndex") ?? 0;

        private static List<string> Strings(JObject args, string name)
        {
            var token = args[name] as JArray;
            return token == null ? new List<string>() : token.Select(t => (string)t).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private static PowerPointShapeFormat Format(JObject args)
        {
            return new PowerPointShapeFormat
            {
                FillColor = args.Value<string>("fillColor"),
                NoFill = args.Value<bool?>("noFill"),
                LineColor = args.Value<string>("lineColor"),
                LineWidth = args.Value<float?>("lineWidth"),
                NoLine = args.Value<bool?>("noLine"),
                FontName = args.Value<string>("fontName"),
                FontSize = args.Value<float?>("fontSize"),
                Bold = args.Value<bool?>("bold"),
                Italic = args.Value<bool?>("italic"),
                FontColor = args.Value<string>("fontColor"),
                Align = args.Value<string>("align"),
                Left = args.Value<float?>("left"),
                Top = args.Value<float?>("top"),
                Width = args.Value<float?>("width"),
                Height = args.Value<float?>("height"),
                Rotation = args.Value<float?>("rotation")
            };
        }

        public static IAgentTool[] CreateAll()
        {
            return new IAgentTool[]
            {
                new PowerPointEditTool(
                    Define(PowerPointToolNames.SetShapeText, "Set Shape Text",
                        "Replaces all text in the specified shape while preserving first-paragraph formatting. Use \\n for line breaks.",
                        SlideIndexSchema + "," + ShapeSchema + ",\"text\":{\"type\":\"string\"}", "\"shape\",\"text\""),
                    "ppt.set_shape_text_failed",
                    (host, a) => host.SetShapeText(Slide(a), a.Value<string>("shape"), a.Value<string>("text"))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.FormatShapes, "Format Shapes",
                        "Sets fill, line, font (name, size, bold, italic, and color), paragraph alignment, position, size in points, and rotation for one or more shapes. Supply only the properties to change.",
                        SlideIndexSchema + "," + ShapesSchema + "," + FormatProperties, "\"shapes\""),
                    "ppt.format_shapes_failed",
                    (host, a) => host.FormatShapes(Slide(a), Strings(a, "shapes"), Format(a))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.ArrangeShapes, "Arrange Shapes",
                        "Aligns, distributes, or changes z-order. Alignment requires at least 2 shapes and distribution at least 3, unless relativeToSlide=true (relative to the full slide).",
                        SlideIndexSchema + "," + ShapesSchema
                            + ",\"action\":{\"type\":\"string\",\"enum\":[\"align_left\",\"align_center\",\"align_right\",\"align_top\",\"align_middle\",\"align_bottom\",\"distribute_horizontal\",\"distribute_vertical\",\"bring_to_front\",\"send_to_back\",\"bring_forward\",\"send_backward\"]}"
                            + ",\"relativeToSlide\":{\"type\":\"boolean\"}",
                        "\"shapes\",\"action\""),
                    "ppt.arrange_shapes_failed",
                    (host, a) => host.ArrangeShapes(Slide(a), Strings(a, "shapes"), a.Value<string>("action"), a.Value<bool?>("relativeToSlide") ?? false)),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.DeleteShapes, "Delete Shapes", "Deletes the specified shapes from the selected slide.",
                        SlideIndexSchema + "," + ShapesSchema, "\"shapes\""),
                    "ppt.delete_shapes_failed",
                    (host, a) => host.DeleteShapes(Slide(a), Strings(a, "shapes"))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.AddTextBox, "Add Text Box",
                        "Adds a text box to the specified slide. Position and size are in points (a 16:9 slide is 960×540). Font and alignment can also be set.",
                        SlideIndexSchema + ",\"text\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"}," + FormatProperties,
                        "\"text\",\"left\",\"top\",\"width\",\"height\""),
                    "ppt.add_text_box_failed",
                    (host, a) =>
                    {
                        var format = Format(a);
                        var spec = new PowerPointTextBoxSpec
                        {
                            Text = a.Value<string>("text"),
                            Name = a.Value<string>("name"),
                            Left = format.Left ?? 0f,
                            Top = format.Top ?? 0f,
                            Width = format.Width ?? 300f,
                            Height = format.Height ?? 50f,
                            Format = format
                        };
                        format.Left = format.Top = format.Width = format.Height = null;
                        return host.AddTextBox(Slide(a), spec);
                    }),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.InsertPicture, "Insert Picture",
                        "Inserts a local image file into the specified slide. If width/height are omitted, preserves the original dimensions.",
                        SlideIndexSchema + ",\"path\":{\"type\":\"string\",\"description\":\"Absolute local path.\"},\"name\":{\"type\":\"string\"},"
                            + "\"left\":{\"type\":\"number\"},\"top\":{\"type\":\"number\"},\"width\":{\"type\":\"number\"},\"height\":{\"type\":\"number\"}",
                        "\"path\""),
                    "ppt.insert_picture_failed",
                    (host, a) => host.InsertPicture(Slide(a), a.Value<string>("path"), a.Value<float?>("left"), a.Value<float?>("top"),
                        a.Value<float?>("width"), a.Value<float?>("height"), a.Value<string>("name"))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.SetNotes, "Set Slide Notes", "Replaces the speaker notes for the specified slide.", SlideIndexSchema + ",\"text\":{\"type\":\"string\"}", "\"text\"", "low"),
                    "ppt.set_notes_failed",
                    (host, a) => host.SetNotes(Slide(a), a.Value<string>("text"))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.AddSlides, "Add Slides",
                        "Inserts new slides at the specified position. layout is a master-layout name or index (omit it for a blank slide); when titles are provided, creates one slide per title and fills the title placeholder.",
                        "\"position\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Slide number to insert at; omit to append at the end.\"},"
                            + "\"layout\":{\"type\":\"string\"},\"count\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":50},"
                            + "\"titles\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":50}",
                        null, "low"),
                    "ppt.add_slides_failed",
                    (host, a) => host.AddSlides(a.Value<int?>("position") ?? 0, a.Value<string>("layout"), Strings(a, "titles"), a.Value<int?>("count") ?? 1)),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.DeleteSlides, "Delete Slides", "Deletes the specified slides; all slides cannot be deleted. User confirmation is required.",
                        "\"slideIndexes\":{\"type\":\"array\",\"items\":{\"type\":\"integer\",\"minimum\":1},\"minItems\":1}",
                        "\"slideIndexes\"", "high", true),
                    "ppt.delete_slides_failed",
                    (host, a) => host.DeleteSlides(((a["slideIndexes"] as JArray) ?? new JArray()).Select(t => (int)t).ToList())),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.MoveSlide, "Move Slide", "Moves a slide to a new position.",
                        "\"fromIndex\":{\"type\":\"integer\",\"minimum\":1},\"toIndex\":{\"type\":\"integer\",\"minimum\":1}",
                        "\"fromIndex\",\"toIndex\""),
                    "ppt.move_slide_failed",
                    (host, a) => host.MoveSlide(a.Value<int?>("fromIndex") ?? 0, a.Value<int?>("toIndex") ?? 0))
            };
        }
    }

    public sealed class PowerPointExtractContentTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.ExtractContent,
            DisplayName = "Extract Presentation Outline",
            Description = "Extracts slide titles, body text (including tables), and optionally notes from the entire presentation into a Markdown outline. Useful for reviewing the presentation before summarizing, rewriting, or translating it.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"includeNotes\":{\"type\":\"boolean\",\"description\":\"Default: false.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var includeNotes = ParseArguments(request).Value<bool?>("includeNotes") ?? false;
                var slides = PowerPointEditTool.GetEditHost(context).ExtractContent(includeNotes);
                var sb = new StringBuilder();
                foreach (var slide in slides)
                {
                    sb.Append("## ").Append(slide.Index).Append(". ").AppendLine(string.IsNullOrEmpty(slide.Title) ? "(无标题)" : slide.Title);
                    foreach (var text in slide.Texts)
                    {
                        sb.Append("- ").AppendLine(text.Replace("\n", "\n  "));
                    }

                    if (!string.IsNullOrEmpty(slide.Notes)) sb.Append("> 备注：").AppendLine(slide.Notes.Replace("\r", " "));
                    sb.AppendLine();
                }

                return Task.FromResult(Success(request, sb.ToString().TrimEnd(), ToJson(slides)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.extract_content_failed"));
            }
        }
    }

    public sealed class PowerPointExportSlideImageTool : PowerPointToolBase
    {
        private const int MaxAttachedImages = 4;

        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.ExportSlideImage,
            DisplayName = "Export Slide Image",
            Description = "Exports up to 8 specified slides as PNG files to a temporary directory; if omitted, exports the current slide. When the model supports image input, includes the first 4 screenshots for visual layout inspection.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                + "\"slideIndexes\":{\"type\":\"array\",\"items\":{\"type\":\"integer\",\"minimum\":1},\"maxItems\":8},"
                + "\"width\":{\"type\":\"integer\",\"minimum\":160,\"maximum\":3840,\"description\":\"Default: 1280.\"},"
                + "\"height\":{\"type\":\"integer\",\"minimum\":90,\"maximum\":2160,\"description\":\"Default: 720.\"}"
                + "},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var args = ParseArguments(request);
                var indexes = ((args["slideIndexes"] as JArray) ?? new JArray()).Select(t => (int)t).ToList();
                var images = PowerPointEditTool.GetEditHost(context).ExportSlideImages(
                    indexes, args.Value<int?>("width") ?? 0, args.Value<int?>("height") ?? 0);
                var text = "已导出 " + images.Count + " 页截图：" + Environment.NewLine
                    + string.Join(Environment.NewLine, images.Select(i => "- 第 " + i.SlideIndex + " 页：" + i.Path));
                var result = Success(request, text, ToJson(images.Select(i => new { i.SlideIndex, i.Path })));
                foreach (var image in images.Take(MaxAttachedImages))
                {
                    AttachImage(result, image.Png, "第 " + image.SlideIndex + " 页截图");
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.export_slide_image_failed"));
            }
        }
    }

    public sealed class PowerPointCheckFontsTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.CheckFonts,
            DisplayName = "Check Fonts",
            Description = "Lists fonts used in the presentation and identifies fonts that are not installed locally, cannot be embedded, or are already embedded. Use before sharing to determine whether ppt_save_with_fonts is needed.",
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
                var report = PowerPointEditTool.GetEditHost(context).CheckFonts();
                var sb = new StringBuilder();
                sb.AppendLine("共使用 " + report.Fonts.Count + " 种字体：");
                foreach (var font in report.Fonts)
                {
                    sb.Append("- ").Append(font.Name)
                        .Append(font.Installed ? "" : " | 本机未安装")
                        .Append(font.Embedded ? " | 已嵌入" : font.Embeddable ? " | 可嵌入" : " | 不可嵌入")
                        .AppendLine();
                }

                return Task.FromResult(Success(request, sb.ToString().TrimEnd(), ToJson(report)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.check_fonts_failed"));
            }
        }
    }

    public sealed class PowerPointSaveWithFontsTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.SaveWithFonts,
            DisplayName = "Save Copy with Embedded Fonts",
            Description = "Saves a copy of the current presentation as a .pptx with fonts embedded. Does not modify the original or overwrite existing files. If path is omitted, saves beside the original with a filename containing _embedded_fonts_timestamp.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = false,
            AffectsDocument = false,
            RiskLevel = "low",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"Optional absolute path for the target .pptx file.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var result = PowerPointEditTool.GetEditHost(context).SaveWithFonts(ParseArguments(request).Value<string>("path"));
                var text = "已另存嵌入字体副本：" + result.Path;
                if (result.NotEmbeddableFonts.Count > 0) text += Environment.NewLine + "以下字体不允许嵌入，接收方可能显示为替代字体：" + string.Join("、", result.NotEmbeddableFonts);
                if (result.MissingFonts.Count > 0) text += Environment.NewLine + "以下字体本机未安装，无法嵌入：" + string.Join("、", result.MissingFonts);
                return Task.FromResult(Success(request, text, ToJson(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.save_copy_with_fonts_failed"));
            }
        }
    }

    public sealed class PowerPointSaveCopyTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.SaveCopy,
            DisplayName = "Save Presentation Copy",
            Description = "Saves a copy of the current presentation as a .pptx without modifying the original or overwriting existing files. If embedFonts is omitted, uses the PowerPoint Slides setting.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = false,
            AffectsDocument = false,
            RiskLevel = "low",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"Optional absolute path for the target .pptx file.\"},\"embedFonts\":{\"type\":\"boolean\",\"description\":\"Whether to embed fonts; if omitted, uses the PowerPoint Slides setting.\"}},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var args = ParseArguments(request);
                var options = context.GetService<PowerPointSlidesOptions>();
                var embedFonts = args.Value<bool?>("embedFonts") ?? (options == null || options.EmbedFontsByDefault);
                var result = PowerPointEditTool.GetEditHost(context).SaveCopy(args.Value<string>("path"), embedFonts);
                var text = (embedFonts ? "已另存嵌入字体副本：" : "已另存演示文稿副本：") + result.Path;
                if (embedFonts && result.NotEmbeddableFonts.Count > 0) text += Environment.NewLine + "以下字体不允许嵌入，接收方可能显示为替代字体：" + string.Join("、", result.NotEmbeddableFonts);
                if (embedFonts && result.MissingFonts.Count > 0) text += Environment.NewLine + "以下字体本机未安装，无法嵌入：" + string.Join("、", result.MissingFonts);
                return Task.FromResult(Success(request, text, ToJson(result)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.save_copy_failed"));
            }
        }
    }

    public sealed class PowerPointExecuteVbaTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.ExecuteVba,
            DisplayName = "Execute VBA",
            Description = "Injects a temporary module into the current presentation and runs VBA, then removes the module. Use only when atomic tools cannot complete the task, such as applying formatting across every slide."
                + " Code must include a parameterless Sub or Function entry point (prefer Main); call NixerLog \"message\" to return logs. Requires user confirmation and trusted access to the VBA project object model.",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = false,
            AffectsDocument = true,
            RequiresConfirmation = true,
            RiskLevel = "high",
            TimeoutSeconds = 120,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                + "\"code\":{\"type\":\"string\",\"description\":\"Complete VBA module source code.\"},"
                + "\"entryPoint\":{\"type\":\"string\",\"description\":\"Entry procedure name; if omitted, prefers Main, then uses the first parameterless procedure.\"}"
                + "},\"required\":[\"code\"],\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                if (context == null) throw new ArgumentNullException(nameof(context));
                var host = context.GetService<IVbaExecutionHostAdapter>()
                    ?? context.GetService<IPowerPointToolHost>() as IVbaExecutionHostAdapter;
                if (host == null || !host.CanExecuteVba) return Task.FromResult(Fail(request, "当前环境不支持执行 VBA。", "ppt.vba_unavailable"));
                var availability = host.GetVbaExecutionAvailability(false);
                if (availability != null && !availability.CanExecute) return Task.FromResult(Fail(request, availability.Reason, "ppt.vba_unavailable"));

                var args = ParseArguments(request);
                var result = host.ExecuteVba(new VbaExecutionRequest
                {
                    Code = args.Value<string>("code"),
                    EntryPoint = args.Value<string>("entryPoint"),
                    RemoveModuleAfterRun = true
                });
                var text = result.Message ?? string.Empty;
                if (result.LogEntries != null && result.LogEntries.Count > 0)
                {
                    text += Environment.NewLine + "VBA 日志（NixerLog）：" + Environment.NewLine + string.Join(Environment.NewLine, result.LogEntries.Select(l => "  " + l));
                }

                return Task.FromResult(result.Success
                    ? Success(request, text, ToJson(result))
                    : Fail(request, text, "ppt.vba_failed"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.vba_failed"));
            }
        }
    }
}
