using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Runtime.Verification
{
    internal static class TrustFooterBuilder
    {
        private const int MaxBasisCount = 3;
        private const int MaxBasisLength = 180;
        private const int MaxScopeCount = 5;
        private const int MaxScopeLength = 180;
        private static readonly Regex ModelFooterPattern = new Regex(
            @"(?im)^[ \t]*(?:(?:[—-]{1,3}[ \t]*)(?:\r?\n[ \t]*)?)?校验[:：][^\r\n]*(?:\r?\n[ \t]*口径[:：][^\r\n]*)?[ \t]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ModelTierClaimPattern = new Regex(
            @"(?im)^[ \t]*(?:#{1,6}[ \t]*)?(?:\d+[.、)）][ \t]*)?(?:验证档位|校验档位|校验级别)[^\r\n]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ExcessBlankLinesPattern = new Regex(
            @"(?:\r?\n[ \t]*){3,}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Build(
            VerificationLedgerSummary summary,
            IReadOnlyList<ExtractedNumber> unresolvedNumbers = null,
            bool numberCorrectionSucceeded = false)
        {
            if (summary == null || !summary.HasExcelDataToolCalls)
            {
                return string.Empty;
            }

            var unresolved = (unresolvedNumbers ?? new List<ExtractedNumber>())
                .Where(number => number != null)
                .ToList();
            var status = BuildStatus(summary, unresolved, numberCorrectionSucceeded);
            var basis = BuildBasisSummary(summary.BasisJson);
            var scope = BuildScopeSummary(summary.ScopeDescriptions);
            var footer = "—\n校验：" + status;
            if (!string.IsNullOrWhiteSpace(scope))
            {
                footer += "\n检查对象：" + scope;
            }
            if ((summary.BasisJson != null && summary.BasisJson.Count > 0)
                || string.IsNullOrWhiteSpace(scope))
            {
                footer += "\n口径：" + basis;
            }
            if (unresolved.Count > 0)
            {
                var rawNumbers = unresolved
                    .Select(number => number.RawText)
                    .Where(rawText => !string.IsNullOrWhiteSpace(rawText))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                footer += "\n未能找到直接证据的数字：" + string.Join("、", rawNumbers)
                    + "（本轮自动追溯未能在用户消息或工具结果中匹配到这些数字）";
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
            bool numberCorrectionSucceeded)
        {
            var hasUnresolvedNumbers = unresolvedNumbers != null
                && unresolvedNumbers.Any(number => number != null);
            var ledgerTier = (summary.Tier ?? string.Empty).ToUpperInvariant();
            var tier = GetEffectiveTier(summary, unresolvedNumbers, numberCorrectionSucceeded);
            if (hasUnresolvedNumbers)
            {
                return "C级：尚未完成有效验证 —— 有部分结论数字未能找到直接证据";
            }

            if (numberCorrectionSucceeded && ledgerTier == "A")
            {
                return "B级：已复核，但仍有局限（最终回答中的数字经过订正）";
            }

            if (tier == "A")
            {
                if (summary.HasKeySetDifferenceCalculation || summary.HasHiddenRowsScan)
                {
                    var verifiedResults = new List<string>();
                    if (summary.HasKeySetDifferenceCalculation)
                    {
                        verifiedResults.Add("双向键集合差异已完整计算");
                    }
                    if (summary.HasHiddenRowsScan)
                    {
                        verifiedResults.Add("隐藏行状态已完整扫描");
                    }
                    if (summary.AssertionCount > 0)
                    {
                        verifiedResults.Add(
                            summary.AssertionCount.ToString("N0", CultureInfo.InvariantCulture)
                            + " 项断言通过");
                    }
                    verifiedResults.Add(
                        "覆盖 " + summary.CoveredRows.ToString("N0", CultureInfo.InvariantCulture) + " 行");
                    return "A级：全量独立复核通过（" + string.Join("；", verifiedResults) + "）";
                }

                if (summary.AssertionCount > 0)
                {
                    return "A级：全量独立复核通过（"
                        + summary.AssertionCount.ToString("N0", CultureInfo.InvariantCulture)
                        + " 项断言通过，覆盖 "
                        + summary.CoveredRows.ToString("N0", CultureInfo.InvariantCulture)
                        + " 行）";
                }

                return "A级：全量独立复核通过（"
                    + summary.OutputItemCount.ToString("N0", CultureInfo.InvariantCulture)
                    + " 个产出项通过独立全量验证）";
            }

            if (tier == "B")
            {
                return "B级：已复核，但仍有局限（"
                    + (string.IsNullOrWhiteSpace(summary.PrimaryReason)
                        ? "验证证据未达到独立全量复核标准"
                        : Truncate(CleanText(summary.PrimaryReason)))
                    + "）";
            }

            return "C级：尚未完成有效验证 —— "
                + (string.IsNullOrWhiteSpace(summary.PrimaryReason)
                    ? "至少一个产出项没有可用的验证证据"
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

        private static string BuildBasisSummary(IList<string> basisJson)
        {
            var formatted = new List<string>();
            foreach (var json in basisJson ?? new List<string>())
            {
                var basis = FormatBasis(json);
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
                return "未提供可解析的结构化口径";
            }

            var visible = formatted.Take(MaxBasisCount).ToList();
            if (formatted.Count > MaxBasisCount)
            {
                visible.Add("等 " + (formatted.Count - MaxBasisCount).ToString(CultureInfo.InvariantCulture) + " 项");
            }

            return string.Join("；", visible);
        }

        private static string BuildScopeSummary(IList<string> scopeDescriptions)
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
                visible.Add("等 " + (scopes.Count - MaxScopeCount).ToString(CultureInfo.InvariantCulture) + " 项");
            }

            return string.Join("；", visible);
        }

        private static string TruncateScope(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxScopeLength)
            {
                return value;
            }

            return value.Substring(0, MaxScopeLength - 1) + "…";
        }

        private static string FormatBasis(string json)
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
                        return "口径信息无法解析";
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

                    var filters = FormatFilters(ReadProperty(root, "filters"));
                    if (filters.Count > 0)
                    {
                        parts.Add("筛选 " + string.Join(" 且 ", filters));
                    }

                    var groupBy = ReadStringArray(ReadProperty(root, "groupBy"));
                    var derivedGroupBy = FormatDerivedGroups(ReadProperty(root, "derivedGroupBy"));
                    var groupNames = groupBy.Concat(derivedGroupBy).ToList();
                    var measures = FormatMeasures(ReadProperty(root, "measures"));
                    if (groupNames.Count > 0 || measures.Count > 0)
                    {
                        var aggregation = groupNames.Count > 0
                            ? "按 " + string.Join("、", groupNames) + " 汇总"
                            : "全局汇总";
                        if (measures.Count > 0)
                        {
                            aggregation += " " + string.Join("、", measures);
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
                        return "未声明详细口径";
                    }

                    return Truncate(string.Join("（", parts.Take(1))
                        + (parts.Count > 1 ? "（" + string.Join("；", parts.Skip(1)) + "）" : string.Empty));
                }
            }
            catch (JsonException)
            {
                return "口径信息无法解析";
            }
        }

        private static List<string> FormatFilters(JsonElement filters)
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

                var displayOperation = GetFilterOperator(operation);
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

        private static List<string> FormatDerivedGroups(JsonElement derivedGroups)
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
                var label = CleanText(column) + "→"
                    + (string.IsNullOrWhiteSpace(alias) ? CleanText(transform) : CleanText(alias))
                    + "（" + CleanText(transform);
                if (group.ValueKind == JsonValueKind.Object
                    && group.TryGetProperty("size", out var size)
                    && size.ValueKind == JsonValueKind.Number
                    && size.TryGetDouble(out var sizeValue)
                    && sizeValue > 0d)
                {
                    if (string.Equals(transform, "numberBucket", StringComparison.OrdinalIgnoreCase))
                    {
                        label += "，桶宽 ";
                    }
                    else if (string.Equals(transform, "prefix", StringComparison.OrdinalIgnoreCase))
                    {
                        label += "，前 ";
                    }
                    if (string.Equals(transform, "numberBucket", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(transform, "prefix", StringComparison.OrdinalIgnoreCase))
                    {
                        label += sizeValue.ToString("G", CultureInfo.InvariantCulture)
                            + (string.Equals(transform, "prefix", StringComparison.OrdinalIgnoreCase)
                                ? " 字符"
                                : string.Empty);
                    }
                }
                result.Add(label + "）");
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

        private static string GetFilterOperator(string operation)
        {
            switch ((operation ?? string.Empty).Trim())
            {
                case "eq": return "=";
                case "ne": return "≠";
                case "gt": return ">";
                case "ge": return "≥";
                case "lt": return "<";
                case "le": return "≤";
                case "contains": return " 包含 ";
                case "startsWith": return " 以…开头 ";
                case "in": return " 属于 ";
                case "isEmpty": return " 为空";
                case "notEmpty": return " 非空";
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
    }
}
