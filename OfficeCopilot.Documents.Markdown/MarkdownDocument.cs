using System;
using System.Collections.Generic;
using System.Text;

namespace OfficeCopilot.Documents.Markdown
{
    public sealed class MarkdownDocument
    {
        public MarkdownDocument()
        {
            Outline = string.Empty;
            Sections = new List<MarkdownSection>();
            Warnings = new List<string>();
        }

        public string SourceFormat { get; set; }

        public string Outline { get; set; }

        public IList<MarkdownSection> Sections { get; private set; }

        public IList<string> Warnings { get; private set; }

        public bool Truncated { get; set; }

        public string ToMarkdown()
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(Outline))
            {
                builder.AppendLine(Outline.Trim());
                builder.AppendLine();
            }

            foreach (var section in Sections)
            {
                if (section == null || string.IsNullOrWhiteSpace(section.Markdown))
                {
                    continue;
                }

                builder.AppendLine(section.Markdown.Trim());
                builder.AppendLine();
            }

            foreach (var warning in Warnings)
            {
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    builder.AppendLine("> Note: " + warning.Trim());
                }
            }

            return builder.ToString().Trim();
        }
    }

    public sealed class MarkdownSection
    {
        public int Index { get; set; }

        public string Title { get; set; }

        public string Markdown { get; set; }

        public bool NeedsVisual { get; set; }

        public string VisualReason { get; set; }
    }

    public sealed class MarkdownConversionOptions
    {
        public MarkdownConversionOptions()
        {
            MaxTotalChars = 200000;
            MaxSheetRows = 200;
            MaxSheetColumns = 40;
            MaxFileBytes = 50L * 1024 * 1024;
            MaxUncompressedPackageBytes = OpenXmlPackageSizeGuard.DefaultMaxUncompressedBytes;
            Timeout = TimeSpan.FromSeconds(60);
            IncludeFormulas = true;
            IncludeHiddenSheets = false;
        }

        public int MaxTotalChars { get; set; }

        public int MaxSheetRows { get; set; }

        public int MaxSheetColumns { get; set; }

        public string WorksheetName { get; set; }

        public string CellRange { get; set; }

        public long MaxFileBytes { get; set; }

        public long MaxUncompressedPackageBytes { get; set; }

        public TimeSpan Timeout { get; set; }

        public bool IncludeFormulas { get; set; }

        public bool IncludeHiddenSheets { get; set; }
    }
}
