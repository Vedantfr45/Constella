using System;
using System.IO;

namespace Constella.Core;

/// <summary>Folders Constella reads and writes. Everything lives under the user's profile.</summary>
internal static class AppPaths
{
    /// <summary>%APPDATA%\Constella: settings (roams with the user).</summary>
    public static string SettingsFolder { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Constella"));

    /// <summary>%LOCALAPPDATA%\Constella: cache and log (machine-local).</summary>
    public static string LocalFolder { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Constella"));

    public static string LyricsCacheFolder { get; } = Ensure(Path.Combine(LocalFolder, "lyrics"));

    public static string SettingsFile => Path.Combine(SettingsFolder, "settings.json");

    public static string LogFile => Path.Combine(LocalFolder, "constella.log");

    /// <summary>Per-app timing nudges (see <c>TimingMemory</c>).</summary>
    public static string TimingFile => Path.Combine(LocalFolder, "app-timing.json");

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
