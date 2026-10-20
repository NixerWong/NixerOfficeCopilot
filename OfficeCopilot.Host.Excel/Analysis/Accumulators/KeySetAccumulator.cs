using System;
using System.Collections.Generic;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Host.Excel.Analysis.Accumulators
{
    internal sealed class KeySetAccumulator
    {
        private readonly Dictionary<string, double> _totals =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _keysInOrder = new List<string>();

        public Dictionary<string, double> Totals
        {
            get { return _totals; }
        }

        public List<string> KeysInOrder
        {
            get { return _keysInOrder; }
        }

        public int RowCount { get; private set; }

        public double Total { get; private set; }

        public void Accept(string key, object rawValue)
        {
            key = key ?? string.Empty;
            RowCount++;

            double value;
            if (!ExcelValueSemantics.TryGetNumber(rawValue, out value))
            {
                value = 0d;
            }

            Total += value;

            double existing;
            if (_totals.TryGetValue(key, out existing))
            {
                _totals[key] = existing + value;
            }
            else
            {
                _totals.Add(key, value);
                _keysInOrder.Add(key);
            }
        }
    }
}
