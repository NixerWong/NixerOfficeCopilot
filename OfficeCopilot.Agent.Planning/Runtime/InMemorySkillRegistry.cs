using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Agent.Planning
{
    public sealed class InMemorySkillRegistry : ISkillRegistry
    {
        private readonly List<SkillDefinition> _skills;

        public InMemorySkillRegistry()
            : this(null)
        {
        }

        public InMemorySkillRegistry(IEnumerable<SkillDefinition> skills)
        {
            _skills = skills == null
                ? new List<SkillDefinition>()
                : skills
                    .Where(skill => skill != null && !string.IsNullOrWhiteSpace(skill.Name))
                    .Select(skill => skill.Clone())
                    .ToList();
        }

        public IReadOnlyList<SkillDefinition> GetSkills()
        {
            return _skills;
        }

        public SkillDefinition FindByName(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
            {
                return null;
            }

            return _skills.FirstOrDefault(
                skill => string.Equals(skill.Name, skillName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 添加或更新 Skill。如已存在同名 Skill 则替换。
        /// </summary>
        public void AddOrUpdate(SkillDefinition skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.Name))
                return;

            var existing = _skills.FindIndex(
                s => string.Equals(s.Name, skill.Name, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
                _skills[existing] = skill.Clone();
            else
                _skills.Add(skill.Clone());
        }
    }
}
