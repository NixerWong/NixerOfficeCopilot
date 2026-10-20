using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Skills
{
    /// <summary>
    /// 从安装目录的内置 Skills 文件夹加载 SKILL.md 文件。
    /// 这些 Skill 随安装包分发，默认位于 &lt;installDir&gt;/BuiltInSkills/ 下。
    ///
    /// 与 <see cref="FileSystemSkillLoader"/> 的区别：
    /// - BuiltInSkillLoader 加载安装目录下的预设 Skill（如 skillify、record-to-memory）
    /// - FileSystemSkillLoader 加载 %APPDATA% 下的用户自定义 Skill
    ///
    /// 加载优先级：文件系统 Skill 可覆盖同名的内置 Skill（在 SkillRegistry 层面合并）。
    /// </summary>
    public sealed class BuiltInSkillLoader : ISkillSource
    {
        private readonly string _rootDirectory;

        /// <summary>
        /// 创建内置 Skill 加载器。
        /// </summary>
        /// <param name="rootDirectory">
        /// 内置 Skill 根目录。为 null 时自动探测：先查找 EntryAssembly 同级的 BuiltInSkills 目录，
        /// 再查找当前 Assembly 同级的 BuiltInSkills 目录。
        /// </param>
        public BuiltInSkillLoader(string rootDirectory = null)
        {
            _rootDirectory = rootDirectory ?? GetDefaultBuiltInSkillsDirectory();
        }

        public string SourceDescription => $"BuiltIn ({_rootDirectory})";

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
                SkillLog.Write($"[BuiltInSkillLoader] Directory not found: {_rootDirectory}");
                return results;
            }

            SkillLog.Write($"[BuiltInSkillLoader] Scanning: {_rootDirectory}");
            var subDirs = Directory.GetDirectories(_rootDirectory);
            SkillLog.Write($"[BuiltInSkillLoader] Found {subDirs.Length} subdirectories");

            foreach (var subDir in subDirs)
            {
                try
                {
                    var dirName = Path.GetFileName(subDir);
                    var skillMdPath = Path.Combine(subDir, "SKILL.md");
                    if (!File.Exists(skillMdPath))
                    {
                        SkillLog.Write($"[BuiltInSkillLoader] Skip '{dirName}': no SKILL.md");
                        continue;
                    }

                    var rawContent = File.ReadAllText(skillMdPath, System.Text.Encoding.UTF8);
                    var (frontmatterYaml, body) = FileSystemSkillLoader.SplitFrontmatter(rawContent);

                    if (string.IsNullOrWhiteSpace(frontmatterYaml))
                    {
                        SkillLog.Write($"[BuiltInSkillLoader] Skip '{dirName}': no YAML frontmatter");
                        continue;
                    }

                    var frontmatter = SkillFrontmatterParser.Parse(frontmatterYaml, skillMdPath);
                    if (frontmatter == null || string.IsNullOrWhiteSpace(frontmatter.Name))
                    {
                        SkillLog.Write($"[BuiltInSkillLoader] Skip '{dirName}': frontmatter missing 'name'");
                        continue;
                    }

                    results.Add(new ParsedSkillFile
                    {
                        Frontmatter = frontmatter,
                        Body = body ?? string.Empty,
                        FilePath = skillMdPath,
                        SourceKind = SkillSourceKind.BuiltIn
                    });

                    SkillLog.Write($"[BuiltInSkillLoader] Loaded '{frontmatter.Name}' from {skillMdPath}");
                }
                catch (Exception ex)
                {
                    SkillLog.Write($"[BuiltInSkillLoader] Error in '{subDir}': {ex.Message}");
                }
            }

            SkillLog.Write($"[BuiltInSkillLoader] Done: {results.Count} skills from {subDirs.Length} dirs");
            return results;
        }

        /// <summary>
        /// 获取默认内置 Skill 目录。
        /// 查找顺序（Office Add-in 兼容）：
        /// 1. AppDomain.BaseDirectory — Add-in 安装根目录（最可靠）
        /// 2. EntryAssembly 所在目录 — 独立 .exe 场景
        /// 3. 当前 Assembly（OfficeCopilot.Skills.dll）所在目录
        /// 4. 降级到当前目录
        /// </summary>
        public static string GetDefaultBuiltInSkillsDirectory()
        {
            var searchDirs = new List<string>();

            // 1. AppDomain.BaseDirectory — 独立 .exe 场景下最可靠
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir))
                searchDirs.Add(baseDir);

            // 2. EntryAssembly 目录（独立 .exe 场景）
            var entryAsm = Assembly.GetEntryAssembly();
            if (entryAsm != null)
            {
                var dir = Path.GetDirectoryName(entryAsm.Location);
                if (!string.IsNullOrWhiteSpace(dir))
                    searchDirs.Add(dir);
            }

            // 3. 当前 Assembly (OfficeCopilot.Skills.dll) 所在目录
            //    注意：VSTO add-in 场景下，AppDomain.BaseDirectory 是 Office 目录，
            //    而当前 DLL 的 Location 才是真正的安装目录。
            var thisAsm = typeof(BuiltInSkillLoader).Assembly;
            if (thisAsm != null)
            {
                var dir = Path.GetDirectoryName(thisAsm.Location);
                if (!string.IsNullOrWhiteSpace(dir))
                    searchDirs.Add(dir);
            }

            // 收集所有搜索目录及其父目录，去重后逐一检查
            var allCandidates = new List<string>(searchDirs);

            // 4. 添加上级目录 —— 处理 Broker 在子目录、BuiltInSkills 在父目录的情况
            foreach (var dir in searchDirs)
            {
                try
                {
                    var parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrWhiteSpace(parent) &&
                        !allCandidates.Exists(c => string.Equals(c, parent, StringComparison.OrdinalIgnoreCase)))
                    {
                        allCandidates.Add(parent);
                    }
                }
                catch
                {
                    // ignore invalid paths
                }
            }

            SkillLog.Write($"[BuiltInSkillLoader] Searching {allCandidates.Count} candidate dirs...");
            foreach (var dir in allCandidates)
            {
                var path = Path.Combine(dir, "BuiltInSkills");
                SkillLog.Write($"[BuiltInSkillLoader]   Check: {path}");
                if (Directory.Exists(path))
                {
                    SkillLog.Write($"[BuiltInSkillLoader] FOUND at: {path}");
                    return path;
                }
            }

            SkillLog.Write($"[BuiltInSkillLoader] NOT FOUND. Searched {allCandidates.Count}: {string.Join("; ", allCandidates)}");

            // Fallback: 当前工作目录
            return Path.Combine(".", "BuiltInSkills");
        }
    }
}
