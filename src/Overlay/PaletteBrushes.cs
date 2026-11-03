using System.Windows.Media;

namespace Constella.Overlay;

/// <summary>Frozen brushes and pens for one palette. Rebuilt only when the colours change.</summary>
internal sealed class PaletteBrushes
{
    public PaletteBrushes(LyricPalette palette)
    {
        Palette = palette;
        Lyric = Solid(palette.Lyric);
        Highlight = Solid(palette.Highlight);
        Secondary = Solid(palette.Secondary);
        LyricHalo = Sparkle.CreateHalo(palette.Lyric);
        HighlightHalo = Sparkle.CreateHalo(palette.Highlight);
        LinkPen = new Pen(Lyric, 1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        LinkPen.Freeze();
    }

    public LyricPalette Palette { get; }

    public SolidColorBrush Lyric { get; }

    public SolidColorBrush Highlight { get; }

    public SolidColorBrush Secondary { get; }

    public Brush LyricHalo { get; }

    public Brush HighlightHalo { get; }

    /// <summary>The thin line joining two stars.</summary>
    public Pen LinkPen { get; }

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
