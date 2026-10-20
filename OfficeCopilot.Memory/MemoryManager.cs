using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Memory
{
    /// <summary>
    /// 记忆管理器：为 UI 面板提供统一的记忆树构建 + 内容读写服务。
    /// 左侧树结构调用 <see cref="BuildTreeAsync"/>，右侧选中节点后调用 <see cref="LoadContentAsync"/> / <see cref="SaveContentAsync"/>。
    /// </summary>
    public sealed class MemoryManager
    {
        private readonly IMemoryStore _store;
        private readonly string _entrypointPath;
        private readonly string _fragmentsDir;

        public MemoryManager(IMemoryStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));

            // 尝试从 FileSystemMemoryStore 获取路径（若为其他实现则用默认路径）
            if (store is FileSystemMemoryStore fsStore)
            {
                _entrypointPath = fsStore.GetEntrypointFilePath();
                _fragmentsDir = fsStore.GetFragmentsDirectory();
            }
            else
            {
                _entrypointPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NixerOfficeCopilot", "memory", "MEMORY.md");
                _fragmentsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NixerOfficeCopilot", "memory", "fragments");
            }
        }

        // ========== 树结构 ==========

        /// <summary>
        /// 确保 MEMORY.md 存在且包含基本模板结构（首次使用或文件损坏时自动修复）。
        /// </summary>
        public async Task EnsureEntrypointExistsAsync()
        {
            await _store.LoadEntrypointAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// 构建树结构：MEMORY.md 为根节点，索引行和未索引片段为其子节点。
        /// </summary>
        public async Task<MemoryTreeNode> BuildTreeAsync(CancellationToken ct = default)
        {
            var entryResult = await _store.LoadEntrypointAsync(ct).ConfigureAwait(false);
            var fragments = await _store.ListFragmentsAsync(ct).ConfigureAwait(false);
            var fragmentDict = fragments.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

            var indexedFragmentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // MEMORY.md 作为根节点
            var entryNode = new MemoryTreeNode
            {
                Id = "entrypoint",
                Name = "MEMORY.md",
                NodeType = MemoryNodeType.Entrypoint,
                UpdatedAtUtc = File.Exists(_entrypointPath)
                    ? File.GetLastWriteTimeUtc(_entrypointPath)
                    : DateTime.MinValue
            };

            // 解析 MEMORY.md 中的索引行：  - [tag] 摘要 -> fragmentId.md
            if (!string.IsNullOrWhiteSpace(entryResult.Content))
            {
                var lines = entryResult.Content.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var trimmed = line.Trim();

                    if (!trimmed.StartsWith("- "))
                        continue;

                    var arrowIdx = trimmed.LastIndexOf("->");
                    if (arrowIdx < 0)
                        continue;

                    var fragmentRef = trimmed.Substring(arrowIdx + 2).Trim();
                    var hadMdSuffix = fragmentRef.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
                    if (hadMdSuffix)
                        fragmentRef = fragmentRef.Substring(0, fragmentRef.Length - 3);

                    if (string.IsNullOrWhiteSpace(fragmentRef))
                        continue;

                    indexedFragmentIds.Add(fragmentRef);

                    var summary = trimmed.Substring(2, arrowIdx - 2).Trim();
                    if (summary.StartsWith("[") && summary.Contains("] "))
                    {
                        var bracketEnd = summary.IndexOf("] ");
                        if (bracketEnd > 0)
                            summary = summary.Substring(bracketEnd + 2).Trim();
                    }

                    fragmentDict.TryGetValue(fragmentRef, out var frag);

                    entryNode.Children.Add(new MemoryTreeNode
                    {
                        Id = "index:" + fragmentRef,
                        Name = string.IsNullOrWhiteSpace(summary) ? fragmentRef : summary,
                        NodeType = MemoryNodeType.IndexEntry,
                        FragmentId = fragmentRef,
                        Tags = frag?.Tags ?? new List<string>(),
                        Scope = frag?.Scope.ToString() ?? "",
                        UpdatedAtUtc = frag?.UpdatedAtUtc ?? DateTime.MinValue
                    });
                }
            }

            var orphanFragments = fragments
                .Where(f => !indexedFragmentIds.Contains(f.Id))
                .OrderBy(f => f.UpdatedAtUtc)
                .ToList();

            foreach (var frag in orphanFragments)
            {
                entryNode.Children.Add(new MemoryTreeNode
                {
                    Id = frag.Id,
                    Name = frag.Content?.Length > 40
                        ? frag.Content.Substring(0, 40).Replace('\n', ' ') + "…"
                        : frag.Content?.Replace('\n', ' ') ?? frag.Id,
                    NodeType = MemoryNodeType.Fragment,
                    Tags = frag.Tags ?? new List<string>(),
                    Scope = frag.Scope.ToString(),
                    UpdatedAtUtc = frag.UpdatedAtUtc
                });
            }

            return entryNode;
        }

        // ========== 内容读写 ==========

        /// <summary>
        /// 加载节点的完整 Markdown 内容（含 YAML frontmatter）。
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <param name="nodeId">节点 ID</param>
        public Task<string> LoadContentAsync(MemoryNodeType nodeType, string nodeId)
        {
            return nodeType switch
            {
                MemoryNodeType.Entrypoint => LoadEntrypointRawAsync(),
                MemoryNodeType.Fragment => LoadFragmentRawAsync(nodeId),
                MemoryNodeType.IndexEntry => LoadFragmentRawAsync(IndexNodeIdToFragmentId(nodeId)),
                _ => Task.FromResult(string.Empty)
            };
        }

        /// <summary>
        /// 保存节点的 Markdown 内容。
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <param name="nodeId">节点 ID（Fragment 时必需）</param>
        /// <param name="rawContent">完整的 Markdown 文本（Entrypoint: 纯 Markdown；Fragment: YAML frontmatter + Markdown body）</param>
        public async Task SaveContentAsync(MemoryNodeType nodeType, string nodeId, string rawContent)
        {
            switch (nodeType)
            {
                case MemoryNodeType.Entrypoint:
                    await _store.SaveEntrypointAsync(rawContent, CancellationToken.None).ConfigureAwait(false);
                    break;

                case MemoryNodeType.Fragment:
                case MemoryNodeType.IndexEntry:
                    {
                        var fragId = nodeType == MemoryNodeType.IndexEntry
                            ? IndexNodeIdToFragmentId(nodeId)
                            : nodeId;
                        if (string.IsNullOrWhiteSpace(fragId))
                            throw new ArgumentException("Fragment nodeId is required.", nameof(nodeId));
                        await SaveFragmentRawAsync(fragId, rawContent).ConfigureAwait(false);
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Cannot save node of type {nodeType}.");
            }
        }

        // ========== Fragment 管理 ==========

        /// <summary>列出所有片段。</summary>
        public Task<IReadOnlyList<MemoryFragment>> ListFragmentsAsync(CancellationToken ct = default)
            => _store.ListFragmentsAsync(ct);

        /// <summary>删除一个片段。</summary>
        public Task DeleteFragmentAsync(string fragmentId, CancellationToken ct = default)
            => _store.DeleteFragmentAsync(fragmentId, ct);

        /// <summary>删除片段，同时从 MEMORY.md 中移除对应的索引行。</summary>
        public async Task DeleteFragmentAndIndexAsync(string fragmentId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(fragmentId))
            {
                return;
            }

            // 1. Delete fragment file (only if it exists)
            var fragPath = Path.Combine(_fragmentsDir, fragmentId + ".md");
            var fragExists = File.Exists(fragPath);

            if (fragExists)
            {
                await _store.DeleteFragmentAsync(fragmentId, ct).ConfigureAwait(false);
            }

            // 2. Remove corresponding index line from MEMORY.md
            var entryExists = File.Exists(_entrypointPath);

            if (!entryExists)
            {
                return;
            }

            var raw = File.ReadAllText(_entrypointPath, System.Text.Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var matchPattern = "-> " + fragmentId + ".md";

            var newLines = lines
                .Where(line =>
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("- ")
                        && line.IndexOf(matchPattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                    return true;
                })
                .ToArray();

            if (newLines.Length != lines.Length)
            {
                var newline = raw.Contains("\r\n") ? "\r\n"
                    : raw.Contains("\r") ? "\r" : "\n";
                var trailing = (raw.EndsWith("\n") || raw.EndsWith("\r")) ? newline : "";
                File.WriteAllText(_entrypointPath,
                    string.Join(newline, newLines) + trailing,
                    System.Text.Encoding.UTF8);
            }
        }

        /// <summary>创建一个新的空白片段并返回其 ID。</summary>
        public async Task<string> CreateFragmentAsync(
            string type, string scope, string content, CancellationToken ct = default)
        {
            var fragmentId = GenerateFragmentId(type);
            var fragment = new MemoryFragment
            {
                Id = fragmentId,
                Type = type ?? "note",
                Scope = ParseScope(scope),
                Tags = new List<string>(),
                Content = content ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await _store.SaveFragmentAsync(fragment, ct).ConfigureAwait(false);
            return fragmentId;
        }

        // ========== 统计 ==========

        /// <summary>获取记忆系统统计信息。</summary>
        public async Task<MemoryStats> GetStatsAsync(CancellationToken ct = default)
        {
            var entryResult = await _store.LoadEntrypointAsync(ct).ConfigureAwait(false);
            var fragments = await _store.ListFragmentsAsync(ct).ConfigureAwait(false);

            long totalFragmentsSize = 0;
            if (Directory.Exists(_fragmentsDir))
            {
                foreach (var file in Directory.GetFiles(_fragmentsDir, "*.md"))
                {
                    try { totalFragmentsSize += new FileInfo(file).Length; }
                    catch { /* skip inaccessible files */ }
                }
            }

            long entrypointSize = 0;
            if (File.Exists(_entrypointPath))
            {
                try { entrypointSize = new FileInfo(_entrypointPath).Length; }
                catch { /* skip */ }
            }

            return new MemoryStats
            {
                EntrypointSizeBytes = entrypointSize,
                EntrypointLineCount = entryResult.LineCount,
                EntrypointTruncated = entryResult.WasLineTruncated || entryResult.WasByteTruncated,
                FragmentCount = fragments.Count,
                TotalFragmentsSizeBytes = totalFragmentsSize
            };
        }

        // ========== 私有方法 ==========

        private Task<string> LoadEntrypointRawAsync()
        {
            if (!File.Exists(_entrypointPath))
                return Task.FromResult(string.Empty);

            return Task.FromResult(File.ReadAllText(_entrypointPath, System.Text.Encoding.UTF8));
        }

        private Task<string> LoadFragmentRawAsync(string fragmentId)
        {
            var filePath = Path.Combine(_fragmentsDir, fragmentId + ".md");
            if (!File.Exists(filePath))
                return Task.FromResult(string.Empty);

            return Task.FromResult(File.ReadAllText(filePath, System.Text.Encoding.UTF8));
        }

        private async Task SaveFragmentRawAsync(string fragmentId, string rawContent)
        {
            if (string.IsNullOrWhiteSpace(fragmentId))
                throw new ArgumentException("fragmentId is required.", nameof(fragmentId));

            // 解析 YAML frontmatter + body
            var (yamlText, body) = SplitFrontmatter(rawContent ?? string.Empty);

            // 从 YAML 解析元数据
            MemoryFragment fragment;
            if (!string.IsNullOrWhiteSpace(yamlText))
            {
                var fm = ParseFrontmatter(yamlText);
                fragment = new MemoryFragment
                {
                    Id = fragmentId,
                    Type = fm.Type ?? "note",
                    Scope = ParseScope(fm.Scope),
                    Tags = fm.Tags ?? new List<string>(),
                    Content = body ?? string.Empty,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }
            else
            {
                // 没有 YAML frontmatter，保留原有元数据（如果存在）
                var existing = await LoadFragmentRawAsync(fragmentId).ConfigureAwait(false);
                var (existingYaml, _) = SplitFrontmatter(existing);
                var fm = !string.IsNullOrWhiteSpace(existingYaml)
                    ? ParseFrontmatter(existingYaml)
                    : new FragmentFrontmatter();

                fragment = new MemoryFragment
                {
                    Id = fragmentId,
                    Type = fm.Type ?? "note",
                    Scope = ParseScope(fm.Scope),
                    Tags = fm.Tags ?? new List<string>(),
                    Content = rawContent ?? string.Empty, // 全文作为 content（无 frontmatter 时）
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }

            await _store.SaveFragmentAsync(fragment, CancellationToken.None).ConfigureAwait(false);
        }

        private static (string yamlText, string body) SplitFrontmatter(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return (null, string.Empty);

            var normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n");
            if (!normalized.StartsWith("---\n"))
                return (null, raw);

            var endIdx = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            if (endIdx < 0)
                return (null, raw);

            var yamlText = normalized.Substring(4, endIdx - 4);
            var body = normalized.Substring(endIdx + 5).TrimStart();
            return (yamlText, body);
        }

        private FragmentFrontmatter ParseFrontmatter(string yamlText)
        {
            if (string.IsNullOrWhiteSpace(yamlText))
                return new FragmentFrontmatter();

            try
            {
                var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                    .IgnoreUnmatchedProperties()
                    .Build();
                return deserializer.Deserialize<FragmentFrontmatter>(yamlText);
            }
            catch
            {
                return new FragmentFrontmatter();
            }
        }

        private static string GenerateFragmentId(string type)
        {
            var prefix = string.IsNullOrWhiteSpace(type) ? "note" : type.ToLowerInvariant();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            return $"{prefix}-{timestamp}";
        }

        /// <summary>从 "index:fragmentId" 格式的节点 ID 中提取 fragmentId。</summary>
        private static string IndexNodeIdToFragmentId(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                return string.Empty;

            if (nodeId.StartsWith("index:", StringComparison.OrdinalIgnoreCase))
                return nodeId.Substring(6);

            return nodeId;
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

        /// <summary>YAML frontmatter 反序列化 POCO。</summary>
        private sealed class FragmentFrontmatter
        {
            // ReSharper disable UnusedAutoPropertyAccessor.Local
            public string Type { get; set; }
            public string Scope { get; set; }
            public List<string> Tags { get; set; } = new List<string>();
            public string UpdatedAt { get; set; }
            // ReSharper restore UnusedAutoPropertyAccessor.Local
        }
    }

    /// <summary>
    /// 记忆系统统计信息。
    /// </summary>
    public sealed class MemoryStats
    {
        /// <summary>MEMORY.md 文件大小（字节）。</summary>
        public long EntrypointSizeBytes { get; set; }

        /// <summary>MEMORY.md 行数。</summary>
        public int EntrypointLineCount { get; set; }

        /// <summary>MEMORY.md 是否被截断。</summary>
        public bool EntrypointTruncated { get; set; }

        /// <summary>片段数量。</summary>
        public int FragmentCount { get; set; }

        /// <summary>所有片段文件总大小（字节）。</summary>
        public long TotalFragmentsSizeBytes { get; set; }
    }
}
