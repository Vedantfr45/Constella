using System.Windows;
using Constella.Core;
using Constella.Lyrics;

namespace Constella.Overlay.Constellation;

/// <summary>The constellation for one lyric line, including its fade-out state.</summary>
internal sealed class LineConstellation
{
    public const double FadeSeconds = 1.0;

    private double? _fadeStart;

    public LineConstellation(LyricLine line, int lineIndex, StarNode[] nodes, Vector drift)
    {
        Line = line;
        LineIndex = lineIndex;
        Nodes = nodes;
        Drift = drift;
    }

    public LyricLine Line { get; }

    public int LineIndex { get; }

    /// <summary>Replaced when the screen size, font or text size changes.</summary>
    public StarNode[] Nodes { get; set; }

    /// <summary>How far the constellation drifts while fading out.</summary>
    public Vector Drift { get; }

    public bool IsFading => _fadeStart is not null;

    public void BeginFade(double wall) => _fadeStart ??= wall;

    public double Alpha(double wall) =>
        _fadeStart is double start ? 1 - Ease.InOutSine(Ease.Progress(wall, start, FadeSeconds)) : 1;

    public Vector Offset(double wall) =>
        _fadeStart is double start ? Drift * Ease.OutCubic(Ease.Progress(wall, start, FadeSeconds)) : default;

    public bool IsGone(double wall) => _fadeStart is double start && wall - start >= FadeSeconds;
}
