using System;
using System.Globalization;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Host.Excel.Analysis.Accumulators
{
    internal sealed class ScalarAccumulator
    {
        private readonly DistinctAccumulator _distinct = new DistinctAccumulator();

        public int Count { get; private set; }

        public int CountA { get; private set; }

        public int NumericCount { get; private set; }

        public int NonNumericCount { get; private set; }

        public int TextNumberCount { get; private set; }

        public int ErrorCount { get; private set; }

        public int BlankCount { get; private set; }

        public double Sum { get; private set; }

        public double? Min { get; private set; }

        public double? Max { get; private set; }

        public string TextMin { get; private set; }

        public string TextMax { get; private set; }

        public string First { get; private set; }

        public string Last { get; private set; }

        public int DistinctCount
        {
            get { return _distinct.Count; }
        }

        public void Accept(object value, string text, bool countOnly)
        {
            if (countOnly)
            {
                Count++;
                CountA++;
                return;
            }

            if (ExcelValueSemantics.IsEmpty(value))
            {
                BlankCount++;
                return;
            }

            Count++;
            CountA++;
            var normalizedText = text ?? string.Empty;
            _distinct.Accept(value, normalizedText);

            if (First == null)
            {
                First = normalizedText;
            }

            Last = normalizedText;

            if (ExcelValueSemantics.IsError(value))
            {
                ErrorCount++;
            }

            double number;
            if (!ExcelValueSemantics.IsError(value) && ExcelValueSemantics.TryGetNumber(value, out number))
            {
                NumericCount++;
                if (value is string)
                {
                    TextNumberCount++;
                }

                Sum += number;
                if (!Min.HasValue || number < Min.Value)
                {
                    Min = number;
                }

                if (!Max.HasValue || number > Max.Value)
                {
                    Max = number;
                }
            }
            else
            {
                NonNumericCount++;
                if (TextMin == null || string.CompareOrdinal(normalizedText, TextMin) < 0)
                {
                    TextMin = normalizedText;
                }

                if (TextMax == null || string.CompareOrdinal(normalizedText, TextMax) > 0)
                {
                    TextMax = normalizedText;
                }
            }
        }

        public string Render(string function, bool isDateColumn)
        {
            switch (function)
            {
                case "sum":
                    return ExcelValueSemantics.FormatNumber(Math.Round(Sum, 10));
                case "count":
                    return Count.ToString(CultureInfo.InvariantCulture);
                case "countdistinct":
                    return DistinctCount.ToString(CultureInfo.InvariantCulture);
                case "errorcount":
                    return ErrorCount.ToString(CultureInfo.InvariantCulture);
                case "average":
                case "avg":
                    return NumericCount == 0
                        ? string.Empty
                        : ExcelValueSemantics.FormatNumber(Math.Round(Sum / NumericCount, 10));
                case "min":
                    return Min.HasValue ? ExcelValueSemantics.ToText(Min.Value, isDateColumn) : (TextMin ?? string.Empty);
                case "max":
                    return Max.HasValue ? ExcelValueSemantics.ToText(Max.Value, isDateColumn) : (TextMax ?? string.Empty);
                case "first":
                    return First ?? string.Empty;
                default:
                    return Last ?? string.Empty;
            }
        }

        public double? RenderNumeric(string function)
        {
            switch (function)
            {
                case "sum": return Sum;
                case "count": return Count;
                case "countdistinct": return DistinctCount;
                case "errorcount": return ErrorCount;
                case "average":
                case "avg": return NumericCount == 0 ? (double?)null : Sum / NumericCount;
                case "min": return Min;
                case "max": return Max;
                default: return null;
            }
        }
    }
}
