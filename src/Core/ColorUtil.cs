using System;
using System.Globalization;
using System.Windows.Media;

namespace Constella.Core;

/// <summary>Hex parsing and HSL conversions for the colour settings and album-art palette.</summary>
internal static class ColorUtil
{
    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return false;
        }
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static Color ParseHexOr(string? text, Color fallback) => TryParseHex(text, out Color c) ? c : fallback;

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Hue in degrees [0, 360), saturation and lightness in [0, 1].</summary>
    public static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        if (max - min < 1e-6)
        {
            return (0, 0, l);
        }
        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
                 : max == g ? (b - r) / d + 2
                 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Ease.Clamp01(s);
        l = Ease.Clamp01(l);
        if (s < 1e-6)
        {
            byte v = (byte)Math.Round(l * 255);
            return Color.FromRgb(v, v, v);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return Color.FromRgb(Channel(p, q, h + 1.0 / 3), Channel(p, q, h), Channel(p, q, h - 1.0 / 3));
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        double v = t < 1.0 / 6 ? p + (q - p) * 6 * t
                 : t < 0.5 ? q
                 : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
                 : p;
        return (byte)Math.Round(Ease.Clamp01(v) * 255);
    }
}
