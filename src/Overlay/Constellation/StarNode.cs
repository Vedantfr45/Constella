using System.Windows;
using System.Windows.Media;
using Constella.Lyrics;

namespace Constella.Overlay.Constellation;

/// <summary>One word of a constellation: its star, its label texts and where the label sits.</summary>
internal sealed class StarNode
{
    public required LyricWord Word { get; init; }

    public required Point Star { get; init; }

    /// <summary>Small, regular-weight label shown after the word has been sung.</summary>
    public required FormattedText Resting { get; init; }

    /// <summary>Large, bold label shown while the word is being sung (already shrunk if it wouldn't fit).</summary>
    public required FormattedText Active { get; init; }

    /// <summary>Side of the star the large label grows out of (it may differ from <see cref="Side"/> near a screen edge).</summary>
    public required int ActiveSide { get; init; }

    /// <summary>Vertical shift of the large label in its own text heights.</summary>
    public required double ActiveNudge { get; init; }

    /// <summary>+1 when the label sits right of the star, -1 for left.</summary>
    public required int Side { get; init; }

    /// <summary>Vertical label shift in text heights (0 = centred on the star), used to dodge neighbours.</summary>
    public required double Nudge { get; init; }

    /// <summary>When the word stops being "current" (the next word starts).</summary>
    public required double ReleaseAt { get; init; }

    /// <summary>Random phase so stars don't twinkle in sync.</summary>
    public required double TwinklePhase { get; init; }
}
