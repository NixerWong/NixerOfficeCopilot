using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace OfficeCopilot.Documents.Markdown
{
    internal static class MarkdownWriter
    {
        public static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("|", "\\|")
                .Replace("\r\n", "<br>")
                .Replace("\r", "<br>")
                .Replace("\n", "<br>");
        }

        public static void AppendRow(StringBuilder builder, IEnumerable<string> cells, CancellationToken cancellationToken)
        {
            builder.Append('|');
            foreach (var cell in cells ?? Enumerable.Empty<string>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.Append(' ');
                builder.Append(Escape(cell));
                builder.Append(" |");
            }
            builder.AppendLine();
        }

        public static void AppendSeparator(
            StringBuilder builder,
            int cellCount,
            IEnumerable<int> rightAlignedColumns,
            CancellationToken cancellationToken)
        {
            var rightAligned = new HashSet<int>(rightAlignedColumns ?? Enumerable.Empty<int>());
            builder.Append('|');
            for (var column = 0; column < cellCount; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.Append(rightAligned.Contains(column) ? " ---: |" : " --- |");
            }
            builder.AppendLine();
        }
    }
}