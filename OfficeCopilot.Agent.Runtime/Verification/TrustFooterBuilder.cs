using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using OfficeCopilot.Localization;

namespace OfficeCopilot.Agent.Runtime.Verification
{
    internal static class TrustFooterBuilder
    {
        private const int MaxBasisCount = 3;
        private const int MaxBasisLength = 180;
        private const int MaxScopeCount = 5;
        private const int MaxScopeLength = 180;
        private static readonly IUserTextLocalizer Localizer = new UserTextLocalizer();
        private static readonly Regex ModelFooterPattern = new Regex(
            @"(?im)^[ \t]*(?:[—-]{1,3}[ \t]*(?:\r?\n[ \t]*)?)?(?:校验|検証|Verification)[:：][^\r\n]*(?:\r?\n[ \t]*(?:检查对象|口径|未能找到直接证据的数字|確認対象|検証対象|集計条件|根拠|根拠が確認できない数値|直接証拠のない数値|直接的な根拠のない数値|根拠のない数値|Checked|Basis|Numbers without direct evidence)[:：][^\r\n]*)*[ \t]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ModelTierClaimPattern = new Regex(
            @"(?im)^[ \t]*(?:#{1,6}[ \t]*)?(?:\d+[.、)）][ \t]*)?(?:検証ランク|検証レベル|検証段階|信頼ランク|信頼レベル|[ABC]級|验证档位|校验档位|校验级别|verification tier|trust tier|tier[ \t]*[abc])[^\r\n]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ExcessBlankLinesPattern = new Regex(
            @"(?:\r?\n[ \t]*){3,}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Build(
            VerificationLedgerSummary summary,
            IReadOnlyList<ExtractedNumber> unresolvedNumbers = null,
            bool numberCorrectionSucceeded = false,
            string uiCulture = null)
        {
            if (summary == null || !summary.HasExcelDataToolCalls)
            {
                return string.Empty;
            }

            var unresolved = (unresolvedNumbers ?? new List<ExtractedNumber>())
                .Where(number => number != null)
                .ToList();
            var culture = ResolveCulture(uiCulture);
            var status = BuildStatus(summary, unresolved, numberCorrectionSucceeded, culture);
            var basis = BuildBasisSummary(summary.BasisJson, culture);
            var scope = BuildScopeSummary(summary.ScopeDescriptions, culture);
            var footer = "—\n" + Localizer.Get("trust.footer.status", culture)
                + LabelSeparator(culture) + status;
            if (!string.IsNullOrWhiteSpace(scope))
            {
                footer += "\n" + Localizer.Get("trust.footer.scope", culture)
                    + LabelSeparator(culture) + scope;
            }
            if ((summary.BasisJson != null && summary.BasisJson.Count > 0)
                || string.IsNullOrWhiteSpace(scope))
            {
                footer += "\n" + Localizer.Get("trust.footer.basis", culture)
                    + LabelSeparator(culture) + basis;
            }
            if (unresolved.Count > 0)
            {
                var rawNumbers = unresolved
                    .Select(number => number.RawText)
                    .Where(rawText => !string.IsNullOrWhiteSpace(rawText))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                footer += "\n" + Localizer.Get("trust.footer.unmatched", culture) + LabelSeparator(culture)
                    + string.Join(ListSeparator(culture), rawNumbers)
                    + LabelSeparator(culture) + Localizer.Get("trust.footer.unmatched.detail", culture);
            }

            return footer;
        }

        public static string RemoveModelGeneratedTrustText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var normalized = ModelFooterPattern.Replace(content, string.Empty);
            normalized = ModelTierClaimPattern.Replace(normalized, string.Empty);
            normalized = ExcessBlankLinesPattern.Replace(normalized, "\n\n");
            return normalized.TrimEnd();
        }

        private static string BuildStatus(
            VerificationLedgerSummary summary,
            IReadOnlyList<ExtractedNumber> unresolvedNumbers,
            bool numberCorrectionSucceeded,
            CultureInfo culture)
        {
            var hasUnresolvedNumbers = unresolvedNumbers != null
                && unresolvedNumbers.Any(number => number != null);
            var ledgerTier = (summary.Tier ?? string.Empty).ToUpperInvariant();
            var tier = GetEffectiveTier(summary, unresolvedNumbers, numberCorrectionSucceeded);
            if (hasUnresolvedNumbers)
            {
                return Localizer.Get("trust.footer.tier.c.unmatched", culture);
            }

            if (numberCorrectionSucceeded && ledgerTier == "A")
            {
                return Localizer.Get("trust.footer.tier.b.corrected", culture);
            }

            if (tier == "A")
            {
                if (summary.HasKeySetDifferenceCalculation || summary.HasHiddenRowsScan)
                {
                    var verifiedResults = new List<string>();
                    if (summary.HasKeySetDifferenceCalculation)
                    {
                        verifiedResults.Add(Localizer.Get("trust.footer.verified.keysets", culture));
                    }
                    if (summary.HasHiddenRowsScan)
                    {
                        verifiedResults.Add(Localizer.Get("trust.footer.verified.hidden", culture));
                    }
                    if (summary.AssertionCount > 0)
                    {
                        verifiedResults.Add(
                            Localizer.Format("trust.footer.tier.a.assertions", culture,
                                summary.AssertionCount.ToString("N0", culture)));
                    }
                    verifiedResults.Add(
                        Localizer.Format("trust.footer.tier.a.coverage", culture,
                            summary.CoveredRows.ToString("N0", culture)));
                    return WrapStatus(
                        Localizer.Get("trust.footer.tier.a.full", culture),
                        string.Join(ClauseSeparator(culture), verifiedResults),
                        culture);
                }

                if (summary.AssertionCount > 0)
                {
                    return WrapStatus(
                        Localizer.Get("trust.footer.tier.a.full", culture),
                        Localizer.Format("trust.footer.tier.a.assertions", culture,
                            summary.AssertionCount.ToString("N0", culture))
                        + InlineSeparator(culture)
                        + Localizer.Format("trust.footer.tier.a.coverage", culture,
                            summary.CoveredRows.ToString("N0", culture)),
                        culture);
                }

                return WrapStatus(
                    Localizer.Get("trust.footer.tier.a.full", culture),
                    Localizer.Format("trust.footer.tier.a.outputs", culture,
                        summary.OutputItemCount.ToString("N0", culture)),
                    culture);
            }

            if (tier == "B")
            {
                return Localizer.Format("trust.footer.tier.b", culture,
                    string.IsNullOrWhiteSpace(summary.PrimaryReason)
                        ? Localizer.Get("trust.footer.reason.b", culture)
                        : Truncate(CleanText(summary.PrimaryReason)));
            }

            return Localizer.Format("trust.footer.tier.c", culture,
                string.IsNullOrWhiteSpace(summary.PrimaryReason)
                    ? Localizer.Get("trust.footer.reason.c", culture)
                    : Truncate(CleanText(summary.PrimaryReason)));
        }

        public static string GetEffectiveTier(
            VerificationLedgerSummary summary,
            IEnumerable<ExtractedNumber> unresolvedNumbers = null,
            bool numberCorrectionSucceeded = false)
        {
            if (summary == null)
            {
                    return string.Empty;
            }

            if ((unresolvedNumbers ?? Enumerable.Empty<ExtractedNumber>())
                    .Any(number => number != null))
            {
                    return "C";
            }

            var tier = (summary.Tier ?? string.Empty).ToUpperInvariant();
            return numberCorrectionSucceeded && tier == "A" ? "B" : tier;
        }

        private static string BuildBasisSummary(IList<string> basisJson, CultureInfo culture)
        {
            var formatted = new List<string>();
            foreach (var json in basisJson ?? new List<string>())
            {
                var basis = FormatBasis(json, culture);
                if (!string.IsNullOrWhiteSpace(basis)
                    && !formatted.Any(existing => string.Equals(
                        existing,
                        basis,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    formatted.Add(basis);
                }
            }

            if (formatted.Count == 0)
            {
                return Localizer.Get("trust.footer.basis.unparseable", culture);
            }

            var visible = formatted.Take(MaxBasisCount).ToList();
            if (formatted.Count > MaxBasisCount)
            {
                visible.Add(Localizer.Format("trust.footer.more", culture,
                    (formatted.Count - MaxBasisCount).ToString("N0", culture)));
            }

            return string.Join(ClauseSeparator(culture), visible);
        }

        private static string BuildScopeSummary(IList<string> scopeDescriptions, CultureInfo culture)
        {
            var scopes = (scopeDescriptions ?? new List<string>())
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Select(scope => TruncateScope(CleanText(scope.Trim())))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (scopes.Count == 0)
            {
                return string.Empty;
            }

            var visible = scopes.Take(MaxScopeCount).ToList();
            if (scopes.Count > MaxScopeCount)
            {
                visible.Add(Localizer.Format("trust.footer.more", culture,
                    (scopes.Count - MaxScopeCount).ToString("N0", culture)));
            }

            return string.Join(ClauseSeparator(culture), visible);
        }

        private static string TruncateScope(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxScopeLength)
            {
                return value;
            }

            return value.Substring(0, MaxScopeLength - 1) + "…";
        }

        private static string FormatBasis(string json, CultureInfo culture)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        return Localizer.Get("trust.footer.basis.unparseable", culture);
                    }

                    var parts = new List<string>();
                    var source = ReadProperty(root, "source");
                    var sourceName = ReadString(source, "worksheetName");
                    if (string.IsNullOrWhiteSpace(sourceName))
                    {
                        sourceName = ReadString(source, "tableName");
                    }
                    if (string.IsNullOrWhiteSpace(sourceName))
                    {
                        sourceName = ReadString(source, "address");
                    }
                    if (!string.IsNullOrWhiteSpace(sourceName))
                    {
                        parts.Add(sourceName);
                    }

                    var filters = FormatFilters(ReadProperty(root, "filters"), culture);
                    if (filters.Count > 0)
                    {
                        parts.Add(Localizer.Get("trust.footer.filter", culture)
                            + string.Join(Localizer.Get("trust.footer.and", culture), filters));
                    }

                    var groupBy = ReadStringArray(ReadProperty(root, "groupBy"));
                    var derivedGroupBy = FormatDerivedGroups(ReadProperty(root, "derivedGroupBy"), culture);
                    var groupNames = groupBy.Concat(derivedGroupBy).ToList();
                    var measures = FormatMeasures(ReadProperty(root, "measures"));
                    if (groupNames.Count > 0 || measures.Count > 0)
                    {
                        var aggregation = groupNames.Count > 0
                            ? Localizer.Get("trust.footer.group.by", culture)
                                + string.Join(ListSeparator(culture), groupNames)
                                + Localizer.Get("trust.footer.group.aggregate", culture)
                            : Localizer.Get("trust.footer.group.all", culture);
                        if (measures.Count > 0)
                        {
                            aggregation += WordSeparator(culture) + string.Join(ListSeparator(culture), measures);
                        }
                        parts.Add(aggregation);
                    }

                    var note = ReadString(root, "note");
                    if (!string.IsNullOrWhiteSpace(note))
                    {
                        parts.Add(CleanText(note));
                    }

                    if (parts.Count == 0)
                    {
                        return Localizer.Get("trust.footer.basis.unspecified", culture);
                    }

                    var open = UsesFullWidthParentheses(culture) ? "（" : " (";
                    var close = UsesFullWidthParentheses(culture) ? "）" : ")";
                    var separator = ClauseSeparator(culture);
                    return Truncate(parts[0]
                        + (parts.Count > 1 ? open + string.Join(separator, parts.Skip(1)) + close : string.Empty));
                }
            }
            catch (JsonException)
            {
                return Localizer.Get("trust.footer.basis.unparseable", culture);
            }
        }

        private static List<string> FormatFilters(JsonElement filters, CultureInfo culture)
        {
            var result = new List<string>();
            if (filters.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var filter in filters.EnumerateArray())
            {
                var column = ReadString(filter, "column");
                var operation = ReadString(filter, "operator");
                var value = ReadString(filter, "value");
                if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(operation))
                {
                    continue;
                }

                var displayOperation = GetFilterOperator(operation, culture);
                result.Add(CleanText(column) + displayOperation
                    + (string.IsNullOrWhiteSpace(value) ? string.Empty : CleanText(value)));
            }

            return result;
        }

        private static List<string> FormatMeasures(JsonElement measures)
        {
            var result = new List<string>();
            if (measures.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var measure in measures.EnumerateArray())
            {
                var column = ReadString(measure, "column");
                var function = ReadString(measure, "function");
                if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(function))
                {
                    continue;
                }

                result.Add(CleanText(function) + "(" + CleanText(column) + ")");
            }

            return result;
        }

        private static List<string> FormatDerivedGroups(JsonElement derivedGroups, CultureInfo culture)
        {
            var result = new List<string>();
            if (derivedGroups.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var group in derivedGroups.EnumerateArray())
            {
                var column = ReadString(group, "column");
                var transform = ReadString(group, "transform");
                if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(transform))
                {
                    continue;
                }

                var alias = ReadString(group, "alias");
                var open = UsesFullWidthParentheses(culture) ? "（" : " (";
                var close = UsesFullWidthParentheses(culture) ? "）" : ")";
                var label = CleanText(column) + "→"
                    + (string.IsNullOrWhiteSpace(alias) ? CleanText(transform) : CleanText(alias))
                    + open + CleanText(transform);
                if (group.ValueKind == JsonValueKind.Object
                    && group.TryGetProperty("size", out var size)
                    && size.ValueKind == JsonValueKind.Number
                    && size.TryGetDouble(out var sizeValue)
                    && sizeValue > 0d)
                {
                    if (string.Equals(transform, "numberBucket", StringComparison.OrdinalIgnoreCase))
                    {
                        label += Localizer.Get("trust.footer.bucket.width", culture);
                    }
                    else if (string.Equals(transform, "prefix", StringComparison.OrdinalIgnoreCase))
                    {
                        label += Localizer.Get("trust.footer.prefix", culture);
                    }
                    if (string.Equals(transform, "numberBucket", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(transform, "prefix", StringComparison.OrdinalIgnoreCase))
                    {
                        label += sizeValue.ToString("G", CultureInfo.InvariantCulture)
                            + (string.Equals(transform, "prefix", StringComparison.OrdinalIgnoreCase)
                                ? Localizer.Get("trust.footer.characters", culture)
                                : string.Empty);
                    }
                }
                result.Add(label + close);
            }

            return result;
        }

        private static List<string> ReadStringArray(JsonElement values)
        {
            var result = new List<string>();
            if (values.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = CleanText(value.GetString());
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Add(text);
                    }
                }
            }

            return result;
        }

        private static JsonElement ReadProperty(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                    ? value
                    : default(JsonElement);
        }

        private static string ReadString(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static string GetFilterOperator(string operation, CultureInfo culture)
        {
            switch ((operation ?? string.Empty).Trim())
            {
                case "eq": return "=";
                case "ne": return "≠";
                case "gt": return ">";
                case "ge": return "≥";
                case "lt": return "<";
                case "le": return "≤";
                case "contains": return Localizer.Get("trust.footer.operator.contains", culture);
                case "startsWith": return Localizer.Get("trust.footer.operator.starts_with", culture);
                case "in": return Localizer.Get("trust.footer.operator.in", culture);
                case "isEmpty": return Localizer.Get("trust.footer.operator.is_empty", culture);
                case "notEmpty": return Localizer.Get("trust.footer.operator.not_empty", culture);
                default: return " " + CleanText(operation) + " ";
            }
        }

        private static string CleanText(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : string.Join(" ", value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxBasisLength)
            {
                return value;
            }

            return value.Substring(0, MaxBasisLength - 1) + "…";
        }

        private static CultureInfo ResolveCulture(string uiCulture)
        {
            if (!string.IsNullOrWhiteSpace(uiCulture))
            {
                return SupportedLanguages.ResolveCulture(uiCulture);
            }

            return CultureInfo.GetCultureInfo(SupportedLanguages.SimplifiedChinese);
        }

        private static string LabelSeparator(CultureInfo culture)
        {
            return UsesFullWidthParentheses(culture)
                    ? string.Empty
                    : " ";
        }

        private static string WrapStatus(string heading, string detail, CultureInfo culture)
        {
            return UsesFullWidthParentheses(culture)
                ? heading + "（" + detail + "）"
                : heading + " (" + detail + ")";
        }

        private static string ListSeparator(CultureInfo culture)
        {
            return UsesFullWidthParentheses(culture) ? "、" : ", ";
        }

        private static string InlineSeparator(CultureInfo culture)
        {
            return culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? "，"
                : culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                    ? "、"
                    : ", ";
        }

        private static string WordSeparator(CultureInfo culture)
        {
            return culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : " ";
        }

        private static string ClauseSeparator(CultureInfo culture)
        {
            return culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                ? "、"
                : culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                    ? "；"
                    : "; ";
        }

        private static bool UsesFullWidthParentheses(CultureInfo culture)
        {
            return culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                || culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
        }
    }
}
