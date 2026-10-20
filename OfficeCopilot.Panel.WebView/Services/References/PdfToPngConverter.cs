using DevExpress.Pdf;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Panel.WebView.Services.References
{
    internal sealed class PdfPageText
    {
        public int PageNumber { get; set; }

        public string Text { get; set; }
    }

    internal static class PdfToPngConverter
    {
        public const int VisualTextThreshold = 50;

        /// <summary>
        /// 获取 PDF 文件的总页数。
        /// </summary>
        public static int GetPageCount(string pdfPath, ILogger logger = null)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                return 0;
            }

            try
            {
                using (var processor = new PdfDocumentProcessor())
                {
                    processor.LoadDocument(pdfPath);
                    var pageCount = processor.Document.Pages.Count;
                    logger?.LogInformation("[PdfToPng] PDF页数: {PageCount}, 文件: {PdfPath}", pageCount, pdfPath);
                    return pageCount;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[PdfToPng] 获取PDF页数失败: {PdfPath}", pdfPath);
                return 0;
            }
        }

        private const int MaxEdgeLength = 1024;
        private const int MaxCachedPageTextFiles = 4;
        private static readonly object PageTextCacheLock = new object();
        private static readonly LinkedList<KeyValuePair<string, List<PdfPageText>>> PageTextCache = new LinkedList<KeyValuePair<string, List<PdfPageText>>>();

        /// <summary>
        /// Same as <see cref="ExtractPageTexts"/>, but reuses the result for an unchanged file so the
        /// visual-page scan and the text preprocessor do not parse the same attachment twice.
        /// </summary>
        public static List<PdfPageText> ExtractPageTextsCached(string pdfPath, ILogger logger = null)
        {
            string key = null;
            try
            {
                var info = new FileInfo(pdfPath);
                if (info.Exists)
                {
                    key = info.FullName.ToUpperInvariant() + "|" + info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "|" + info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                key = null;
            }

            if (key != null)
            {
                lock (PageTextCacheLock)
                {
                    for (var node = PageTextCache.First; node != null; node = node.Next)
                    {
                        if (string.Equals(node.Value.Key, key, StringComparison.Ordinal))
                        {
                            PageTextCache.Remove(node);
                            PageTextCache.AddFirst(node);
                            return ClonePageTexts(node.Value.Value);
                        }
                    }
                }
            }

            var result = ExtractPageTexts(pdfPath, logger);
            if (key != null && result.Count > 0)
            {
                lock (PageTextCacheLock)
                {
                    PageTextCache.AddFirst(new KeyValuePair<string, List<PdfPageText>>(key, ClonePageTexts(result)));
                    while (PageTextCache.Count > MaxCachedPageTextFiles)
                    {
                        PageTextCache.RemoveLast();
                    }
                }
            }
            return result;
        }

        private static List<PdfPageText> ClonePageTexts(IEnumerable<PdfPageText> pages)
        {
            return pages.Select(page => new PdfPageText { PageNumber = page.PageNumber, Text = page.Text }).ToList();
        }

        public static List<PdfPageText> ExtractPageTexts(string pdfPath, ILogger logger = null)
        {
            var result = new List<PdfPageText>();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                return result;
            }

            try
            {
                using (var processor = new PdfDocumentProcessor())
                {
                    processor.LoadDocument(pdfPath);
                    for (var pageNumber = 1; pageNumber <= processor.Document.Pages.Count; pageNumber++)
                    {
                        result.Add(new PdfPageText
                        {
                            PageNumber = pageNumber,
                            Text = processor.GetPageText(pageNumber) ?? string.Empty
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[PdfText] 提取 PDF 页面文本失败: {PdfPath}", pdfPath);
                result.Clear();
            }

            return result;
        }

        public static int GetVisualFallbackPageCount(string pdfPath)
        {
            return ExtractPageTexts(pdfPath)
                .Count(page => string.IsNullOrWhiteSpace(page.Text) || page.Text.Trim().Length < VisualTextThreshold);
        }

        public static List<string> ConvertPdfPagesToPng(string sourcePath, IEnumerable<int> pageNumbers, string outputDir, ILogger logger = null)
        {
            var result = new List<string>();
            var pages = pageNumbers == null ? new List<int>() : pageNumbers.Distinct().Where(page => page > 0).ToList();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath) || pages.Count == 0)
            {
                return result;
            }

            if (string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.GetDirectoryName(sourcePath);
            }

            Directory.CreateDirectory(outputDir);
            try
            {
                using (var processor = new PdfDocumentProcessor())
                {
                    processor.LoadDocument(sourcePath);
                    foreach (var pageNumber in pages)
                    {
                        if (pageNumber > processor.Document.Pages.Count)
                        {
                            continue;
                        }

                        using (var bitmap = processor.CreateBitmap(pageNumber, MaxEdgeLength))
                        {
                            var outputPath = Path.Combine(outputDir, "page_" + pageNumber.ToString() + ".png");
                            bitmap.Save(outputPath, ImageFormat.Png);
                            result.Add(outputPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[PdfToPng] 低文本页面渲染失败: {SourcePath}", sourcePath);
                foreach (var path in result)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch
                    {
                    }
                }
                result.Clear();
            }

            return result;
        }

        /// <summary>
        /// 将 PDF 的每一页转换为 PNG 图像（最大边长 1024px）。
        /// 返回生成的 PNG 文件路径列表。
        /// </summary>
        public static List<string> ConvertPdfToPng(string sourcePath, string outputDir, ILogger logger = null)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                logger?.LogWarning("[PdfToPng] 源文件无效或不存在: {SourcePath}", sourcePath);
                return result;
            }

            if (string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.GetDirectoryName(sourcePath);
            }

            Directory.CreateDirectory(outputDir);
            logger?.LogInformation("[PdfToPng] 开始转换, 源文件: {SourcePath}, 输出目录: {OutputDir}, 最大边长: {MaxEdge}", sourcePath, outputDir, MaxEdgeLength);

            try
            {
                using (var processor = new PdfDocumentProcessor())
                {
                    processor.LoadDocument(sourcePath);
                    var pageCount = processor.Document.Pages.Count;

                    for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
                    {
                        // CreateBitmap 的 pageNumber 参数从 1 开始
                        using (var bitmap = processor.CreateBitmap(pageIndex + 1, MaxEdgeLength))
                        {
                            var outputPath = Path.Combine(outputDir, "page_" + (pageIndex + 1).ToString() + ".png");
                            bitmap.Save(outputPath, ImageFormat.Png);
                            result.Add(outputPath);
                            logger?.LogInformation("[PdfToPng] 第{Page}页转换完成: {OutputPath}, 尺寸: {Width}x{Height}", pageIndex + 1, outputPath, bitmap.Width, bitmap.Height);
                        }
                    }
                }

                logger?.LogInformation("[PdfToPng] 全部转换完成, 共{Count}页, 输出目录: {OutputDir}", result.Count, outputDir);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[PdfToPng] 转换失败, 源文件: {SourcePath}", sourcePath);

                // 清理已生成的文件
                foreach (var path in result)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch
                    {
                    }
                }

                result.Clear();
            }

            return result;
        }
    }
}
