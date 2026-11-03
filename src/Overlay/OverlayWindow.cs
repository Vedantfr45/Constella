using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Constella.Audio;
using Constella.Core;
using Constella.Lyrics;
using Constella.Media;
using Microsoft.Win32;

namespace Constella.Overlay;

/// <summary>
/// The full-screen overlay: transparent, borderless, always on top, click-through, and hidden
/// from Alt+Tab. It runs a 60 fps frame loop only while something is playing; after a few
/// seconds of pause it fades out, hides itself and stops drawing (and listening) entirely.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private const double PauseGraceSeconds = 5;
    private const double FadeInTau = 0.35;
    private const double FadeOutTau = 0.3;

    private readonly PlaybackClock _clock;
    private readonly LoudnessMeter _meter;
    private readonly Grid _root = new();
    private readonly Rectangle _backdrop = new() { Fill = Brushes.Black, IsHitTestVisible = false };
    private readonly Starfield _starfield = new();
    private readonly EdgeGlow _edge = new();
    private readonly TrackCard _card = new();
    private readonly Toast _toast = new();
    private ILyricStyle _style;
    private string _styleId = "";

    private IntPtr _hwnd;
    private string? _monitor;
    private bool _loopRunning;
    private TimeSpan _lastRenderTime;
    private double _lastWall;
    private double _master;

    private bool _userVisible = true;
    private bool _suppressed;
    private TrackInfo? _track;
    private double _offsetSeconds;
    private LyricLook? _look;

    private readonly PaletteFader _palette;
    private Backdrop _backdropKind = Backdrop.Clear;
    private double _brightness = 1;
    private string _hideShortcut = string.Empty;
    private bool _coveredDesktop;

    public OverlayWindow(PlaybackClock clock, LoudnessMeter meter, LyricPalette palette)
    {
        _clock = clock;
        _meter = meter;
        _palette = new PaletteFader(palette);
        _style = LyricStyles.Create("constellation");

        Title = "Constella overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Left = -32000; // Off screen until SourceInitialized puts it on the chosen monitor.
        Top = -32000;
        Width = 1;
        Height = 1;

        TextOptions.SetTextFormattingMode(_root, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(_root, TextRenderingMode.Grayscale);
        _root.IsHitTestVisible = false;
        _root.Opacity = 0;
        _root.Children.Add(_backdrop);
        _root.Children.Add(_starfield);
        _root.Children.Add(_style.Visual);
        _root.Children.Add(_card);
        _root.Children.Add(_edge);
        _root.Children.Add(_toast);
        Content = _root;

        SourceInitialized += OnSourceInitialized;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>Applies everything from settings except colours (the app decides those, see <see cref="SetPalette"/>).</summary>
    public void ApplySettings(AppSettings settings)
    {
        if (settings.Style != _styleId)
        {
            SwapStyle(settings.Style);
        }

        var look = new LyricLook(LyricFonts.ById(settings.Font), settings.TextScale);
        if (look != _look)
        {
            _look = look;
            _style.SetLook(look);
            _card.SetLook(look);
        }

        _brightness = settings.LyricBrightness;
        _style.Visual.Opacity = _brightness;
        _hideShortcut = Ui.Controls.HotkeyBox.Describe(settings.HotkeyModifiers, settings.HotkeyKey);
        if (settings.Backdrop != _backdropKind)
        {
            _backdropKind = settings.Backdrop;
            if (_loopRunning)
            {
                ShowBackdropHint();
            }
            NotifyCoverage();
        }
        _backdrop.Opacity = settings.Backdrop switch
        {
            Backdrop.Black => 1.0,
            Backdrop.Dim => 0.72,
            _ => 0.0,
        };
        _edge.ApplySettings(settings);
        _userVisible = settings.LyricsVisible;

        if (settings.Monitor != _monitor)
        {
            _monitor = settings.Monitor;
            ApplyBounds();
        }
        if (_loopRunning)
        {
            if (_edge.NeedsLoudness) _meter.Start(); else _meter.Stop();
        }
        Refresh();
    }

    /// <summary>Total seconds to delay the lyrics by (settings, per-song nudge and built-in lead combined).</summary>
    public void SetLyricOffset(double seconds) => _offsetSeconds = seconds;

    /// <summary>Steps aside (fades out and stops drawing) while the lock screen is up.</summary>
    public void SetSuppressed(bool suppressed)
    {
        _suppressed = suppressed;
        Refresh();
    }

    /// <summary>Shows a short message (only visible while the overlay is showing).</summary>
    public void ShowToast(string message) => _toast.Show(message, _clock.WallSeconds);

    /// <summary>Raised after the overlay puts itself back on top, so windows that must stay above it can follow.</summary>
    public event Action? Raised;

    /// <summary>Raised when <see cref="CoversDesktop"/> changes.</summary>
    public event Action<bool>? CoversDesktopChanged;

    /// <summary>True while the overlay is on screen with a Dim or Black backdrop, hiding the desktop.</summary>
    public bool CoversDesktop => _loopRunning && _backdropKind != Backdrop.Clear;

    /// <summary>Fades the lyric colours to a new palette.</summary>
    public void SetPalette(LyricPalette palette) => _palette.FadeTo(palette, _clock.WallSeconds, animate: _loopRunning);

    public void SetTrack(TrackInfo? track)
    {
        _track = track;
        double wall = _clock.WallSeconds;
        _style.SetSheet(null, wall);
        _card.Show(track, wall);
        Refresh();
    }

    public void SetLyrics(LyricsResult result)
    {
        _style.SetSheet(result.Sheet, _clock.WallSeconds);
        _card.SetLyrics(result);
        Refresh();
    }

    /// <summary>Re-checks whether the overlay should be running (call after play/pause/track changes).</summary>
    public void Refresh()
    {
        if (ShouldShow && !_loopRunning)
        {
            StartLoop();
        }
    }

    public void Shutdown()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        StopLoop();
        Close();
    }

    private bool ShouldShow =>
        _userVisible && !_suppressed && _track is not null && (_clock.IsPlaying || _clock.SecondsPaused < PauseGraceSeconds);

    private void SwapStyle(string id)
    {
        int at = _root.Children.IndexOf(_style.Visual);
        _root.Children.Remove(_style.Visual);
        _style = LyricStyles.Create(id);
        _styleId = id;
        _root.Children.Insert(at, _style.Visual);
        _style.Visual.Opacity = _brightness;
        if (_look is not null)
        {
            _style.SetLook(_look);
        }
    }

    private void StartLoop()
    {
        _loopRunning = true;
        _lastWall = _clock.WallSeconds;
        if (!IsVisible)
        {
            Show();
        }
        ApplyBounds();
        ShowBackdropHint();
        NotifyCoverage();
        if (_edge.NeedsLoudness)
        {
            _meter.Start();
        }
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopLoop()
    {
        if (!_loopRunning)
        {
            return;
        }
        _loopRunning = false;
        NotifyCoverage();
        CompositionTarget.Rendering -= OnRendering;
        _meter.Stop();
        Hide();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per frame; only do work once per frame.
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderTime)
            {
                return;
            }
            _lastRenderTime = args.RenderingTime;
        }

        double wall = _clock.WallSeconds;
        double dt = Math.Clamp(wall - _lastWall, 0, 0.1);
        _lastWall = wall;

        bool show = ShouldShow;
        _master = Ease.Approach(_master, show ? 1 : 0, dt, show ? FadeInTau : FadeOutTau);
        if (!show && _master < 0.004)
        {
            _master = 0;
            _root.Opacity = 0;
            StopLoop();
            return;
        }
        _root.Opacity = _master;

        var palette = _palette.Update(wall);
        double songTime = _clock.Position - _offsetSeconds;
        var frame = new FrameContext(wall, dt, songTime, palette, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _starfield.Update(wall, palette.Lyric);
        _style.Update(frame);
        _card.Update(wall, songTime, palette);
        _toast.Update(wall);
        _edge.Tick(wall, palette.Lyric, _edge.NeedsLoudness ? _meter.Sample(dt) : 0);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        long style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        // Click-through (TRANSPARENT + LAYERED), not in Alt+Tab (TOOLWINDOW), never takes focus (NOACTIVATE).
        style |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED |
                 NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        ApplyBounds();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DPICHANGED)
        {
            // Moving to a screen with different scaling: let WPF rescale, then cover the screen exactly again.
            Dispatcher.InvokeAsync(ApplyBounds, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(ApplyBounds, DispatcherPriority.Background);

    /// <summary>Covers the chosen monitor exactly and stays above other windows.</summary>
    private void ApplyBounds()
    {
        ScreenCover.Apply(_hwnd, _monitor);
        Raised?.Invoke();
    }

    /// <summary>
    /// With a Dim or Black backdrop the overlay hides the desktop, so say how to get rid of it
    /// for a few seconds whenever it appears.
    /// </summary>
    private void NotifyCoverage()
    {
        bool covers = CoversDesktop;
        if (covers != _coveredDesktop)
        {
            _coveredDesktop = covers;
            CoversDesktopChanged?.Invoke(covers);
        }
    }

    private void ShowBackdropHint()
    {
        if (_backdropKind != Backdrop.Clear)
        {
            _toast.Show($"{_hideShortcut} hides the lyrics", _clock.WallSeconds, holdSeconds: 4);
        }
    }
}
