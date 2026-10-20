using System.Collections.Generic;

namespace OfficeCopilot.Host.Abstractions
{
    public interface IPowerPointToolHost
    {
        string HostApplicationName { get; }

        PowerPointPresentationInfo GetActivePresentationInfo();

        PowerPointSelectionInfo GetSelectionInfo();

        List<PowerPointSlideSummary> ListSlides();

        /// <summary>读取单页详情。<paramref name="slideIndex"/> 为 1 起的页码；≤ 0 表示当前页。</summary>
        PowerPointSlideDetail ReadSlide(int slideIndex, bool includeNotes);

        DocumentBackupListResult ListBackups(int maxCount);

        DocumentRestoreResult RestoreBackup(DocumentRestoreRequest request);
    }

    public sealed class PowerPointPresentationInfo
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsSaved { get; set; }
        public int SlideCount { get; set; }
        public int CurrentSlideIndex { get; set; }
        public float SlideWidth { get; set; }
        public float SlideHeight { get; set; }

        /// <summary>没有任何页，或所有页只有空占位符（新建空白文稿的状态）。</summary>
        public bool IsBlank { get; set; }

        /// <summary>文稿由演示工作区构建时的 deckId（NIXER.DECK 标签）；否则为 null。</summary>
        public string DeckId { get; set; }
    }

    public sealed class PowerPointSelectionInfo
    {
        public string Text { get; set; }
        public int SlideIndex { get; set; }
        public string ShapeName { get; set; }
        public string SelectionType { get; set; }
        public List<string> ShapeNames { get; set; }
    }

    public sealed class PowerPointSlideSummary
    {
        public int Index { get; set; }
        public int SlideId { get; set; }
        public string LayoutName { get; set; }
        public string Title { get; set; }
        public int ShapeCount { get; set; }
        public bool Hidden { get; set; }
        public bool HasNotes { get; set; }

        /// <summary>工作区页 id（NIXER.SLIDE 标签，如 s03）；非工作区构建的页为 null。</summary>
        public string DeckSlideId { get; set; }
    }

    public sealed class PowerPointSlideDetail
    {
        public PowerPointSlideDetail()
        {
            Shapes = new List<PowerPointShapeInfo>();
        }

        public int Index { get; set; }
        public int SlideId { get; set; }
        public string LayoutName { get; set; }
        public float SlideWidth { get; set; }
        public float SlideHeight { get; set; }
        public string Notes { get; set; }
        public List<PowerPointShapeInfo> Shapes { get; set; }
    }

    public sealed class PowerPointShapeInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string PlaceholderType { get; set; }
        public float Left { get; set; }
        public float Top { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Rotation { get; set; }
        public int ZOrder { get; set; }
        public string Text { get; set; }
        public bool HasTable { get; set; }
        public bool HasChart { get; set; }
        public int TableRows { get; set; }
        public int TableColumns { get; set; }
    }
}
