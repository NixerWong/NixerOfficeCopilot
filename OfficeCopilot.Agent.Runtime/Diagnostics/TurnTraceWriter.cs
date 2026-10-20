using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Diagnostics
{
    /// <summary>
    /// 回合级结构化轨迹（JSONL）。每回合一个文件，一行一个事件。
    /// 这是所有度量、回放与归因的唯一数据源；写失败绝不能影响主流程。
    /// </summary>
    internal static class TurnTraceWriter
    {
        private static readonly ConcurrentDictionary<string, int> _sequenceByTurn =
            new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        private static readonly object _writeSyncRoot = new object();

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            // 中文不转义成 \uXXXX，轨迹文件要能直接肉眼读。
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        /// <summary>写入一条事件。<paramref name="fields"/> 会与公共字段合并后序列化为一行 JSON。</summary>
        public static void Write(string conversationId, string messageId, string kind, IDictionary<string, object> fields)
        {
            try
            {
                var turnKey = BuildTurnKey(conversationId, messageId);
                var seq = _sequenceByTurn.AddOrUpdate(turnKey, 1, (k, current) => current + 1);

                var record = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["seq"] = seq,
                    ["ts"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ["kind"] = kind ?? string.Empty,
                    ["conversationId"] = conversationId ?? string.Empty,
                    ["messageId"] = messageId ?? string.Empty
                };

                if (fields != null)
                {
                    foreach (var pair in fields)
                    {
                        if (string.IsNullOrEmpty(pair.Key)) continue;
                        record[pair.Key] = pair.Value;
                    }
                }

                var line = JsonSerializer.Serialize(record, SerializerOptions);
                var path = BuildTracePath(conversationId, messageId);
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

                lock (_writeSyncRoot)
                {
                    File.AppendAllText(path, line + "\n", Utf8NoBom);
                }
            }
            catch (Exception)
            {
                // 轨迹是观测手段，不是业务路径。写失败必须静默。
            }
        }

        /// <summary>SHA256 前 4 字节的十六进制，用于在不展开完整参数时判断“是不是同一次调用”。</summary>
        public static string ComputeArgumentsHash(string argumentsJson)
        {
            if (string.IsNullOrEmpty(argumentsJson)) return string.Empty;
            try
            {
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Utf8NoBom.GetBytes(argumentsJson));
                    var builder = new StringBuilder(8);
                    for (var i = 0; i < 4 && i < hash.Length; i++) builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                    return builder.ToString();
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || maxLength <= 0) return value ?? string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private static string BuildTurnKey(string conversationId, string messageId)
        {
            return (conversationId ?? string.Empty) + "|" + (messageId ?? string.Empty);
        }

        private static string BuildTracePath(string conversationId, string messageId)
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "Traces",
                DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

            var fileName = Sanitize(string.IsNullOrWhiteSpace(conversationId) ? "no-conversation" : conversationId)
                + "_" + Sanitize(string.IsNullOrWhiteSpace(messageId) ? "no-message" : messageId)
                + ".jsonl";

            return Path.Combine(root, fileName);
        }

        private static string Sanitize(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                var bad = false;
                for (var i = 0; i < invalid.Length; i++)
                {
                    if (invalid[i] == c) { bad = true; break; }
                }
                builder.Append(bad ? '_' : c);
            }

            var result = builder.ToString();
            return result.Length > 64 ? result.Substring(0, 64) : result;
        }
    }
}
