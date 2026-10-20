using System;
using System.IO;

namespace OfficeCopilot.Agent.Abstractions
{
    /// <summary>
    /// 把 stdio 服务器的启动命令解析为可直接启动的路径。
    /// UseShellExecute=false 时 CreateProcess 只会给裸名补 ".exe"，无法启动 npx 这类 ".cmd" 垫片，
    /// 因此裸名要先在 PATH 中按可执行扩展名查找成完整路径。
    /// </summary>
    public static class McpCommandResolver
    {
        private static readonly string[] LaunchableExtensions = { ".com", ".exe", ".bat", ".cmd" };

        public static string Resolve(string command, string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(command))
                return command;

            var name = command.Trim();
            var wd = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory.Trim();

            // 带路径的命令按工作目录解析；Path.Combine 遇到绝对路径会直接返回它本身。
            if (name.IndexOfAny(new[] { '\\', '/' }) >= 0 || Path.IsPathRooted(name))
            {
                var candidate = wd != null ? Path.Combine(wd, name) : name;
                return FindWithExtensions(candidate) ?? candidate;
            }

            // 裸名只查 PATH，不查工作目录，避免命中服务器目录下的同名文件。
            return FindOnPath(name) ?? name;
        }

        private static string FindOnPath(string name)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
                return null;

            foreach (var entry in path.Split(';'))
            {
                var dir = entry.Trim().Trim('"');
                if (dir.Length == 0)
                    continue;

                string found;
                try
                {
                    found = FindWithExtensions(Path.Combine(dir, name));
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (found != null)
                    return found;
            }

            return null;
        }

        // 无扩展名的同名文件（如 npm 自带的 sh 脚本 "npx"）不能直接启动，必须补上可执行扩展名。
        private static string FindWithExtensions(string path)
        {
            if (Path.GetExtension(path).Length > 0 && File.Exists(path))
                return path;

            foreach (var ext in LaunchableExtensions)
            {
                var withExt = path + ext;
                if (File.Exists(withExt))
                    return withExt;
            }

            return null;
        }
    }
}
