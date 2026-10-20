using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.OpenXml.Core
{
    /// <summary>
    /// Outcome of a template merge: how many placeholders were replaced, which
    /// keys were consumed, and which placeholders had no matching data.
    /// </summary>
    public sealed class MergeResult
    {
        public int ReplacedCount { get; }

        public IReadOnlyList<string> UnresolvedPlaceholders { get; }

        public IReadOnlyList<string> UsedKeys { get; }

        public MergeResult(int replacedCount, IEnumerable<string> unresolvedPlaceholders, IEnumerable<string> usedKeys)
        {
            ReplacedCount = replacedCount;
            UnresolvedPlaceholders = (unresolvedPlaceholders ?? Enumerable.Empty<string>()).ToList();
            UsedKeys = (usedKeys ?? Enumerable.Empty<string>()).ToList();
        }
    }
}
