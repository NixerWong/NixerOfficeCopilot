using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Runtime.Verification
{
    internal static class NumberExtractor
    {
        private const int ContextRadius = 48;

        private static readonly Regex CellAddressPattern = new Regex(
            @"(?<![A-Za-z\p{N}_])\$?[A-Za-z]{1,3}\$?\d+(?::\$?[A-Za-z]{1,3}\$?\d+)?(?![A-Za-z\p{N}_])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex WorksheetRowRangePattern = new Regex(
            @"(?:(?:数据行|工作表行|(?:data\s+)?rows?)\s*[:：]?\s*\d+\s*[-\u2013\u2014]\s*\d+"
                + @"|第\s*\d+\s*[-\u2013\u2014]\s*\d+\s*行)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex ContextUsageNoticePattern = new Regex(
            @"\[\s*上下文用量已达\s*\d+\s*/\s*\d+\s*tokens\s*\(\s*\d+(?:\.\d+)?%\s*\)[^\]]*\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex DateOrTimePattern = new Regex(
            @"(?<!\d)(?:\d{4}[-/]\d{1,2}(?:[-/]\d{1,2})?"
                + @"|\d{4}年(?:\d{1,2}月(?:\d{1,2}日)?)?"
                + @"|\d{1,2}月(?:\d{1,2}日)?"
                + @"|\d{1,2}:\d{2}(?::\d{2})?)(?!\d)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ListNumberPattern = new Regex(
            @"(?m)^[ \t]*\d{1,3}[.)](?=[ \t]|\r?$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex MarkdownRankHeaderPattern = new Regex(
            @"^(?:排名|名次|位次|rank|ranking)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex MarkdownTableSeparatorCellPattern = new Regex(
            @"^:?-{3,}:?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IReadOnlyList<ExtractedNumber> Extract(
            string answer,
            IEnumerable<string> worksheetNames = null,
            int ignoreStandaloneIntegersThrough = 10)
        {
            var result = new List<ExtractedNumber>();
            if (string.IsNullOrWhiteSpace(answer))
            {
                return result;
            }

            var normalized = NumericValueParser.NormalizeDigitsAndPunctuation(answer);
            var candidates = normalized.ToCharArray();
            MaskMarkdownCode(candidates);
            MaskSequentialMarkdownRankColumns(candidates);
            MaskWorksheetNames(candidates, worksheetNames);
            MaskMatches(candidates, ContextUsageNoticePattern);
            MaskMatches(candidates, CellAddressPattern);
            MaskMatches(candidates, WorksheetRowRangePattern);
            MaskMatches(candidates, DateOrTimePattern);
            MaskMatches(candidates, ListNumberPattern);

            foreach (var number in NumericValueParser.Parse(new string(candidates)))
            {
                if (ShouldIgnore(number, ignoreStandaloneIntegersThrough))
                {
                    continue;
                }

                result.Add(new ExtractedNumber(
                    answer.Substring(number.StartIndex, number.Length),
                    number.Value,
                    number.DecimalPlaces,
                    number.IsPercentage,
                    number.StartIndex,
                    number.Length,
                    GetContext(answer, number.StartIndex, number.Length)));
            }

            return result;
        }

        private static void MaskSequentialMarkdownRankColumns(char[] text)
        {
            var lineStart = 0;
            while (lineStart < text.Length)
            {
                var lineEnd = FindLineEnd(text, lineStart);
                var lineContentEnd = TrimLineEnding(text, lineEnd);
                if (TryReadMarkdownTableCells(text, lineStart, lineContentEnd, out var headerCells))
                {
                    var rankColumn = headerCells.FindIndex(cell =>
                        MarkdownRankHeaderPattern.IsMatch(cell.Value));
                    var separatorStart = GetNextLineStart(text, lineEnd);
                    if (rankColumn >= 0
                        && separatorStart < text.Length
                        && TryReadMarkdownTableCells(
                            text,
                            separatorStart,
                            TrimLineEnding(text, FindLineEnd(text, separatorStart)),
                            out var separatorCells)
                        && IsMarkdownTableSeparator(separatorCells, headerCells.Count))
                    {
                        var rankCells = new List<MarkdownTableCell>();
                        var expectedRank = 1;
                        var hasSequentialRanks = true;
                        var rowStart = GetNextLineStart(text, FindLineEnd(text, separatorStart));
                        while (rowStart < text.Length)
                        {
                            var rowEnd = FindLineEnd(text, rowStart);
                            if (!TryReadMarkdownTableCells(
                                    text,
                                    rowStart,
                                    TrimLineEnding(text, rowEnd),
                                    out var rowCells))
                            {
                                break;
                            }

                            if (rowCells.Count != headerCells.Count)
                            {
                                hasSequentialRanks = false;
                                break;
                            }

                            var rankCell = rowCells[rankColumn];
                            if (!int.TryParse(
                                    rankCell.Value,
                                    NumberStyles.None,
                                    CultureInfo.InvariantCulture,
                                    out var rank))
                            {
                                if (!string.IsNullOrWhiteSpace(rankCell.Value)
                                    && rankCell.Value != "—"
                                    && rankCell.Value != "–"
                                    && rankCell.Value != "-")
                                {
                                    hasSequentialRanks = false;
                                }
                                break;
                            }

                            if (rank != expectedRank)
                            {
                                hasSequentialRanks = false;
                                break;
                            }

                            rankCells.Add(rankCell);
                            expectedRank++;
                            rowStart = GetNextLineStart(text, rowEnd);
                        }

                        if (hasSequentialRanks)
                        {
                            // These are positional labels; other numeric table cells remain evidence-checked.
                            foreach (var rankCell in rankCells)
                            {
                                MaskRange(text, rankCell.Start, rankCell.Length);
                            }
                        }
                    }
                }

                lineStart = GetNextLineStart(text, lineEnd);
            }
        }

        private static bool TryReadMarkdownTableCells(
            char[] text,
            int lineStart,
            int lineEnd,
            out List<MarkdownTableCell> cells)
        {
            cells = null;
            while (lineStart < lineEnd && char.IsWhiteSpace(text[lineStart]))
            {
                lineStart++;
            }

            while (lineEnd > lineStart && char.IsWhiteSpace(text[lineEnd - 1]))
            {
                lineEnd--;
            }

            if (lineEnd - lineStart < 3
                || text[lineStart] != '|'
                || text[lineEnd - 1] != '|')
            {
                return false;
            }

            cells = new List<MarkdownTableCell>();
            var cellStart = lineStart + 1;
            for (var i = cellStart; i < lineEnd; i++)
            {
                if (text[i] != '|' || IsEscapedMarkdownPipe(text, i))
                {
                    continue;
                }

                var start = cellStart;
                var end = i;
                while (start < end && char.IsWhiteSpace(text[start]))
                {
                    start++;
                }
                while (end > start && char.IsWhiteSpace(text[end - 1]))
                {
                    end--;
                }

                var value = new string(text, start, end - start)
                    .Trim('*', '_', '`')
                    .Trim();
                cells.Add(new MarkdownTableCell(start, end - start, value));
                cellStart = i + 1;
            }

            return cells.Count >= 2;
        }

        private static bool IsEscapedMarkdownPipe(char[] text, int index)
        {
            var backslashCount = 0;
            for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            {
                backslashCount++;
            }

            return backslashCount % 2 != 0;
        }

        private static bool IsMarkdownTableSeparator(
            IReadOnlyList<MarkdownTableCell> cells,
            int expectedCellCount)
        {
            return cells != null
                && cells.Count == expectedCellCount
                && cells.All(cell => MarkdownTableSeparatorCellPattern.IsMatch(cell.Value));
        }

        private static int FindLineEnd(char[] text, int lineStart)
        {
            var lineEnd = lineStart;
            while (lineEnd < text.Length && text[lineEnd] != '\n')
            {
                lineEnd++;
            }

            return lineEnd;
        }

        private static int TrimLineEnding(char[] text, int lineEnd)
        {
            return lineEnd > 0 && text[lineEnd - 1] == '\r'
                ? lineEnd - 1
                : lineEnd;
        }

        private static int GetNextLineStart(char[] text, int lineEnd)
        {
            return lineEnd < text.Length && text[lineEnd] == '\n'
                ? lineEnd + 1
                : text.Length;
        }

        private static bool ShouldIgnore(
            ParsedNumericValue number,
            int ignoreStandaloneIntegersThrough)
        {
            return ignoreStandaloneIntegersThrough >= 0
                && number.DecimalPlaces == 0
                && !number.IsPercentage
                && !number.HasMagnitudeUnit
                && number.Value >= 0m
                && number.Value <= ignoreStandaloneIntegersThrough;
        }

        private static string GetContext(string answer, int startIndex, int length)
        {
            var start = Math.Max(0, startIndex - ContextRadius);
            var end = Math.Min(answer.Length, startIndex + length + ContextRadius);
            return answer.Substring(start, end - start)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
        }

        private static void MaskWorksheetNames(char[] text, IEnumerable<string> worksheetNames)
        {
            if (worksheetNames == null)
            {
                return;
            }

            foreach (var worksheetName in worksheetNames)
            {
                if (string.IsNullOrWhiteSpace(worksheetName))
                {
                    continue;
                }

                var normalizedName = NumericValueParser.NormalizeDigitsAndPunctuation(worksheetName);
                var pattern = new Regex(
                    Regex.Escape(normalizedName),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                MaskMatches(text, pattern);
            }
        }

        private static void MaskMatches(char[] text, Regex pattern)
        {
            foreach (Match match in pattern.Matches(new string(text)))
            {
                MaskRange(text, match.Index, match.Length);
            }
        }

        private static void MaskMarkdownCode(char[] text)
        {
            var inFence = false;
            var fenceCharacter = '\0';
            var fenceLength = 0;
            var lineStart = 0;
            while (lineStart < text.Length)
            {
                var lineEnd = lineStart;
                while (lineEnd < text.Length && text[lineEnd] != '\n')
                {
                    lineEnd++;
                }

                var contentEnd = lineEnd;
                if (contentEnd > lineStart && text[contentEnd - 1] == '\r')
                {
                    contentEnd--;
                }
                var fullLineEnd = lineEnd < text.Length ? lineEnd + 1 : lineEnd;

                if (inFence)
                {
                    if (TryReadFence(text, lineStart, contentEnd, out var marker, out var length, out var afterRun)
                        && marker == fenceCharacter
                        && length >= fenceLength
                        && ContainsOnlyWhitespace(text, afterRun, contentEnd))
                    {
                        inFence = false;
                    }

                    MaskRange(text, lineStart, fullLineEnd - lineStart);
                }
                else if (TryReadFence(text, lineStart, contentEnd, out var marker, out var length, out _))
                {
                    inFence = true;
                    fenceCharacter = marker;
                    fenceLength = length;
                    MaskRange(text, lineStart, fullLineEnd - lineStart);
                }

                lineStart = fullLineEnd;
            }

            MaskInlineCode(text);
        }

        private static bool TryReadFence(
            char[] text,
            int start,
            int end,
            out char marker,
            out int length,
            out int afterRun)
        {
            var cursor = start;
            var indentation = 0;
            while (cursor < end && text[cursor] == ' ' && indentation < 4)
            {
                cursor++;
                indentation++;
            }

            marker = cursor < end ? text[cursor] : '\0';
            length = 0;
            afterRun = cursor;
            if (indentation > 3 || (marker != '`' && marker != '~'))
            {
                return false;
            }

            while (afterRun < end && text[afterRun] == marker)
            {
                afterRun++;
            }
            length = afterRun - cursor;
            return length >= 3;
        }

        private static bool ContainsOnlyWhitespace(char[] text, int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                if (text[i] != ' ' && text[i] != '\t')
                {
                    return false;
                }
            }

            return true;
        }

        private static void MaskInlineCode(char[] text)
        {
            var index = 0;
            while (index < text.Length)
            {
                if (text[index] != '`')
                {
                    index++;
                    continue;
                }

                var openingLength = CountRun(text, index, '`');
                if (IsEscaped(text, index))
                {
                    index += openingLength;
                    continue;
                }

                var close = FindMatchingBacktickRun(text, index + openingLength, openingLength);
                if (close < 0)
                {
                    index += openingLength;
                    continue;
                }

                var closingLength = CountRun(text, close, '`');
                MaskRange(text, index, close + closingLength - index);
                index = close + closingLength;
            }
        }

        private static int FindMatchingBacktickRun(char[] text, int start, int expectedLength)
        {
            var index = start;
            while (index < text.Length)
            {
                if (text[index] != '`')
                {
                    index++;
                    continue;
                }

                var runLength = CountRun(text, index, '`');
                if (runLength == expectedLength && !IsEscaped(text, index))
                {
                    return index;
                }
                index += runLength;
            }

            return -1;
        }

        private static int CountRun(char[] text, int start, char value)
        {
            var end = start;
            while (end < text.Length && text[end] == value)
            {
                end++;
            }
            return end - start;
        }

        private static bool IsEscaped(char[] text, int index)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
            {
                slashCount++;
            }
            return slashCount % 2 == 1;
        }

        private static void MaskRange(char[] text, int start, int length)
        {
            var end = Math.Min(text.Length, start + length);
            for (var i = Math.Max(0, start); i < end; i++)
            {
                if (text[i] != '\r' && text[i] != '\n')
                {
                    text[i] = ' ';
                }
            }
        }

        private sealed class MarkdownTableCell
        {
            public MarkdownTableCell(int start, int length, string value)
            {
                Start = start;
                Length = length;
                Value = value ?? string.Empty;
            }

            public int Start { get; private set; }

            public int Length { get; private set; }

            public string Value { get; private set; }
        }
    }

    internal sealed class ExtractedNumber
    {
        public ExtractedNumber(
            string rawText,
            decimal value,
            int decimalPlaces,
            bool isPercentage,
            int startIndex,
            int length,
            string context)
        {
            RawText = rawText ?? string.Empty;
            Value = value;
            DecimalPlaces = decimalPlaces;
            IsPercentage = isPercentage;
            StartIndex = startIndex;
            Length = length;
            Context = context ?? string.Empty;
        }

        public string RawText { get; private set; }

        public decimal Value { get; private set; }

        public int DecimalPlaces { get; private set; }

        public bool IsPercentage { get; private set; }

        public int StartIndex { get; private set; }

        public int Length { get; private set; }

        public string Context { get; private set; }
    }
}
