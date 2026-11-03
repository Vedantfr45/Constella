using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Constella.Core;
using Constella.Media;

namespace Constella.Lyrics;

public enum LyricsStatus
{
    Found,
    NotFound,
    Failed,
}

public sealed record LyricsResult(LyricsStatus Status, LyricSheet? Sheet)
{
    public static LyricsResult NotFound { get; } = new(LyricsStatus.NotFound, null);

    public static LyricsResult Failed { get; } = new(LyricsStatus.Failed, null);
}

/// <summary>Finds lyrics for a song: local cache first, then LRCLIB.</summary>
internal sealed class LyricsService : IDisposable
{
    private readonly LrclibClient _lrclib = new();

    public async Task<LyricsResult> GetAsync(TrackInfo track, CancellationToken ct)
    {
        string key = LyricsCache.KeyFor(track);
        if (LyricsCache.TryRead(key) is string cached && LrcParser.Parse(cached) is LyricSheet cachedSheet)
        {
            return new LyricsResult(LyricsStatus.Found, cachedSheet);
        }
        if (LyricsCache.IsRecentMiss(key))
        {
            return LyricsResult.NotFound;
        }

        try
        {
            foreach (var query in TrackQuery.CandidatesFor(track))
            {
                string? lrc = await _lrclib.FindSyncedAsync(query, track.DurationSeconds, ct).ConfigureAwait(false);
                if (lrc is not null && LrcParser.Parse(lrc) is LyricSheet sheet)
                {
                    LyricsCache.Write(key, lrc);
                    Log.Info($"Lyrics found on LRCLIB for \"{query.Title}\" by \"{query.Artist}\"");
                    return new LyricsResult(LyricsStatus.Found, sheet);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            // Offline or LRCLIB is down: don't remember this as "no lyrics", try again next time.
            Log.Warn($"Lyrics lookup failed: {ex.Message}");
            return LyricsResult.Failed;
        }

        LyricsCache.WriteMiss(key);
        return LyricsResult.NotFound;
    }

    /// <summary>
    /// Loads a .lrc file chosen by the user. When a song is playing the file is also saved to the
    /// cache, so it is used automatically next time that song plays.
    /// </summary>
    /// <returns>The parsed lyrics, or null when the file has no time stamps.</returns>
    public static LyricSheet? Import(string path, TrackInfo? track)
    {
        string text = File.ReadAllText(path);
        var sheet = LrcParser.Parse(text);
        if (sheet is not null && track is not null)
        {
            LyricsCache.Write(LyricsCache.KeyFor(track), text);
        }
        return sheet;
    }

    public void Dispose() => _lrclib.Dispose();
}
