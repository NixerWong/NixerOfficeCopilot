using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace OfficeCopilot.Agent.Persistence.Jsonl
{
    /// <summary>
    /// Phase 1.1b：JSONL 会话的 SQLite 摘要索引，避免 <see cref="JsonlConversationStore.ListAsync"/>
    /// 每次都做"扫目录 + 逐文件解析首行"。Schema 见下文 <see cref="EnsureSchema"/>。
    /// </summary>
    /// <remarks>
    /// 实现说明：
    /// <list type="bullet">
    /// <item>所有操作打开一个临时连接（SQLite 单文件场景下 open/close 成本极低），避免长连接的文件锁问题。</item>
    /// <item>索引文件位于 <c>&lt;rootDirectory&gt;/sessions/index.sqlite</c>，与 JSONL 文件共生命周期。</item>
    /// <item>启动时调用 <see cref="RebuildFromDirectory"/> 同步现有 JSONL 文件——索引丢失或落后 disk 时仍能恢复。</item>
    /// <item>查询和落盘失败一律 swallow（写日志到 stderr）——索引是性能优化，不能阻塞主流程。</item>
    /// </list>
    /// </remarks>
    internal sealed class SqliteConversationIndex
    {
        public const string IndexFileName = "index.sqlite";

        private readonly string _databasePath;
        private readonly string _connectionString;

        public SqliteConversationIndex(string sessionsDirectory)
        {
            if (string.IsNullOrWhiteSpace(sessionsDirectory))
            {
                throw new ArgumentException("Sessions directory is required.", nameof(sessionsDirectory));
            }

            Directory.CreateDirectory(sessionsDirectory);
            _databasePath = Path.Combine(sessionsDirectory, IndexFileName);
            _connectionString = "Data Source=" + _databasePath;

            EnsureSchema();
        }

        internal string DatabasePath
        {
            get { return _databasePath; }
        }

        private void EnsureSchema()
        {
            try
            {
                using (var connection = OpenConnection())
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS conversations (
    conv_id TEXT PRIMARY KEY,
    host_app TEXT NOT NULL DEFAULT '',
    title TEXT NOT NULL DEFAULT '',
    message_count INTEGER NOT NULL DEFAULT 0,
    created_at_utc TEXT NOT NULL DEFAULT '',
    updated_at_utc TEXT NOT NULL DEFAULT '',
    file_path TEXT NOT NULL DEFAULT '',
    metadata_json TEXT NOT NULL DEFAULT '{}'
);

CREATE INDEX IF NOT EXISTS idx_conv_updated_at ON conversations(updated_at_utc DESC);
CREATE INDEX IF NOT EXISTS idx_conv_host_updated_at ON conversations(host_app, updated_at_utc DESC);
";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[sqlite-index] schema init failed at '" + _databasePath + "': " + ex.Message);
            }
        }

        public void Upsert(ConversationSummary summary, string filePath)
        {
            if (summary == null || string.IsNullOrWhiteSpace(summary.ConversationId))
            {
                return;
            }

            try
            {
                using (var connection = OpenConnection())
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
INSERT INTO conversations
    (conv_id, host_app, title, message_count, created_at_utc, updated_at_utc, file_path, metadata_json)
VALUES
    ($conv_id, $host_app, $title, $message_count, $created, $updated, $file_path, $metadata)
ON CONFLICT(conv_id) DO UPDATE SET
    host_app = excluded.host_app,
    title = excluded.title,
    message_count = excluded.message_count,
    created_at_utc = excluded.created_at_utc,
    updated_at_utc = excluded.updated_at_utc,
    file_path = excluded.file_path,
    metadata_json = excluded.metadata_json;
";
                    cmd.Parameters.AddWithValue("$conv_id", summary.ConversationId);
                    cmd.Parameters.AddWithValue("$host_app", summary.HostApplicationName ?? string.Empty);
                    cmd.Parameters.AddWithValue("$title", summary.Title ?? string.Empty);
                    cmd.Parameters.AddWithValue("$message_count", summary.MessageCount);
                    cmd.Parameters.AddWithValue("$created", FormatUtc(summary.CreatedAtUtc));
                    cmd.Parameters.AddWithValue("$updated", FormatUtc(summary.UpdatedAtUtc));
                    cmd.Parameters.AddWithValue("$file_path", filePath ?? string.Empty);
                    cmd.Parameters.AddWithValue("$metadata", SerializeMetadata(summary.Metadata));

                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[sqlite-index] upsert failed for conv_id='" + summary.ConversationId + "': " + ex.Message);
            }
        }

        public void Delete(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return;
            }

            try
            {
                using (var connection = OpenConnection())
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM conversations WHERE conv_id = $conv_id;";
                    cmd.Parameters.AddWithValue("$conv_id", conversationId);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[sqlite-index] delete failed for conv_id='" + conversationId + "': " + ex.Message);
            }
        }

        public IReadOnlyList<ConversationSummary> List(int take)
        {
            var effectiveTake = take <= 0 ? int.MaxValue : take;
            var results = new List<ConversationSummary>();

            try
            {
                using (var connection = OpenConnection())
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT conv_id, host_app, title, message_count, created_at_utc, updated_at_utc, metadata_json
FROM conversations
ORDER BY updated_at_utc DESC
LIMIT $take;
";
                    cmd.Parameters.AddWithValue("$take", effectiveTake);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var summary = new ConversationSummary
                            {
                                ConversationId = reader.GetString(0),
                                HostApplicationName = reader.GetString(1),
                                Title = reader.GetString(2),
                                MessageCount = reader.GetInt32(3),
                                CreatedAtUtc = ParseUtc(reader.GetString(4)),
                                UpdatedAtUtc = ParseUtc(reader.GetString(5))
                            };

                            DeserializeMetadataInto(reader.GetString(6), summary);
                            results.Add(summary);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[sqlite-index] list failed: " + ex.Message);
            }

            return results;
        }

        public int Count()
        {
            try
            {
                using (var connection = OpenConnection())
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM conversations;";
                    var raw = cmd.ExecuteScalar();
                    return raw == null ? 0 : Convert.ToInt32(raw);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[sqlite-index] count failed: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// 扫描 <paramref name="sessionsDirectory"/> 下所有 <c>*.jsonl</c>，把它们的 head 摘要重建到索引中。
        /// 用于：索引文件丢失、broker 第一次启动且磁盘上已有遗留 JSONL、或运行时检测到索引落后于磁盘。
        /// 每条记录是 <see cref="Upsert"/>，因此对同一 conversationId 多次扫描幂等。
        /// </summary>
        public int RebuildFromDirectory(
            string sessionsDirectory,
            Func<string, ConversationSummary> readSummaryFromFile,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionsDirectory)
                || !Directory.Exists(sessionsDirectory)
                || readSummaryFromFile == null)
            {
                return 0;
            }

            var written = 0;
            foreach (var file in Directory.EnumerateFiles(sessionsDirectory, "*.jsonl", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsIndexableConversationFile(file))
                {
                    continue;
                }

                ConversationSummary summary;
                try
                {
                    summary = readSummaryFromFile(file);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[sqlite-index] rebuild skipped '" + file + "': " + ex.Message);
                    continue;
                }

                if (summary == null || string.IsNullOrWhiteSpace(summary.ConversationId))
                {
                    continue;
                }

                Upsert(summary, file);
                written++;
            }

            return written;
        }

        /// <summary>
        /// 索引只收录普通会话文件；压缩快照（<c>*.compact.jsonl</c>）不入索引。
        /// 对账计数与重建必须共用这一口径，否则会把“索引行数 &lt; 文件数”误判为索引落后而每次全量重建。
        /// </summary>
        internal static bool IsIndexableConversationFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".compact.jsonl", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetFileName(path), IndexFileName, StringComparison.OrdinalIgnoreCase);
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private static string FormatUtc(DateTime value)
        {
            // ISO 8601, sortable as text — 与 ORDER BY updated_at_utc DESC 配合。
            return value == default ? string.Empty : value.ToUniversalTime().ToString("O");
        }

        private static DateTime ParseUtc(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return default;
            }

            DateTime parsed;
            return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out parsed)
                ? parsed
                : default;
        }

        private static string SerializeMetadata(IDictionary<string, string> metadata)
        {
            if (metadata == null || metadata.Count == 0)
            {
                return "{}";
            }

            try
            {
                return JsonSerializer.Serialize(metadata);
            }
            catch
            {
                return "{}";
            }
        }

        private static void DeserializeMetadataInto(string json, ConversationSummary summary)
        {
            if (summary == null || string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed == null)
                {
                    return;
                }

                foreach (var kv in parsed)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                    {
                        summary.Metadata[kv.Key] = kv.Value ?? string.Empty;
                    }
                }
            }
            catch
            {
                // ignore malformed payloads — column gets reset on next upsert.
            }
        }
    }
}
