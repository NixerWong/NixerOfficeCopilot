using OfficeCopilot.Host.Abstractions;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PowerPointInterop = Microsoft.Office.Interop.PowerPoint;

namespace OfficeCopilot.Host.PowerPoint
{
    public sealed partial class PowerPointHostAdapter
    {
        private const int ShapeTextLimit = 4000;
        private const int NotesTextLimit = 4000;
        private const int TitleLimit = 200;

        public List<PowerPointSlideSummary> ListSlides()
        {
            return _dispatcher.Invoke(() =>
            {
                var presentation = GetRequiredPresentation();
                var slides = presentation.Slides;
                var result = new List<PowerPointSlideSummary>(slides.Count);
                for (var i = 1; i <= slides.Count; i++)
                {
                    var slide = slides[i];
                    result.Add(new PowerPointSlideSummary
                    {
                        Index = slide.SlideIndex,
                        SlideId = slide.SlideID,
                        LayoutName = SafeString(() => slide.CustomLayout.Name),
                        Title = Truncate(GetSlideTitle(slide), TitleLimit),
                        ShapeCount = SafeInt(() => slide.Shapes.Count),
                        Hidden = SafeBool(() => ComLate.GetTri(slide.SlideShowTransition, "Hidden")),
                        HasNotes = GetNotesText(slide).Length > 0,
                        DeckSlideId = ComSlideBuilder.GetTag(slide.Tags, ComSlideBuilder.DeckSlideTag)
                    });
                }

                return result;
            });
        }

        public PowerPointSlideDetail ReadSlide(int slideIndex, bool includeNotes)
        {
            return _dispatcher.Invoke(() =>
            {
                var presentation = GetRequiredPresentation();
                var index = slideIndex > 0 ? slideIndex : GetCurrentSlideIndex();
                if (index <= 0)
                {
                    index = 1;
                }

                var count = presentation.Slides.Count;
                if (index > count)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(slideIndex),
                        "页码超出范围：" + index + "（共 " + count + " 页）。");
                }

                var slide = presentation.Slides[index];
                var detail = new PowerPointSlideDetail
                {
                    Index = slide.SlideIndex,
                    SlideId = slide.SlideID,
                    LayoutName = SafeString(() => slide.CustomLayout.Name),
                    SlideWidth = SafeFloat(() => presentation.PageSetup.SlideWidth),
                    SlideHeight = SafeFloat(() => presentation.PageSetup.SlideHeight),
                    Notes = includeNotes ? Truncate(GetNotesText(slide), NotesTextLimit) : null
                };

                var shapes = slide.Shapes;
                for (var i = 1; i <= shapes.Count; i++)
                {
                    detail.Shapes.Add(ReadShape(shapes[i]));
                }

                return detail;
            });
        }

        public IList<int> AppendSlides(IList<PowerPointDeckBuildSlide> inputs)
        {
            return PlaceSlides(inputs, PowerPointDeckBuildTarget.Append());
        }

        public IList<int> PlaceSlides(IList<PowerPointDeckBuildSlide> inputs, PowerPointDeckBuildTarget target)
        {
            if (inputs == null || inputs.Count == 0) throw new ArgumentException("构建页列表为空。", nameof(inputs));
            target = target ?? PowerPointDeckBuildTarget.Append();
            return _dispatcher.Invoke(() =>
            {
                PowerPointInterop.Presentation presentation;
                if (target.IsNew)
                {
                    if (_application == null) throw new InvalidOperationException("PowerPoint 未连接。");
                    presentation = (PowerPointInterop.Presentation)ComLate.Call(_application.Presentations, "Add", -1);
                    presentation.PageSetup.SlideWidth = 960f;
                    presentation.PageSetup.SlideHeight = 540f;
                }
                else
                {
                    presentation = GetRequiredPresentation();
                }

                if (target.IsReuseBlank)
                {
                    if (!IsBlankPresentation(presentation)) throw new InvalidOperationException("当前演示文稿已有内容，不能按空白文稿直接构建。");
                    if (!ComLate.GetTri(presentation, "ReadOnly"))
                    {
                        presentation.PageSetup.SlideWidth = 960f;
                        presentation.PageSetup.SlideHeight = 540f;
                    }
                }

                if (ComLate.GetTri(presentation, "ReadOnly")) throw new InvalidOperationException("当前演示文稿为只读状态，无法构建。");
                var width = SafeFloat(() => presentation.PageSetup.SlideWidth);
                var height = SafeFloat(() => presentation.PageSetup.SlideHeight);
                if (Math.Abs(width - 960f) > 2f || Math.Abs(height - 540f) > 2f)
                {
                    throw new InvalidOperationException("当前页面尺寸为 " + width + "×" + height + " pt；构建要求 960×540 pt（16:9）。");
                }

                var originalCount = presentation.Slides.Count;
                if (target.IsReplace && (target.ReplaceFrom < 1 || target.ReplaceTo < target.ReplaceFrom || target.ReplaceTo > originalCount))
                {
                    throw new ArgumentOutOfRangeException(nameof(target), "替换范围无效：第 " + target.ReplaceFrom + "–" + target.ReplaceTo + " 页（共 " + originalCount + " 页）。");
                }

                if (target.IsReplace && inputs.Count != target.ReplaceTo - target.ReplaceFrom + 1)
                {
                    throw new InvalidOperationException("工作区构建页数必须与替换范围页数一致，拒绝可能导致重复页面的构建。");
                }

                foreach (var input in inputs)
                {
                    if (input == null || input.Plan == null) throw new ArgumentException("构建页计划为空。", nameof(inputs));
                    if (Math.Abs(input.Plan.WidthPoints - width) > 2 || Math.Abs(input.Plan.HeightPoints - height) > 2)
                    {
                        throw new InvalidOperationException("第 " + (inputs.IndexOf(input) + 1) + " 页采集尺寸为 "
                            + input.Plan.WidthPoints + "×" + input.Plan.HeightPoints + " pt，与当前演示文稿不匹配。");
                    }
                }

                var insertAt = target.IsReplace ? target.ReplaceFrom : originalCount + 1;
                BeginUndo();
                var added = new List<PowerPointInterop.Slide>(inputs.Count);
                try
                {
                    foreach (var input in inputs)
                    {
                        var slide = presentation.Slides.Add(insertAt + added.Count, PowerPointInterop.PpSlideLayout.ppLayoutBlank);
                        added.Add(slide);
                        ComSlideBuilder.Append(slide, input);
                    }
                }
                catch
                {
                    foreach (var slide in added)
                    {
                        try { slide.Delete(); } catch (Exception) { }
                    }
                    throw;
                }

                if (target.IsReplace)
                {
                    var oldStart = target.ReplaceFrom + added.Count;
                    for (var i = target.ReplaceTo - target.ReplaceFrom; i >= 0; i--)
                    {
                        presentation.Slides[oldStart + i].Delete();
                    }
                }
                else if (target.IsReuseBlank)
                {
                    for (var i = originalCount; i >= 1; i--)
                    {
                        presentation.Slides[i].Delete();
                    }
                }

                var indexes = new List<int>(added.Count);
                foreach (var slide in added)
                {
                    indexes.Add(slide.SlideIndex);
                }

                ComSlideBuilder.SetTag(presentation.Tags, ComSlideBuilder.DeckTag, inputs[0].DeckId);
                return indexes;
            });
        }

        private static bool IsBlankPresentation(PowerPointInterop.Presentation presentation)
        {
            var slides = presentation.Slides;
            for (var i = 1; i <= slides.Count; i++)
            {
                var shapes = slides[i].Shapes;
                for (var j = 1; j <= shapes.Count; j++)
                {
                    var shape = shapes[j];
                    if (ComLate.GetInt(shape, "Type", -2) != MsoPlaceholder) return false;
                    if (ComLate.GetTri(shape, "HasTable") || ComLate.GetTri(shape, "HasChart")) return false;
                    // 填了图片等内容的占位符没有文本框。
                    if (!ComLate.GetTri(shape, "HasTextFrame")) return false;
                    if (ComLate.GetTri(shape.TextFrame, "HasText")) return false;
                }
            }

            return true;
        }

        public PptVerificationReport VerifySlides(IList<int> slideIndexes, IList<PowerPointDeckBuildSlide> inputs)
        {
            if (slideIndexes == null || inputs == null) throw new ArgumentNullException(slideIndexes == null ? nameof(slideIndexes) : nameof(inputs));
            return _dispatcher.Invoke(() => PptVerifier.Verify(GetRequiredPresentation(), slideIndexes, inputs));
        }

        private static PowerPointShapeInfo ReadShape(PowerPointInterop.Shape shape)
        {
            var typeCode = ComLate.GetInt(shape, "Type", -2);
            var info = new PowerPointShapeInfo
            {
                Id = SafeInt(() => shape.Id),
                Name = SafeString(() => shape.Name),
                Kind = DescribeShapeType(typeCode),
                Left = SafeFloat(() => shape.Left),
                Top = SafeFloat(() => shape.Top),
                Width = SafeFloat(() => shape.Width),
                Height = SafeFloat(() => shape.Height),
                Rotation = SafeFloat(() => shape.Rotation),
                ZOrder = SafeInt(() => shape.ZOrderPosition),
                HasTable = SafeBool(() => ComLate.GetTri(shape, "HasTable")),
                HasChart = SafeBool(() => ComLate.GetTri(shape, "HasChart")),
                PlaceholderType = string.Empty,
                Text = string.Empty
            };

            if (typeCode == MsoPlaceholder)
            {
                info.PlaceholderType = SafeString(() => DescribePlaceholder(shape.PlaceholderFormat.Type));
            }

            if (SafeBool(() => ComLate.GetTri(shape, "HasTextFrame"))
                && SafeBool(() => ComLate.GetTri(shape.TextFrame, "HasText")))
            {
                info.Text = Truncate(SafeString(() => shape.TextFrame.TextRange.Text), ShapeTextLimit);
            }

            if (info.HasTable)
            {
                info.TableRows = SafeInt(() => shape.Table.Rows.Count);
                info.TableColumns = SafeInt(() => shape.Table.Columns.Count);
            }

            return info;
        }

        private static string GetSlideTitle(PowerPointInterop.Slide slide)
        {
            try
            {
                if (!ComLate.GetTri(slide.Shapes, "HasTitle"))
                {
                    return string.Empty;
                }

                return (slide.Shapes.Title.TextFrame.TextRange.Text ?? string.Empty).Trim();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string GetNotesText(PowerPointInterop.Slide slide)
        {
            try
            {
                var body = ComSlideBuilder.FindNotesBody(slide);
                return body == null ? string.Empty : (body.TextFrame.TextRange.Text ?? string.Empty).Trim();
            }
            catch (COMException)
            {
                return string.Empty;
            }
        }

        private const int MsoPlaceholder = 14;

        /// <summary>MsoShapeType 数值 → 简短名称（晚绑定取得，避免 office.dll 依赖）。</summary>
        internal static string DescribeShapeType(int typeCode)
        {
            switch (typeCode)
            {
                case 1: return "autoshape";
                case 2: return "callout";
                case 3: return "chart";
                case 5: return "freeform";
                case 6: return "group";
                case 7: return "ole";
                case 9: return "line";
                case 10: return "linked_ole";
                case 11: return "linked_picture";
                case 12: return "ole_control";
                case 13: return "picture";
                case MsoPlaceholder: return "placeholder";
                case 15: return "wordart";
                case 16: return "media";
                case 17: return "textbox";
                case 19: return "table";
                case 20: return "canvas";
                case 22: return "ink";
                case 24: return "smartart";
                case 26: return "web_video";
                case 28: return "graphic";
                case 29: return "linked_graphic";
                case 30: return "3d_model";
                default: return "other";
            }
        }

        private static string DescribePlaceholder(PowerPointInterop.PpPlaceholderType type)
        {
            var name = type.ToString();
            return name.StartsWith("ppPlaceholder", StringComparison.Ordinal)
                ? name.Substring("ppPlaceholder".Length).ToLowerInvariant()
                : name;
        }
    }
}
