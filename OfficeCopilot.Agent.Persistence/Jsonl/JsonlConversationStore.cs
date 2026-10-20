using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeCopilot.Diagnostics.Logging;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Persistence.Jsonl
{
    /// <summary>
    /// 主力会话存储实现：把每条消息写成一行 JSON 追加到磁盘文件。
    /// 适合"对话流"这种 append-only 模型，配合简单 head record（首行）记录 metadata。
    ///
    /// 文件格式：每行一条 <see cref="JsonlRecord"/> 序列化结果，UTF-8 无 BOM。
    /// <list type="bullet">
    ///   <item><c>{"kind":"head","conversationId":"...","createdAtUtc":"...","metadata":{...}}</c></item>
    ///   <item><c>{"kind":"msg","payload":{ ...BrokerConversationMessage... }}</c></item>
    /// </list>
    /// 设计 head + msg 两种 kind 是为了将来加 compact / snip 时不破坏格式。
    ///
    /// 阶段 1.1 简化项：
    /// <list type="bullet">
    ///   <item><see cref="ListAsync"/> 走目录扫描 + 逐文件读首行。Sqlite 索引在 Phase 1.1.2 引入后替换。</item>
    ///   <item>同一 conversationId 跨日重新打开时，会沿用首次创建的日期目录文件（<see cref="JsonlPaths.TryResolveExistingPath"/>）。</item>
    ///   <item>不做并发锁。同一 broker 进程内同一会话由调用方串行；多进程并发同写一个文件不在阶段 1.1 范围。</item>
    /// </list>
    /// </summary>
    public sealed class JsonlConversationStore : IConversationStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            // 紧凑输出，每条记录单行
            WriteIndented = false,
            // camelCase 属性名 —— 文件人眼更易读，与 cc-haha 等社区项目格式一致
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            // 反序列化时也要能识别 camelCase
            PropertyNameCaseInsensitive = true,
            // 默认对中文不转义，保持文件可读
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly JsonlPaths _paths;
        private readonly SqliteConversationIndex _index;
        private int _indexReconciled;

        public JsonlConversationStore()
            : this(JsonlPaths.DefaultRootDirectory, useIndex: true)
        {
        }

        public JsonlConversationStore(string rootDirectory)
            : this(rootDirectory, useIndex: true)
        {
        }

        public JsonlConversationStore(string rootDirectory, bool useIndex)
            : this(new JsonlPaths(rootDirectory, () => DateTime.UtcNow), useIndex)
        {
        }

        /// <summary>
        /// 按宿主作用域创建存储：所有文件、索引都落在 <c>&lt;root&gt;\&lt;hostScope&gt;</c> 之下。
        /// <paramref name="rootDirectory"/> 为空时使用默认根目录。
        /// </summary>
        public JsonlConversationStore(string rootDirectory, string hostScope)
            : this(new JsonlPaths(ResolveScopedRoot(rootDirectory, hostScope), () => DateTime.UtcNow), useIndex: true)
        {
        }

        private static string ResolveScopedRoot(string rootDirectory, string hostScope)
        {
            ConversationScope.Validate(hostScope);

            var root = string.IsNullOrWhiteSpace(rootDirectory) ? JsonlPaths.DefaultRootDirectory : rootDirectory;
            return Path.Combine(root, hostScope);
        }

        // 暴露给测试：注入虚拟”今天“时钟以验证日期分桶。useIndex=false 可完全禁用 sqlite 索引（回退到目录扫描）。
        internal JsonlConversationStore(JsonlPaths paths)
            : this(paths, useIndex: true)
        {
        }

        internal JsonlConversationStore(JsonlPaths paths, bool useIndex)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _index = useIndex ? TryCreateIndex(_paths.SessionsDirectory) : null;
        }

        private static SqliteConversationIndex TryCreateIndex(string sessionsDirectory)
        {
            try
            {
                return new SqliteConversationIndex(sessionsDirectory);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[jsonl-store] sqlite index disabled: " + ex.Message);
                return null;
            }
        }

        public async Task<BrokerConversationState> GetOrCreateAsync(string conversationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = string.IsNullOrWhiteSpace(conversationId)
                ? Guid.NewGuid().ToString("N")
                : conversationId.Trim();

            var existingPath = _paths.TryResolveExistingPath(id);
            if (existingPath != null)
            {
                var conv = await LoadFromFileAsync(id, existingPath, cancellationToken).ConfigureAwait(false);
                await LoadCompactAsync(conv, cancellationToken).ConfigureAwait(false);
                return conv;
            }

            var conversation = new BrokerConversationState { ConversationId = id };
            await WriteHeadAsync(conversation, cancellationToken).ConfigureAwait(false);
            await LoadCompactAsync(conversation, cancellationToken).ConfigureAwait(false);
            return conversation;
        }

        public async Task AppendMessageAsync(
            string conversationId,
            BrokerConversationMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (message == null)
            {
                return;
            }

            var conversation = await GetOrCreateAsync(conversationId, cancellationToken).ConfigureAwait(false);
            if (conversation.Messages == null)
            {
                conversation.Messages = new List<BrokerConversationMessage>();
            }

            conversation.Messages.Add(message);
            conversation.UpdatedAtUtc = DateTime.UtcNow;

            var path = _paths.TryResolveExistingPath(conversation.ConversationId)
                ?? _paths.BuildTodayPath(conversation.ConversationId);

            await AppendLineAsync(path, BuildMessageRecord(message), cancellationToken).ConfigureAwait(false);

            // Also append to compact file if compact chain is active
            if (conversation.CompactMessages != null)
            {
                conversation.CompactMessages.Add(message);
                await AppendCompactMessageAsync(conversation.ConversationId, message, cancellationToken).ConfigureAwait(false);
            }

            // Phase 1.1b：追加单条也刷一次索引（messageCount / updatedAt 才能保持最新）。
            RefreshIndexForFile(conversation.ConversationId, path);
        }

        public async Task SaveAsync(BrokerConversationState conversation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (conversation == null || string.IsNullOrWhiteSpace(conversation.ConversationId))
            {
                return;
            }

            conversation.UpdatedAtUtc = DateTime.UtcNow;

            // 重写策略：原子替换（写临时文件 → 改名）。avoids 半写状态。
            var path = _paths.TryResolveExistingPath(conversation.ConversationId)
                ?? _paths.BuildTodayPath(conversation.ConversationId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var tempPath = path + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync(SerializeHead(conversation)).ConfigureAwait(false);

                if (conversation.Messages != null)
                {
                    foreach (var message in conversation.Messages)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (message == null)
                        {
                            continue;
                        }

                        await writer.WriteLineAsync(BuildMessageRecord(message)).ConfigureAwait(false);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }

            // Phase 1.1b：落盘成功后刷新 sqlite 索引摘要。失败由 index 自行 swallow + 写 stderr，不影响主流程。
            RefreshIndexForFile(conversation.ConversationId, path);

            // Also save compact chain if active
            if (conversation.CompactMessages != null)
            {
                await SaveCompactAsync(conversation, cancellationToken).ConfigureAwait(false);
            }

            OfficeCopilotLog.Write("Persistence", $"[jsonl-store] Save OK: {path} convId={conversation.ConversationId} msgs={conversation.Messages?.Count ?? 0}");
        }

        public Task<IReadOnlyList<ConversationSummary>> ListAsync(int take, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(_paths.SessionsDirectory))
            {
                OfficeCopilotLog.Write("Persistence", $"[jsonl-store] ListAsync: sessions dir not found: {_paths.SessionsDirectory}");
                return Task.FromResult<IReadOnlyList<ConversationSummary>>(Array.Empty<ConversationSummary>());
            }

            // Phase 1.1b：优先走 sqlite 索引；冷启动或索引落后磁盘时按需 lazy-reconcile。
            if (_index != null)
            {
                EnsureIndexReconciled(cancellationToken);
                var indexed = _index.List(take);
                OfficeCopilotLog.Write("Persistence", $"[jsonl-store] ListAsync: index returned {indexed.Count} summaries (take={take})");
                if (indexed.Count > 0)
                {
                    return Task.FromResult(indexed);
                }
                // 索引为空但磁盘可能有遗留文件——继续走目录扫描兜底。
            }

            var effectiveTake = take <= 0 ? int.MaxValue : take;
            var summaries = new List<ConversationSummary>();

            var files = Directory.EnumerateFiles(_paths.SessionsDirectory, "*.jsonl", SearchOption.AllDirectories)
                .Where(SqliteConversationIndex.IsIndexableConversationFile)
                .ToList();
            OfficeCopilotLog.Write("Persistence", $"[jsonl-store] ListAsync: directory scan found {files.Count} .jsonl files in {_paths.SessionsDirectory}");

            foreach (var file in files
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .Take(effectiveTake))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var summary = TryBuildSummaryFromFile(file);
                if (summary != null)
                {
                    summaries.Add(summary);
                    // 顺手补写索引，使后续 List 走快路径。
                    if (_index != null)
                    {
                        _index.Upsert(summary, file);
                    }
                }
            }

            return Task.FromResult<IReadOnlyList<ConversationSummary>>(summaries);
        }

        public Task<bool> DeleteAsync(string conversationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return Task.FromResult(false);
            }

            var path = _paths.TryResolveExistingPath(conversationId);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return Task.FromResult(false);
            }

            try
            {
                File.Delete(path);
                // Phase 1.1b：磁盘删除成功后同步索引；索引层失败仅写 stderr，不阻塞返回值。
                if (_index != null)
                {
                    _index.Delete(conversationId);
                }
                return Task.FromResult(true);
            }
            catch (IOException)
            {
                // 文件被占用等情况：吞掉异常，由上层视情况处理
                return Task.FromResult(false);
            }
        }

        // ------------------------------------------------------------------
        // 内部辅助
        // ------------------------------------------------------------------

        private void RefreshIndexForFile(string conversationId, string filePath)
        {
            if (_index == null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            try
            {
                var summary = TryBuildSummaryFromFile(filePath);
                if (summary != null)
                {
                    if (string.IsNullOrWhiteSpace(summary.ConversationId) && !string.IsNullOrWhiteSpace(conversationId))
                    {
                        summary.ConversationId = conversationId;
                    }

                    _index.Upsert(summary, filePath);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[jsonl-store] index refresh failed for '" + filePath + "': " + ex.Message);
            }
        }

        /// <summary>
        /// 第一次 List 时，若 sqlite 记录数 &lt; 磁盘 jsonl 文件数（说明索引落后或全新建立），
        /// 触发一次全量 reconcile。后续 List 直接走索引。线程安全：用 Interlocked 防止并发重复跑。
        /// </summary>
        private void EnsureIndexReconciled(CancellationToken cancellationToken)
        {
            if (_index == null || Interlocked.CompareExchange(ref _indexReconciled, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var indexCount = _index.Count();
                var diskCount = Directory
                    .EnumerateFiles(_paths.SessionsDirectory, "*.jsonl", SearchOption.AllDirectories)
                    .Count(SqliteConversationIndex.IsIndexableConversationFile);
                if (indexCount >= diskCount)
                {
                    return;
                }

                _index.RebuildFromDirectory(_paths.SessionsDirectory, TryBuildSummaryFromFile, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[jsonl-store] index reconcile failed: " + ex.Message);
            }
        }

        private async Task WriteHeadAsync(BrokerConversationState conversation, CancellationToken cancellationToken)
        {
            var path = _paths.BuildTodayPath(conversation.ConversationId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // 仅当文件不存在时写 head（GetOrCreateAsync 已先查过；这里防御性再判一次）
            if (File.Exists(path))
            {
                OfficeCopilotLog.Write("Persistence", $"[jsonl-store] WriteHead SKIP (already exists): {path}");
                return;
            }

            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync(SerializeHead(conversation)).ConfigureAwait(false);
            }

            OfficeCopilotLog.Write("Persistence", $"[jsonl-store] WriteHead OK: {path} convId={conversation.ConversationId}");
        }

        private static async Task AppendLineAsync(string path, string line, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }
        }

        private static async Task<BrokerConversationState> LoadFromFileAsync(
            string conversationId,
            string path,
            CancellationToken cancellationToken)
        {
            var conversation = new BrokerConversationState
            {
                ConversationId = conversationId,
                Messages = new List<BrokerConversationMessage>()
            };

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                string line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var record = TryDeserializeRecord(line);
                    if (record == null)
                    {
                        continue;
                    }

                    if (string.Equals(record.Kind, "head", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyHeadToConversation(record, conversation);
                    }
                    else if (string.Equals(record.Kind, "msg", StringComparison.OrdinalIgnoreCase)
                        && record.Payload != null)
                    {
                        conversation.Messages.Add(record.Payload);
                    }
                }
            }

            return conversation;
        }

        private static ConversationSummary TryBuildSummaryFromFile(string path)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                var summary = new ConversationSummary
                {
                    UpdatedAtUtc = fileInfo.LastWriteTimeUtc,
                    CreatedAtUtc = fileInfo.CreationTimeUtc
                };

                string firstUserContent = null;
                string explicitTitle = null;
                int messageCount = 0;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var record = TryDeserializeRecord(line);
                        if (record == null)
                        {
                            continue;
                        }

                        if (string.Equals(record.Kind, "head", StringComparison.OrdinalIgnoreCase))
                        {
                            summary.ConversationId = record.ConversationId ?? string.Empty;
                            if (record.CreatedAtUtc != default)
                            {
                                summary.CreatedAtUtc = record.CreatedAtUtc;
                            }
                            if (record.UpdatedAtUtc != default)
                            {
                                summary.UpdatedAtUtc = record.UpdatedAtUtc;
                            }
                            if (record.Metadata != null)
                            {
                                foreach (var kv in record.Metadata)
                                {
                                    if (!string.IsNullOrWhiteSpace(kv.Key))
                                    {
                                        summary.Metadata[kv.Key] = kv.Value ?? string.Empty;
                                    }
                                }

                                string hostApp;
                                if (record.Metadata.TryGetValue("hostApplicationName", out hostApp))
                                {
                                    summary.HostApplicationName = hostApp ?? string.Empty;
                                }

                                string title;
                                if (record.Metadata.TryGetValue("title", out title)
                                    && !string.IsNullOrWhiteSpace(title))
                                {
                                    explicitTitle = title.Trim();
                                }
                            }
                            continue;
                        }

                        if (string.Equals(record.Kind, "msg", StringComparison.OrdinalIgnoreCase) && record.Payload != null)
                        {
                            messageCount++;
                            if (firstUserContent == null
                                && string.Equals(record.Payload.Role, "user", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(record.Payload.Content))
                            {
                                var content = record.Payload.Content.Trim();
                                firstUserContent = content.Length > 80 ? content.Substring(0, 80) : content;
                            }
                        }
                    }
                }

                summary.MessageCount = messageCount;
                summary.Title = !string.IsNullOrWhiteSpace(explicitTitle) ? explicitTitle : (firstUserContent ?? string.Empty);
                if (string.IsNullOrEmpty(summary.ConversationId))
                {
                    summary.ConversationId = Path.GetFileNameWithoutExtension(path);
                }

                return summary;
            }
            catch
            {
                return null;  // 跳过损坏的文件，让 list 仍可返回其它会话
            }
        }

        // ------------------------------------------------------------------
        // 序列化层
        // ------------------------------------------------------------------

        private static string SerializeHead(BrokerConversationState conversation)
        {
            var record = new JsonlRecord
            {
                Kind = "head",
                ConversationId = conversation.ConversationId,
                CreatedAtUtc = conversation.CreatedAtUtc,
                UpdatedAtUtc = conversation.UpdatedAtUtc,
                Metadata = conversation.Metadata == null ? null : new Dictionary<string, string>(conversation.Metadata, StringComparer.OrdinalIgnoreCase)
            };
            return JsonSerializer.Serialize(record, JsonOptions);
        }

        // ------------------------------------------------------------------
        // Compact 文件（{id}.compact.jsonl）
        // ------------------------------------------------------------------

        private string GetCompactPath(string conversationId)
        {
            var basePath = _paths.TryResolveExistingPath(conversationId)
                ?? _paths.BuildTodayPath(conversationId);
            return Path.ChangeExtension(basePath, ".compact.jsonl");
        }

        /// <summary>
        /// 保存 CompactMessages 到 {id}.compact.jsonl。
        /// </summary>
        public async Task SaveCompactAsync(BrokerConversationState conversation, CancellationToken cancellationToken)
        {
            if (conversation == null || conversation.CompactMessages == null || conversation.CompactMessages.Count == 0)
            {
                OfficeCopilotLog.Write("Persistence", $"[jsonl-store] SaveCompactAsync SKIP: CompactMessages null or empty. convId={conversation?.ConversationId}");
                return;
            }

            try
            {
                var path = GetCompactPath(conversation.ConversationId);
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var tempPath = path + ".tmp";
                var count = 0;
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    foreach (var msg in conversation.CompactMessages)
                    {
                        if (msg == null) continue;
                        await writer.WriteLineAsync(BuildMessageRecord(msg)).ConfigureAwait(false);
                        count++;
                    }
                }

                File.Delete(path);
                File.Move(tempPath, path);
                OfficeCopilotLog.Write("Persistence", $"[jsonl-store] SaveCompactAsync OK: {path} msgs={count}");
            }
            catch (Exception ex)
            {
                OfficeCopilotLog.Write("Persistence", $"[jsonl-store] SaveCompactAsync FAILED: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载 CompactMessages 从 {id}.compact.jsonl。文件不存在时从 Messages 初始化。
        /// </summary>
        private async Task LoadCompactAsync(BrokerConversationState conversation, CancellationToken cancellationToken)
        {
            var path = GetCompactPath(conversation.ConversationId);
            if (!File.Exists(path))
            {
                // 兼容旧会话：没有 compact 文件时，从 Messages 初始化 CompactMessages
                if (conversation.Messages != null && conversation.Messages.Count > 0)
                {
                    conversation.CompactMessages = new List<BrokerConversationMessage>(conversation.Messages);
                }
                return;
            }

            var compactMessages = new List<BrokerConversationMessage>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var record = TryDeserializeRecord(line);
                    if (record?.Payload != null)
                        compactMessages.Add(record.Payload);
                }
            }

            if (compactMessages.Count > 0)
                conversation.CompactMessages = compactMessages;
        }

        /// <summary>
        /// 追加一条消息到 compact 文件。
        /// </summary>
        public async Task AppendCompactMessageAsync(string conversationId, BrokerConversationMessage message, CancellationToken cancellationToken)
        {
            if (message == null) return;
            var path = GetCompactPath(conversationId);
            await AppendLineAsync(path, BuildMessageRecord(message), cancellationToken).ConfigureAwait(false);
        }

        private static string BuildMessageRecord(BrokerConversationMessage message)
        {
            var record = new JsonlRecord
            {
                Kind = "msg",
                Payload = message
            };
            return JsonSerializer.Serialize(record, JsonOptions);
        }

        private static JsonlRecord TryDeserializeRecord(string line)
        {
            try
            {
                return JsonSerializer.Deserialize<JsonlRecord>(line, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyHeadToConversation(JsonlRecord record, BrokerConversationState conversation)
        {
            if (record.CreatedAtUtc != default)
            {
                conversation.CreatedAtUtc = record.CreatedAtUtc;
            }

            if (record.UpdatedAtUtc != default)
            {
                conversation.UpdatedAtUtc = record.UpdatedAtUtc;
            }

            if (record.Metadata != null && conversation.Metadata != null)
            {
                foreach (var pair in record.Metadata)
                {
                    conversation.Metadata[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>
        /// 文件中单行的并集结构：head 行用 ConversationId/CreatedAtUtc/Metadata；msg 行用 Payload。
        /// 没有用 polymorphism 是为了让 System.Text.Json 不需要 derived-type 设置，跨版本更稳。
        /// </summary>
        private sealed class JsonlRecord
        {
            public string Kind { get; set; }

            public string ConversationId { get; set; }

            public DateTime CreatedAtUtc { get; set; }

            public DateTime UpdatedAtUtc { get; set; }

            public Dictionary<string, string> Metadata { get; set; }

            public BrokerConversationMessage Payload { get; set; }
        }
    }

}
