using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Agent.Planning
{
    public static class SkillToolFilter
    {
        public static IReadOnlyList<string> FilterToolNames(
            SkillDefinition skill,
            IEnumerable<string> availableToolNames)
        {
            return FilterToolNames(skill == null ? null : new[] { skill }, availableToolNames);
        }

        public static IReadOnlyList<string> FilterToolNames(
            IEnumerable<SkillDefinition> skills,
            IEnumerable<string> availableToolNames)
        {
            var available = (availableToolNames ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var activeSkills = (skills ?? Enumerable.Empty<SkillDefinition>())
                .Where(skill => skill != null)
                .ToList();

            if (activeSkills.Count == 0)
            {
                return available;
            }

            var allowAll = activeSkills.Any(skill => skill.AllowedToolNames == null || skill.AllowedToolNames.Count == 0);
            if (allowAll)
            {
                return OrderToolNames(activeSkills, available);
            }

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var skill in activeSkills)
            {
                foreach (var toolName in skill.AllowedToolNames)
                {
                    if (!string.IsNullOrWhiteSpace(toolName))
                    {
                        allowed.Add(toolName);
                    }
                }
            }

            return OrderToolNames(activeSkills, available.Where(allowed.Contains).ToList());
        }

        private static IReadOnlyList<string> OrderToolNames(
            SkillDefinition skill,
            IList<string> toolNames)
        {
            return OrderToolNames(skill == null ? null : new[] { skill }, toolNames);
        }

        private static IReadOnlyList<string> OrderToolNames(
            IEnumerable<SkillDefinition> skills,
            IList<string> toolNames)
        {
            var names = toolNames == null
                ? new List<string>()
                : toolNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var activeSkills = (skills ?? Enumerable.Empty<SkillDefinition>())
                .Where(skill => skill != null)
                .ToList();

            if (activeSkills.Count == 0)
            {
                return names;
            }

            var ordered = new List<string>();
            foreach (var skill in activeSkills)
            {
                if (skill.PreferredToolNames == null || skill.PreferredToolNames.Count == 0)
                {
                    continue;
                }

                foreach (var preferredToolName in skill.PreferredToolNames)
                {
                    if (string.IsNullOrWhiteSpace(preferredToolName))
                    {
                        continue;
                    }

                    var match = names.FirstOrDefault(name => string.Equals(name, preferredToolName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(match)
                        && !ordered.Any(name => string.Equals(name, match, StringComparison.OrdinalIgnoreCase)))
                    {
                        ordered.Add(match);
                    }
                }
            }

            foreach (var toolName in names)
            {
                if (!ordered.Any(name => string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase)))
                {
                    ordered.Add(toolName);
                }
            }

            return ordered;
        }
    }
}
