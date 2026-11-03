using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Constella.Audio;
using Constella.Core;
using Constella.LockScreen;
using Constella.Lyrics;
using Constella.Media;
using Constella.Overlay;
using Constella.Ui;
using Constella.Ui.Controls;

namespace Constella;

/// <summary>
/// Wires everything together: media session â†’ lyrics lookup â†’ overlay (and lock screen), plus
/// settings, tray, hotkeys and start-with-Windows. All methods run on the UI thread.
/// </summary>
internal sealed class AppController : ISettingsHost, IDisposable
{
    /// <summary>Words start appearing this much before they are sung, because they take a moment to fade in.</summary>
    private const double LyricLeadSeconds = 0.15;

    /// <summary>One press of the timing keys moves this song's lyrics by this much.</summary>
    private const double NudgeStep = 0.1;

    // Fixed shortcuts: Ctrl+Alt+K for the lock screen, Ctrl+Alt+[ and ] to nudge timing.
    // (Not Ctrl+Alt+arrows: some graphics drivers use those to rotate the screen.)
    private const int FixedModifiers = AppSettings.ModControl | AppSettings.ModAlt;
    private const int LockKey = 0x4B;    // K
    private const int EarlierKey = 0xDB; // [ (VK_OEM_4)
    private const int LaterKey = 0xDD;   // ] (VK_OEM_6)

    private readonly PlaybackClock _clock = new();
    private readonly MediaWatcher _media;
    private readonly LyricsService _lyrics = new();
    private readonly TimingMemory _timing = new();
    private readonly LoudnessMeter _meter;
    private readonly OverlayWindow _overlay;
    private readonly TrayIcon _tray = new();
    private readonly GlobalHotkey _hotkey = new();
    private readonly GlobalHotkey _lockHotkey = new();
    private readonly GlobalHotkey _earlierHotkey = new();
    private readonly GlobalHotkey _laterHotkey = new();

    private SettingsWindow? _settingsWindow;
    private LockScreenWindow? _lockScreen;
    private TrackInfo? _track;
    private LyricSheet? _sheet;
    private byte[]? _art;
    private double _appNudge;
    private bool _isPlaying;
    private LyricPalette? _artPalette;
    private CancellationTokenSource? _lookup;
    private bool _hotkeyAvailable;
    private bool? _startupApplied;

    public AppController(Dispatcher dispatcher)
    {
        _media = new MediaWatcher(dispatcher);
        _meter = new LoudnessMeter(dispatcher);
        _overlay = new OverlayWindow(_clock, _meter, LyricPalette.FromSettings(Settings.Current));
    }

    public SettingsStore Settings { get; } = new();

    public string? NowPlaying => _track is null ? null : _track.DisplayName + (_isPlaying ? string.Empty : "  (paused)");

    public string? AppTimingSummary => _timing.Summary();

    /// <summary>The lock screen shortcut, for display.</summary>
    public static string LockShortcut => HotkeyBox.Describe(FixedModifiers, LockKey);

    public void Start()
    {
        var settings = Settings.Current;
        _hotkeyAvailable = _hotkey.Register(settings.HotkeyModifiers, settings.HotkeyKey);
        RegisterFixed(_lockHotkey, LockKey, "lock screen");
        RegisterFixed(_earlierHotkey, EarlierKey, "lyrics earlier");
        RegisterFixed(_laterHotkey, LaterKey, "lyrics later");
        ApplySettings(settings);

        Settings.Changed += ApplySettings;
        _hotkey.Pressed += ToggleLyrics;
        _lockHotkey.Pressed += ToggleLockScreen;
        _earlierHotkey.Pressed += () => NudgeTiming(-NudgeStep);
        _laterHotkey.Pressed += () => NudgeTiming(NudgeStep);
        _tray.SettingsRequested += OpenSettings;
        _tray.SettingsToggleRequested += ToggleSettings;
        _tray.ToggleRequested += ToggleLyrics;
        _tray.LockScreenRequested += ShowLockScreen;
        _tray.LoadLyricsRequested += LoadLyricsFile;
        _tray.QuitRequested += Quit;
        _tray.SetLockShortcut(LockShortcut);
        _overlay.Raised += () => _settingsWindow?.KeepAboveOverlay();
        _overlay.CoversDesktopChanged += covers => _settingsWindow?.SetAboveOverlay(covers);
        _media.TrackChanged += OnTrackChanged;
        _media.PlaybackSampled += OnPlaybackSampled;
        _media.ArtChanged += OnArtChanged;
        _media.Start();
        Log.Info("Constella started");
    }

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
            {
                _settingsWindow.WindowState = System.Windows.WindowState.Normal;
            }
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.SetAboveOverlay(_overlay.CoversDesktop);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        if (!_hotkeyAvailable)
        {
            _settingsWindow.ShowHotkeyProblem("That shortcut is taken by another app. Pick a new one.");
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Tray icon click: closes the settings window if it's showing, otherwise opens (or restores) it.</summary>
    public void ToggleSettings()
    {
        if (_settingsWindow is { WindowState: not System.Windows.WindowState.Minimized })
        {
            _settingsWindow.Close();
        }
        else
        {
            OpenSettings();
        }
    }

    public void ToggleLyrics() => Settings.Update(s => s.LyricsVisible = !s.LyricsVisible);

    public void ShowLockScreen()
    {
        if (_lockScreen is not null)
        {
            _lockScreen.Activate();
            return;
        }
        var lockScreen = new LockScreenWindow(_clock, CurrentPalette());
        lockScreen.ApplySettings(Settings.Current);
        lockScreen.SetSong(_track, _sheet, _art);
        lockScreen.SetLyricOffset(LyricOffset());
        lockScreen.Closed += (_, _) =>
        {
            _lockScreen = null;
            _overlay.SetSuppressed(false);
        };
        _lockScreen = lockScreen;
        _overlay.SetSuppressed(true);
        lockScreen.Show();
        lockScreen.Activate();
    }

    public bool TryChangeHotkey(int modifiers, int virtualKey)
    {
        var current = Settings.Current;
        if (_hotkey.Register(modifiers, virtualKey))
        {
            _hotkeyAvailable = true;
            Settings.Update(s =>
            {
                s.HotkeyModifiers = modifiers;
                s.HotkeyKey = virtualKey;
            });
            return true;
        }
        // Put the old one back.
        _hotkeyAvailable = _hotkey.Register(current.HotkeyModifiers, current.HotkeyKey);
        return false;
    }

    public void ResetAppTimings()
    {
        _timing.Clear();
        _appNudge = 0;
        UpdateLyricOffset();
        _settingsWindow?.UpdateTimingSummary();
    }

    public void LoadLyricsFile()
    {
        if (_track is null)
        {
            _tray.Notify("Play the song first", "Start the song, then load its .lrc file so Constella knows which song it belongs to.");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Lyrics for {_track.DisplayName}",
            Filter = "Synced lyrics (*.lrc)|*.lrc|Text files (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(_settingsWindow) != true)
        {
            return;
        }

        try
        {
            var sheet = LyricsService.Import(dialog.FileName, _track);
            if (sheet is null)
            {
                _tray.Notify("No time stamps found", "That file has no [mm:ss.xx] time stamps, so it can't be synced.");
                return;
            }
            _lookup?.Cancel();
            ShowLyrics(new LyricsResult(LyricsStatus.Found, sheet));
            Log.Info($"Loaded lyrics from {dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not read lyrics file", ex);
            _tray.Notify("Couldn't open that file", ex.Message);
        }
    }

    public void Quit()
    {
        Log.Info("Quit requested");
        Settings.Flush();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _lookup?.Cancel();
        _media.Dispose();
        _settingsWindow?.Close();
        _lockScreen?.Close();
        _overlay.Shutdown();
        _meter.Dispose();
        _hotkey.Dispose();
        _lockHotkey.Dispose();
        _earlierHotkey.Dispose();
        _laterHotkey.Dispose();
        _tray.Dispose();
        _lyrics.Dispose();
    }

    private static void RegisterFixed(GlobalHotkey hotkey, int key, string purpose)
    {
        if (!hotkey.Register(FixedModifiers, key))
        {
            Log.Warn($"Shortcut {HotkeyBox.Describe(FixedModifiers, key)} ({purpose}) is taken by another app");
        }
    }

    private void ToggleLockScreen()
    {
        if (_lockScreen is not null)
        {
            _lockScreen.Close();
        }
        else
        {
            ShowLockScreen();
        }
    }

    /// <summary>
    /// Moves the lyrics earlier (negative) or later for the app that's playing, remembers it for
    /// that app, and says so on screen.
    /// </summary>
    private void NudgeTiming(double delta)
    {
        if (_track is null)
        {
            return;
        }
        string app = _track.SourceApp;
        _appNudge = _timing.Adjust(app, delta);
        UpdateLyricOffset();
        string message = Math.Abs(_appNudge) < 0.001
            ? $"{app} lyrics timing reset"
            : $"{app} lyrics {Math.Abs(_appNudge):0.0} s {(_appNudge < 0 ? "earlier" : "later")}";
        _overlay.ShowToast(message);
        _lockScreen?.ShowToast(message);
        _settingsWindow?.UpdateTimingSummary();
        Log.Info($"Timing nudge for {app}: {_appNudge:+0.00;-0.00} s");
    }

    /// <summary>Total delay for the lyrics: the user's setting, plus the playing app's nudge, minus the built-in lead.</summary>
    private double LyricOffset() => Settings.Current.LyricsOffset + _appNudge - LyricLeadSeconds;

    private void UpdateLyricOffset()
    {
        double offset = LyricOffset();
        _overlay.SetLyricOffset(offset);
        _lockScreen?.SetLyricOffset(offset);
    }

    private void ApplySettings(AppSettings settings)
    {
        _overlay.ApplySettings(settings);
        _lockScreen?.ApplySettings(settings);
        UpdatePalette();
        UpdateLyricOffset();
        if (_startupApplied != settings.StartWithWindows)
        {
            StartupRegistration.Apply(settings.StartWithWindows);
            _startupApplied = settings.StartWithWindows;
        }
        _tray.SetLyricsVisible(settings.LyricsVisible, HotkeyBox.Describe(settings.HotkeyModifiers, settings.HotkeyKey));
    }

    private LyricPalette CurrentPalette() =>
        Settings.Current.AutoColorFromArt && _artPalette is LyricPalette fromArt
            ? fromArt
            : LyricPalette.FromSettings(Settings.Current);

    private void UpdatePalette()
    {
        var palette = CurrentPalette();
        _overlay.SetPalette(palette);
        _lockScreen?.SetPalette(palette);
    }

    private void OnTrackChanged(TrackInfo? track)
    {
        _track = track;
        _sheet = null;
        _art = null;
        _clock.Reset();
        _artPalette = null;
        _appNudge = track is null ? 0 : _timing.Get(track.SourceApp);
        UpdateLyricOffset();
        UpdatePalette();
        _overlay.SetTrack(track);
        _lockScreen?.SetSong(track, null, null);
        UpdateNowPlaying();

        _lookup?.Cancel();
        _lookup = null;
        if (track is not null)
        {
            Log.Info($"Now playing: {track.Title} - {track.Artist} [{track.Album}] ({track.SourceApp})");
            _ = LoadLyricsAsync(track);
        }
    }

    private async Task LoadLyricsAsync(TrackInfo track)
    {
        var lookup = new CancellationTokenSource();
        _lookup = lookup;
        try
        {
            var result = await _lyrics.GetAsync(track, lookup.Token);
            // Still the same song? (The user may have skipped while we were searching.)
            if (!lookup.IsCancellationRequested && ReferenceEquals(track, _track))
            {
                Log.Info($"Lyrics: {result.Status}");
                ShowLyrics(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Skipped to another song.
        }
        finally
        {
            lookup.Dispose();
            if (ReferenceEquals(_lookup, lookup))
            {
                _lookup = null;
            }
        }
    }

    private void ShowLyrics(LyricsResult result)
    {
        _sheet = result.Sheet;
        _overlay.SetLyrics(result);
        _lockScreen?.SetSong(_track, _sheet, _art);
    }

    private void OnPlaybackSampled(PlaybackSample sample)
    {
        _clock.Sync(sample);
        if (sample.IsPlaying != _isPlaying)
        {
            _isPlaying = sample.IsPlaying;
            UpdateNowPlaying();
        }
        _overlay.Refresh();
    }

    private async void OnArtChanged(byte[]? art)
    {
        var track = _track;
        if (art is null)
        {
            return;
        }
        _art = art;
        _lockScreen?.SetSong(_track, _sheet, _art);
        // Decoding and colour analysis happen off the UI thread.
        var palette = await Task.Run(() => ArtPalette.FromImage(art));
        if (ReferenceEquals(track, _track))
        {
            _artPalette = palette;
            UpdatePalette();
        }
    }

    private void UpdateNowPlaying()
    {
        _tray.SetNowPlaying(NowPlaying);
        _settingsWindow?.UpdateNowPlaying();
    }
}
