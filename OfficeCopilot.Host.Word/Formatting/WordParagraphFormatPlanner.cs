using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Word.Formatting
{
    /// <summary>Paragraph state captured once from the document before a batch format.</summary>
    public sealed class WordParagraphSnapshot
    {
        public int Index { get; set; }
        public string Text { get; set; }
        public bool InTable { get; set; }
    }

    public sealed class WordPlannedParagraph
    {
        public int Index { get; set; }
        public string Role { get; set; }
    }

    public sealed class WordParagraphFormatPlan
    {
        public WordParagraphFormatPlan()
        {
            Items = new List<WordPlannedParagraph>();
            Skipped = new List<WordParagraphFormatSkip>();
        }

        public IList<WordPlannedParagraph> Items { get; private set; }
        public IList<WordParagraphFormatSkip> Skipped { get; private set; }
    }

    /// <summary>
    /// COM-free planning for word_format_paragraphs: validates the request, resolves built-in styles
    /// and verifies each paragraph's leading text so index drift never formats the wrong paragraph.
    /// </summary>
    public static class WordParagraphFormatPlanner
    {
        private const int SnippetLength = 20;

        // Values of Microsoft.Office.Interop.Word.WdBuiltinStyle.
        private static readonly Dictionary<string, int> BuiltinStyles = CreateBuiltinStyles();

        public static IEnumerable<string> SupportedBuiltinStyles
        {
            get { return new[] { "Title", "Subtitle", "Heading1", "Heading2", "Heading3", "Heading4", "Heading5", "Heading6", "Heading7", "Heading8", "Heading9", "Normal", "BodyText", "Caption", "ListParagraph", "Quote", "IntenseQuote", "BlockQuotation" }; }
        }

        /// <summary>Resolves English ids and Chinese UI names (e.g. "Heading 1" / "标题 1") to a WdBuiltinStyle value.</summary>
        public static bool TryResolveBuiltinStyle(string name, out int styleId)
        {
            styleId = 0;
            var key = NormalizeStyleKey(name);
            return key.Length > 0 && BuiltinStyles.TryGetValue(key, out styleId);
        }

        /// <summary>Returns an error message, or null when the request is valid.</summary>
        public static string Validate(WordFormatParagraphsRequest request)
        {
            if (request == null)
            {
                return "Request is required.";
            }

            if (request.Roles == null || request.Roles.Count == 0)
            {
                return "roles is required: define at least one role, e.g. {\"body\":{\"builtinStyle\":\"Normal\"}}.";
            }

            foreach (var pair in request.Roles)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    return "Role names must not be empty.";
                }

                var role = pair.Value;
                if (role == null)
                {
                    return "Role '" + pair.Key + "' has no settings.";
                }

                int ignored;
                if (!string.IsNullOrWhiteSpace(role.BuiltinStyle) && !TryResolveBuiltinStyle(role.BuiltinStyle, out ignored))
                {
                    return "Role '" + pair.Key + "' uses unknown builtinStyle '" + role.BuiltinStyle + "'. Supported: "
                        + string.Join(", ", SupportedBuiltinStyles) + ". Use styleName for custom styles.";
                }
            }

            var hasAssignments = request.Paragraphs != null && request.Paragraphs.Count > 0;
            if (!hasAssignments && string.IsNullOrWhiteSpace(request.DefaultRole))
            {
                return "Provide paragraphs and/or defaultRole.";
            }

            if (!string.IsNullOrWhiteSpace(request.DefaultRole) && !request.Roles.ContainsKey(request.DefaultRole))
            {
                return "defaultRole '" + request.DefaultRole + "' is not defined in roles.";
            }

            if (hasAssignments)
            {
                var seen = new HashSet<int>();
                foreach (var item in request.Paragraphs)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (item.Index < 1)
                    {
                        return "Paragraph index must be >= 1 (got " + item.Index + ").";
                    }

                    if (string.IsNullOrWhiteSpace(item.Role) || !request.Roles.ContainsKey(item.Role))
                    {
                        return "Paragraph " + item.Index + " uses undefined role '" + item.Role + "'.";
                    }

                    if (!seen.Add(item.Index))
                    {
                        return "Paragraph " + item.Index + " is listed more than once.";
                    }
                }
            }

            return null;
        }

        public static WordParagraphFormatPlan BuildPlan(WordFormatParagraphsRequest request, IList<WordParagraphSnapshot> paragraphs)
        {
            var plan = new WordParagraphFormatPlan();
            var byIndex = (paragraphs ?? new List<WordParagraphSnapshot>())
                .Where(p => p != null)
                .GroupBy(p => p.Index)
                .ToDictionary(g => g.Key, g => g.First());
            var total = byIndex.Count;
            var assigned = new HashSet<int>();
            var items = new List<WordPlannedParagraph>();

            foreach (var item in (request.Paragraphs ?? new List<WordParagraphRoleAssignment>()).Where(p => p != null).OrderBy(p => p.Index))
            {
                assigned.Add(item.Index);
                WordParagraphSnapshot snapshot;
                if (!byIndex.TryGetValue(item.Index, out snapshot))
                {
                    plan.Skipped.Add(new WordParagraphFormatSkip
                    {
                        Index = item.Index,
                        Reason = "out of range (document has " + total + " paragraphs)"
                    });
                    continue;
                }

                if (!MatchesExpected(snapshot.Text, item.Expect))
                {
                    plan.Skipped.Add(new WordParagraphFormatSkip
                    {
                        Index = item.Index,
                        Reason = "expect mismatch: expected '" + Snippet(item.Expect) + "', actual '" + Snippet(snapshot.Text) + "'"
                    });
                    continue;
                }

                items.Add(new WordPlannedParagraph { Index = item.Index, Role = item.Role });
            }

            if (!string.IsNullOrWhiteSpace(request.DefaultRole))
            {
                foreach (var snapshot in byIndex.Values)
                {
                    if (assigned.Contains(snapshot.Index) || snapshot.InTable || NormalizeText(snapshot.Text).Length == 0)
                    {
                        continue;
                    }

                    items.Add(new WordPlannedParagraph { Index = snapshot.Index, Role = request.DefaultRole });
                }
            }

            foreach (var item in items.OrderBy(p => p.Index))
            {
                plan.Items.Add(item);
            }

            return plan;
        }

        /// <summary>
        /// True when the selection ends with a paragraph mark that the replacement text does not
        /// supply itself, so the mark must be kept to avoid merging with the next paragraph.
        /// </summary>
        public static bool ShouldKeepTrailingParagraphMark(string selectedText, string replacement)
        {
            if (string.IsNullOrEmpty(selectedText) || !selectedText.EndsWith("\r", StringComparison.Ordinal))
            {
                return false;
            }

            return string.IsNullOrEmpty(replacement)
                || !(replacement.EndsWith("\r", StringComparison.Ordinal) || replacement.EndsWith("\n", StringComparison.Ordinal));
        }

        /// <summary>Whitespace-insensitive prefix match; an empty expectation always matches.</summary>
        public static bool MatchesExpected(string actual, string expected)
        {
            var normalizedExpected = NormalizeText(expected);
            if (normalizedExpected.Length == 0)
            {
                return true;
            }

            return NormalizeText(actual).StartsWith(normalizedExpected, StringComparison.Ordinal);
        }

        public static string BuildSummary(WordFormatParagraphsResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append("Formatted ").Append(result.AppliedCount).Append(" of ").Append(result.TotalParagraphs).Append(" paragraphs");
            if (result.RoleCounts != null && result.RoleCounts.Count > 0)
            {
                sb.Append(" (").Append(string.Join(", ", result.RoleCounts.Select(p => p.Key + " " + p.Value))).Append(')');
            }

            sb.Append('.');
            if (result.UpdatedStyles != null && result.UpdatedStyles.Count > 0)
            {
                sb.Append(" Updated style definitions: ").Append(string.Join(", ", result.UpdatedStyles)).Append('.');
            }

            if (result.Skipped != null && result.Skipped.Count > 0)
            {
                sb.Append(" Skipped ").Append(result.Skipped.Count).Append(": ");
                sb.Append(string.Join("; ", result.Skipped.Select(s => "#" + s.Index + " " + s.Reason)));
                sb.Append(". Fix only the skipped paragraphs; do not redo the whole batch.");
            }
            else if (result.Success && result.AppliedCount > 0)
            {
                sb.Append(" No paragraph was skipped: formatting is complete. Do not re-apply it paragraph by paragraph with word_select_paragraph / word_format_selection / word_apply_style.");
            }

            if (result.UndoGrouped)
            {
                sb.Append(" The whole batch can be undone with a single Ctrl+Z.");
            }

            return sb.ToString();
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (!char.IsWhiteSpace(c) && !char.IsControl(c))
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static string Snippet(string text)
        {
            var normalized = NormalizeText(text);
            return normalized.Length <= SnippetLength ? normalized : normalized.Substring(0, SnippetLength) + "…";
        }

        private static string NormalizeStyleKey(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : new string(name.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').ToArray()).ToLowerInvariant();
        }

        private static Dictionary<string, int> CreateBuiltinStyles()
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            Action<int, string[]> add = (id, names) =>
            {
                foreach (var n in names)
                {
                    map[NormalizeStyleKey(n)] = id;
                }
            };

            add(-1, new[] { "Normal", "正文" });
            add(-63, new[] { "Title", "标题" });
            add(-75, new[] { "Subtitle", "副标题" });
            for (var level = 1; level <= 9; level++)
            {
                add(-1 - level, new[] { "Heading" + level, "标题" + level });
            }

            add(-67, new[] { "BodyText", "正文文本" });
            add(-35, new[] { "Caption", "题注" });
            add(-180, new[] { "ListParagraph", "列表段落" });
            add(-181, new[] { "Quote", "引用" });
            add(-182, new[] { "IntenseQuote", "明显引用" });
            add(-85, new[] { "BlockQuotation", "块引用" });
            return map;
        }
    }
}
