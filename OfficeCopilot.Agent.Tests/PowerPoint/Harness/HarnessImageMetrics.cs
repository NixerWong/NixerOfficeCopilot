using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OfficeCopilot.Agent.Tests.PowerPoint.Harness
{
    internal static class HarnessImageMetrics
    {
        private const int WindowSize = 8;
        private const double C1 = 6.5025;
        private const double C2 = 58.5225;

        public static double StructuralSimilarity(Bitmap expected, Bitmap actual)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (actual == null) throw new ArgumentNullException(nameof(actual));
            if (expected.Size != actual.Size)
            {
                throw new ArgumentException("Images must have the same dimensions.");
            }

            var expectedPixels = ReadPixels(expected);
            var actualPixels = ReadPixels(actual);
            var windows = 0;
            var total = 0.0;

            for (var top = 0; top < expected.Height; top += WindowSize)
            {
                for (var left = 0; left < expected.Width; left += WindowSize)
                {
                    total += WindowSimilarity(expectedPixels, actualPixels, expected.Width, expected.Height, left, top);
                    windows++;
                }
            }

            return windows == 0 ? 1.0 : total / windows;
        }

        public static void SaveDifferenceHeatmap(Bitmap expected, Bitmap actual, string path)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (actual == null) throw new ArgumentNullException(nameof(actual));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
            if (expected.Size != actual.Size)
            {
                throw new ArgumentException("Images must have the same dimensions.");
            }

            var expectedPixels = ReadPixels(expected);
            var actualPixels = ReadPixels(actual);
            using (var heatmap = new Bitmap(expected.Width, expected.Height, PixelFormat.Format32bppArgb))
            {
                var bounds = new Rectangle(0, 0, heatmap.Width, heatmap.Height);
                var data = heatmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var rowLength = heatmap.Width * 4;
                    var row = new byte[rowLength];
                    for (var y = 0; y < heatmap.Height; y++)
                    {
                        for (var x = 0; x < heatmap.Width; x++)
                        {
                            var index = (y * heatmap.Width + x) * 4;
                            var difference = Math.Max(
                                Math.Abs(expectedPixels[index] - actualPixels[index]),
                                Math.Max(
                                    Math.Abs(expectedPixels[index + 1] - actualPixels[index + 1]),
                                    Math.Abs(expectedPixels[index + 2] - actualPixels[index + 2])));
                            var intensity = Math.Min(255, difference * 3);
                            var rowIndex = x * 4;
                            row[rowIndex] = 0;
                            row[rowIndex + 1] = 0;
                            row[rowIndex + 2] = (byte)intensity;
                            row[rowIndex + 3] = 255;
                        }

                        var destinationRow = IntPtr.Add(data.Scan0, y * data.Stride);
                        Marshal.Copy(row, 0, destinationRow, rowLength);
                    }
                }
                finally
                {
                    heatmap.UnlockBits(data);
                }

                heatmap.Save(path, ImageFormat.Png);
            }
        }

        private static byte[] ReadPixels(Bitmap source)
        {
            var bounds = new Rectangle(0, 0, source.Width, source.Height);
            var data = source.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var rowLength = source.Width * 4;
                var bytes = new byte[rowLength * source.Height];
                for (var y = 0; y < source.Height; y++)
                {
                    var sourceRow = IntPtr.Add(data.Scan0, y * data.Stride);
                    Marshal.Copy(sourceRow, bytes, y * rowLength, rowLength);
                }

                return bytes;
            }
            finally
            {
                source.UnlockBits(data);
            }
        }

        private static double WindowSimilarity(byte[] expected, byte[] actual, int width, int height, int left, int top)
        {
            var right = Math.Min(width, left + WindowSize);
            var bottom = Math.Min(height, top + WindowSize);
            var count = 0;
            var sumExpected = 0.0;
            var sumActual = 0.0;
            var sumExpectedSquared = 0.0;
            var sumActualSquared = 0.0;
            var sumProducts = 0.0;

            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var expectedGray = Gray(expected, width, x, y);
                    var actualGray = Gray(actual, width, x, y);
                    sumExpected += expectedGray;
                    sumActual += actualGray;
                    sumExpectedSquared += expectedGray * expectedGray;
                    sumActualSquared += actualGray * actualGray;
                    sumProducts += expectedGray * actualGray;
                    count++;
                }
            }

            var meanExpected = sumExpected / count;
            var meanActual = sumActual / count;
            var varianceExpected = Math.Max(0, sumExpectedSquared / count - meanExpected * meanExpected);
            var varianceActual = Math.Max(0, sumActualSquared / count - meanActual * meanActual);
            var covariance = sumProducts / count - meanExpected * meanActual;
            var numerator = (2 * meanExpected * meanActual + C1) * (2 * covariance + C2);
            var denominator = (meanExpected * meanExpected + meanActual * meanActual + C1)
                * (varianceExpected + varianceActual + C2);
            return denominator == 0 ? 1 : numerator / denominator;
        }

        private static int Gray(byte[] pixels, int width, int x, int y)
        {
            var index = (y * width + x) * 4;
            var blue = pixels[index];
            var green = pixels[index + 1];
            var red = pixels[index + 2];
            return (int)(0.299 * red + 0.587 * green + 0.114 * blue);
        }
    }
}
