using DevExpress.XtraRichEdit;
using OfficeCopilot.Documents.Markdown;
using System;
using System.IO;
using System.Threading;
using DxPresentation = DevExpress.Docs.Presentation.Presentation;

namespace OfficeCopilot.Panel.WebView.Services.References
{
    internal static class VisualFallbackRenderer
    {
        public static bool TryRenderToPdf(
            string sourcePath,
            string extension,
            string outputPath,
            out string error,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)
                || string.IsNullOrWhiteSpace(outputPath))
            {
                error = "The source document or output path is unavailable.";
                return false;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceExtension = (extension ?? Path.GetExtension(sourcePath)).ToLowerInvariant();
                OpenXmlPackageSizeGuard.EnsureWithinLimit(
                    sourcePath,
                    OpenXmlPackageSizeGuard.DefaultMaxUncompressedBytes,
                    cancellationToken);
                if (string.Equals(sourceExtension, ".doc", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceExtension, ".ppt", StringComparison.OrdinalIgnoreCase))
                {
                    var normalizer = new LegacyOfficeFormatNormalizer();
                    var normalizedPath = normalizer.GetOrCreateNormalizedCopy(sourcePath, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return TryRenderToPdf(normalizedPath, normalizer.GetTargetExtension(sourceExtension), outputPath, out error, cancellationToken);
                }

                // Workbooks are never rendered: print pagination splits sheets arbitrarily and the structured
                // Markdown plus sheet/range fetch already carries the cell and chart data.
                switch (sourceExtension)
                {
                    case ".docx":
                        using (var server = new RichEditDocumentServer())
                        {
                            if (!server.LoadDocument(sourcePath))
                            {
                                error = "The Word document could not be loaded.";
                                return false;
                            }
                            server.ExportToPdf(outputPath);
                        }
                        break;
                    case ".pptx":
                        using (var input = File.OpenRead(sourcePath))
                        using (var presentation = new DxPresentation(input))
                        using (var output = File.Create(outputPath))
                        {
                            presentation.ExportToPdf(output);
                        }
                        break;
                    default:
                        error = "Visual rendering is not supported for this document format.";
                        return false;
                }

                cancellationToken.ThrowIfCancellationRequested();
                return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                return false;
            }
        }
    }
}