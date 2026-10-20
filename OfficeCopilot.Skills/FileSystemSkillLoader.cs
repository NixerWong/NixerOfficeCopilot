using OfficeCopilot.Agent.Planning;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Skills
{
    /// <summary>
    /// 从文件系统加载 SKILL.md 文件。
    /// 扫描 <paramref name="rootDirectory"/> 下的每个子目录；应传入宿主专属目录，见 <see cref="SkillStorageLocations"/>。
    /// 参考 cc-haha <c>loadSkillsDir.ts</c> 的目录扫描逻辑。
    /// </summary>
    public sealed class FileSystemSkillLoader : ISkillSource
    {
        private readonly string _rootDirectory;
        private readonly string _hostKind;

        /// <param name="rootDirectory">扫描根目录；空串表示没有可加载的目录。</param>
        /// <param name="hostKind">所属宿主（如 excel），非空时覆盖每个 Skill 的 host 元数据，使其只对该宿主可见。</param>
        public FileSystemSkillLoader(string rootDirectory, string hostKind = null)
        {
            _rootDirectory = rootDirectory ?? string.Empty;
            _hostKind = hostKind ?? string.Empty;
        }

        public string SourceDescription => $"FileSystem ({_rootDirectory})";

        public Task<IReadOnlyList<ParsedSkillFile>> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Load());
        }

        public Task ReloadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Load());
        }

        /// <summary>
        /// 同步加载。本类的 IO 全是同步的，<see cref="LoadAsync"/> 只是 `Task.FromResult` 包装；
        /// 构造期调用方应直接用本方法，避免 `.GetAwaiter().GetResult()` 这种伪异步阻塞写法。
        /// </summary>
        public IReadOnlyList<ParsedSkillFile> Load()
        {
            var results = new List<ParsedSkillFile>();

            if (!Directory.Exists(_rootDirectory))
            {
                return results;
            }

            foreach (var subDir in Directory.GetDirectories(_rootDirectory))
            {
                try
                {
                    var skillMdPath = Path.Combine(subDir, "SKILL.md");
                    if (!File.Exists(skillMdPath))
                    {
                        continue;
                    }

                    var parsed = ParseSkillFile(skillMdPath);
                    if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Frontmatter.Name))
                    {
                        results.Add(parsed);
                    }
                }
                catch (Exception ex)
                {
                    SkillLog.Write($"Failed to load skill from '{subDir}': {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// 解析单个 SKILL.md 文件：分离 YAML frontmatter 和 Markdown 正文。
        /// 格式：<c>---\nYAML...\n---\nMarkdown 正文</c>
        /// </summary>
        private ParsedSkillFile ParseSkillFile(string filePath)
        {
            var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var (frontmatterYaml, body) = SplitFrontmatter(content);

            if (string.IsNullOrWhiteSpace(frontmatterYaml))
            {
                return null; // 无 frontmatter = 不是有效的 Skill 文件
            }

            var frontmatter = SkillFrontmatterParser.Parse(frontmatterYaml, filePath);
            if (string.IsNullOrWhiteSpace(frontmatter.Name))
            {
                SkillLog.Write($"Skipping '{filePath}': frontmatter has no name.");
                return null;
            }

            if (_hostKind.Length > 0)
            {
                // 目录即宿主归属：覆盖 SKILL.md 中可能声明的 host，保证用户 Skill 不会跨宿主出现。
                if (frontmatter.Metadata == null)
                {
                    frontmatter.Metadata = new Dictionary<string, string>();
                }

                frontmatter.Metadata[SkillMetadataKeys.HostKind] = _hostKind;
            }

            return new ParsedSkillFile
            {
                Frontmatter = frontmatter,
                Body = body ?? string.Empty,
                FilePath = filePath,
                SourceKind = SkillSourceKind.FileSystem
            };
        }

        /// <summary>
        /// 分离 YAML frontmatter 和正文。
        /// 与 cc-haha <c>parseFrontmatter</c> 行为一致：
        /// 文件以 <c>---\n</c> 开头，以前导 <c>---</c> 结束。
        /// </summary>
        public static (string frontmatterYaml, string body) SplitFrontmatter(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return (null, content);
            }

            content = content.Replace("\r\n", "\n").Replace("\r", "\n");

            // 必须以 ---\n 开头
            if (!content.StartsWith("---\n", StringComparison.Ordinal))
            {
                return (null, content);
            }

            // 查找第二个 ---（单独成行）
            var endIndex = content.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                // 也可能以 --- 结尾（文件末无换行）
                if (content.EndsWith("\n---", StringComparison.Ordinal))
                {
                    var yaml2 = content.Substring(4, content.Length - 4 - 4);
                    return (yaml2.Trim(), string.Empty);
                }

                return (null, content);
            }

            var yaml = content.Substring(4, endIndex - 4);
            var body = content.Substring(endIndex + 5); // 跳过 \n---\n
            return (yaml.Trim(), body.TrimStart());
        }

    }
}
