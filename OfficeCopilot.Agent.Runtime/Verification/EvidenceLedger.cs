using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using OfficeCopilot.Agent.Persistence;

namespace OfficeCopilot.Agent.Runtime.Verification
{
    internal sealed class EvidenceLedger
    {
        private const int MaximumExcelRowNumber = 1048576;

        private static readonly Regex CellRangeAddressPattern = new Regex(
            @"(?<![A-Za-z0-9_])\$?(?<startColumn>[A-Za-z]{1,3})\$?(?<startRow>[1-9]\d*):\$?(?<endColumn>[A-Za-z]{1,3})\$?(?<endRow>[1-9]\d*)(?![A-Za-z0-9_])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private readonly List<NumberEvidence> _numbers = new List<NumberEvidence>();
        private readonly List<NumberEvidence> _rangeRowSpans = new List<NumberEvidence>();
        private readonly HashSet<EvidenceKey> _seen = new HashSet<EvidenceKey>();

        private EvidenceLedger()
        {
        }

        public IReadOnlyList<NumberEvidence> Numbers
        {
            get { return _numbers; }
        }

        public IReadOnlyList<NumberEvidence> RangeRowSpans
        {
            get { return _rangeRowSpans; }
        }

        public static EvidenceLedger FromConversationMessages(
            IEnumerable<BrokerConversationMessage> messages)
        {
            var ledger = new EvidenceLedger();
            foreach (var message in messages ?? Enumerable.Empty<BrokerConversationMessage>())
            {
                ledger.RecordMessage(message);
            }

            return ledger;
        }

        public static EvidenceLedger CreateEmpty()
        {
            return new EvidenceLedger();
        }

        public void RecordMessage(BrokerConversationMessage message)
        {
            if (message == null)
            {
                return;
            }

            if (IsUserMessage(message))
            {
                RecordText(message.Content, EvidenceOrigin.UserMessage, string.Empty, message.MessageId);
                return;
            }

            if (!IsToolResult(message))
            {
                return;
            }

            var isCalculation = string.Equals(
                message.Name,
                "runtime.calculate",
                StringComparison.OrdinalIgnoreCase);
            if (isCalculation && !HasSourcedCalculationResult(message.StructuredContentJson))
            {
                return;
            }

            var origin = isCalculation
                ? EvidenceOrigin.CalculationResult
                : EvidenceOrigin.ToolResult;
            RecordText(message.Content, origin, message.Name, message.MessageId);
            if (!isCalculation)
            {
                RecordText(message.StructuredContentJson, origin, message.Name, message.MessageId);
            }
        }

        public void RecordToolResult(
            string toolName,
            string content,
            string structuredContentJson,
            string toolCallId)
        {
            RecordMessage(new BrokerConversationMessage
            {
                MessageType = "tool_result",
                Role = "tool_result",
                Name = toolName ?? string.Empty,
                Content = content ?? string.Empty,
                StructuredContentJson = structuredContentJson ?? string.Empty,
                ToolCallId = toolCallId ?? string.Empty,
                MessageId = toolCallId ?? string.Empty
            });
        }

        public bool ContainsOperand(decimal value)
        {
            return _numbers.Any(number => number.Value == value
                || (number.IsPercentage && number.Value / 100m == value));
        }

        public bool TryFindSameMessageOperandSource(
            decimal firstValue,
            decimal secondValue,
            out string sourceName,
            out string messageId)
        {
            sourceName = string.Empty;
            messageId = string.Empty;
            foreach (var first in _numbers.Where(number =>
                number.Kind == EvidenceKind.NumericValue
                && !number.IsPercentage
                && number.Value == firstValue
                && !string.IsNullOrWhiteSpace(number.MessageId)))
            {
                var second = _numbers.FirstOrDefault(number =>
                    number.Kind == EvidenceKind.NumericValue
                    && !number.IsPercentage
                    && number.Value == secondValue
                    && number.Origin == first.Origin
                    && string.Equals(number.SourceName, first.SourceName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(number.MessageId, first.MessageId, StringComparison.Ordinal));
                if (second != null)
                {
                    sourceName = first.SourceName ?? string.Empty;
                    messageId = first.MessageId;
                    return true;
                }
            }

            return false;
        }

        public bool IsPercentageFraction(decimal value)
        {
            return _numbers.Any(number => number.IsPercentage
                && number.Value / 100m == value);
        }

        private static bool HasSourcedCalculationResult(string structuredContentJson)
        {
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    return document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("operandsSourced", out var sourced)
                        && sourced.ValueKind == JsonValueKind.True;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsUserMessage(BrokerConversationMessage message)
        {
            return string.IsNullOrWhiteSpace(message.ToolCallId)
                && (string.Equals(message.MessageType, "user", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsToolResult(BrokerConversationMessage message)
        {
            return string.Equals(message.MessageType, "tool_result", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Role, "tool_result", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(message.ToolCallId));
        }

        private void RecordText(
            string text,
            EvidenceOrigin origin,
            string sourceName,
            string messageId)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            foreach (var parsed in NumericValueParser.Parse(text))
            {
                AddEvidence(new NumberEvidence(
                    parsed.Value,
                    parsed.DecimalPlaces,
                    parsed.IsPercentage,
                    origin,
                    sourceName ?? string.Empty,
                    messageId ?? string.Empty,
                    EvidenceKind.NumericValue));
            }

            if (origin != EvidenceOrigin.ToolResult)
            {
                return;
            }

            var normalizedText = NumericValueParser.NormalizeDigitsAndPunctuation(text);
            foreach (Match match in CellRangeAddressPattern.Matches(normalizedText))
            {
                if (!int.TryParse(
                        match.Groups["startRow"].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var startRow)
                    || !int.TryParse(
                        match.Groups["endRow"].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var endRow)
                    || startRow > MaximumExcelRowNumber
                    || endRow > MaximumExcelRowNumber
                    || !IsValidExcelColumn(match.Groups["startColumn"].Value)
                    || !IsValidExcelColumn(match.Groups["endColumn"].Value))
                {
                    continue;
                }

                var rowCount = Math.Abs((long)endRow - startRow) + 1L;
                AddEvidence(new NumberEvidence(
                    rowCount,
                    0,
                    false,
                    origin,
                    sourceName ?? string.Empty,
                    messageId ?? string.Empty,
                    EvidenceKind.CellRangeRowSpan));
            }
        }

        private static bool IsValidExcelColumn(string columnName)
        {
            var columnNumber = 0;
            foreach (var character in columnName.ToUpperInvariant())
            {
                columnNumber = (columnNumber * 26) + character - 'A' + 1;
                if (columnNumber > 16384)
                {
                    return false;
                }
            }

            return columnNumber > 0;
        }

        private void AddEvidence(NumberEvidence evidence)
        {
            var key = new EvidenceKey(evidence);
            if (!_seen.Add(key))
            {
                return;
            }

            var target = evidence.Kind == EvidenceKind.CellRangeRowSpan
                ? _rangeRowSpans
                : _numbers;
            target.Add(evidence);
        }

        internal enum EvidenceOrigin
        {
            UserMessage,
            ToolResult,
            CalculationResult,
            DerivedCalculation
        }

        internal enum EvidenceKind
        {
            NumericValue,
            CellRangeRowSpan
        }

        internal sealed class NumberEvidence
        {
            public NumberEvidence(
                decimal value,
                int decimalPlaces,
                bool isPercentage,
                EvidenceOrigin origin,
                string sourceName,
                string messageId,
                EvidenceKind kind)
            {
                Value = value;
                DecimalPlaces = decimalPlaces;
                IsPercentage = isPercentage;
                Origin = origin;
                SourceName = sourceName;
                MessageId = messageId;
                Kind = kind;
            }

            public decimal Value { get; private set; }

            public int DecimalPlaces { get; private set; }

            public bool IsPercentage { get; private set; }

            public EvidenceOrigin Origin { get; private set; }

            public string SourceName { get; private set; }

            public string MessageId { get; private set; }

            public EvidenceKind Kind { get; private set; }
        }

        private struct EvidenceKey : IEquatable<EvidenceKey>
        {
            private readonly decimal _value;
            private readonly int _decimalPlaces;
            private readonly bool _isPercentage;
            private readonly EvidenceOrigin _origin;
            private readonly string _sourceName;
            private readonly string _messageId;
            private readonly EvidenceKind _kind;

            public EvidenceKey(NumberEvidence evidence)
            {
                _value = evidence.Value;
                _decimalPlaces = evidence.DecimalPlaces;
                _isPercentage = evidence.IsPercentage;
                _origin = evidence.Origin;
                _sourceName = evidence.SourceName;
                _messageId = evidence.MessageId;
                _kind = evidence.Kind;
            }

            public bool Equals(EvidenceKey other)
            {
                return _value == other._value
                    && _decimalPlaces == other._decimalPlaces
                    && _isPercentage == other._isPercentage
                    && _origin == other._origin
                    && _kind == other._kind
                    && string.Equals(_sourceName, other._sourceName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(_messageId, other._messageId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is EvidenceKey && Equals((EvidenceKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _value.GetHashCode();
                    hash = (hash * 397) ^ _decimalPlaces;
                    hash = (hash * 397) ^ _isPercentage.GetHashCode();
                    hash = (hash * 397) ^ _origin.GetHashCode();
                    hash = (hash * 397) ^ _kind.GetHashCode();
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_sourceName ?? string.Empty);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(_messageId ?? string.Empty);
                    return hash;
                }
            }
        }
    }
}
