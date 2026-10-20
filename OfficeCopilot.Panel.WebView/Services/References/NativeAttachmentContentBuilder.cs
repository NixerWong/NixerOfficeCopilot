using Microsoft.Extensions.Logging;
using OfficeCopilot.Panel.Abstractions;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Panel.WebView.Services.References
{
    internal sealed class NativeAttachmentContent
    {
        public NativeAttachmentContent()
        {
            Type = string.Empty;
            MediaType = string.Empty;
            FileName = string.Empty;
            FileContentBase64 = string.Empty;
        }

        public string Type { get; set; }

        public string MediaType { get; set; }

        public string FileName { get; set; }

        public string FileContentBase64 { get; set; }
    }

    internal static class NativeAttachmentContentBuilder
    {
        private static readonly long[] JpegQualitySteps = { 90L, 80L, 70L, 60L, 50L, 40L, 30L };

        public static NativeAttachmentContent Build(
            PreparedReference reference,
            ChatDefaultsSettings chatDefaults,
            ILogger logger)
        {
            if (reference == null || reference.Metadata == null)
            {
                return null;
            }

            string path;
            if (!reference.Metadata.TryGetValue("localPath", out path)
                || string.IsNullOrWhiteSpace(path)
                || !File.Exists(path))
            {
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                var isImage = ReferencePreprocessorUtilities.IsImageMimeType(reference.MimeType);
                var nativeThresholdBytes = ReferencePreprocessorUtilities.KilobytesToBytes(
                    Math.Max(1, chatDefaults == null ? 1024 : chatDefaults.NativeThresholdKb));
                var effectiveBytes = bytes;
                var effectiveMimeType = string.IsNullOrWhiteSpace(reference.MimeType)
                    ? "application/octet-stream"
                    : reference.MimeType.Trim();

                if (isImage && effectiveBytes.LongLength > nativeThresholdBytes)
                {
                    if (chatDefaults == null || !chatDefaults.AutoResizeBigImage)
                    {
                        return null;
                    }

                    byte[] resizedBytes;
                    string resizedMimeType;
                    if (!TryResizeImageToThreshold(path, effectiveMimeType, nativeThresholdBytes, out resizedBytes, out resizedMimeType))
                    {
                        logger?.LogWarning(
                            "大图缩放失败，无法作为原生附件发送。ReferenceName={ReferenceName} SizeBytes={SizeBytes} ThresholdBytes={ThresholdBytes}",
                            reference.DisplayName ?? string.Empty,
                            effectiveBytes.LongLength,
                            nativeThresholdBytes);
                        return null;
                    }

                    effectiveBytes = resizedBytes;
                    effectiveMimeType = resizedMimeType;
                }

                if (effectiveBytes.LongLength > nativeThresholdBytes)
                {
                    logger?.LogWarning(
                        "原生附件超过大小阈值，已跳过。ReferenceName={ReferenceName} SizeBytes={SizeBytes} ThresholdBytes={ThresholdBytes}",
                        reference.DisplayName ?? string.Empty,
                        effectiveBytes.LongLength,
                        nativeThresholdBytes);
                    return null;
                }

                var fileName = BuildFileName(reference, path, effectiveMimeType);
                return new NativeAttachmentContent
                {
                    Type = isImage ? "input_image" : "input_file",
                    MediaType = effectiveMimeType,
                    FileName = fileName,
                    FileContentBase64 = Convert.ToBase64String(effectiveBytes)
                };
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "读取原生参考文件失败：{ReferenceName}", reference.DisplayName ?? string.Empty);
                return null;
            }
        }

        private static string BuildFileName(PreparedReference reference, string path, string mimeType)
        {
            var fileName = string.IsNullOrWhiteSpace(reference == null ? null : reference.DisplayName)
                ? Path.GetFileName(path)
                : reference.DisplayName.Trim();

            if (!ReferencePreprocessorUtilities.IsImageMimeType(mimeType))
            {
                return fileName;
            }

            var expectedExtension = GetPreferredFileExtension(mimeType);
            if (string.IsNullOrWhiteSpace(expectedExtension))
            {
                return fileName;
            }

            var currentExtension = Path.GetExtension(fileName);
            if (string.Equals(currentExtension, expectedExtension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "image";
            }

            return baseName + expectedExtension;
        }

        private static string GetPreferredFileExtension(string mimeType)
        {
            switch ((mimeType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "image/png":
                    return ".png";
                case "image/jpeg":
                    return ".jpg";
                case "image/gif":
                    return ".gif";
                case "image/bmp":
                    return ".bmp";
                default:
                    return string.Empty;
            }
        }

        private static bool TryResizeImageToThreshold(string path, string originalMimeType, long maxBytes, out byte[] resizedBytes, out string outputMimeType)
        {
            resizedBytes = null;
            outputMimeType = originalMimeType;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || maxBytes <= 0)
            {
                return false;
            }

            using (var image = Image.FromFile(path))
            {
                var hasAlpha = Image.IsAlphaPixelFormat(image.PixelFormat)
                    || Image.IsExtendedPixelFormat(image.PixelFormat);
                outputMimeType = hasAlpha ? "image/png" : "image/jpeg";

                for (var scaleStep = 0; scaleStep < 8; scaleStep++)
                {
                    var scale = Math.Pow(0.85d, scaleStep);
                    var width = Math.Max(1, (int)Math.Round(image.Width * scale));
                    var height = Math.Max(1, (int)Math.Round(image.Height * scale));

                    if (string.Equals(outputMimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var quality in JpegQualitySteps)
                        {
                            var candidate = RenderResizedImage(image, width, height, outputMimeType, quality);
                            if (candidate != null && candidate.LongLength <= maxBytes)
                            {
                                resizedBytes = candidate;
                                return true;
                            }
                        }
                    }
                    else
                    {
                        var candidate = RenderResizedImage(image, width, height, outputMimeType, 0L);
                        if (candidate != null && candidate.LongLength <= maxBytes)
                        {
                            resizedBytes = candidate;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static byte[] RenderResizedImage(Image image, int width, int height, string mimeType, long jpegQuality)
        {
            using (var bitmap = new Bitmap(width, height))
            {
                bitmap.SetResolution(image.HorizontalResolution, image.VerticalResolution);

                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.Clear(string.Equals(mimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
                        ? Color.White
                        : Color.Transparent);
                    graphics.DrawImage(image, new Rectangle(0, 0, width, height));
                }

                using (var stream = new MemoryStream())
                {
                    if (string.Equals(mimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        var encoder = GetEncoder(ImageFormat.Jpeg);
                        if (encoder == null)
                        {
                            return null;
                        }

                        using (var encoderParameters = new EncoderParameters(1))
                        {
                            encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, jpegQuality);
                            bitmap.Save(stream, encoder, encoderParameters);
                        }
                    }
                    else
                    {
                        bitmap.Save(stream, ImageFormat.Png);
                    }

                    return stream.ToArray();
                }
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            return ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(codec => codec.FormatID == format.Guid);
        }
    }
}
