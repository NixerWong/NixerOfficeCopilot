using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Analysis.Accumulators
{
    internal sealed class ColumnProfileAccumulator
    {
        private readonly bool _isDate;
        private readonly Dictionary<string, int> _frequencies =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _kinds = new HashSet<string>(StringComparer.Ordinal);
        private int _numericCount;
        private double _sum;
        private double? _numericMin;
        private double? _numericMax;
        private string _textMin;
        private string _textMax;
        private int _emptyCount;
        private int _nonEmptyCount;
        private int _errorCount;
        private int _textNumberCount;
        private readonly List<int> _textNumberSampleRows = new List<int>();

        public ColumnProfileAccumulator(bool isDate)
        {
            _isDate = isDate;
        }

        public void Accept(object value)
        {
            Accept(value, null);
        }

        public void Accept(object value, int? worksheetRow)
        {
            if (ExcelValueSemantics.IsEmpty(value))
            {
                _emptyCount++;
                return;
            }

            _nonEmptyCount++;
            if (ExcelValueSemantics.IsError(value))
            {
                _errorCount++;
            }

            _kinds.Add(ExcelValueSemantics.ClassifyKind(value, _isDate));
            var text = ExcelValueSemantics.ToText(value, _isDate);
            int existing;
            _frequencies[text] = _frequencies.TryGetValue(text, out existing) ? existing + 1 : 1;

            double number;
            if (!ExcelValueSemantics.IsError(value) && ExcelValueSemantics.TryGetNumber(value, out number))
            {
                _numericCount++;
                if (value is string)
                {
                    _textNumberCount++;
                    if (worksheetRow.HasValue && _textNumberSampleRows.Count < 10)
                    {
                        _textNumberSampleRows.Add(worksheetRow.Value);
                    }
                }

                _sum += number;
                if (!_numericMin.HasValue || number < _numericMin.Value)
                {
                    _numericMin = number;
                }

                if (!_numericMax.HasValue || number > _numericMax.Value)
                {
                    _numericMax = number;
                }
            }
            else
            {
                if (_textMin == null || string.CompareOrdinal(text, _textMin) < 0)
                {
                    _textMin = text;
                }

                if (_textMax == null || string.CompareOrdinal(text, _textMax) > 0)
                {
                    _textMax = text;
                }
            }
        }

        public int NumericCount
        {
            get { return _numericCount; }
        }

        public int TextNumberCount
        {
            get { return _textNumberCount; }
        }

        public int NonEmptyCount
        {
            get { return _nonEmptyCount; }
        }

        public IList<int> TextNumberSampleRows
        {
            get { return _textNumberSampleRows; }
        }

        public ExcelColumnProfile ToProfile(int index, string name, int topK)
        {
            var effectiveTopK = topK > 0 ? topK : 10;
            var profile = new ExcelColumnProfile
            {
                Index = index,
                Name = name,
                EmptyCount = _emptyCount,
                NonEmptyCount = _nonEmptyCount,
                ErrorCount = _errorCount,
                DistinctCount = _frequencies.Count,
                InferredType = ExcelAggregationEngine.ResolveInferredType(_kinds)
            };

            if (_numericCount > 0)
            {
                profile.HasNumericStats = true;
                profile.Sum = Math.Round(_sum, 10);
                profile.Average = Math.Round(_sum / _numericCount, 10);
                profile.MinText = _numericMin.HasValue
                    ? ExcelValueSemantics.ToText(_numericMin.Value, _isDate)
                    : string.Empty;
                profile.MaxText = _numericMax.HasValue
                    ? ExcelValueSemantics.ToText(_numericMax.Value, _isDate)
                    : string.Empty;
            }
            else
            {
                profile.MinText = _textMin ?? string.Empty;
                profile.MaxText = _textMax ?? string.Empty;
            }

            profile.TopValues = _frequencies
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(effectiveTopK)
                .Select(pair => new ExcelValueFrequency { Value = pair.Key, Count = pair.Value })
                .ToList();
            return profile;
        }
    }
}
