using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OfficeCopilot.Panel.WebAssets
{
    public static class WebAssetsLocator
    {
        private const string ResourcePrefix = "wwwroot/";

        public static string EnsureAssetsRoot()
        {
            var targetRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "WebAssets",
                "wwwroot");

            Directory.CreateDirectory(targetRoot);

            var assembly = typeof(WebAssetsLocator).Assembly;
            var resourceNames = assembly
                .GetManifestResourceNames()
                .Where(name => name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (resourceNames.Length == 0)
            {
                throw new InvalidOperationException("未找到嵌入的 Web 资源。");
            }

            foreach (var resourceName in resourceNames)
            {
                var relativePath = resourceName.Substring(ResourcePrefix.Length)
                    .Replace('/', Path.DirectorySeparatorChar);

                var targetPath = Path.Combine(targetRoot, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetPath);

                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                using (var resourceStream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resourceStream == null)
                    {
                        throw new InvalidOperationException("无法读取嵌入资源：" + resourceName);
                    }

                    using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        resourceStream.CopyTo(fileStream);
                    }
                }
            }

            return targetRoot;
        }
    }
}
