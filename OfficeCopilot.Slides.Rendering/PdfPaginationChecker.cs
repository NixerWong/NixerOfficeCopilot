using System;
using System.Collections.Generic;
using System.IO;
using DevExpress.Pdf;

namespace OfficeCopilot.Slides.Rendering
{
    public sealed class PdfPaginationReport
    {
        public int PageCount { get; set; }

        public IList<string> OverflowText { get; set; } = new List<string>();
    }

    /// <summary>解析 WebView2 打印结果，检查页数并提取超出预期页数的文字。</summary>
    public static class PdfPaginationChecker
    {
        public static PdfPaginationReport Inspect(byte[] pdf, int expectedSlides)
        {
            if (pdf == null || pdf.Length == 0) throw new ArgumentException("PDF 数据为空。", nameof(pdf));
            if (expectedSlides < 0) throw new ArgumentOutOfRangeException(nameof(expectedSlides));

            using (var processor = new PdfDocumentProcessor())
            using (var stream = new MemoryStream(pdf, false))
            {
                processor.LoadDocument(stream);
                var count = processor.Document.Pages.Count;
                var overflow = new List<string>();
                for (var page = expectedSlides + 1; page <= count; page++)
                {
                    var text = processor.GetPageText(page);
                    if (!string.IsNullOrWhiteSpace(text)) overflow.Add(text.Trim());
                }

                return new PdfPaginationReport { PageCount = count, OverflowText = overflow };
            }
        }
    }
}