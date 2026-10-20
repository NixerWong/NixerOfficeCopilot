using OfficeCopilot.Agent.Planning;
using System;
using System.Collections.Generic;
using System.IO;

namespace OfficeCopilot.Skills
{
    /// <summary>
    /// 用户自定义 Skill 的存放位置。三个 Office 插件共用 <c>%APPDATA%\NixerOfficeCopilot\skills\</c>，
    /// 每个宿主只读写自己的子目录（excel / word / powerpoint），互不可见。
    /// </summary>
    public static class SkillStorageLocations
    {
        public const string ExcelFolder = "excel";
        public const string WordFolder = "word";
        public const string PowerPointFolder = "powerpoint";

        public static string GetSharedRoot()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "NixerOfficeCopilot", "skills");
        }

        /// <summary>宿主对应的子目录名；无法识别的宿主返回空串。</summary>
        public static string GetHostFolderName(string hostApplicationName)
        {
            switch (SkillHostMatcher.Normalize(hostApplicationName))
            {
                case "excel":
                    return ExcelFolder;
                case "word":
                    return WordFolder;
                case "powerpoint":
                    return PowerPointFolder;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 宿主专属的用户 Skill 目录。宿主无法识别时返回空串，调用方据此拒绝读写（fail-closed）。
        /// </summary>
        public static string GetHostDirectory(string hostApplicationName)
        {
            var folder = GetHostFolderName(hostApplicationName);
            return folder.Length == 0 ? string.Empty : Path.Combine(GetSharedRoot(), folder);
        }

        /// <summary>
        /// 共享根目录下遗留的平铺 Skill（目录直接包含 SKILL.md）。它们不属于任何宿主，不会被加载。
        /// </summary>
        public static IReadOnlyList<string> FindLegacyFlatSkillFolders()
        {
            var root = GetSharedRoot();
            var legacy = new List<string>();
            if (!Directory.Exists(root))
            {
                return legacy;
            }

            foreach (var dir in Directory.GetDirectories(root))
            {
                if (File.Exists(Path.Combine(dir, "SKILL.md")))
                {
                    legacy.Add(Path.GetFileName(dir));
                }
            }

            return legacy;
        }
    }
}
