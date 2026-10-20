using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Skills;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Agent.Runtime.Composition
{
    /// <summary>
    /// 每次读取前检查用户技能目录下 SKILL.md 的指纹（路径、修改时间、长度）。
    /// 指纹变化时重新加载，使设置窗口里的启用/禁用、新建或编辑的技能无需重启即可生效。
    /// </summary>
    public sealed class ReloadingSkillRegistry : ISkillRegistry
    {
        private readonly object _gate = new object();
        private readonly Func<InMemorySkillRegistry> _load;
        private readonly string _userSkillsDirectory;
        private InMemorySkillRegistry _current;
        private string _fingerprint;

        public ReloadingSkillRegistry(Func<InMemorySkillRegistry> load, string userSkillsDirectory)
        {
            _load = load ?? throw new ArgumentNullException(nameof(load));
            _userSkillsDirectory = userSkillsDirectory ?? throw new ArgumentNullException(nameof(userSkillsDirectory));
            Current();
        }

        public IReadOnlyList<SkillDefinition> GetSkills()
        {
            return Current().GetSkills();
        }

        public SkillDefinition FindByName(string skillName)
        {
            return Current().FindByName(skillName);
        }

        public void AddOrUpdate(SkillDefinition skill)
        {
            Current().AddOrUpdate(skill);
        }

        private InMemorySkillRegistry Current()
        {
            lock (_gate)
            {
                var fingerprint = ComputeFingerprint(_userSkillsDirectory);
                if (_current == null || !string.Equals(fingerprint, _fingerprint, StringComparison.Ordinal))
                {
                    if (_current != null)
                    {
                        SkillLog.Write("[AgentRuntime] User SKILL.md files changed; reloading skill registry.");
                    }

                    _current = _load();
                    _fingerprint = fingerprint;
                }

                return _current;
            }
        }

        internal static string ComputeFingerprint(string rootDirectory)
        {
            try
            {
                if (!Directory.Exists(rootDirectory))
                {
                    return string.Empty;
                }

                var parts = new List<string>();
                foreach (var subDir in Directory.GetDirectories(rootDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    var skillFile = Path.Combine(subDir, "SKILL.md");
                    if (!File.Exists(skillFile))
                    {
                        continue;
                    }

                    var info = new FileInfo(skillFile);
                    parts.Add($"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}");
                }

                return string.Join("\n", parts);
            }
            catch (IOException)
            {
                // 无法枚举时返回唯一值，保证本次读取强制重新加载，而不是沿用可能过期的结果。
                return Guid.NewGuid().ToString();
            }
            catch (UnauthorizedAccessException)
            {
                return Guid.NewGuid().ToString();
            }
        }
    }
}
