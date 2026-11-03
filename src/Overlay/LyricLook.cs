using System.Windows;
using System.Windows.Media;
using Constella.Core;

namespace Constella.Overlay;

/// <summary>The lyric colours.</summary>
/// <param name="Ambient">Background colour for full-screen scenes such as the lock screen.</param>
public readonly record struct LyricPalette(Color Lyric, Color Highlight, Color Secondary, Color Ambient)
{
    /// <summary>White lyrics on a soft silver-grey, used for black, white and grey album covers.</summary>
    public static LyricPalette Monochrome { get; } = new(
        Lyric: Color.FromRgb(0xF4, 0xF4, 0xF6),
        Highlight: Colors.White,
        Secondary: Color.FromRgb(0xC9, 0xCC, 0xD2),
        Ambient: Color.FromRgb(0x8E, 0x91, 0x97));

    public static LyricPalette FromSettings(AppSettings s)
    {
        var lyric = ColorUtil.ParseHexOr(s.LyricColor, Colors.Wheat);
        var (hue, saturation, _) = ColorUtil.ToHsl(lyric);
        return new LyricPalette(
            lyric,
            ColorUtil.ParseHexOr(s.HighlightColor, Colors.White),
            ColorUtil.ParseHexOr(s.SecondaryColor, Colors.Gray),
            ColorUtil.FromHsl(hue, saturation * 0.45, 0.13));
    }

    public static LyricPalette Mix(LyricPalette from, LyricPalette to, double t) => new(
        Ease.Mix(from.Lyric, to.Lyric, t),
        Ease.Mix(from.Highlight, to.Highlight, t),
        Ease.Mix(from.Secondary, to.Secondary, t),
        Ease.Mix(from.Ambient, to.Ambient, t));
}

/// <summary>Font and size settings handed to a lyric style.</summary>
/// <param name="ReferenceHeight">
/// Height that text sizes are measured against, in device-independent pixels. 0 means the style's
/// own drawing area. The lock screen draws lyrics in a band across the middle of the screen and
/// passes the full screen height here, so its words come out the same size as on the overlay.
/// </param>
public sealed record LyricLook(LyricFont Font, double TextScale, double ReferenceHeight = 0)
{
    /// <summary>1/1000 of the reference height, times the user's text size: sizes scale with any screen.</summary>
    public double Unit(Size area) => (ReferenceHeight > 0 ? ReferenceHeight : area.Height) / 1000.0 * TextScale;
}

/// <summary>
/// The part of a lyric area that text may use: a margin on every side keeps words clear of the
/// screen edges and of the edge glow.
/// </summary>
public static class SafeArea
{
    /// <summary>Margin on each side, as a fraction of the area's width and height.</summary>
    public const double Margin = 0.06;

    public static Rect Of(Size area) => new(
        area.Width * Margin, area.Height * Margin, area.Width * (1 - 2 * Margin), area.Height * (1 - 2 * Margin));
}
