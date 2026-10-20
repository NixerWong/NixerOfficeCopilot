using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Host.PowerPoint
{
    internal static class PowerPointFontNameResolver
    {
        internal static IList<string> NormalizePlans(IEnumerable<SlideBuildPlan> plans, ISet<string> registeredFontNames)
        {
            if (plans == null) throw new ArgumentNullException(nameof(plans));

            if (registeredFontNames == null || registeredFontNames.Count == 0)
            {
                return new List<string> { "Windows 注册字体字典中没有可用的字体名称。" };
            }

            var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plan in plans)
            {
                if (plan == null) throw new ArgumentException("字体计划中不能包含 null 页面。", nameof(plans));

                foreach (var textBlock in plan.TextBlocks ?? new List<PlannedTextBlock>())
                {
                    if (textBlock == null) continue;
                    textBlock.FontName = NormalizeName(textBlock.FontName, registeredFontNames, unresolved);
                    textBlock.FontNameFarEast = NormalizeName(textBlock.FontNameFarEast, registeredFontNames, unresolved);
                }

                foreach (var table in plan.Tables ?? new List<PlannedTable>())
                {
                    if (table == null) continue;
                    foreach (var row in table.Rows ?? new List<IList<PlannedTableCell>>())
                    {
                        if (row == null) continue;
                        foreach (var cell in row)
                        {
                            if (cell == null) continue;
                            cell.FontName = NormalizeName(cell.FontName, registeredFontNames, unresolved);
                            cell.FontNameFarEast = NormalizeName(cell.FontNameFarEast, registeredFontNames, unresolved);
                        }
                    }
                }
            }

            return unresolved.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizeName(string name, ISet<string> registeredFontNames, ISet<string> unresolved)
        {
            if (string.IsNullOrWhiteSpace(name) || FontPolicy.IsGenericFamily(name) || registeredFontNames.Contains(name))
            {
                return name;
            }

            var normalized = InstalledFontState.NormalizeFontName(name);
            if (!string.Equals(name, normalized, StringComparison.Ordinal) && registeredFontNames.Contains(normalized))
            {
                return normalized;
            }

            unresolved.Add(name);
            return name;
        }
    }
}
