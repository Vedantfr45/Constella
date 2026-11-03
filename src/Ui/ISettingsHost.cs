using Constella.Core;

namespace Constella.Ui;

/// <summary>What the settings window and tray menu can ask the app to do.</summary>
internal interface ISettingsHost
{
    SettingsStore Settings { get; }

    /// <summary>Short "now playing" text for the status pill, or null when nothing is playing.</summary>
    string? NowPlaying { get; }

    /// <summary>Per-app timing nudges, e.g. "Spotify +0.2 s · Chrome −0.1 s", or null when there are none.</summary>
    string? AppTimingSummary { get; }

    /// <returns>False if another app already owns that shortcut.</returns>
    bool TryChangeHotkey(int modifiers, int virtualKey);

    void ToggleLyrics();

    void ShowLockScreen();

    void ResetAppTimings();

    void LoadLyricsFile();

    void Quit();
}
