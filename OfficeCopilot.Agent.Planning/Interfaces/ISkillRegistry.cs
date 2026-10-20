using System.Collections.Generic;

namespace OfficeCopilot.Agent.Planning
{
    public interface ISkillRegistry
    {
        IReadOnlyList<SkillDefinition> GetSkills();

        SkillDefinition FindByName(string skillName);

        /// <summary>添加或更新 Skill（用于热加载/动态注册）。</summary>
        void AddOrUpdate(SkillDefinition skill);
    }
}
