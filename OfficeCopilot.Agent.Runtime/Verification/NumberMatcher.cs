using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Runtime.Verification
{
    internal static class NumberMatcher
    {
        private static readonly Regex SubtractionEquationPattern = new Regex(
            @"(?<![\p{L}\p{N}_])(?<left>(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)\s*-\s*"
                + @"(?<right>(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)\s*=\s*"
                + @"(?<result>(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)(?![\p{L}\p{N}_])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static NumberMatchReport Match(
            IEnumerable<ExtractedNumber> answerNumbers,
            EvidenceLedger evidenceLedger,
            string answerText = null)
        {
            var matched = new List<NumberMatchRecord>();
            var unmatched = new List<ExtractedNumber>();
            var evidence = evidenceLedger == null
                ? new List<EvidenceLedger.NumberEvidence>()
                : evidenceLedger.Numbers.Concat(evidenceLedger.RangeRowSpans).ToList();
            evidence.AddRange(GetVerifiedSubtractionEvidence(answerText, evidenceLedger));

            foreach (var answerNumber in answerNumbers ?? Enumerable.Empty<ExtractedNumber>())
            {
                if (answerNumber == null)
                {
                    continue;
                }

                NumberMatchRecord record = null;
                foreach (var evidenceNumber in evidence)
                {
                    if (TryMatch(answerNumber, evidenceNumber, out var matchKind))
                    {
                        if (record == null || matchKind < record.MatchKind)
                        {
                            record = new NumberMatchRecord(answerNumber, evidenceNumber, matchKind);
                        }

                        if (matchKind == NumberMatchKind.Direct)
                        {
                            break;
                        }
                    }
                }

                if (record == null)
                {
                    unmatched.Add(answerNumber);
                }
                else
                {
                    matched.Add(record);
                }
            }

            return new NumberMatchReport(matched, unmatched);
        }

        private static IEnumerable<EvidenceLedger.NumberEvidence> GetVerifiedSubtractionEvidence(
            string answerText,
            EvidenceLedger evidenceLedger)
        {
            if (string.IsNullOrWhiteSpace(answerText) || evidenceLedger == null)
            {
                yield break;
            }

            var normalizedAnswer = NumericValueParser.NormalizeDigitsAndPunctuation(answerText);
            foreach (Match equation in SubtractionEquationPattern.Matches(normalizedAnswer))
            {
                if (!TryParseEquationNumber(equation.Groups["left"].Value, out var left)
                    || !TryParseEquationNumber(equation.Groups["right"].Value, out var right)
                    || !TryParseEquationNumber(equation.Groups["result"].Value, out var result)
                    || left.IsPercentage
                    || right.IsPercentage
                    || result.IsPercentage
                    || !evidenceLedger.TryFindSameMessageOperandSource(
                        left.Value,
                        right.Value,
                        out var sourceName,
                        out var messageId))
                {
                    continue;
                }

                decimal calculated;
                try
                {
                    calculated = left.Value - right.Value;
                }
                catch (OverflowException)
                {
                    continue;
                }

                if (calculated != result.Value)
                {
                    continue;
                }

                yield return new EvidenceLedger.NumberEvidence(
                    calculated,
                    result.DecimalPlaces,
                    false,
                    EvidenceLedger.EvidenceOrigin.DerivedCalculation,
                    "verified subtraction from " + sourceName,
                    messageId,
                    EvidenceLedger.EvidenceKind.NumericValue);
            }
        }

        private static bool TryParseEquationNumber(string text, out ParsedNumericValue number)
        {
            number = null;
            var parsed = NumericValueParser.Parse(text);
            if (parsed.Count != 1)
            {
                return false;
            }

            number = parsed[0];
            return true;
        }

        private static bool TryMatch(
            ExtractedNumber answer,
            EvidenceLedger.NumberEvidence evidence,
            out NumberMatchKind matchKind)
        {
            var digits = Math.Min(Math.Max(answer.DecimalPlaces, 0), 28);
            if (evidence.Kind == EvidenceLedger.EvidenceKind.CellRangeRowSpan)
            {
                if (!answer.IsPercentage
                    && IsExplicitRangeRowSpanClaim(answer)
                    && Round(evidence.Value, digits) == answer.Value)
                {
                    matchKind = NumberMatchKind.CellRangeRowSpan;
                    return true;
                }

                matchKind = default(NumberMatchKind);
                return false;
            }

            if (answer.IsPercentage == evidence.IsPercentage
                && Round(evidence.Value, digits) == answer.Value)
            {
                matchKind = evidence.Origin == EvidenceLedger.EvidenceOrigin.DerivedCalculation
                    ? NumberMatchKind.DerivedCalculation
                    : NumberMatchKind.Direct;
                return true;
            }

            if (answer.IsPercentage && !evidence.IsPercentage
                && TryScalePercent(evidence.Value, digits, answer.Value))
            {
                matchKind = NumberMatchKind.PercentageScaled;
                return true;
            }

            if (answer.IsPercentage == evidence.IsPercentage
                && TryAbsoluteMatch(answer, evidence, digits))
            {
                matchKind = NumberMatchKind.AbsoluteValue;
                return true;
            }

            if (answer.IsPercentage && !evidence.IsPercentage
                && TryAbsolutePercentMatch(answer, evidence, digits))
            {
                matchKind = NumberMatchKind.PercentageScaled;
                return true;
            }

            matchKind = default(NumberMatchKind);
            return false;
        }

        private static bool IsExplicitRangeRowSpanClaim(ExtractedNumber answer)
        {
            var rawNumber = NumericValueParser.NormalizeDigitsAndPunctuation(answer.RawText);
            if (string.IsNullOrWhiteSpace(rawNumber))
            {
                return false;
            }

            var number = "(?<![0-9])" + Regex.Escape(rawNumber) + "(?![0-9])";
            var pattern = "含表头[^0-9]{0,8}" + number + "[^0-9]{0,4}行"
                + "|(?:selected\\s+)?range[^\\r\\n]{0,48}?(?:spans?|covers?|contains?)[^0-9]{0,8}"
                    + number + "\\s+rows?\\b"
                + "|" + number + "\\s+rows?\\s+including\\s+(?:the\\s+)?header";

            return Regex.IsMatch(
                NumericValueParser.NormalizeDigitsAndPunctuation(answer.Context),
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool TryScalePercent(decimal value, int digits, decimal expected)
        {
            try
            {
                return Round(value * 100m, digits) == expected;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryAbsoluteMatch(
            ExtractedNumber answer,
            EvidenceLedger.NumberEvidence evidence,
            int digits)
        {
            try
            {
                return Math.Abs(Round(evidence.Value, digits))
                    == Math.Abs(Round(answer.Value, digits));
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryAbsolutePercentMatch(
            ExtractedNumber answer,
            EvidenceLedger.NumberEvidence evidence,
            int digits)
        {
            try
            {
                return Math.Abs(Round(evidence.Value * 100m, digits))
                    == Math.Abs(Round(answer.Value, digits));
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static decimal Round(decimal value, int digits)
        {
            return decimal.Round(value, digits, MidpointRounding.AwayFromZero);
        }
    }

    internal enum NumberMatchKind
    {
        Direct,
        DerivedCalculation,
        PercentageScaled,
        AbsoluteValue,
        CellRangeRowSpan
    }

    internal sealed class NumberMatchRecord
    {
        public NumberMatchRecord(
            ExtractedNumber answer,
            EvidenceLedger.NumberEvidence evidence,
            NumberMatchKind matchKind)
        {
            Answer = answer;
            Evidence = evidence;
            MatchKind = matchKind;
        }

        public ExtractedNumber Answer { get; private set; }

        public EvidenceLedger.NumberEvidence Evidence { get; private set; }

        public NumberMatchKind MatchKind { get; private set; }
    }

    internal sealed class NumberMatchReport
    {
        public NumberMatchReport(
            IList<NumberMatchRecord> matched,
            IList<ExtractedNumber> unmatched)
        {
            Matched = new List<NumberMatchRecord>(matched ?? new List<NumberMatchRecord>());
            Unmatched = new List<ExtractedNumber>(unmatched ?? new List<ExtractedNumber>());
        }

        public IReadOnlyList<NumberMatchRecord> Matched { get; private set; }

        public IReadOnlyList<ExtractedNumber> Unmatched { get; private set; }

        public bool AllMatched
        {
            get { return Unmatched.Count == 0; }
        }
    }
}
