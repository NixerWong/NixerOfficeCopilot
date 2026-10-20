using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Skills
{
    /// <summary>
    /// Skill 加载源：从文件系统或内置资源读取 SKILL.md 文件。
    /// </summary>
    public interface ISkillSource
    {
        /// <summary>加载所有 SKILL.md 文件并返回解析结果。</summary>
        Task<IReadOnlyList<ParsedSkillFile>> LoadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 重新读取源文件。注意：仅返回最新解析结果，不会更新任何已构建的 Skill 注册表；
        /// 运行中的注册表在 Broker 启动时构建，磁盘变更需重启后生效。
        /// </summary>
        Task ReloadAsync(CancellationToken cancellationToken);

        /// <summary>Skill 源描述（用于诊断日志）。</summary>
        string SourceDescription { get; }
    }
}
