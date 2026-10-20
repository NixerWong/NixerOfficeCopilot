using System;
using System.IO;
using System.Text;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Phase 3: Plan 文件管理工具。
    /// 对应 cc-haha <c>utils/plans.ts</c>。
    /// Plan 文件存储路径：%APPDATA%/NixerOfficeCopilot/plans/plan-{timestamp}.md
    /// </summary>
    internal static class PlanFileManager
    {
        private static readonly string PlansDirectory;

        static PlanFileManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            PlansDirectory = Path.Combine(appData, "NixerOfficeCopilot", "plans");
        }

        /// <summary>获取 plans 目录路径。</summary>
        public static string GetPlansDirectory() => PlansDirectory;

        /// <summary>确保 plans 目录存在。</summary>
        public static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(PlansDirectory))
                Directory.CreateDirectory(PlansDirectory);
        }

        /// <summary>将 plan 写入文件，返回文件路径。</summary>
        public static string WritePlan(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentNullException(nameof(content));

            EnsureDirectoryExists();

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var fileName = $"plan-{timestamp}.md";
            var filePath = Path.Combine(PlansDirectory, fileName);

            // Write with UTF-8 BOM for Notepad compatibility
            File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            return filePath;
        }

        /// <summary>读取 plan 文件内容。</summary>
        public static string ReadPlan(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return string.Empty;

            return File.ReadAllText(filePath, Encoding.UTF8);
        }

        /// <summary>删除 plan 文件。</summary>
        public static void DeletePlan(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>列出所有 plan 文件（按创建时间降序）。</summary>
        public static string[] ListPlans()
        {
            EnsureDirectoryExists();
            var files = Directory.GetFiles(PlansDirectory, "plan-*.md");
            Array.Sort(files, (a, b) => string.Compare(b, a, StringComparison.OrdinalIgnoreCase));
            return files;
        }
    }
}
