using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Constella.Core;

namespace Constella.Overlay;

/// <summary>
/// Picks lyric colours from album art: finds the most prominent saturated hue and turns it into
/// three soft, readable tints (the lyrics always sit on dark, so they're kept light). Black,
/// white and grey covers get <see cref="LyricPalette.Monochrome"/>: white lyrics on silver-grey.
/// </summary>
internal static class ArtPalette
{
    private const int SampleSize = 48;
    private const int HueBins = 36;

    /// <returns>A palette, or null when the art can't be read (then the user's colours are used).</returns>
    public static LyricPalette? FromImage(byte[] imageBytes)
    {
        BitmapSource bitmap;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(imageBytes);
            image.DecodePixelWidth = SampleSize;
            image.EndInit();
            bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            bitmap.Freeze();
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or FileFormatException)
        {
            Log.Warn($"Album art could not be decoded: {ex.Message}");
            return null;
        }

        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        // Weighted hue histogram: vivid, mid-bright pixels count most.
        var weight = new double[HueBins];
        var sumR = new double[HueBins];
        var sumG = new double[HueBins];
        var sumB = new double[HueBins];
        double total = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            var color = Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
            var (h, s, l) = ColorUtil.ToHsl(color);
            if (s < 0.18 || l < 0.12 || l > 0.92)
            {
                continue;
            }
            double w = s * s * (1 - Math.Abs(l - 0.5) * 1.4);
            int bin = (int)(h / 360 * HueBins) % HueBins;
            weight[bin] += w;
            sumR[bin] += color.R * w;
            sumG[bin] += color.G * w;
            sumB[bin] += color.B * w;
            total += w;
        }

        if (total < width * height * 0.015)
        {
            return LyricPalette.Monochrome; // Black, white or grey cover: no colour worth taking.
        }

        // Strongest hue, counting its two neighbours so a gradient isn't split across bins.
        int best = 0;
        double bestScore = -1;
        for (int b = 0; b < HueBins; b++)
        {
            double score = weight[b] + 0.5 * (weight[(b + 1) % HueBins] + weight[(b + HueBins - 1) % HueBins]);
            if (score > bestScore)
            {
                bestScore = score;
                best = b;
            }
        }

        if (weight[best] <= 0)
        {
            int next = (best + 1) % HueBins, previous = (best + HueBins - 1) % HueBins;
            best = weight[next] >= weight[previous] ? next : previous;
        }

        var dominant = Color.FromRgb(
            (byte)(sumR[best] / weight[best]),
            (byte)(sumG[best] / weight[best]),
            (byte)(sumB[best] / weight[best]));
        var (hue, sat, _) = ColorUtil.ToHsl(dominant);
        double tint = Math.Clamp(sat * 0.7, 0.25, 0.6);

        return new LyricPalette(
            Lyric: ColorUtil.FromHsl(hue, tint, 0.80),
            Highlight: ColorUtil.FromHsl(hue, tint * 0.55, 0.93),
            Secondary: ColorUtil.FromHsl(hue, tint * 0.6, 0.64),
            Ambient: ColorUtil.FromHsl(hue, Math.Clamp(sat, 0.3, 0.7), 0.2));
    }
}
