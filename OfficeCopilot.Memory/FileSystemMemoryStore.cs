using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OfficeCopilot.Memory
{
    /// <summary>
    /// 基于文件系统的记忆存储实现。
    /// 布局：
    /// <c>%APPDATA%/NixerOfficeCopilot/memory/MEMORY.md</c> — 入口文件
    /// <c>%APPDATA%/NixerOfficeCopilot/memory/fragments/*.md</c> — 记忆片段
    ///
    /// 与 cc-haha 对齐的常量：
    /// <list type="bullet">
    /// <item><c>MAX_ENTRYPOINT_LINES = 200</c></item>
    /// <item><c>MAX_ENTRYPOINT_BYTES = 25_000</c></item>
    /// </list>
    /// </summary>
    public sealed class FileSystemMemoryStore : IMemoryStore
    {
        public const string EntrypointName = "MEMORY.md";
        public const int MaxEntrypointLines = 200;
        public const int MaxEntrypointBytes = 25_000;
        private const string FragmentsDirName = "fragments";

        private readonly string _rootDirectory;
        private readonly IDeserializer _yamlDeserializer;

        public FileSystemMemoryStore(string rootDirectory = null)
        {
            _rootDirectory = rootDirectory ?? GetDefaultMemoryDirectory();
            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public static string GetDefaultMemoryDirectory()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "NixerOfficeCopilot", "memory");
        }

        public const string DefaultEntrypointTemplate =
@"# 用户记忆索引

> 此文件由系统自动维护。每行索引对应一条记忆片段。
> 你可以手动编辑此文件来调整索引，或在记忆管理器面板中编辑。

## 偏好与习惯
<!-- 格式: - [tag] 摘要 -> fragment-id.md -->

## 项目约束

## 常见错误与教训

## 常用命令与环境
";

        // ---- Entrypoint ----

        public Task<EntrypointLoadResult> LoadEntrypointAsync(CancellationToken ct)
        {
            var path = Path.Combine(_rootDirectory, EntrypointName);

            try
            {
                var exists = File.Exists(path);
                var len = exists ? new FileInfo(path).Length : -1L;

                if (!exists || len == 0)
                {
                    WriteDefaultTemplate(path);
                    return Task.FromResult(TemplateResult());
                }

                var raw = File.ReadAllText(path, System.Text.Encoding.UTF8);

                if (string.IsNullOrWhiteSpace(raw))
                {
                    WriteDefaultTemplate(path);
                    return Task.FromResult(TemplateResult());
                }

                var result = TruncateEntrypoint(raw);
                return Task.FromResult(result);
            }
            catch (Exception)
            {
                return Task.FromResult(new EntrypointLoadResult());
            }
        }

        private static void WriteDefaultTemplate(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, DefaultEntrypointTemplate, System.Text.Encoding.UTF8);
        }

        private static EntrypointLoadResult TemplateResult() => new EntrypointLoadResult
        {
            Content = DefaultEntrypointTemplate,
            LineCount = DefaultEntrypointTemplate.Split('\n').Length,
            ByteCount = DefaultEntrypointTemplate.Length
        };

        /// <summary>
        /// 截断 MEMORY.md 内容。先按行截断，再按字节截断。
        /// 与 cc-haha <c>truncateEntrypointContent</c> 行为一致。
        /// </summary>
        public static EntrypointLoadResult TruncateEntrypoint(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new EntrypointLoadResult();
            }

            var trimmed = raw.Trim();
            var lines = trimmed.Split('\n');
            var lineCount = lines.Length;
            var byteCount = trimmed.Length;

            var wasLineTruncated = lineCount > MaxEntrypointLines;
            var wasByteTruncated = byteCount > MaxEntrypointBytes;

            if (!wasLineTruncated && !wasByteTruncated)
            {
                return new EntrypointLoadResult
                {
                    Content = trimmed,
                    LineCount = lineCount,
                    ByteCount = byteCount
                };
            }

            var truncated = wasLineTruncated
                ? string.Join("\n", lines.Take(MaxEntrypointLines))
                : trimmed;

            if (truncated.Length > MaxEntrypointBytes)
            {
                var cutAt = truncated.LastIndexOf('\n', MaxEntrypointBytes);
                truncated = truncated.Substring(0, cutAt > 0 ? cutAt : MaxEntrypointBytes);
            }

            var reason = wasByteTruncated && !wasLineTruncated
                ? $"{byteCount} bytes (limit: {MaxEntrypointBytes})"
                : wasLineTruncated && !wasByteTruncated
                    ? $"{lineCount} lines (limit: {MaxEntrypointLines})"
                    : $"{lineCount} lines and {byteCount} bytes";

            return new EntrypointLoadResult
            {
                Content = truncated
                    + $"\n\n> WARNING: MEMORY.md is {reason}. Only part of it was loaded. "
                    + "Keep index entries to one line; move detail into fragment files.",
                LineCount = lineCount,
                ByteCount = byteCount,
                WasLineTruncated = wasLineTruncated,
                WasByteTruncated = wasByteTruncated
            };
        }

        public Task SaveEntrypointAsync(string content, CancellationToken ct)
        {
            var dir = _rootDirectory;
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, EntrypointName);
            File.WriteAllText(path, content ?? string.Empty, System.Text.Encoding.UTF8);

            return Task.CompletedTask;
        }

        /// <summary>获取入口文件完整路径（供 UI 层使用）。</summary>
        public string GetEntrypointFilePath() => Path.Combine(_rootDirectory, EntrypointName);

        /// <summary>获取 fragments 目录路径（供 UI 层使用）。</summary>
        public string GetFragmentsDirectory() => Path.Combine(_rootDirectory, FragmentsDirName);

        public Task AppendEntrypointIndexAsync(string indexLine, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(indexLine))
                return Task.CompletedTask;

            var dir = _rootDirectory;
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, EntrypointName);

            // 读取现有内容，检查是否已有相同 fragmentId 的引用
            var existing = File.Exists(path) ? File.ReadAllText(path, System.Text.Encoding.UTF8) : string.Empty;
            var existingLines = existing.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // 提取 indexLine 中的 fragmentId（格式: "... -> xxx.md"）
            var arrowIdx = indexLine.LastIndexOf("->");
            if (arrowIdx > 0)
            {
                var fragmentRef = indexLine.Substring(arrowIdx + 2).Trim();
                // 去掉 .md 后缀
                if (fragmentRef.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    fragmentRef = fragmentRef.Substring(0, fragmentRef.Length - 3);

                // 如果已有引用同一 fragment 的行，跳过（去重）
                foreach (var line in existingLines)
                {
                    if (line.Contains(fragmentRef))
                        return Task.CompletedTask; // 已存在，跳过
                }
            }

            // 追加新行
            var newContent = existing.TrimEnd();
            if (!string.IsNullOrEmpty(newContent))
                newContent += "\n";
            newContent += indexLine.Trim() + "\n";

            File.WriteAllText(path, newContent, System.Text.Encoding.UTF8);
            return Task.CompletedTask;
        }

        // ---- Fragments ----

        public Task<IReadOnlyList<MemoryFragment>> ListFragmentsAsync(CancellationToken ct)
        {
            var dir = Path.Combine(_rootDirectory, FragmentsDirName);
            if (!Directory.Exists(dir))
                return Task.FromResult<IReadOnlyList<MemoryFragment>>(Array.Empty<MemoryFragment>());

            var fragments = new List<MemoryFragment>();
            foreach (var file in Directory.GetFiles(dir, "*.md"))
            {
                try
                {
                    var fragment = ParseFragmentFile(file);
                    if (fragment != null)
                        fragments.Add(fragment);
                }
                catch (Exception)
                {
                }
            }

            fragments.Sort((a, b) => b.UpdatedAtUtc.CompareTo(a.UpdatedAtUtc));
            return Task.FromResult<IReadOnlyList<MemoryFragment>>(fragments);
        }

        public async Task<IReadOnlyList<MemoryFragment>> FindRelevantAsync(
            string query, int topK, CancellationToken ct)
        {
            var all = await ListFragmentsAsync(ct).ConfigureAwait(false);
            if (all.Count == 0 || string.IsNullOrWhiteSpace(query))
                return Array.Empty<MemoryFragment>();

            // Simple keyword-based relevance scoring
            var keywords = query.ToLowerInvariant()
                .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            var scored = all
                .Select(f => new
                {
                    Fragment = f,
                    Score = keywords.Sum(kw =>
                        (f.Content?.ToLowerInvariant().Contains(kw) ?? false ? 3 : 0)
                        + (f.Tags?.Any(t => t.ToLowerInvariant().Contains(kw)) ?? false ? 5 : 0)
                        + (f.Type?.ToLowerInvariant().Contains(kw) ?? false ? 2 : 0))
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(Math.Max(1, topK))
                .Select(x => x.Fragment)
                .ToList();

            return scored;
        }

        public async Task SaveFragmentAsync(MemoryFragment fragment, CancellationToken ct)
        {
            if (fragment == null || string.IsNullOrWhiteSpace(fragment.Id))
                return;

            var dir = Path.Combine(_rootDirectory, FragmentsDirName);
            Directory.CreateDirectory(dir);

            var filePath = Path.Combine(dir, fragment.Id + ".md");

            var yaml = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Serialize(new FragmentFrontmatter
                {
                    Type = fragment.Type,
                    Scope = fragment.Scope.ToString().ToLowerInvariant(),
                    Tags = fragment.Tags,
                    UpdatedAt = fragment.UpdatedAtUtc.ToString("O")
                });

            var content = $"---\n{yaml}---\n{fragment.Content}";
#if NET8_0_OR_GREATER
            await File.WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);
#else
            ct.ThrowIfCancellationRequested();
            File.WriteAllText(filePath, content, System.Text.Encoding.UTF8);
            await Task.CompletedTask.ConfigureAwait(false);
#endif
        }

        public Task DeleteFragmentAsync(string fragmentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(fragmentId))
                return Task.CompletedTask;

            var filePath = Path.Combine(_rootDirectory, FragmentsDirName, fragmentId + ".md");
            if (File.Exists(filePath))
                File.Delete(filePath);

            return Task.CompletedTask;
        }

        public Task<int> CountFragmentsForConversationAsync(string conversationId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
                return Task.FromResult(0);

            var fragmentsDir = Path.Combine(_rootDirectory, FragmentsDirName);
            if (!Directory.Exists(fragmentsDir))
                return Task.FromResult(0);

            var prefix = "auto-" + conversationId + "-";
            var count = Directory.GetFiles(fragmentsDir, prefix + "*.md").Length;
            return Task.FromResult(count);
        }

        // ---- Private helpers ----

        private MemoryFragment ParseFragmentFile(string filePath)
        {
            var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var normalized = content.Replace("\r\n", "\n").Replace("\r", "\n");

            string yamlText = null;
            string body = normalized;

            if (normalized.StartsWith("---\n"))
            {
                var endIdx = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
                if (endIdx > 0)
                {
                    yamlText = normalized.Substring(4, endIdx - 4);
                    body = normalized.Substring(endIdx + 5).TrimStart();
                }
            }

            var fm = yamlText != null
                ? _yamlDeserializer.Deserialize<FragmentFrontmatter>(yamlText)
                : new FragmentFrontmatter();

            return new MemoryFragment
            {
                Id = Path.GetFileNameWithoutExtension(filePath),
                Type = fm.Type ?? string.Empty,
                Scope = ParseScope(fm.Scope),
                Tags = fm.Tags ?? new List<string>(),
                Content = body ?? string.Empty,
                UpdatedAtUtc = ParseDateTime(fm.UpdatedAt) ?? File.GetLastWriteTimeUtc(filePath)
            };
        }

        private static MemoryScope ParseScope(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope))
                return MemoryScope.User;

            return scope.ToLowerInvariant() switch
            {
                "workspace" => MemoryScope.Workspace,
                "session" => MemoryScope.Session,
                _ => MemoryScope.User
            };
        }

        private static DateTime? ParseDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTime.TryParse(value, out var dt) ? dt : (DateTime?)null;
        }

        // ---- YAML frontmatter for fragment files ----

        private sealed class FragmentFrontmatter
        {
            public string Type { get; set; }
            public string Scope { get; set; }
            public List<string> Tags { get; set; } = new List<string>();
            public string UpdatedAt { get; set; }
        }
    }
}
