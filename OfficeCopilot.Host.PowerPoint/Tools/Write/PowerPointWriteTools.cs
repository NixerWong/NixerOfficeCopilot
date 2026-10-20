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

        private const string SlideIndexSchema = "\"slideIndex\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"1 起的页码；省略表示当前页。\"}";
        private const string ShapeSchema = "\"shape\":{\"type\":\"string\",\"description\":\"形状名称或数字 Id（见 ppt_read_slide）。\"}";
        private const string ShapesSchema = "\"shapes\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"description\":\"形状名称或数字 Id 列表（见 ppt_read_slide）。\"}";
        private const string FormatProperties =
            "\"fillColor\":{\"type\":\"string\",\"description\":\"#RRGGBB\"},"
            + "\"noFill\":{\"type\":\"boolean\"},"
            + "\"lineColor\":{\"type\":\"string\"},"
            + "\"lineWidth\":{\"type\":\"number\",\"description\":\"磅\"},"
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
                    Define(PowerPointToolNames.SetShapeText, "设置形状文字",
                        "替换指定形状的全部文字（保留首段格式）。换行用 \\n。",
                        SlideIndexSchema + "," + ShapeSchema + ",\"text\":{\"type\":\"string\"}", "\"shape\",\"text\""),
                    "ppt.set_shape_text_failed",
                    (host, a) => host.SetShapeText(Slide(a), a.Value<string>("shape"), a.Value<string>("text"))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.FormatShapes, "格式化形状",
                        "设置一个或多个形状的填充、边框、字体（名称/字号/粗斜体/颜色）、段落对齐、位置尺寸（磅）与旋转。只传需要修改的属性。",
                        SlideIndexSchema + "," + ShapesSchema + "," + FormatProperties, "\"shapes\""),
                    "ppt.format_shapes_failed",
                    (host, a) => host.FormatShapes(Slide(a), Strings(a, "shapes"), Format(a))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.ArrangeShapes, "排列形状",
                        "对齐、分布或调整层级。对齐至少 2 个形状、分布至少 3 个，除非 relativeToSlide=true（相对整页）。",
                        SlideIndexSchema + "," + ShapesSchema
                            + ",\"action\":{\"type\":\"string\",\"enum\":[\"align_left\",\"align_center\",\"align_right\",\"align_top\",\"align_middle\",\"align_bottom\",\"distribute_horizontal\",\"distribute_vertical\",\"bring_to_front\",\"send_to_back\",\"bring_forward\",\"send_backward\"]}"
                            + ",\"relativeToSlide\":{\"type\":\"boolean\"}",
                        "\"shapes\",\"action\""),
                    "ppt.arrange_shapes_failed",
                    (host, a) => host.ArrangeShapes(Slide(a), Strings(a, "shapes"), a.Value<string>("action"), a.Value<bool?>("relativeToSlide") ?? false)),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.DeleteShapes, "删除形状", "删除指定页上的形状。",
                        SlideIndexSchema + "," + ShapesSchema, "\"shapes\""),
                    "ppt.delete_shapes_failed",
                    (host, a) => host.DeleteShapes(Slide(a), Strings(a, "shapes"))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.AddTextBox, "添加文本框",
                        "在指定页添加文本框，位置尺寸单位为磅（16:9 页面为 960×540）。可同时设置字体与对齐。",
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
                    Define(PowerPointToolNames.InsertPicture, "插入图片",
                        "把本机图片文件插入指定页。省略 width/height 时保持原始尺寸。",
                        SlideIndexSchema + ",\"path\":{\"type\":\"string\",\"description\":\"本机绝对路径\"},\"name\":{\"type\":\"string\"},"
                            + "\"left\":{\"type\":\"number\"},\"top\":{\"type\":\"number\"},\"width\":{\"type\":\"number\"},\"height\":{\"type\":\"number\"}",
                        "\"path\""),
                    "ppt.insert_picture_failed",
                    (host, a) => host.InsertPicture(Slide(a), a.Value<string>("path"), a.Value<float?>("left"), a.Value<float?>("top"),
                        a.Value<float?>("width"), a.Value<float?>("height"), a.Value<string>("name"))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.SetNotes, "设置备注", "替换指定页的演讲者备注。", SlideIndexSchema + ",\"text\":{\"type\":\"string\"}", "\"text\"", "low"),
                    "ppt.set_notes_failed",
                    (host, a) => host.SetNotes(Slide(a), a.Value<string>("text"))),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.AddSlides, "新增幻灯片",
                        "在指定位置插入新页。layout 为母版版式名称或序号（省略为空白页）；titles 给出时按标题数新增并填写标题占位符。",
                        "\"position\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"插入后的页码；省略为末尾。\"},"
                            + "\"layout\":{\"type\":\"string\"},\"count\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":50},"
                            + "\"titles\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":50}",
                        null, "low"),
                    "ppt.add_slides_failed",
                    (host, a) => host.AddSlides(a.Value<int?>("position") ?? 0, a.Value<string>("layout"), Strings(a, "titles"), a.Value<int?>("count") ?? 1)),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.DeleteSlides, "删除幻灯片", "删除指定页（不可删除全部页）。需要用户确认。",
                        "\"slideIndexes\":{\"type\":\"array\",\"items\":{\"type\":\"integer\",\"minimum\":1},\"minItems\":1}",
                        "\"slideIndexes\"", "high", true),
                    "ppt.delete_slides_failed",
                    (host, a) => host.DeleteSlides(((a["slideIndexes"] as JArray) ?? new JArray()).Select(t => (int)t).ToList())),
                new PowerPointEditTool(
                    Define(PowerPointToolNames.MoveSlide, "移动幻灯片", "把一页移动到新位置。",
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
            DisplayName = "提取全文大纲",
            Description = "按页提取整份演示文稿的标题、正文（含表格）与可选备注，输出 Markdown 大纲。适合总结、改写或翻译前先通读。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"includeNotes\":{\"type\":\"boolean\",\"description\":\"默认 false。\"}},\"additionalProperties\":false}"
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
            DisplayName = "导出幻灯片截图",
            Description = "把指定页（最多 8 页，省略为当前页）导出为 PNG 到临时目录；模型支持图像输入时附带前 4 张截图，用于视觉检查排版。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                + "\"slideIndexes\":{\"type\":\"array\",\"items\":{\"type\":\"integer\",\"minimum\":1},\"maxItems\":8},"
                + "\"width\":{\"type\":\"integer\",\"minimum\":160,\"maximum\":3840,\"description\":\"默认 1280。\"},"
                + "\"height\":{\"type\":\"integer\",\"minimum\":90,\"maximum\":2160,\"description\":\"默认 720。\"}"
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
            DisplayName = "检查字体",
            Description = "列出演示文稿使用的字体，标出本机未安装、不可嵌入与已嵌入的字体。交付他人前用于判断是否需要 ppt_save_with_fonts。",
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
            DisplayName = "另存嵌入字体副本",
            Description = "把当前演示文稿另存为嵌入字体的 .pptx 副本（不修改原文件、不覆盖已有文件）。省略 path 时保存到原文件旁，文件名带「_嵌入字体_时间戳」。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = false,
            AffectsDocument = false,
            RiskLevel = "low",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"可选，目标 .pptx 绝对路径。\"}},\"additionalProperties\":false}"
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
            DisplayName = "另存演示文稿副本",
            Description = "另存当前演示文稿为 .pptx 副本，不修改原文件且不覆盖已有文件。embedFonts 省略时采用 PowerPoint Slides 设置。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = false,
            AffectsDocument = false,
            RiskLevel = "low",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"可选，目标 .pptx 绝对路径。\"},\"embedFonts\":{\"type\":\"boolean\",\"description\":\"是否嵌入字体；省略时使用 PowerPoint Slides 设置。\"}},\"additionalProperties\":false}"
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
            DisplayName = "执行 VBA",
            Description = "在当前演示文稿注入临时模块并执行 VBA（执行后移除模块）。仅在原子工具无法完成时使用，例如批量遍历全部页修改格式。"
                + "代码须包含无参数的 Sub/Function 入口（优先 Main）；可调用 NixerLog \"消息\" 回传日志。需要用户确认，且要求已信任 VBA 工程对象模型访问。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = false,
            AffectsDocument = true,
            RequiresConfirmation = true,
            RiskLevel = "high",
            TimeoutSeconds = 120,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                + "\"code\":{\"type\":\"string\",\"description\":\"完整 VBA 模块代码。\"},"
                + "\"entryPoint\":{\"type\":\"string\",\"description\":\"入口过程名；省略时优先 Main，否则取第一个无参过程。\"}"
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
