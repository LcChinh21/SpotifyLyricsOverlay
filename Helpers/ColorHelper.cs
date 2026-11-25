using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpotifyLyricsOverlay.Helpers
{
    public static class ColorHelper
    {
        /// <summary>
        /// Extracts a vibrant, legible accent color from an album art thumbnail.
        /// </summary>
        public static Color ExtractDominantColor(BitmapSource? source, Color fallback)
        {
            if (source == null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
                return fallback;

            try
            {
                var bgra = source.Format == PixelFormats.Bgra32
                    ? source
                    : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

                int width = bgra.PixelWidth;
                int height = bgra.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[height * stride];
                bgra.CopyPixels(pixels, stride, 0);

                int stepX = Math.Max(1, width / 32);
                int stepY = Math.Max(1, height / 32);

                var buckets = new Dictionary<int, (double score, long rSum, long gSum, long bSum, int count)>();

                for (int y = 0; y < height; y += stepY)
                {
                    for (int x = 0; x < width; x += stepX)
                    {
                        int idx = y * stride + x * 4;
                        byte b = pixels[idx];
                        byte g = pixels[idx + 1];
                        byte r = pixels[idx + 2];
                        byte a = pixels[idx + 3];

                        if (a < 128) continue;

                        int max = Math.Max(r, Math.Max(g, b));
                        int min = Math.Min(r, Math.Min(g, b));
                        double brightness = max / 255.0;
                        double saturation = max == 0 ? 0.0 : (max - min) / (double)max;

                        // Filter out near-black, near-white, and dull gray pixels
                        if (brightness < 0.18 || saturation < 0.18) continue;

                        int key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                        double weight = 1.0 + saturation * 3.5;

                        if (buckets.TryGetValue(key, out var entry))
                        {
                            buckets[key] = (entry.score + weight, entry.rSum + r, entry.gSum + g, entry.bSum + b, entry.count + 1);
                        }
                        else
                        {
                            buckets[key] = (weight, r, g, b, 1);
                        }
                    }
                }

                if (buckets.Count == 0)
                    return fallback;

                double bestScore = -1;
                Color bestColor = fallback;

                foreach (var kv in buckets.Values)
                {
                    if (kv.score > bestScore)
                    {
                        bestScore = kv.score;
                        byte r = (byte)(kv.rSum / kv.count);
                        byte g = (byte)(kv.gSum / kv.count);
                        byte b = (byte)(kv.bSum / kv.count);
                        bestColor = Color.FromRgb(r, g, b);
                    }
                }

                return EnsureBrightLegibleColor(bestColor);
            }
            catch
            {
                return fallback;
            }
        }

        private static Color EnsureBrightLegibleColor(Color c)
        {
            // Perceived luminance (0..1)
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            if (lum >= 0.55) return c;

            // Boost toward white/brighter tint while preserving hue
            double factor = 0.55 - lum;
            byte r = (byte)Math.Clamp(c.R + (255 - c.R) * factor * 1.4, 0, 255);
            byte g = (byte)Math.Clamp(c.G + (255 - c.G) * factor * 1.4, 0, 255);
            byte b = (byte)Math.Clamp(c.B + (255 - c.B) * factor * 1.4, 0, 255);
            return Color.FromRgb(r, g, b);
        }
    }
}
