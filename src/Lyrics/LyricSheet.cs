using System.Collections.Generic;

namespace Constella.Lyrics;

/// <summary>One word with the time (in song seconds) it is sung.</summary>
public sealed record LyricWord(string Text, double Start, double End);

/// <summary>
/// One lyric line. <see cref="End"/> is when the next line starts. A line with no words marks an
/// instrumental gap.
/// </summary>
public sealed record LyricLine(double Start, double End, string Text, IReadOnlyList<LyricWord> Words)
{
    public double LastWordEnd => Words.Count > 0 ? Words[^1].End : Start;
}

/// <summary>Time-synced lyrics for one song, sorted by start time.</summary>
public sealed class LyricSheet
{
    public LyricSheet(IReadOnlyList<LyricLine> lines, bool hasWordTiming)
    {
        Lines = lines;
        HasWordTiming = hasWordTiming;
    }

    public IReadOnlyList<LyricLine> Lines { get; }

    /// <summary>True when the file had per-word time stamps; otherwise word times are estimated.</summary>
    public bool HasWordTiming { get; }

    public double FirstLineStart => Lines.Count > 0 ? Lines[0].Start : 0;

    /// <summary>Index of the line playing at <paramref name="time"/>, or -1 before the first line.</summary>
    public int IndexAt(double time)
    {
        int lo = 0, hi = Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Lines[mid].Start <= time)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }
}
