using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Constella.Core;
using Constella.Media;

namespace Constella.Lyrics;

/// <summary>
/// Stores downloaded (or manually loaded) LRC files in %LOCALAPPDATA%\Constella\lyrics, one file
/// per song. Songs with no lyrics get a small ".miss" marker so we don't ask LRCLIB again for a while.
/// </summary>
internal static class LyricsCache
{
    private static readonly TimeSpan MissLifetime = TimeSpan.FromHours(12);

    public static string KeyFor(TrackInfo track)
    {
        string identity = $"{track.Artist.Trim().ToLowerInvariant()}\u001f{track.Title.Trim().ToLowerInvariant()}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public static string? TryRead(string key)
    {
        try
        {
            string path = LrcPath(key);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException ex)
        {
            Log.Warn($"Lyrics cache read failed: {ex.Message}");
            return null;
        }
    }

    public static void Write(string key, string lrc)
    {
        try
        {
            File.WriteAllText(LrcPath(key), lrc);
            File.Delete(MissPath(key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Lyrics cache write failed: {ex.Message}");
        }
    }

    public static bool IsRecentMiss(string key)
    {
        var marker = new FileInfo(MissPath(key));
        return marker.Exists && DateTime.UtcNow - marker.LastWriteTimeUtc < MissLifetime;
    }

    public static void WriteMiss(string key)
    {
        try
        {
            File.WriteAllText(MissPath(key), DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Lyrics cache write failed: {ex.Message}");
        }
    }

    private static string LrcPath(string key) => Path.Combine(AppPaths.LyricsCacheFolder, key + ".lrc");

    private static string MissPath(string key) => Path.Combine(AppPaths.LyricsCacheFolder, key + ".miss");
}
