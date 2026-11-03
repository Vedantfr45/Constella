namespace Constella.Media;

/// <summary>The song a media app is currently showing in Windows' media controls.</summary>
public sealed record TrackInfo(string Title, string Artist, string Album, string SourceApp, double? DurationSeconds)
{
    /// <summary>Identifies the song. Duration is left out because some apps publish it a moment later.</summary>
    public string Identity => $"{SourceApp}\u001f{Title}\u001f{Artist}\u001f{Album}";

    public string DisplayName => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Title} · {Artist}";
}

/// <summary>One reading of the playback state, taken about twice a second (and on every change).</summary>
/// <param name="Timestamp">
/// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> when the position was read, so the
/// clock can account for the time the sample spent waiting to reach the UI thread.
/// </param>
public readonly record struct PlaybackSample(bool IsPlaying, double PositionSeconds, double Rate, long Timestamp);
