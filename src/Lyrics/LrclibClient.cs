using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Constella.Lyrics;

/// <summary>
/// Looks up synced lyrics on LRCLIB (https://lrclib.net), a free lyrics database that needs no key.
/// </summary>
internal sealed class LrclibClient : IDisposable
{
    private const string BaseUrl = "https://lrclib.net/api/";

    /// <summary>Search results whose length differs more than this from the song are ignored.</summary>
    private const double MaxDurationDifference = 8;

    private readonly HttpClient _http;

    public LrclibClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(10) };
        // LRCLIB asks clients to identify themselves.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Constella/1.0 (+https://github.com/Vedantfr45)");
    }

    /// <returns>LRC text with time stamps, or null when LRCLIB has none for this query.</returns>
    public async Task<string?> FindSyncedAsync(TrackQuery query, double? durationSeconds, CancellationToken ct)
    {
        // 1) Exact match. Needs the song length, which LRCLIB uses to tell versions apart.
        if (durationSeconds is double duration && query.Artist.Length > 0)
        {
            string url = "get?track_name=" + Uri.EscapeDataString(query.Title) +
                         "&artist_name=" + Uri.EscapeDataString(query.Artist) +
                         (query.Album.Length > 0 ? "&album_name=" + Uri.EscapeDataString(query.Album) : string.Empty) +
                         "&duration=" + Math.Round(duration).ToString(CultureInfo.InvariantCulture);
            using var exact = await GetJsonAsync(url, ct).ConfigureAwait(false);
            if (exact is not null && Synced(exact.RootElement) is string lrc)
            {
                return lrc;
            }
        }

        // 2) Search, then pick the closest result that has synced lyrics.
        string search = query.Artist.Length > 0
            ? "search?track_name=" + Uri.EscapeDataString(query.Title) + "&artist_name=" + Uri.EscapeDataString(query.Artist)
            : "search?q=" + Uri.EscapeDataString(query.Title);
        using var results = await GetJsonAsync(search, ct).ConfigureAwait(false);
        if (results is null || results.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? best = null;
        double bestScore = double.MaxValue;
        foreach (var item in results.RootElement.EnumerateArray())
        {
            if (Synced(item) is not string lrc)
            {
                continue;
            }
            double score = 0;
            if (durationSeconds is double songLength &&
                item.TryGetProperty("duration", out var d) && d.TryGetDouble(out double itemLength))
            {
                score = Math.Abs(itemLength - songLength);
                if (score > MaxDurationDifference)
                {
                    continue;
                }
            }
            if (item.TryGetProperty("trackName", out var name) &&
                !string.Equals(name.GetString(), query.Title, StringComparison.OrdinalIgnoreCase))
            {
                score += 2; // Prefer exact title matches.
            }
            if (score < bestScore)
            {
                bestScore = score;
                best = lrc;
            }
        }
        return best;
    }

    public void Dispose() => _http.Dispose();

    private static string? Synced(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty("syncedLyrics", out var synced) &&
        synced.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(synced.GetString())
            ? synced.GetString()
            : null;

    /// <returns>Parsed JSON, or null for 404 (not found).</returns>
    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }
}
