using System;
using System.Collections.Generic;

namespace OfficeCopilot.Host.PowerPoint
{
    internal static class CjkTextRanges
    {
        internal struct CjkTextRange
        {
            public int Start { get; private set; }

            public int Length { get; private set; }

            public CjkTextRange(int start, int length)
            {
                Start = start;
                Length = length;
            }
        }

        internal static IEnumerable<CjkTextRange> Find(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            var runStart = -1;
            for (var index = 0; index < text.Length;)
            {
                var isSurrogatePair = Char.IsSurrogatePair(text, index);
                var codePoint = isSurrogatePair ? Char.ConvertToUtf32(text, index) : text[index];
                var codeUnitLength = isSurrogatePair ? 2 : 1;
                if (IsCjk(codePoint))
                {
                    if (runStart < 0) runStart = index;
                }
                else if (runStart >= 0)
                {
                    yield return new CjkTextRange(runStart, index - runStart);
                    runStart = -1;
                }

                index += codeUnitLength;
            }

            if (runStart >= 0) yield return new CjkTextRange(runStart, text.Length - runStart);
        }

        private static bool IsCjk(int codePoint)
        {
            return (codePoint >= 0x2E80 && codePoint <= 0x2FFF)
                || (codePoint >= 0x3000 && codePoint <= 0x30FF)
                || (codePoint >= 0x3100 && codePoint <= 0x312F)
                || (codePoint >= 0x31A0 && codePoint <= 0x31FF)
                || (codePoint >= 0x3400 && codePoint <= 0x4DBF)
                || (codePoint >= 0x4E00 && codePoint <= 0x9FFF)
                || (codePoint >= 0xA960 && codePoint <= 0xA97F)
                || (codePoint >= 0xAC00 && codePoint <= 0xD7FF)
                || (codePoint >= 0xF900 && codePoint <= 0xFAFF)
                || (codePoint >= 0xFE30 && codePoint <= 0xFE4F)
                || (codePoint >= 0xFF00 && codePoint <= 0xFFEF)
                || (codePoint >= 0x20000 && codePoint <= 0x2FA1F)
                || (codePoint >= 0x30000 && codePoint <= 0x323AF);
        }
    }
}
