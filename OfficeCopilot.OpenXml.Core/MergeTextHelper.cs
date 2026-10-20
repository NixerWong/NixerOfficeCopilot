using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OfficeCopilot.OpenXml.Core
{
    /// <summary>
    /// Shared {{key}} placeholder replacement used by the format engines' merge
    /// implementations (docx/xlsx/pptx). Ported from officecli TemplateMerger.
    /// </summary>
    public static class MergeTextHelper
    {
        public static readonly Regex PlaceholderPattern =
            new Regex(@"\{\{\s*(\w[\w.\-\[\] ]*?)\s*\}\}", RegexOptions.Compiled);

        /// <summary>
        /// Replace every {{key}} in one left-to-right scan. Substituted values are
        /// never re-scanned, so a value containing {{other}} stays literal.
        /// Tracks used keys and the substitution count.
        /// </summary>
        public static string SinglePassReplace(
            string input,
            MergeData data,
            out bool matched,
            ISet<string> usedKeys,
            Action<int> bumpReplacements)
        {
            matched = false;
            if (string.IsNullOrEmpty(input) || !input.Contains("{{"))
            {
                return input;
            }

            int localCount = 0;
            var result = PlaceholderPattern.Replace(input, m =>
            {
                var key = m.Groups[1].Value;
                string replacement;
                if (data.TryGet(key, out replacement))
                {
                    usedKeys.Add(key);
                    localCount++;
                    return replacement;
                }
                return m.Value;
            });

            if (localCount > 0)
            {
                bumpReplacements(localCount);
            }
            matched = localCount > 0;
            return result;
        }
    }
}
