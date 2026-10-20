using System;
using System.IO;

namespace OfficeCopilot.Agent.Persistence.Jsonl
{
    /// <summary>
    /// JSONL 会话文件的路径解析。<br/>
    /// 物理布局（蓝图 §3.1）：
    /// <code>
    /// &lt;rootDirectory&gt;/sessions/&lt;yyyyMMdd&gt;/&lt;conversationId&gt;.jsonl
    /// </code>
    /// 其中 <c>&lt;rootDirectory&gt;</c> 默认为 <c>%APPDATA%/NixerOfficeCopilot</c>。
    /// 用日期分文件夹既方便按时间清理，也避免单目录文件数膨胀（一日多会话时仍可控）。
    ///
    /// 同一 conversationId 在多日打开时会落到不同日期文件夹 → <see cref="TryResolveExistingAsync"/>
    /// 自后向前扫近 N 天找已存在的文件；找不到则按今天创建。这一行为对单测可见，必要时由测试桩注入虚拟"今天"。
    /// </summary>
    internal sealed class JsonlPaths
    {
        public const string SessionsFolderName = "sessions";

        private readonly string _rootDirectory;
        private readonly Func<DateTime> _utcNowProvider;

        public JsonlPaths(string rootDirectory, Func<DateTime> utcNowProvider)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Root directory must be provided.", nameof(rootDirectory));
            }

            _rootDirectory = rootDirectory;
            _utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
        }

        /// <summary>默认根路径：<c>%APPDATA%/NixerOfficeCopilot</c>。</summary>
        public static string DefaultRootDirectory
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "NixerOfficeCopilot");
            }
        }

        public string SessionsDirectory
        {
            get { return Path.Combine(_rootDirectory, SessionsFolderName); }
        }

        /// <summary>
        /// 今天日期子目录的绝对路径。调用方负责按需 <c>Directory.CreateDirectory</c>。
        /// </summary>
        public string TodayDirectory
        {
            get { return Path.Combine(SessionsDirectory, _utcNowProvider().ToString("yyyyMMdd")); }
        }

        /// <summary>按 conversationId 构造今天目录下的文件路径。</summary>
        public string BuildTodayPath(string conversationId)
        {
            return Path.Combine(TodayDirectory, EncodeConversationFileName(conversationId));
        }

        /// <summary>
        /// 在 <see cref="SessionsDirectory"/> 下所有日期子目录里寻找已存在的会话文件。
        /// 返回 <c>null</c> 表示该 conversationId 当前没有任何持久化文件。
        /// 实现走 <see cref="Directory.EnumerateDirectories"/>，性能足够初版；后续 Sqlite 索引会承担实际查找。
        /// </summary>
        public string TryResolveExistingPath(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || !Directory.Exists(SessionsDirectory))
            {
                return null;
            }

            var fileName = EncodeConversationFileName(conversationId);
            foreach (var dayDir in Directory.EnumerateDirectories(SessionsDirectory))
            {
                var candidate = Path.Combine(dayDir, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 编码 conversationId 为合法文件名（去掉 Windows 文件名禁用字符 + 加 .jsonl 扩展）。
        /// 不做哈希；保持人类可读，便于排查。
        /// </summary>
        internal static string EncodeConversationFileName(string conversationId)
        {
            var safe = string.IsNullOrWhiteSpace(conversationId)
                ? "unnamed"
                : conversationId.Trim();

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '_');
            }

            return safe + ".jsonl";
        }
    }
}
