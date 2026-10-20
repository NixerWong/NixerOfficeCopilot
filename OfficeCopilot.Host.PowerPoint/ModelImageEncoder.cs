using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Host.PowerPoint
{
    /// <summary>
    /// 把截图压到适合交给模型的尺寸（默认 ≤ 1280×720）并编码成 JPEG；
    /// 模型侧只需看清版面，原尺寸 PNG 留在工作区给用户看。
    /// </summary>
    internal static class ModelImageEncoder
    {
        public const int DefaultMaxWidth = 1280;
        public const int DefaultMaxHeight = 720;
        private const long JpegQuality = 82L;

        public static byte[] Encode(byte[] imageBytes, int maxWidth = DefaultMaxWidth, int maxHeight = DefaultMaxHeight)
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                return null;
            }

            using (var input = new MemoryStream(imageBytes))
            using (var source = Image.FromStream(input))
            {
                return Encode(source, maxWidth, maxHeight);
            }
        }

        public static byte[] EncodeFile(string path, int maxWidth = DefaultMaxWidth, int maxHeight = DefaultMaxHeight)
        {
            return Encode(File.ReadAllBytes(path), maxWidth, maxHeight);
        }

        private static byte[] Encode(Image source, int maxWidth, int maxHeight)
        {
            var scale = Math.Min(1d, Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            using (var target = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(target))
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(source, 0, 0, width, height);
                }

                var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (var output = new MemoryStream())
                {
                    if (codec == null)
                    {
                        target.Save(output, ImageFormat.Jpeg);
                    }
                    else
                    {
                        using (var parameters = new EncoderParameters(1))
                        {
                            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
                            target.Save(output, codec, parameters);
                        }
                    }

                    return output.ToArray();
                }
            }
        }
    }
}
