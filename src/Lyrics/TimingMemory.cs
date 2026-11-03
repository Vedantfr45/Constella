using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Constella.Core;

namespace Constella.Lyrics;

/// <summary>
/// Remembers a timing nudge per source app (Ctrl+Alt+[ and ]). Apps report the song position with
/// different delays (a browser tab lags behind Spotify, for example), so each app keeps its own
/// correction. Stored as a small JSON map, app name → seconds, in %LOCALAPPDATA%\Constella\app-timing.json.
/// </summary>
internal sealed class TimingMemory
{
    /// <summary>Largest nudge either way, in seconds.</summary>
    public const double Limit = 5;

    private readonly Dictionary<string, double> _offsets;

    public TimingMemory() => _offsets = Load();

    /// <returns>Seconds to delay this app's lyrics by (negative = earlier); 0 when never nudged.</returns>
    public double Get(string sourceApp) => _offsets.TryGetValue(sourceApp, out double offset) ? offset : 0;

    /// <summary>Changes this app's nudge by <paramref name="delta"/> seconds and saves it.</summary>
    /// <returns>The new nudge.</returns>
    public double Adjust(string sourceApp, double delta)
    {
        double offset = Math.Round(Math.Clamp(Get(sourceApp) + delta, -Limit, Limit), 2);
        if (Math.Abs(offset) < 0.001)
        {
            _offsets.Remove(sourceApp);
            offset = 0;
        }
        else
        {
            _offsets[sourceApp] = offset;
        }
        Save();
        return offset;
    }

    /// <summary>Forgets every app's nudge.</summary>
    public void Clear()
    {
        _offsets.Clear();
        Save();
    }

    /// <summary>E.g. "Spotify +0.2 s · Chrome −0.1 s", or null when nothing is nudged.</summary>
    public string? Summary() => _offsets.Count == 0
        ? null
        : string.Join("  ·  ", _offsets.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Key} {p.Value:+0.0;−0.0} s"));

    private static Dictionary<string, double> Load()
    {
        try
        {
            if (File.Exists(AppPaths.TimingFile))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(AppPaths.TimingFile));
                if (loaded is not null)
                {
                    // App names are matched case-insensitively ("chrome" and "Chrome" are the same app).
                    return new Dictionary<string, double>(
                        loaded.Where(p => double.IsFinite(p.Value) && !string.IsNullOrWhiteSpace(p.Key))
                              .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                              .Select(g => g.First()),
                        StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Timing file unreadable, starting fresh ({ex.Message})");
        }
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    private void Save()
    {
        try
        {
            string temp = AppPaths.TimingFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_offsets));
            File.Move(temp, AppPaths.TimingFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not save timing nudges: {ex.Message}");
        }
    }
}
