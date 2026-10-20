using System;
using System.IO;

namespace OfficeCopilot.Slides.Assets
{
    /// <summary>
    /// 素材根目录：内置（随插件发布的 SlideAssets）与用户扩展（%APPDATA%\NixerOfficeCopilot\slides）。
    /// 两者目录结构相同：catalog / templates / presets / fonts。
    /// </summary>
    public sealed class SlideAssetPaths
    {
        public const string CatalogFileRelative = "catalog\\styles.json";
        public const string FontsJsonRelative = "fonts\\fonts.json";

        public SlideAssetPaths(string builtInRoot, string userRoot)
        {
            BuiltInRoot = builtInRoot ?? string.Empty;
            UserRoot = userRoot ?? string.Empty;
        }

        public string BuiltInRoot { get; }
        public string UserRoot { get; }

        public static SlideAssetPaths CreateDefault()
        {
            return new SlideAssetPaths(GetDefaultBuiltInRoot(), GetDefaultUserRoot());
        }

        public static string GetDefaultBuiltInRoot()
        {
            // VSTO 会影子复制程序集，Location 指向不含 SlideAssets 的缓存目录；CodeBase 保留原始安装位置。
            var assembly = typeof(SlideAssetPaths).Assembly;
            string fallback = null;
            foreach (var baseDir in new[] { CodeBaseDirectory(assembly), SafeDirectory(assembly.Location), AppDomain.CurrentDomain.BaseDirectory })
            {
                if (string.IsNullOrEmpty(baseDir)) continue;
                var root = Path.Combine(baseDir, "SlideAssets");
                if (File.Exists(Path.Combine(root, CatalogFileRelative))) return root;
                if (fallback == null) fallback = root;
            }

            return fallback ?? "SlideAssets";
        }

        private static string CodeBaseDirectory(System.Reflection.Assembly assembly)
        {
            try
            {
                var uri = new Uri(assembly.CodeBase);
                return uri.IsFile ? Path.GetDirectoryName(uri.LocalPath) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string SafeDirectory(string path)
        {
            return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
        }

        public static string GetDefaultUserRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NixerOfficeCopilot",
                "slides");
        }

        public static string TemplatesDir(string root) { return Path.Combine(root, "templates"); }
        public static string PresetsDir(string root) { return Path.Combine(root, "presets"); }
        public static string FontsDir(string root) { return Path.Combine(root, "fonts"); }
    }
}
