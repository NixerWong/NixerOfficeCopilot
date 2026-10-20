using System;
using System.IO;

namespace OfficeCopilot.Slides.AssetMerge
{
    /// <summary>
    /// 一次性素材合并工具（不随产品发布）。
    /// 用法：OfficeCopilot.Slides.AssetMerge [--root &lt;仓库根&gt;] [--source &lt;PPTskill 目录&gt;] [--target &lt;SlideAssets 目录&gt;]
    /// 退出码：0 成功；1 参数 / 目录错误；2 合并完成但仍有未改写的外链。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);

            string root = null, source = null, target = null;
            for (var i = 0; i < args.Length; i++)
            {
                var next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "--root": root = next; i++; break;
                    case "--source": source = next; i++; break;
                    case "--target": target = next; i++; break;
                    case "-h":
                    case "--help":
                        Console.WriteLine("OfficeCopilot.Slides.AssetMerge [--root <repo>] [--source <docs/PPTskill>] [--target <SlideAssets>]");
                        return 0;
                }
            }

            root = root ?? FindRepoRoot(Environment.CurrentDirectory) ?? FindRepoRoot(AppDomain.CurrentDomain.BaseDirectory);
            if (root == null && (source == null || target == null))
            {
                Console.Error.WriteLine("找不到仓库根目录（NixerOfficeCopilot.sln），请用 --root 指定。");
                return 1;
            }

            source = Path.GetFullPath(source ?? Path.Combine(root, "docs", "PPTskill"));
            target = Path.GetFullPath(target ?? Path.Combine(root, "OfficeCopilot.Slides.Core", "SlideAssets"));
            if (!Directory.Exists(source))
            {
                Console.Error.WriteLine("源目录不存在：" + source);
                return 1;
            }

            var merger = new AssetMerger(source, target, Console.Out);
            var report = merger.Run();
            Console.WriteLine();
            Console.WriteLine("模板 " + report.Templates + "，预设 " + report.Presets + "，版式 " + report.Layouts
                + "，字体族 " + report.FontFamilies + "，未改写外链 " + report.UnresolvedReferences);
            return report.UnresolvedReferences > 0 ? 2 : 0;
        }

        private static string FindRepoRoot(string start)
        {
            var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "NixerOfficeCopilot.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }
    }
}
