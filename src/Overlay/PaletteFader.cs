using Constella.Core;

namespace Constella.Overlay;

/// <summary>Cross-fades the lyric colours when the song (or the user's colour choice) changes.</summary>
internal sealed class PaletteFader
{
    private const double FadeSeconds = 1.4;

    private LyricPalette _from;
    private LyricPalette _to;
    private double _changedAt = double.NegativeInfinity;

    public PaletteFader(LyricPalette palette) => Current = _from = _to = palette;

    /// <summary>The colours for the current frame.</summary>
    public LyricPalette Current { get; private set; }

    /// <param name="animate">False when nothing is on screen, so there's no point fading.</param>
    public void FadeTo(LyricPalette palette, double wall, bool animate)
    {
        if (palette == _to)
        {
            return;
        }
        _from = animate ? Current : palette;
        _to = palette;
        _changedAt = wall;
        if (!animate)
        {
            Current = palette;
        }
    }

    /// <returns>The colours for this frame.</returns>
    public LyricPalette Update(double wall)
    {
        double t = Ease.InOutSine(Ease.Progress(wall, _changedAt, FadeSeconds));
        Current = LyricPalette.Mix(_from, _to, t);
        return Current;
    }
}
