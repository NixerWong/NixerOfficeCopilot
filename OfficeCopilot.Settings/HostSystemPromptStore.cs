using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OfficeCopilot.Settings
{
    /// <summary>
    /// 宿主系统提示词的纯文本存储：<c>%APPDATA%\NixerOfficeCopilot\prompts\{Host}.{ask|agent}.txt</c>。
    /// 文件不存在或内容为空白时回落到 <see cref="BuiltInHostPrompts"/>，因此"清空文件"等价于"恢复默认"。
    /// 播种/重置时在同目录写 <c>.seed</c> 哈希；文件与哈希一致（用户未改过）时始终使用最新的内置默认。
    /// </summary>
    public static class HostSystemPromptStore
    {
        public static string GetPromptsFolder()
        {
            return Path.Combine(OfficeCopilotStoragePaths.GetRootFolder(), "prompts");
        }

        public static string GetFilePath(string hostApplicationName, HostPromptMode mode)
        {
            var host = HostApplicationNames.Normalize(hostApplicationName);
            var suffix = mode == HostPromptMode.Agent ? "agent" : "ask";
            return Path.Combine(GetPromptsFolder(), host + "." + suffix + ".txt");
        }

        /// <summary>
        /// 取本轮实际生效的宿主提示词（用户自定义优先，否则内置默认）。
        /// </summary>
        public static string GetEffectivePrompt(string hostApplicationName, HostPromptMode mode)
        {
            var path = GetFilePath(hostApplicationName, mode);
            var custom = TryReadFile(path);
            var builtIn = BuiltInHostPrompts.Get(hostApplicationName, mode);
            if (string.IsNullOrWhiteSpace(custom))
            {
                return builtIn;
            }

            // 打开编辑器时播种的副本若从未改过，就跟随内置默认升级，否则旧版提示词会永久冻结。
            if (IsUnmodifiedSeed(custom, TryReadFile(GetSeedHashPath(path))))
            {
                if (ComputeSeedHash(custom) != ComputeSeedHash(builtIn))
                {
                    TryWriteSeed(path, builtIn);
                }

                return builtIn;
            }

            return custom.Trim();
        }

        /// <summary>文件内容与播种时记录的哈希一致，说明用户没有改过。</summary>
        public static bool IsUnmodifiedSeed(string content, string seedHash)
        {
            return !string.IsNullOrWhiteSpace(seedHash)
                && string.Equals(ComputeSeedHash(content), seedHash.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>忽略首尾空白与换行风格差异的 SHA-256。</summary>
        public static string ComputeSeedHash(string content)
        {
            var normalized = (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        private static string GetSeedHashPath(string promptPath)
        {
            return promptPath + ".seed";
        }

        private static void WriteSeed(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
            File.WriteAllText(GetSeedHashPath(path), ComputeSeedHash(content), new UTF8Encoding(false));
        }

        private static void TryWriteSeed(string path, string content)
        {
            try
            {
                WriteSeed(path, content);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// 确保文件存在（不存在时以内置默认播种），返回文件完整路径供外部编辑器打开。
        /// </summary>
        public static string EnsureFile(string hostApplicationName, HostPromptMode mode)
        {
            var path = GetFilePath(hostApplicationName, mode);
            if (File.Exists(path))
            {
                return path;
            }

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            WriteSeed(path, BuiltInHostPrompts.Get(hostApplicationName, mode));
            return path;
        }

        /// <summary>
        /// 用内置默认覆盖文件内容。
        /// </summary>
        public static string ResetToBuiltIn(string hostApplicationName, HostPromptMode mode)
        {
            var path = GetFilePath(hostApplicationName, mode);
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            WriteSeed(path, BuiltInHostPrompts.Get(hostApplicationName, mode));
            return path;
        }

        private static string TryReadFile(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
