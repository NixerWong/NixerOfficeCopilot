using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Panel.WebView.Services.References
{
    internal static class AttachmentFileTypes
    {
        private static readonly string[] TextExtensions =
        {
            ".txt", ".md", ".markdown", ".csv", ".json", ".xml", ".yaml", ".yml", ".log", ".ini", ".config"
        };

        private static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
        };

        private static readonly string[] PdfExtensions = { ".pdf" };
        private static readonly string[] SpreadsheetExtensions = { ".xlsx", ".xls", ".csv", ".xlsm" };
        private static readonly string[] WordExtensions = { ".docx", ".doc" };
        private static readonly string[] PowerPointExtensions = { ".pptx", ".ppt" };

        private static readonly HashSet<string> TextExtensionSet = CreateSet(TextExtensions);
        private static readonly HashSet<string> ImageExtensionSet = CreateSet(ImageExtensions);
        private static readonly HashSet<string> PdfExtensionSet = CreateSet(PdfExtensions);
        private static readonly HashSet<string> SpreadsheetExtensionSet = CreateSet(SpreadsheetExtensions);
        private static readonly HashSet<string> WordExtensionSet = CreateSet(WordExtensions);
        private static readonly HashSet<string> PowerPointExtensionSet = CreateSet(PowerPointExtensions);
        private static readonly HashSet<string> AllExtensionSet = CreateSet(TextExtensions
            .Concat(ImageExtensions)
            .Concat(PdfExtensions)
            .Concat(SpreadsheetExtensions)
            .Concat(WordExtensions)
            .Concat(PowerPointExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());

        public static bool IsSupported(string extension)
        {
            return AllExtensionSet.Contains(Normalize(extension));
        }

        public static bool IsText(string extension)
        {
            return TextExtensionSet.Contains(Normalize(extension));
        }

        public static bool IsImage(string extension)
        {
            return ImageExtensionSet.Contains(Normalize(extension));
        }

        public static bool IsPdf(string extension)
        {
            return PdfExtensionSet.Contains(Normalize(extension));
        }

        public static bool IsSpreadsheet(string extension)
        {
            return SpreadsheetExtensionSet.Contains(Normalize(extension));
        }

        public static bool IsWord(string extension)
        {
            return WordExtensionSet.Contains(Normalize(extension));
        }

        public static bool IsPowerPoint(string extension)
        {
            return PowerPointExtensionSet.Contains(Normalize(extension));
        }

        public static string BuildOpenFileDialogFilter()
        {
            var allExtensions = TextExtensions
                .Concat(ImageExtensions)
                .Concat(PdfExtensions)
                .Concat(SpreadsheetExtensions)
                .Concat(WordExtensions)
                .Concat(PowerPointExtensions)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return string.Join("|", new[]
            {
                "支持的文件", BuildPattern(allExtensions),
                "文本文件", BuildPattern(TextExtensions),
                "图片文件", BuildPattern(ImageExtensions),
                "PDF 文件", BuildPattern(PdfExtensions),
                "Excel 工作簿", BuildPattern(SpreadsheetExtensions),
                "Word 文档", BuildPattern(WordExtensions),
                "PowerPoint 演示文稿", BuildPattern(PowerPointExtensions)
            });
        }

        private static HashSet<string> CreateSet(IEnumerable<string> extensions)
        {
            return new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildPattern(IEnumerable<string> extensions)
        {
            return string.Join(";", extensions.Select(extension => "*" + extension));
        }

        private static string Normalize(string extension)
        {
            var value = (extension ?? string.Empty).Trim();
            return value.Length > 0 && value[0] != '.' ? "." + value : value;
        }
    }
}