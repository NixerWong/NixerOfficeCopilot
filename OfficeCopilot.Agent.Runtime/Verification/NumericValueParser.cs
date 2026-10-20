using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Runtime.Verification
{
    internal static class NumericValueParser
    {
        private static readonly Regex NumberPattern = new Regex(
            @"(?<![A-Za-z\p{N}_])(?<sign>[-+]?)(?<integer>(?:\d{1,3}(?:,\d{3})+|\d+))"
                + @"(?:\.(?<fraction>\d+))?(?:\s*(?<unit>[万亿%]))?"
                + @"(?![A-Za-z\p{N}_.]|,(?=\d{1,2}(?:\D|$)))",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IReadOnlyList<ParsedNumericValue> Parse(string text)
        {
            var result = new List<ParsedNumericValue>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var normalized = NormalizeDigitsAndPunctuation(text);
            foreach (Match match in NumberPattern.Matches(normalized))
            {
                var integer = match.Groups["integer"].Value.Replace(",", string.Empty);
                var fraction = match.Groups["fraction"].Value;
                var numericText = match.Groups["sign"].Value + integer
                    + (fraction.Length == 0 ? string.Empty : "." + fraction);
                if (!decimal.TryParse(
                    numericText,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var value))
                {
                    continue;
                }

                var unit = match.Groups["unit"].Value;
                var isPercentage = unit == "%";
                try
                {
                    if (unit == "万")
                    {
                        value *= 10000m;
                    }
                    else if (unit == "亿")
                    {
                        value *= 100000000m;
                    }
                }
                catch (OverflowException)
                {
                    continue;
                }

                result.Add(new ParsedNumericValue(
                    match.Index,
                    match.Length,
                    value,
                    fraction.Length,
                    isPercentage,
                    unit == "万" || unit == "亿"));
            }

            return result;
        }

        public static string NormalizeDigitsAndPunctuation(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            var normalized = new StringBuilder(text.Length);
            foreach (var character in text)
            {
                if (character >= '\uFF10' && character <= '\uFF19')
                {
                    normalized.Append((char)('0' + character - '\uFF10'));
                    continue;
                }

                switch (character)
                {
                    case '\uFF0C': normalized.Append(','); break;
                    case '\uFF0E': normalized.Append('.'); break;
                    case '\uFF0B': normalized.Append('+'); break;
                    case '\uFF0D':
                    case '\u2212':
                    case '\uFE63': normalized.Append('-'); break;
                    case '\uFF05':
                    case '\uFE6A': normalized.Append('%'); break;
                    case '\uFF0F': normalized.Append('/'); break;
                    case '\uFF1A': normalized.Append(':'); break;
                    default: normalized.Append(character); break;
                }
            }

            return normalized.ToString();
        }
    }

    internal sealed class ParsedNumericValue
    {
        public ParsedNumericValue(
            int startIndex,
            int length,
            decimal value,
            int decimalPlaces,
            bool isPercentage,
            bool hasMagnitudeUnit)
        {
            StartIndex = startIndex;
            Length = length;
            Value = value;
            DecimalPlaces = decimalPlaces;
            IsPercentage = isPercentage;
            HasMagnitudeUnit = hasMagnitudeUnit;
        }

        public int StartIndex { get; private set; }

        public int Length { get; private set; }

        public decimal Value { get; private set; }

        public int DecimalPlaces { get; private set; }

        public bool IsPercentage { get; private set; }

        public bool HasMagnitudeUnit { get; private set; }
    }
}
