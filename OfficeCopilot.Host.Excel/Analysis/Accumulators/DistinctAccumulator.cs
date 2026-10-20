using System.Collections.Generic;
using System;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Host.Excel.Analysis.Accumulators
{
    internal sealed class DistinctAccumulator
    {
        private readonly HashSet<string> _values = new HashSet<string>(StringComparer.Ordinal);

        public int Count
        {
            get { return _values.Count; }
        }

        public void Accept(object value, string text)
        {
            if (!ExcelValueSemantics.IsEmpty(value))
            {
                _values.Add(text ?? string.Empty);
            }
        }
    }
}
