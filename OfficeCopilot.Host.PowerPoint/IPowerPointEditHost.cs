using System.Collections.Generic;

namespace OfficeCopilot.Host.PowerPoint
{
    /// <summary>
    /// PowerPoint 原子编辑能力（蓝图 P4）。实现方负责切到 UI 线程、开启撤销单元，
    /// 工具层只做参数解析与结果组织。页码均为 1 起；≤ 0 表示当前页。形状用名称或数字 Id 定位。
    /// </summary>
    public interface IPowerPointEditHost
    {
        PowerPointEditResult SetShapeText(int slideIndex, string shape, string text);

        PowerPointEditResult FormatShapes(int slideIndex, IList<string> shapes, PowerPointShapeFormat format);

        PowerPointEditResult ArrangeShapes(int slideIndex, IList<string> shapes, string action, bool relativeToSlide);

        PowerPointEditResult DeleteShapes(int slideIndex, IList<string> shapes);

        PowerPointEditResult AddTextBox(int slideIndex, PowerPointTextBoxSpec spec);

        PowerPointEditResult InsertPicture(int slideIndex, string path, float? left, float? top, float? width, float? height, string name);

        PowerPointEditResult SetNotes(int slideIndex, string text);

        PowerPointEditResult AddSlides(int position, string layout, IList<string> titles, int count);

        PowerPointEditResult DeleteSlides(IList<int> slideIndexes);

        PowerPointEditResult MoveSlide(int fromIndex, int toIndex);

        PowerPointFontReport CheckFonts();

        PowerPointSaveCopyResult SaveWithFonts(string targetPath);

        PowerPointSaveCopyResult SaveCopy(string targetPath, bool embedFonts);

        IList<PowerPointSlideImage> ExportSlideImages(IList<int> slideIndexes, int width, int height);

        IList<PowerPointSlideContent> ExtractContent(bool includeNotes);
    }

    public sealed class PowerPointEditResult
    {
        public string Message { get; set; }

        public List<int> SlideIndexes { get; set; } = new List<int>();

        public List<string> ShapeNames { get; set; } = new List<string>();
    }

    public sealed class PowerPointShapeFormat
    {
        public string FillColor { get; set; }
        public bool? NoFill { get; set; }
        public string LineColor { get; set; }
        public float? LineWidth { get; set; }
        public bool? NoLine { get; set; }
        public string FontName { get; set; }
        public float? FontSize { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public string FontColor { get; set; }
        public string Align { get; set; }
        public float? Left { get; set; }
        public float? Top { get; set; }
        public float? Width { get; set; }
        public float? Height { get; set; }
        public float? Rotation { get; set; }
    }

    public sealed class PowerPointTextBoxSpec
    {
        public string Text { get; set; }
        public float Left { get; set; }
        public float Top { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string Name { get; set; }
        public PowerPointShapeFormat Format { get; set; }
    }

    public sealed class PowerPointFontInfo
    {
        public string Name { get; set; }
        public bool Embedded { get; set; }
        public bool Embeddable { get; set; }
        public bool Installed { get; set; }
    }

    public sealed class PowerPointFontReport
    {
        public List<PowerPointFontInfo> Fonts { get; set; } = new List<PowerPointFontInfo>();
    }

    public sealed class PowerPointSaveCopyResult
    {
        public string Path { get; set; }
        public List<string> NotEmbeddableFonts { get; set; } = new List<string>();
        public List<string> MissingFonts { get; set; } = new List<string>();
    }

    public sealed class PowerPointSlideImage
    {
        public int SlideIndex { get; set; }
        public string Path { get; set; }
        public byte[] Png { get; set; }
    }

    public sealed class PowerPointSlideContent
    {
        public int Index { get; set; }
        public string Title { get; set; }
        public List<string> Texts { get; set; } = new List<string>();
        public string Notes { get; set; }
    }
}
