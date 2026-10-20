using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace OfficeCopilot.Documents.Markdown
{
    public static class OpenXmlPackageSizeGuard
    {
        public const long DefaultMaxUncompressedBytes = 256L * 1024 * 1024;

        public static void EnsureWithinLimit(string path, long maxUncompressedBytes, CancellationToken cancellationToken)
        {
            if (maxUncompressedBytes <= 0 || !IsOpenXmlPackage(path))
            {
                return;
            }

            long totalBytes = 0;
            var buffer = new byte[81920];
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.Length > maxUncompressedBytes - totalBytes)
                    {
                        throw new InvalidDataException("The document exceeds the configured uncompressed package-size limit.");
                    }

                    using (var entryStream = entry.Open())
                    {
                        int bytesRead;
                        while ((bytesRead = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            totalBytes += bytesRead;
                            if (totalBytes > maxUncompressedBytes)
                            {
                                throw new InvalidDataException("The document exceeds the configured uncompressed package-size limit.");
                            }
                        }
                    }
                }
            }
        }

        private static bool IsOpenXmlPackage(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".pptx", StringComparison.OrdinalIgnoreCase);
        }
    }
}